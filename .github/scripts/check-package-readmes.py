#!/usr/bin/env python3
"""Verify that every packed Naravel .nupkg advertises and contains the README its project declares.

Usage: check-package-readmes.py <directory-containing-nupkgs>
       check-package-readmes.py --self-test

For each src/*/*.csproj and each templates/*/*.csproj (the `Naravel.Templates` pack, PDR-025) the script finds the `None` item that packs a file at the package path README.md, then checks the
produced package: README.md at the package root with the same bytes as that file, and <readme>README.md</readme> in the
nuspec. It also fails when a project declares no README, when a project produced no package (or two), and when the directory
holds a package no project accounts for. It also fails when the README has a relative link or image: nuget.org does not
render a relative image and a relative link has no page to point at, and a package README cannot be corrected after the version
is published. The symbol packages (.snupkg) are not looked at.
"""

from __future__ import annotations

import re
import sys
import tempfile
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

FENCE_OPEN = re.compile(r"^[ \t]*(`{3,}|~{3,})(.*)$")
INLINE_CODE = re.compile(r"`[^`\n]*`")
TARGETS = (
    re.compile(r"!?\[[^\]\n]*\]\(\s*<?([^)\s>]+)"),  # [text](target) and ![alt](target)
    re.compile(r"^\s{0,3}\[[^\]\n]+\]:\s*<?(\S+)", re.MULTILINE),  # [label]: target
    re.compile(r"""\b(?:src|href)\s*=\s*["']([^"']+)["']""", re.IGNORECASE),  # raw HTML
)
ABSOLUTE = ("https://", "http://", "mailto:", "#")


def without_fenced_code(markdown: str) -> str:
    """The Markdown without its fenced code blocks, following CommonMark: a fence closes with the same character, at least as
    many times as it opened (so a four-backtick block can show a three-backtick example), and a fence that never closes runs to
    the end of the file."""
    kept: list[str] = []
    fence: re.Pattern[str] | None = None
    for line in markdown.splitlines():
        if fence is not None:
            if fence.match(line):
                fence = None
            continue
        opener = FENCE_OPEN.match(line)
        if opener and not (opener[1][0] == "`" and "`" in opener[2]):  # a backtick fence's info string has no backtick
            fence = re.compile(rf"^[ \t]*{re.escape(opener[1][0])}{{{len(opener[1])},}}[ \t]*$")
            continue
        kept.append(line)
    return "\n".join(kept)


def relative_targets(markdown: str) -> list[str]:
    """Link and image targets that are neither absolute nor an in-page anchor, ignoring code."""
    prose = INLINE_CODE.sub("", without_fenced_code(markdown))
    found = [target for pattern in TARGETS for target in pattern.findall(prose)]
    return sorted({target for target in found if not target.lower().startswith(ABSOLUTE)})


def local_name(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def package_projects(root: Path = ROOT) -> list[Path]:
    """The projects that make a package: the ones directly under src/ and the template pack under templates/."""
    return sorted([*(root / "src").glob("*/*.csproj"), *(root / "templates").glob("*/*.csproj")])


def readme_source(csproj: Path, root: Path = ROOT) -> tuple[str, Path | None, str | None]:
    """Return (package id, README source file, problem) for one project."""
    tree = ET.parse(csproj).getroot()
    package_id = next((n.text.strip() for n in tree.iter("PackageId") if n.text), csproj.stem)
    readme_file = next((n.text.strip() for n in tree.iter("PackageReadmeFile") if n.text), None)
    if readme_file != "README.md":
        return package_id, None, f"{csproj.relative_to(root)} must set <PackageReadmeFile>README.md</PackageReadmeFile> (found {readme_file!r})"

    sources = [
        node.get("Include", "")
        for node in tree.iter("None")
        if node.get("Pack", "").lower() == "true" and node.get("PackagePath", "").replace("\\", "/") == "README.md"
    ]
    if len(sources) != 1:
        return package_id, None, (
            f"{csproj.relative_to(root)} must pack exactly one file with PackagePath=\"README.md\" "
            f"(add docs/nuget/{package_id}.md and a <None Include=... Pack=\"true\" PackagePath=\"README.md\" /> item); found {len(sources)}"
        )

    path = (csproj.parent / sources[0].replace("\\", "/")).resolve()
    if not path.is_file():
        return package_id, None, f"{csproj.relative_to(root)} packs {sources[0]} as the README, but that file does not exist"
    return package_id, path, None


def verify(package_dir: Path, root: Path = ROOT) -> tuple[list[str], int]:
    """Return (errors, number of packages expected) for the .nupkg files in package_dir."""
    errors: list[str] = []

    expected: dict[str, Path] = {}
    for csproj in package_projects(root):
        package_id, source, problem = readme_source(csproj, root)
        if problem:
            errors.append(problem)
        else:
            expected[package_id] = source  # type: ignore[assignment]

    packages = sorted(package_dir.glob("*.nupkg"))
    claimed: set[Path] = set()
    for package_id, source in expected.items():
        # "Naravel.Queue" must not claim "Naravel.Queue.Redis.0.1.0.nupkg": the part after the id has to start with a version.
        pattern = re.compile(rf"^{re.escape(package_id)}\.\d+\.\d+\.\d+.*\.nupkg$")
        matches = [p for p in packages if pattern.match(p.name)]
        if len(matches) != 1:
            errors.append(f"expected exactly one {package_id} .nupkg in {package_dir}, found {len(matches)}")
            continue

        package = matches[0]
        claimed.add(package)
        try:
            with zipfile.ZipFile(package) as archive:
                names = set(archive.namelist())
                if "README.md" not in names:
                    errors.append(f"{package.name} does not contain README.md at the package root")
                elif archive.read("README.md") != source.read_bytes():
                    errors.append(f"README.md in {package.name} is not the content of {source.relative_to(root)}")
                else:
                    for target in relative_targets(archive.read("README.md").decode("utf-8")):
                        errors.append(f"README.md in {package.name} has a relative link or image ({target}); use an absolute https URL")

                nuspecs = [name for name in names if name.lower().endswith(".nuspec")]
                if len(nuspecs) != 1:
                    errors.append(f"expected one .nuspec in {package.name}, found {len(nuspecs)}")
                    continue
                metadata = next((n for n in ET.fromstring(archive.read(nuspecs[0])).iter() if local_name(n.tag) == "metadata"), None)
                readme = None if metadata is None else next((n for n in metadata if local_name(n.tag) == "readme"), None)
                value = None if readme is None else (readme.text or "").strip()
                if value != "README.md":
                    errors.append(f"{nuspecs[0]} in {package.name} must declare <readme>README.md</readme>; found {value!r}")
        except (OSError, zipfile.BadZipFile, ET.ParseError) as exc:
            errors.append(f"cannot inspect {package}: {exc}")

    for package in packages:
        if package not in claimed:
            errors.append(f"{package.name} does not belong to any project under src/ or templates/ (or its project declares no README)")
    return errors, len(expected)


def main(argv: list[str]) -> int:
    if argv[1:] == ["--self-test"]:
        return self_test()
    if len(argv) != 2:
        print("usage: check-package-readmes.py <directory-containing-nupkgs> | --self-test", file=sys.stderr)
        return 1

    errors, count = verify(Path(argv[1]))
    if errors:
        for message in errors:
            print(f"ERROR: {message}", file=sys.stderr)
        return 1

    print(f"Package READMEs verified: {count} packages, each with the declared README and nuspec metadata")
    return 0


# --- self-test: a small repository tree and fake packages in a temporary directory ----------------------------------------------

PROJECT = """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><PackageReadmeFile>README.md</PackageReadmeFile></PropertyGroup>
  <ItemGroup><None Include="..\\..\\docs\\nuget\\{name}.md" Pack="true" PackagePath="README.md" /></ItemGroup>
</Project>
"""
NUSPEC = "<package><metadata><id>{name}</id><readme>README.md</readme></metadata></package>"


def self_test() -> int:
    def tree(base: Path, template_page: str = "# T\n\n[site](https://example.com)\n", template_csproj: str | None = None) -> None:
        for folder, name, page in (("src", "Lib", "# Lib\n\n![badge](https://example.com/a.svg)\n"), ("templates", "Tpl", template_page)):
            (base / folder / name).mkdir(parents=True)
            (base / folder / name / f"{name}.csproj").write_text(
                template_csproj if (name == "Tpl" and template_csproj is not None) else PROJECT.format(name=name), encoding="utf-8")
            (base / "docs" / "nuget").mkdir(parents=True, exist_ok=True)
            (base / "docs" / "nuget" / f"{name}.md").write_text(page, encoding="utf-8")

    def package(directory: Path, name: str, readme: bytes, declare: bool = True) -> None:
        with zipfile.ZipFile(directory / f"{name}.1.0.0.nupkg", "w") as archive:
            archive.writestr("README.md", readme)
            archive.writestr(f"{name}.nuspec", NUSPEC.format(name=name) if declare else "<package><metadata /></package>")

    lib_page = b"# Lib\n\n![badge](https://example.com/a.svg)\n"
    tpl_page = b"# T\n\n[site](https://example.com)\n"
    cases = [
        # (description, tree options, packages to create, expected number of errors, text the errors must contain)
        ("both packages are right", {}, {"Lib": lib_page, "Tpl": tpl_page}, 0, ""),
        ("the template pack is missing", {}, {"Lib": lib_page}, 1, "expected exactly one Tpl"),
        ("a package that no project owns", {}, {"Lib": lib_page, "Tpl": tpl_page, "Other": b"x"}, 1, "does not belong to any project under src/ or templates/"),
        ("the template README is not the page", {}, {"Lib": lib_page, "Tpl": b"# Another\n"}, 1, "is not the content of docs/nuget/Tpl.md"),
        ("the template page has a relative link", {"template_page": "# T\n\n[guide](docs/guide.md)\n"}, {"Lib": lib_page, "Tpl": b"# T\n\n[guide](docs/guide.md)\n"}, 1, "relative link or image (docs/guide.md)"),
        ("the template project declares no README", {"template_csproj": "<Project Sdk=\"Microsoft.NET.Sdk\" />"}, {"Lib": lib_page, "Tpl": tpl_page}, 2, "PackageReadmeFile"),
    ]
    failed = 0
    for description, options, packages, expected, needle in cases:
        with tempfile.TemporaryDirectory() as temp:
            base = Path(temp) / "repo"
            output = Path(temp) / "nupkgs"
            output.mkdir()
            tree(base, **options)
            for name, readme in packages.items():
                package(output, name, readme)
            errors, _ = verify(output, base)
        if len(errors) != expected or (needle and not any(needle in error for error in errors)):
            failed += 1
            print(f"::error title=README check self-test::{description}: expected {expected} error(s) containing {needle!r}, got {errors}")
    if failed:
        return 1
    print(f"check-package-readmes self-test: {len(cases)} cases passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
