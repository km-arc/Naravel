#!/usr/bin/env python3
"""Check the versions the `dotnet new` templates write into the projects they generate (PDR-025 D8).

The projects under templates/<pack>/content/<template>/ are copied to a user's machine, where there is no Directory.Packages.props
and no VersionPrefix, so their versions are written into the template itself. Nothing else compares them with this repository, and
a stale one would only show up in an application that a user generated. This script fails when:

- the NaravelVersion symbol of a template.json (the version that replaces the placeholder in the generated project) is not the
  VersionPrefix of Directory.Build.props, so a release would generate applications that reference the previous version;
- a Naravel.* package in a template project has a version of its own instead of the placeholder, so the version would be written in
  two places;
- the placeholder is not used by any project, or the version number itself is written in another file of the template;
- a package of a template project that is neither Naravel's nor one of the three test packages approved by PDR-025 D8 (xunit,
  xunit.runner.visualstudio, Microsoft.NET.Test.Sdk) is referenced: a new dependency needs a decision record first;
- one of those three test packages has a version that is not the one in Directory.Packages.props, which the repository's own tests use.

A repository with no template that has a NaravelVersion symbol fails too, so the check cannot pass by finding nothing.

Usage:
  check-template-pins.py               check this repository
  check-template-pins.py --self-test   the script's own checks on small trees it builds (no dotnet needed)
"""

from __future__ import annotations

import json
import re
import sys
import tempfile
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
VERSION_SYMBOL = "NaravelVersion"
APPROVED_TEST_PACKAGES = ("xunit", "xunit.runner.visualstudio", "Microsoft.NET.Test.Sdk")
TEXT_SUFFIXES = {".cs", ".csproj", ".json", ".md", ".html", ".txt", ".props", ".targets", ".yml", ".yaml", ".xml", ".config"}


def version_prefix(root: Path) -> tuple[str | None, list[str]]:
    props = ET.parse(root / "Directory.Build.props").getroot()
    values = [(node.text or "").strip() for group in props.findall("PropertyGroup") for node in group if node.tag == "VersionPrefix"]
    if len(values) != 1 or not values[0]:
        return None, ["Directory.Build.props must define exactly one non-empty VersionPrefix"]
    return values[0], []


def central_pins(root: Path) -> dict[str, str]:
    props = ET.parse(root / "Directory.Packages.props").getroot()
    return {(item.get("Include") or "").casefold(): item.get("Version", "") for item in props.iter("PackageVersion") if item.get("Include")}


def template_configs(root: Path) -> list[Path]:
    """Every templates/<pack>/content/<template>/.template.config/template.json."""
    return sorted((root / "templates").glob("*/content/*/.template.config/template.json"))


def files_of(template_directory: Path) -> list[Path]:
    """The files of a template folder, build output of a hand-built copy left out."""
    return sorted(
        path for path in template_directory.rglob("*")
        if path.is_file() and not {"bin", "obj"} & set(path.relative_to(template_directory).parts))


def check(root: Path) -> tuple[list[str], list[str]]:
    """(errors, facts). The facts are what was compared, printed so that a pass can be read as a pass of something."""
    errors: list[str] = []
    facts: list[str] = []

    prefix, problems = version_prefix(root)
    errors.extend(problems)
    pins = central_pins(root)

    checked = 0
    for config in template_configs(root):
        folder = config.parent.parent
        label = folder.relative_to(root).as_posix()
        try:
            document = json.loads(config.read_text(encoding="utf-8"))
        except ValueError as error:
            errors.append(f"{label}/.template.config/template.json is not valid JSON: {error}")
            continue
        symbol = (document.get("symbols") or {}).get(VERSION_SYMBOL)
        if symbol is None:
            continue  # a template that references no package (an item template) has no version to check
        checked += 1

        default, placeholder = symbol.get("defaultValue"), symbol.get("replaces")
        if prefix is not None and default != prefix:
            errors.append(f"{label}: {VERSION_SYMBOL} defaults to {default!r} but VersionPrefix in Directory.Build.props is {prefix!r}")
        if not placeholder:
            errors.append(f"{label}: the {VERSION_SYMBOL} symbol has no 'replaces' placeholder")
            continue

        placeholder_uses = 0
        for project in (path for path in files_of(folder) if path.suffix == ".csproj"):
            name = project.relative_to(root).as_posix()
            try:
                references = list(ET.parse(project).getroot().iter("PackageReference"))
            except ET.ParseError as error:
                errors.append(f"{name}: invalid project XML: {error}")
                continue
            for reference in references:
                package = reference.get("Include") or reference.get("Update") or ""
                version = reference.get("Version") or ""
                if package.startswith("Naravel."):
                    if version == placeholder:
                        placeholder_uses += 1
                    else:
                        errors.append(f"{name}: {package} has the version {version!r}; it must be {placeholder} so that the version is "
                                      f"written once, in template.json")
                elif package in APPROVED_TEST_PACKAGES:
                    pinned = pins.get(package.casefold())
                    if pinned is None:
                        errors.append(f"{name}: {package} has no version in Directory.Packages.props to compare with")
                    elif version != pinned:
                        errors.append(f"{name}: {package} is pinned to {version!r} but Directory.Packages.props has {pinned!r}")
                else:
                    errors.append(f"{name}: {package or '(a reference without a package id)'} is not approved for a generated project; "
                                  f"PDR-025 D8 approves the Naravel packages and {', '.join(APPROVED_TEST_PACKAGES)}, and a new "
                                  f"dependency needs a decision record first")
        if placeholder_uses == 0:
            errors.append(f"{label}: no project uses the placeholder {placeholder}, so {VERSION_SYMBOL} changes nothing")

        if prefix:
            # The version as a number of its own: not the tail of a longer one (10.2.0), not the head of one (0.2.01 or 0.2.0.1); a full stop
            # that ends a sentence is fine.
            literal = re.compile(rf"(?<![\w.]){re.escape(prefix)}(?!\w|\.\d)")
            for path in files_of(folder):
                if path == config or path.suffix not in TEXT_SUFFIXES:
                    continue
                try:
                    text = path.read_text(encoding="utf-8")
                except UnicodeDecodeError:
                    continue
                if literal.search(text):
                    errors.append(f"{path.relative_to(root).as_posix()}: writes the version {prefix} itself; it belongs only in template.json")

        facts.append(f"{label}: {VERSION_SYMBOL} {default}, {placeholder_uses} Naravel reference(s) use {placeholder}")

    if checked == 0:
        errors.append("no template with a NaravelVersion symbol was found under templates/*/content/*/.template.config/")
    elif prefix is not None:
        pinned = ", ".join(f"{name} {pins.get(name.casefold(), '?')}" for name in APPROVED_TEST_PACKAGES)
        facts.append(f"VersionPrefix {prefix}; test packages compared with Directory.Packages.props: {pinned}")
    return errors, facts


# ---------------------------------------------------------------------------------------------------------------------------------
# Self-test: small trees written to a temporary directory, one defect at a time.
# ---------------------------------------------------------------------------------------------------------------------------------

BUILD_PROPS = "<Project><PropertyGroup><VersionPrefix>0.2.0</VersionPrefix></PropertyGroup></Project>"
PACKAGES_PROPS = """<Project><PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup><ItemGroup>
<PackageVersion Include="xunit" Version="2.9.3" /><PackageVersion Include="xunit.runner.visualstudio" Version="4.0.1" />
<PackageVersion Include="Microsoft.NET.Test.Sdk" Version="18.11.0" /></ItemGroup></Project>"""
TEMPLATE_JSON = json.dumps({"symbols": {"NaravelVersion": {"type": "parameter", "defaultValue": "0.2.0", "replaces": "NARAVEL_VERSION"}}})
APP_CSPROJ = """<Project Sdk="Microsoft.NET.Sdk.Web"><ItemGroup>
<PackageReference Include="Naravel.Cache" Version="NARAVEL_VERSION" />
<!--#if (queue == "redis") --><PackageReference Include="Naravel.Queue.Redis" Version="NARAVEL_VERSION" /><!--#endif-->
</ItemGroup></Project>"""
TEST_CSPROJ = """<Project Sdk="Microsoft.NET.Sdk"><ItemGroup>
<PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.11.0" /><PackageReference Include="xunit" Version="2.9.3" />
<PackageReference Include="xunit.runner.visualstudio" Version="4.0.1" /></ItemGroup></Project>"""


def build_tree(root: Path, **changes: str | None) -> None:
    """A minimal repository; a keyword replaces the content of one file, and None leaves it out."""
    files = {
        "Directory.Build.props": BUILD_PROPS,
        "Directory.Packages.props": PACKAGES_PROPS,
        "templates/Pack/content/app/.template.config/template.json": TEMPLATE_JSON,
        "templates/Pack/content/app/App.csproj": APP_CSPROJ,
        "templates/Pack/content/app/tests/App.Tests/App.Tests.csproj": TEST_CSPROJ,
        "templates/Pack/content/app/README.md": "# App\n\nRun it with dotnet run.\n",
    }
    names = {"build_props": "Directory.Build.props", "packages_props": "Directory.Packages.props",
             "template_json": "templates/Pack/content/app/.template.config/template.json", "app_csproj": "templates/Pack/content/app/App.csproj",
             "test_csproj": "templates/Pack/content/app/tests/App.Tests/App.Tests.csproj", "readme": "templates/Pack/content/app/README.md"}
    for key, value in changes.items():
        files[names[key]] = value
    for relative, text in files.items():
        if text is None:
            continue
        path = root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8")


def errors_of(**changes: str | None) -> list[str]:
    with tempfile.TemporaryDirectory() as tmp:
        build_tree(Path(tmp), **changes)
        return check(Path(tmp))[0]


def self_test() -> int:
    failures: list[str] = []

    def expect(name: str, condition: bool) -> None:
        if not condition:
            failures.append(name)

    def mentions(errors: list[str], *needles: str) -> bool:
        """Exactly one problem, and it says all of the needles."""
        return len(errors) == 1 and all(needle in errors[0] for needle in needles)

    def reports(errors: list[str], *needles: str) -> bool:
        """At least one problem says all of the needles (a defect can break more than one rule)."""
        return any(all(needle in error for needle in needles) for error in errors)

    with tempfile.TemporaryDirectory() as tmp:
        build_tree(Path(tmp))
        errors, facts = check(Path(tmp))
    expect("a consistent tree passes, a conditional block in the project file included", errors == [])
    expect("a pass says what was compared", any("NaravelVersion 0.2.0" in fact for fact in facts) and any("xunit 2.9.3" in fact for fact in facts))

    expect("a default that is not VersionPrefix fails",
           mentions(errors_of(template_json=TEMPLATE_JSON.replace('"0.2.0"', '"0.1.1"')), "defaults to '0.1.1'", "'0.2.0'"))
    expect("a literal Naravel version fails",
           reports(errors_of(app_csproj=APP_CSPROJ.replace('Naravel.Cache" Version="NARAVEL_VERSION"', 'Naravel.Cache" Version="0.2.0"')),
                   "Naravel.Cache", "written once"))
    expect("a test package that differs from Directory.Packages.props fails",
           mentions(errors_of(test_csproj=TEST_CSPROJ.replace('"2.9.3"', '"2.9.2"')), "xunit", "'2.9.2'", "'2.9.3'"))
    expect("a test package without a central version fails",
           mentions(errors_of(packages_props=PACKAGES_PROPS.replace('<PackageVersion Include="xunit" Version="2.9.3" />', "")),
                    "xunit has no version in Directory.Packages.props"))
    expect("a package that is not approved fails",
           mentions(errors_of(test_csproj=TEST_CSPROJ.replace("</ItemGroup>", '<PackageReference Include="FluentAssertions" Version="7.2.2" /></ItemGroup>')),
                    "FluentAssertions", "not approved"))
    expect("the version written in another file fails", mentions(errors_of(readme="# App\n\nNeeds Naravel 0.2.0.\n"), "README.md", "0.2.0"))
    expect("a longer number that contains the version is not the version",
           errors_of(readme="# App\n\nTargets net10.0 and 10.2.0.1 and 0.2.01.\n") == [])
    expect("an unused placeholder fails",
           mentions(errors_of(app_csproj="<Project Sdk=\"Microsoft.NET.Sdk.Web\" />"), "no project uses the placeholder"))
    expect("a template.json that is not JSON fails", reports(errors_of(template_json="{"), "not valid JSON"))
    expect("a symbol without a placeholder fails",
           reports(errors_of(template_json=json.dumps({"symbols": {"NaravelVersion": {"defaultValue": "0.2.0"}}})), "no 'replaces'"))
    expect("a project file that is not XML fails", reports(errors_of(app_csproj="<Project"), "invalid project XML"))
    expect("a repository with no versioned template fails", mentions(errors_of(template_json=None), "no template with a NaravelVersion symbol"))
    expect("an item template without the symbol is left alone",
           errors_of(template_json=json.dumps({"symbols": {"x": {}}})) == ["no template with a NaravelVersion symbol was found under "
                                                                         "templates/*/content/*/.template.config/"])
    expect("two VersionPrefix values fail",
           reports(errors_of(build_props=BUILD_PROPS.replace("</PropertyGroup>", "<VersionPrefix>0.3.0</VersionPrefix></PropertyGroup>")),
                   "exactly one"))
    expect("a sentence that ends with the version is still the version written elsewhere",
           reports(errors_of(readme="Needs 0.2.0."), "README.md"))

    for failure in failures:
        print(f"FAIL {failure}", file=sys.stderr)
    print(f"check-template-pins self-test: {'FAILED' if failures else 'all cases passed'}")
    return 1 if failures else 0


def main() -> int:
    if "--self-test" in sys.argv[1:]:
        return self_test()
    errors, facts = check(ROOT)
    for fact in facts:
        print(fact)
    for error in errors:
        print(f"::error title=Template pins::{error}")
    if errors:
        print(f"{len(errors)} template pin problem(s)")
        return 1
    print("Template versions and test pins are consistent with the repository.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
