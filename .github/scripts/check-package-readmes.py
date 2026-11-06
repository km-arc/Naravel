#!/usr/bin/env python3
"""Verify that every packed Naravel .nupkg advertises and contains the README its project declares.

Usage: check-package-readmes.py <directory-containing-nupkgs>

For each src/*/*.csproj the script finds the `None` item that packs a file at the package path README.md, then checks the
produced package: README.md at the package root with the same bytes as that file, and <readme>README.md</readme> in the
nuspec. It also fails when a project declares no README, when a project produced no package (or two), and when the directory
holds a package no project accounts for. It also fails when the README has a relative link or image: nuget.org does not
render a relative image and a relative link has no page to point at, and a package README cannot be corrected after the version
is published. The symbol packages (.snupkg) are not looked at.
"""

from __future__ import annotations

import re
import sys
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

FENCE = re.compile(r"^(```|~~~).*?^\1[ \t]*$", re.MULTILINE | re.DOTALL)
INLINE_CODE = re.compile(r"`[^`\n]*`")
TARGETS = (
    re.compile(r"!?\[[^\]\n]*\]\(\s*<?([^)\s>]+)"),  # [text](target) and ![alt](target)
    re.compile(r"^\s{0,3}\[[^\]\n]+\]:\s*<?(\S+)", re.MULTILINE),  # [label]: target
    re.compile(r"""\b(?:src|href)\s*=\s*["']([^"']+)["']""", re.IGNORECASE),  # raw HTML
)
ABSOLUTE = ("https://", "http://", "mailto:", "#")


def relative_targets(markdown: str) -> list[str]:
    """Link and image targets that are neither absolute nor an in-page anchor, ignoring code."""
    prose = INLINE_CODE.sub("", FENCE.sub("", markdown))
    found = [target for pattern in TARGETS for target in pattern.findall(prose)]
    return sorted({target for target in found if not target.lower().startswith(ABSOLUTE)})


def local_name(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def readme_source(csproj: Path) -> tuple[str, Path | None, str | None]:
    """Return (package id, README source file, problem) for one project."""
    tree = ET.parse(csproj).getroot()
    package_id = next((n.text.strip() for n in tree.iter("PackageId") if n.text), csproj.stem)
    readme_file = next((n.text.strip() for n in tree.iter("PackageReadmeFile") if n.text), None)
    if readme_file != "README.md":
        return package_id, None, f"{csproj.relative_to(ROOT)} must set <PackageReadmeFile>README.md</PackageReadmeFile> (found {readme_file!r})"

    sources = [
        node.get("Include", "")
        for node in tree.iter("None")
        if node.get("Pack", "").lower() == "true" and node.get("PackagePath", "").replace("\\", "/") == "README.md"
    ]
    if len(sources) != 1:
        return package_id, None, (
            f"{csproj.relative_to(ROOT)} must pack exactly one file with PackagePath=\"README.md\" "
            f"(add docs/nuget/{package_id}.md and a <None Include=... Pack=\"true\" PackagePath=\"README.md\" /> item); found {len(sources)}"
        )

    path = (csproj.parent / sources[0].replace("\\", "/")).resolve()
    if not path.is_file():
        return package_id, None, f"{csproj.relative_to(ROOT)} packs {sources[0]} as the README, but that file does not exist"
    return package_id, path, None


def main() -> int:
    if len(sys.argv) != 2:
        print("usage: check-package-readmes.py <directory-containing-nupkgs>", file=sys.stderr)
        return 1

    package_dir = Path(sys.argv[1])
    errors: list[str] = []

    expected: dict[str, Path] = {}
    for csproj in sorted((ROOT / "src").glob("*/*.csproj")):
        package_id, source, problem = readme_source(csproj)
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
                    errors.append(f"README.md in {package.name} is not the content of {source.relative_to(ROOT)}")
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
            errors.append(f"{package.name} does not belong to any project under src/ (or its project declares no README)")

    if errors:
        for message in errors:
            print(f"ERROR: {message}", file=sys.stderr)
        return 1

    print(f"Package READMEs verified: {len(expected)} packages, each with the declared README and nuspec metadata")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
