#!/usr/bin/env python3
"""Verify the Foundation .nupkg advertises and contains its package README."""

from __future__ import annotations

import sys
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path


def local_name(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def fail(message: str) -> int:
    print(f"ERROR: {message}", file=sys.stderr)
    return 1


# The README that must be packed. The csproj packs this file as the package-root README.md, so the bytes must match;
# metadata alone would not notice the repository overview being packed under the same name.
EXPECTED_README = Path(__file__).resolve().parents[2] / "docs" / "en" / "foundation.md"


def main() -> int:
    if len(sys.argv) != 2:
        return fail("usage: check-foundation-readme.py <directory-containing-nupkg>")

    package_dir = Path(sys.argv[1])
    packages = sorted(package_dir.glob("Naravel.Foundation.*.nupkg"))
    if len(packages) != 1:
        return fail(f"expected exactly one Naravel.Foundation .nupkg in {package_dir}, found {len(packages)}")

    package = packages[0]
    try:
        with zipfile.ZipFile(package) as archive:
            names = set(archive.namelist())
            if "README.md" not in names:
                return fail(f"{package.name} does not contain README.md at the package root")

            if not EXPECTED_README.is_file():
                return fail(f"cannot find {EXPECTED_README} to compare the packed README with")
            if archive.read("README.md") != EXPECTED_README.read_bytes():
                return fail(f"README.md in {package.name} is not the content of docs/en/foundation.md")

            nuspecs = [name for name in names if name.lower().endswith(".nuspec")]
            if len(nuspecs) != 1:
                return fail(f"expected one .nuspec in {package.name}, found {len(nuspecs)}")

            root = ET.fromstring(archive.read(nuspecs[0]))
            metadata = next((node for node in root.iter() if local_name(node.tag) == "metadata"), None)
            if metadata is None:
                return fail(f"{nuspecs[0]} has no metadata element")

            readme = next((node for node in metadata if local_name(node.tag) == "readme"), None)
            if readme is None or (readme.text or "").strip() != "README.md":
                value = None if readme is None else (readme.text or "").strip()
                return fail(f"{nuspecs[0]} must declare <readme>README.md</readme>; found {value!r}")
    except (OSError, zipfile.BadZipFile, ET.ParseError) as exc:
        return fail(f"cannot inspect {package}: {exc}")

    print(f"Foundation package README verified: {package.name}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
