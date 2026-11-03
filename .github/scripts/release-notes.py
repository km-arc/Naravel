#!/usr/bin/env python3
"""Create the GitHub Release for every version tag that has none, with the notes taken from CHANGELOG.md.

Usage:
    release-notes.py [--dry-run] [--tag vX.Y.Z] [--changelog CHANGELOG.md]
    release-notes.py --self-test

Without --tag every `v<major>.<minor>.<patch>[-suffix]` tag of the repository is looked at: a tag that already has a release is
left alone, a tag whose version has no content in the changelog (an old preview tag, say) is skipped with a note, and the rest
get a release. With --tag only that tag is handled, and a missing changelog section is an error, so a release tag can never get an
empty release. Releases are created oldest first, and only the highest stable version is marked "Latest", so filling in an old
version afterwards never takes the badge from the newest one.

The notes are read from the checked-out CHANGELOG.md; when that has no section for the tag the file as it was at the tag is tried.
--dry-run prints what would be created and changes nothing. `gh` needs GH_TOKEN (and GH_REPO outside a clone of the repository).
"""

from __future__ import annotations

import argparse
import os
import re
import subprocess
import sys
import tempfile
from dataclasses import dataclass
from pathlib import Path
from typing import Callable

ROOT = Path(__file__).resolve().parents[2]
PLACEHOLDER = "_Nothing yet._"
VERSION_TAG = re.compile(r"^v(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z.-]+))?$")


def section(text: str, version: str) -> str | None:
    """Return the body of the `## [version]` section, or None when it is missing or empty."""
    lines = text.splitlines()
    heading = re.compile(rf"^## \[{re.escape(version)}\](\s|$)")
    start = next((i for i, line in enumerate(lines) if heading.match(line)), None)
    if start is None:
        return None
    end = next((i for i in range(start + 1, len(lines)) if lines[i].startswith("## ")), len(lines))
    body = "\n".join(lines[start + 1:end]).strip()
    return None if not body or body == PLACEHOLDER else body


def sort_key(tag: str) -> tuple[int, int, int, bool, str]:
    """Version order: a stable release sorts after its own pre-releases (suffixes compare as text, which is enough here)."""
    major, minor, patch, suffix = VERSION_TAG.match(tag).groups()  # type: ignore[union-attr]
    return int(major), int(minor), int(patch), suffix is None, suffix or ""


def is_prerelease(tag: str) -> bool:
    return VERSION_TAG.match(tag).group(4) is not None  # type: ignore[union-attr]


@dataclass(frozen=True)
class Step:
    tag: str
    action: str  # create | exists | skip | error
    detail: str = ""
    notes: str = ""
    latest: bool | None = None  # None for a pre-release, which can never be "Latest"
    prerelease: bool = False


def release_notes(tag: str, body: str) -> str:
    version = tag[1:]
    return (
        f"{body}\n\n---\n\nAll Naravel packages share this version, for example "
        f"[Naravel.Foundation {version}](https://www.nuget.org/packages/Naravel.Foundation/{version}).\n"
    )


def plan(all_tags: list[str], selected: list[str], existing: set[str], body_for: Callable[[str], str | None], explicit: bool) -> list[Step]:
    stable = [t for t in all_tags if not is_prerelease(t)]
    highest = max(stable, key=sort_key) if stable else None
    steps: list[Step] = []
    for tag in sorted(selected, key=sort_key):
        if tag in existing:
            steps.append(Step(tag, "exists", "a release already exists; left as it is"))
            continue
        body = body_for(tag)
        if body is None:
            detail = f"CHANGELOG.md has no section with content for [{tag[1:]}]"
            steps.append(Step(tag, "error" if explicit else "skip", detail))
            continue
        pre = is_prerelease(tag)
        steps.append(Step(tag, "create", notes=release_notes(tag, body), prerelease=pre, latest=None if pre else tag == highest))
    return steps


def create_args(step: Step) -> list[str]:
    args = ["release", "create", step.tag, "--verify-tag", "--title", f"Naravel {step.tag[1:]}"]
    if step.prerelease:
        args.append("--prerelease")
    else:
        args.append("--latest" if step.latest else "--latest=false")
    return args


def run(*command: str) -> subprocess.CompletedProcess[str]:
    return subprocess.run(command, capture_output=True, text=True, cwd=ROOT)


def version_tags() -> list[str]:
    result = run("git", "tag", "--list", "v*.*.*")
    if result.returncode:
        raise SystemExit(f"ERROR: git tag failed: {result.stderr.strip()}")
    return sorted((t for t in result.stdout.split() if VERSION_TAG.match(t)), key=sort_key)


def release_tags() -> set[str]:
    result = run("gh", "release", "list", "--limit", "1000", "--json", "tagName", "--jq", ".[].tagName")
    if result.returncode:
        raise SystemExit(f"ERROR: gh release list failed: {result.stderr.strip()}")
    return set(result.stdout.split())


def changelog_at(tag: str) -> str:
    result = run("git", "show", f"{tag}:CHANGELOG.md")
    return result.stdout if result.returncode == 0 else ""


def report(steps: list[Step], dry_run: bool) -> None:
    path = os.environ.get("GITHUB_STEP_SUMMARY")
    if not path:
        return
    verb = {"create": "would create" if dry_run else "created", "exists": "already exists", "skip": "skipped", "error": "error"}
    with open(path, "a", encoding="utf-8") as out:
        out.write("| Tag | Result | Note |\n| --- | --- | --- |\n")
        for s in steps:
            note = s.detail or ("" if s.latest is None else ("marked Latest" if s.latest else "not Latest"))
            out.write(f"| `{s.tag}` | {verb[s.action]} | {note} |\n")


def self_test() -> int:
    failed = 0
    total = 0

    def check(name: str, actual: object, expected: object) -> None:
        nonlocal failed, total
        total += 1
        if actual != expected:
            print(f"FAIL {name}: expected {expected!r}, got {actual!r}", file=sys.stderr)
            failed += 1

    sample = (
        "# Changelog\n\n## [Unreleased]\n\n_Nothing yet._\n\n"
        "## [0.1.10] - 2026-12-01\n\n### Fixed\n- ten\n\n"
        "## [0.1.1] - 2026-11-02\n\n### Added\n- one\n\n### Fixed\n- two\n\n"
        "## [0.1.0] - 2026-11-01\n\n- zero\n"
    )
    check("exact heading, stops at the next one", section(sample, "0.1.1"), "### Added\n- one\n\n### Fixed\n- two")
    check("0.1.10 is not 0.1.1", section(sample, "0.1.10"), "### Fixed\n- ten")
    check("last section runs to the end", section(sample, "0.1.0"), "- zero")
    check("placeholder is empty", section(sample, "Unreleased"), None)
    check("no such heading", section(sample, "0.2.0"), None)
    check("a prefix is not a version", section(sample, "0.1"), None)
    check("the real changelog still has 0.1.0", bool(section((ROOT / "CHANGELOG.md").read_text(encoding="utf-8"), "0.1.0")), True)

    tags = ["v0.1.0-preview", "v0.1.0", "v0.1.1", "v0.1.10", "v0.2.0-rc.1"]
    check("version order", sorted(reversed(tags), key=sort_key), tags)

    bodies = {"v0.1.0": "zero", "v0.1.1": "one", "v0.2.0-rc.1": "candidate"}

    def body_for(tag: str) -> str | None:
        return bodies.get(tag)

    def shape(steps: list[Step]) -> list[tuple[str, str, bool | None]]:
        return [(s.tag, s.action, s.latest) for s in steps]

    released = ["v0.1.0-preview", "v0.1.0", "v0.1.1"]
    # Nothing released yet: oldest first, the old preview tag is skipped, only the highest stable version is "Latest".
    check("backfill from nothing", shape(plan(released, released, set(), body_for, explicit=False)),
          [("v0.1.0-preview", "skip", None), ("v0.1.0", "create", False), ("v0.1.1", "create", True)])
    # A rerun changes nothing that already has a release.
    check("backfill is idempotent", shape(plan(released, released, {"v0.1.0", "v0.1.1"}, body_for, explicit=False)),
          [("v0.1.0-preview", "skip", None), ("v0.1.0", "exists", None), ("v0.1.1", "exists", None)])
    # Filling in an old version after a newer one exists must not move "Latest".
    check("old version filled in late", shape(plan(released, ["v0.1.0"], {"v0.1.1"}, body_for, explicit=True)),
          [("v0.1.0", "create", False)])
    # A tag asked for by name must have notes.
    check("explicit tag without notes", shape(plan(released, ["v0.1.0-preview"], set(), body_for, explicit=True)),
          [("v0.1.0-preview", "error", None)])
    # A pre-release is flagged as one, is never "Latest", and does not take the badge from the highest stable version.
    pre = plan(released + ["v0.2.0-rc.1"], ["v0.2.0-rc.1"], set(), body_for, explicit=True)
    check("pre-release step", shape(pre), [("v0.2.0-rc.1", "create", None)])
    check("pre-release flag", pre[0].prerelease, True)
    check("pre-release arguments", create_args(pre[0]), ["release", "create", "v0.2.0-rc.1", "--verify-tag", "--title", "Naravel 0.2.0-rc.1", "--prerelease"])

    latest = plan(released, ["v0.1.1"], set(), body_for, explicit=True)[0]
    check("latest arguments", create_args(latest), ["release", "create", "v0.1.1", "--verify-tag", "--title", "Naravel 0.1.1", "--latest"])
    older = plan(released, ["v0.1.0"], set(), body_for, explicit=True)[0]
    check("not-latest arguments", create_args(older)[-1], "--latest=false")
    check("notes link the package page of that version", "https://www.nuget.org/packages/Naravel.Foundation/0.1.1)" in latest.notes, True)

    print(f"release-notes self-test: {total - failed} of {total} checks ok")
    return 1 if failed else 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--dry-run", action="store_true", help="print what would be created and change nothing")
    parser.add_argument("--tag", help="only this tag (for example v0.1.1); its changelog section must exist")
    parser.add_argument("--changelog", type=Path, default=ROOT / "CHANGELOG.md")
    args = parser.parse_args()

    if args.self_test:
        return self_test()

    all_tags = version_tags()
    if args.tag:
        if not VERSION_TAG.match(args.tag):
            print(f"ERROR: not a version tag: {args.tag}", file=sys.stderr)
            return 1
        if args.tag not in all_tags:
            print(f"ERROR: the tag {args.tag} does not exist in this clone", file=sys.stderr)
            return 1
    selected = [args.tag] if args.tag else all_tags

    changelog = args.changelog.read_text(encoding="utf-8")

    def body_for(tag: str) -> str | None:
        return section(changelog, tag[1:]) or section(changelog_at(tag), tag[1:])

    steps = plan(all_tags, selected, release_tags(), body_for, explicit=bool(args.tag))

    failed = 0
    for step in steps:
        if step.action == "create":
            flags = "pre-release" if step.prerelease else ("marked Latest" if step.latest else "not marked Latest")
            if args.dry_run:
                print(f"WOULD CREATE {step.tag} ({flags}) with these notes:")
                print("\n".join(f"    {line}" for line in step.notes.splitlines()))
                continue
            with tempfile.NamedTemporaryFile("w", suffix=".md", delete=False, encoding="utf-8") as handle:
                handle.write(step.notes)
            try:
                result = run("gh", *create_args(step), "--notes-file", handle.name)
            finally:
                os.unlink(handle.name)
            if result.returncode:
                print(f"ERROR: could not create the release for {step.tag}: {result.stderr.strip()}", file=sys.stderr)
                failed += 1
            else:
                print(f"CREATED {step.tag} ({flags}): {result.stdout.strip()}")
        elif step.action == "error":
            print(f"ERROR: {step.tag}: {step.detail}", file=sys.stderr)
            failed += 1
        else:
            print(f"{step.action.upper()} {step.tag}: {step.detail}")

    report(steps, args.dry_run)
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
