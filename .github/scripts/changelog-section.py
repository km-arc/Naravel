#!/usr/bin/env python3
"""Print the body of one version's section of CHANGELOG.md, for the GitHub release notes.

Usage:
    changelog-section.py <version> [CHANGELOG.md]    e.g. changelog-section.py 0.1.1
    changelog-section.py --self-test

The section starts at the heading `## [<version>]` and ends at the next `## ` heading or the end of the file. The command
fails (exit 1) when the heading is missing or the section has no content, so a tag cannot get an empty release.
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PLACEHOLDER = "_Nothing yet._"


def section(text: str, version: str) -> str | None:
    """Return the section body, or None when the heading is missing or the body is empty."""
    lines = text.splitlines()
    heading = re.compile(rf"^## \[{re.escape(version)}\](\s|$)")
    start = next((i for i, line in enumerate(lines) if heading.match(line)), None)
    if start is None:
        return None
    end = next((i for i in range(start + 1, len(lines)) if lines[i].startswith("## ")), len(lines))
    body = "\n".join(lines[start + 1:end]).strip()
    return None if not body or body == PLACEHOLDER else body


def self_test() -> int:
    sample = (
        "# Changelog\n\n## [Unreleased]\n\n_Nothing yet._\n\n"
        "## [0.1.10] - 2026-12-01\n\n### Fixed\n- ten\n\n"
        "## [0.1.1] - 2026-11-02\n\n### Added\n- one\n\n### Fixed\n- two\n\n"
        "## [0.1.0] - 2026-11-01\n\n- zero\n"
    )
    cases = [
        ("0.1.1", "### Added\n- one\n\n### Fixed\n- two"),   # exact match, stops at the next heading
        ("0.1.10", "### Fixed\n- ten"),                       # 0.1.1 must not match 0.1.10 and vice versa
        ("0.1.0", "- zero"),                                  # last section runs to the end of the file
        ("Unreleased", None),                                 # placeholder only
        ("0.2.0", None),                                      # no such heading
        ("0.1", None),                                        # prefix of a version is not a version
    ]
    failed = 0
    for version, expected in cases:
        actual = section(sample, version)
        if actual != expected:
            print(f"FAIL {version}: expected {expected!r}, got {actual!r}", file=sys.stderr)
            failed += 1
    real = section((ROOT / "CHANGELOG.md").read_text(encoding="utf-8"), "0.1.0")
    if not real:
        print("FAIL: the real CHANGELOG.md has no content for 0.1.0", file=sys.stderr)
        failed += 1
    print(f"changelog-section self-test: {len(cases) + 1 - failed} of {len(cases) + 1} cases ok")
    return 1 if failed else 0


def main() -> int:
    args = sys.argv[1:]
    if args == ["--self-test"]:
        return self_test()
    if len(args) not in (1, 2):
        print(__doc__, file=sys.stderr)
        return 1
    version = args[0].removeprefix("v")
    path = Path(args[1]) if len(args) == 2 else ROOT / "CHANGELOG.md"
    body = section(path.read_text(encoding="utf-8"), version)
    if body is None:
        print(f"ERROR: {path} has no section with content for [{version}]", file=sys.stderr)
        return 1
    print(body)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
