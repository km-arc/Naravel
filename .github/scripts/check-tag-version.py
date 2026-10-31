#!/usr/bin/env python3
"""Fail unless every packed .nupkg carries the version the release tag names.

Usage:
  check-tag-version.py <tag> <directory with the .nupkg files>
  check-tag-version.py --self-test

A tag such as ``v0.1.0`` names version ``0.1.0``. The packages take their version from Directory.Build.props, so a tag that
disagrees (an old preview tag, a typo, a tag cut from an older commit) would publish packages under the wrong release name.
Package files are named ``<PackageId>.<version>.nupkg``; the check looks for ``.<version>.nupkg`` at the end of the name, with
the leading dot, so ``X.10.1.0.nupkg`` is not mistaken for version ``0.1.0``.

The publish job only runs on tags, so the ``pins`` job runs ``--self-test`` on every pull request to keep this script working.
"""
import contextlib
import io
import re
import sys
import tempfile
from pathlib import Path

TAG = re.compile(r"^v(\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?)$")


def check(tag: str, directory: Path) -> int:
    match = TAG.match(tag)
    if not match:
        print(f"::error title=Tag/version::'{tag}' is not a version tag like v1.2.3 or v1.2.3-preview")
        return 1
    expected = match.group(1)
    packages = sorted(directory.glob("*.nupkg"))
    if not packages:
        print(f"::error title=Tag/version::no .nupkg files in {directory}; nothing to publish")
        return 1
    wrong = [p.name for p in packages if not p.name.endswith(f".{expected}.nupkg")]
    if wrong:
        shown = ", ".join(wrong[:5]) + (" ..." if len(wrong) > 5 else "")
        print(
            f"::error title=Tag/version::tag {tag} names version {expected}, but {len(wrong)} of {len(packages)} packages "
            f"have another version ({shown}). Fix VersionPrefix/VersionSuffix in Directory.Build.props or tag the right commit."
        )
        return 1
    print(f"All {len(packages)} packages are version {expected}, as tag {tag} says.")
    return 0


def self_test() -> int:
    cases = [
        # (tag, package file names, expected exit code)
        ("v0.1.0", ["Naravel.Foundation.0.1.0.nupkg", "Naravel.Queue.0.1.0.nupkg", "Naravel.Queue.0.1.0.snupkg"], 0),
        ("v0.1.0-preview", ["Naravel.Foundation.0.1.0-preview.nupkg"], 0),
        ("v0.1.0-preview", ["Naravel.Foundation.0.1.0.nupkg"], 1),
        ("v0.1.0", ["Naravel.Foundation.0.1.0.nupkg", "Naravel.Queue.0.1.0-preview.nupkg"], 1),
        ("v0.1.0", ["Naravel.Foundation.10.1.0.nupkg"], 1),
        ("v0.1.0", [], 1),
        ("0.1.0", ["Naravel.Foundation.0.1.0.nupkg"], 1),
        ("v0.1", ["Naravel.Foundation.0.1.0.nupkg"], 1),
    ]
    failed = 0
    for tag, names, expected in cases:
        with tempfile.TemporaryDirectory() as tmp:
            for name in names:
                (Path(tmp) / name).touch()
            # The failing cases print ::error annotations on purpose; keep them out of the run's annotations.
            with contextlib.redirect_stdout(io.StringIO()):
                actual = check(tag, Path(tmp))
        if actual != expected:
            failed += 1
            print(f"::error title=Tag/version self-test::tag {tag} with {names}: expected exit {expected}, got {actual}")
    if failed:
        return 1
    print(f"check-tag-version self-test: {len(cases)} cases passed.")
    return 0


def main(argv: list[str]) -> int:
    if argv[1:] == ["--self-test"]:
        return self_test()
    if len(argv) != 3:
        print(__doc__, file=sys.stderr)
        return 2
    return check(argv[1], Path(argv[2]))


if __name__ == "__main__":
    sys.exit(main(sys.argv))
