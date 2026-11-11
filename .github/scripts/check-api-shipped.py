#!/usr/bin/env python3
"""Fail while any project's PublicAPI.Unshipped.txt still lists an entry.

Usage:
  check-api-shipped.py [--root <repository root>]
  check-api-shipped.py --self-test

PublicAPI.Unshipped.txt lists the public API that no published version contains yet; PublicAPI.Shipped.txt lists what a
published version contains. A release moves the first into the second (AGENTS.md, hard rule 12). A version pushed with entries
still in an Unshipped file would publish API that the baseline calls unreleased, and the next change to it would not be seen as
a breaking change. NuGet cannot edit a version once it is pushed, so the check runs before the push.

An entry is any line that is not blank and is not the ``#nullable enable`` header, so a pending ``*REMOVED*`` line counts too.
The check names each project that still has entries, so a release commit that forgot one is easy to find. A repository without
any PublicAPI.Unshipped.txt under ``src`` also fails: finding nothing is more likely a wrong path than a clean baseline.

CI runs this on the Ubuntu leg of the ``fast`` job only for version tags (``refs/tags/v*``), and the publish job waits for that
job. On pull requests and on main the files are not empty (they hold the work of the next release), so the check does not run
there; the ``pins`` job runs ``--self-test`` on every pull request.
"""
import argparse
import contextlib
import io
import sys
import tempfile
from pathlib import Path

HEADER = "#nullable enable"
FILE_NAME = "PublicAPI.Unshipped.txt"


def entries(path: Path) -> list[str]:
    """The lines of one Unshipped file that are API entries (not blank, not the header)."""
    # utf-8-sig drops a byte order mark; strip() drops CRLF and padding.
    lines = (line.strip() for line in path.read_text(encoding="utf-8-sig").splitlines())
    return [line for line in lines if line and line != HEADER]


def unshipped_files(root: Path) -> list[Path]:
    """Every Unshipped file under src, leaving out build output (bin, obj)."""
    found = (root / "src").rglob(FILE_NAME)
    return sorted(p for p in found if not {"bin", "obj"} & set(p.relative_to(root).parts))


def check(root: Path) -> int:
    files = unshipped_files(root)
    if not files:
        print(f"::error title=API shipped::no {FILE_NAME} found under {root / 'src'}; run this from the repository root or pass --root")
        return 1
    failed = 0
    for path in files:
        found = entries(path)
        if not found:
            continue
        failed += 1
        relative = path.relative_to(root).as_posix()
        project = path.parent.name
        print(
            f"::error file={relative},title=API shipped::{project} still lists {len(found)} unreleased API "
            f"{'entry' if len(found) == 1 else 'entries'} in {FILE_NAME}. Move them into PublicAPI.Shipped.txt in the release "
            f"commit, or tag a commit that has done so."
        )
    if failed:
        print(f"{failed} of {len(files)} projects have API that is not marked as shipped; nothing may be published from this tag.")
        return 1
    print(f"All {len(files)} {FILE_NAME} files are empty: the API baseline matches what this tag publishes.")
    return 0


def self_test() -> int:
    # Each case: (name, {relative path: file bytes}, expected exit code, project names that must be named, names that must not).
    header = b"#nullable enable\n"
    entry = b"Naravel.Thing.Run() -> void\n"
    cases = [
        ("clean", {"src/A/" + FILE_NAME: header, "src/B/" + FILE_NAME: header}, 0, [], ["A", "B"]),
        ("one project with an entry", {"src/A/" + FILE_NAME: header, "src/B/" + FILE_NAME: header + entry}, 1, ["B"], ["A"]),
        ("two projects are both named", {"src/A/" + FILE_NAME: header + entry, "src/B/" + FILE_NAME: header + entry}, 1, ["A", "B"], []),
        ("header only with a byte order mark, CRLF and blank lines",
         {"src/A/" + FILE_NAME: b"\xef\xbb\xbf#nullable enable\r\n\r\n  \r\n"}, 0, [], ["A"]),
        ("an empty file", {"src/A/" + FILE_NAME: b""}, 0, [], ["A"]),
        ("a pending removal counts", {"src/A/" + FILE_NAME: header + b"*REMOVED*Naravel.Thing.Old() -> void\n"}, 1, ["A"], []),
        ("an entry with CRLF and padding counts", {"src/A/" + FILE_NAME: b"#nullable enable\r\n  Naravel.Thing.Run() -> void  \r\n"}, 1, ["A"], []),
        ("a file nested below the project is found", {"src/Group/A/" + FILE_NAME: header + entry}, 1, ["A"], []),
        ("build output is ignored", {"src/A/" + FILE_NAME: header, "src/A/obj/" + FILE_NAME: header + entry, "src/A/bin/" + FILE_NAME: header + entry}, 0, [], ["obj", "bin"]),
        ("a file outside src is ignored", {"src/A/" + FILE_NAME: header, "tests/T/" + FILE_NAME: header + entry}, 0, [], ["T"]),
        ("no file at all fails", {"src/A/readme.txt": b"x"}, 1, [], []),
    ]
    failed = 0
    for name, files, expected, named, not_named in cases:
        with tempfile.TemporaryDirectory() as tmp:
            for relative, content in files.items():
                target = Path(tmp) / relative
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(content)
            # The failing cases print ::error annotations on purpose; keep them out of the run's annotations.
            out = io.StringIO()
            with contextlib.redirect_stdout(out):
                actual = check(Path(tmp))
        text = out.getvalue()
        # A project is named in the annotation as "<name> still lists"; "file=src/<name>/" would also match, so look for the sentence.
        problems = []
        if actual != expected:
            problems.append(f"expected exit {expected}, got {actual}")
        problems += [f"{p} should be named" for p in named if f"{p} still lists" not in text]
        problems += [f"{p} should not be named" for p in not_named if f"{p} still lists" in text or f"/{p}/" in text]
        if problems:
            failed += 1
            print(f"::error title=API shipped self-test::{name}: {'; '.join(problems)}")
    if failed:
        return 1
    print(f"check-api-shipped self-test: {len(cases)} cases passed.")
    return 0


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description="Fail while a PublicAPI.Unshipped.txt still lists an entry.")
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[2], help="repository root (default: this script's repository)")
    parser.add_argument("--self-test", action="store_true", help="run the built-in cases instead of checking a repository")
    args = parser.parse_args(argv[1:])
    return self_test() if args.self_test else check(args.root)


if __name__ == "__main__":
    sys.exit(main(sys.argv))
