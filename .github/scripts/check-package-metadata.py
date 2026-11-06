#!/usr/bin/env python3
"""Verify the metadata nuget.org shows for every packed Naravel package.

NuGet metadata cannot be edited after a version is pushed, so a missing field is only visible on the package page after it is
too late. This reads the .nuspec inside every .nupkg in a directory and fails on a package whose:

- description is missing or is the SDK default ("Package Description") or is too short to say what the package is;
- license is not the MIT expression, or the repository / project URL is missing;
- copyright differs from the "Copyright (c) ..." line in LICENSE;
- tags are missing or fewer than four (Microsoft: "include several tags with key terms related to your package"; nuget.org search matches on them);
- release notes do not link to the GitHub release for that package's own version (v<version>);
- icon does not match build/icon.png (declared and packed when the file exists, absent when it does not).

Usage: check-package-metadata.py <directory-containing-nupkgs>
"""

from __future__ import annotations

import re
import sys
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MIN_DESCRIPTION = 40
MIN_TAGS = 4
SDK_DEFAULT_DESCRIPTION = "Package Description"


def local_name(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def license_copyright() -> str | None:
    for line in (ROOT / "LICENSE").read_text(encoding="utf-8").splitlines():
        if line.strip().lower().startswith("copyright"):
            return line.strip()
    return None


def read_metadata(archive: zipfile.ZipFile, package: Path) -> tuple[dict[str, ET.Element], str]:
    nuspecs = [name for name in archive.namelist() if name.lower().endswith(".nuspec")]
    if len(nuspecs) != 1:
        raise ValueError(f"expected one .nuspec in {package.name}, found {len(nuspecs)}")
    root = ET.fromstring(archive.read(nuspecs[0]))
    metadata = next((node for node in root.iter() if local_name(node.tag) == "metadata"), None)
    if metadata is None:
        raise ValueError(f"{nuspecs[0]} has no metadata element")
    return {local_name(node.tag): node for node in metadata}, nuspecs[0]


def text(fields: dict[str, ET.Element], name: str) -> str:
    node = fields.get(name)
    return (node.text or "").strip() if node is not None else ""


def check_package(package: Path, copyright_line: str | None, icon_exists: bool) -> list[str]:
    problems: list[str] = []
    with zipfile.ZipFile(package) as archive:
        fields, _ = read_metadata(archive, package)
        names = set(archive.namelist())
        version = text(fields, "version")

        description = text(fields, "description")
        if not description or description == SDK_DEFAULT_DESCRIPTION:
            problems.append(f"description is missing or the SDK default ({description!r}); set <Description> in the project")
        elif len(description) < MIN_DESCRIPTION:
            problems.append(f"description is {len(description)} characters, under {MIN_DESCRIPTION}: {description!r}")

        license_node = fields.get("license")
        if license_node is None or license_node.get("type") != "expression" or (license_node.text or "").strip() != "MIT":
            problems.append("license must be the MIT expression")
        if not text(fields, "projectUrl"):
            problems.append("projectUrl is missing")
        repository = fields.get("repository")
        if repository is None or not repository.get("url"):
            problems.append("repository url is missing")

        tags = text(fields, "tags").split()
        if not tags:
            problems.append("tags are missing")
        elif len(tags) < MIN_TAGS:
            problems.append(f"only {len(tags)} tags ({' '.join(tags)}); give at least {MIN_TAGS} key terms, because nuget.org search matches on tags")

        actual_copyright = text(fields, "copyright")
        if not actual_copyright:
            problems.append("copyright is missing")
        elif copyright_line is not None and actual_copyright != copyright_line:
            problems.append(f"copyright {actual_copyright!r} differs from LICENSE {copyright_line!r}")

        notes = text(fields, "releaseNotes")
        if not notes:
            problems.append("release notes are missing")
        elif not re.search(rf"/releases/tag/v{re.escape(version)}(?![\w.-])", notes):
            problems.append(f"release notes do not link to the GitHub release for v{version}: {notes!r}")

        icon = text(fields, "icon")
        if icon_exists:
            if icon != "icon.png" or "icon.png" not in names:
                problems.append("build/icon.png exists, so the package must declare <icon>icon.png</icon> and contain icon.png")
        elif icon:
            problems.append(f"the package declares an icon ({icon!r}) but build/icon.png does not exist")
    return problems


def main() -> int:
    if len(sys.argv) != 2:
        print("usage: check-package-metadata.py <directory-containing-nupkgs>", file=sys.stderr)
        return 1

    package_dir = Path(sys.argv[1])
    packages = sorted(package_dir.glob("*.nupkg"))
    if not packages:
        print(f"ERROR: no .nupkg in {package_dir}", file=sys.stderr)
        return 1

    copyright_line = license_copyright()
    icon_exists = (ROOT / "build" / "icon.png").is_file()
    failed = 0
    for package in packages:
        try:
            problems = check_package(package, copyright_line, icon_exists)
        except (OSError, zipfile.BadZipFile, ET.ParseError, ValueError) as exc:
            problems = [f"cannot inspect: {exc}"]
        for problem in problems:
            print(f"ERROR: {package.name}: {problem}", file=sys.stderr)
        failed += bool(problems)

    if failed:
        print(f"{failed} of {len(packages)} packages have metadata problems", file=sys.stderr)
        return 1
    print(f"Package metadata verified for {len(packages)} packages")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
