#!/usr/bin/env python3
"""Reports names in the documentation that the source does not define (AGENTS.md hard rule 13: never invent an API, a package or a
configuration key).

Report only: findings are printed (and appended to $GITHUB_STEP_SUMMARY when set) and the exit code stays 0. Pass --strict to make a
finding fail the run. A crash of the script itself always fails, so a broken check is not mistaken for a clean one.

What it compares, and what it cannot see:

* Registration methods. Every `AddSomething(` or `AddSomething<` in README.md, docs/en and docs/nuget must appear in some
  PublicAPI.*.txt baseline, or be listed as `name:` in doc-names.allow (Microsoft, BCL and third-party APIs, or a labelled
  illustration).
* Package ids. Every `dotnet add package Naravel.X` must be the PackageId (or file name) of a project under src/.
* Configuration keys. A key in a ```json block, a `Section:Key` path in backticks or a `Section__Key` environment variable name must
  be read by the source: a string literal passed to an indexer, GetSection or GetValue, a section-name default, or the name of a
  property of an *Options / *Settings class. A segment right after a section whose children the code enumerates (`Stores`,
  `Mailers`, ...) or right after a `<name>` / `*` placeholder is a name the user chooses, so it is not checked.

It checks that a name exists, not what it means: signatures, defaults, units and the order of calls stay with the tests that mirror
the guides (DocsExampleTests). Two limits follow from that. A key is matched by its own name anywhere in the source, not by its
position in the tree, so a real key placed under the wrong section is not caught. A key that the code builds in another way than the
ones above can show up as a false finding; list it as `key:` in doc-names.allow with the reason.
"""

from __future__ import annotations

import json
import os
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
ALLOW = Path(__file__).with_name("doc-names.allow")

DOC_GLOBS = ("README.md", "docs/en/*.md", "docs/nuget/*.md")
FENCE_OPEN = re.compile(r"^[ \t]*(`{3,}|~{3,})[ \t]*([\w-]*)(.*)$")

# --- source side ------------------------------------------------------------------------------------------------------------

LITERAL_READS = (
    re.compile(r'\[\s*"([A-Za-z][\w:]*)"\s*\]'),  # configuration["Key"], section["Key"]
    re.compile(r'GetSection\(\s*"([A-Za-z][\w:]*)"\s*\)'),
    re.compile(r'GetValue<[^>]+>\(\s*"([A-Za-z][\w:]*)"'),
    re.compile(r'\bsectionName\s*=\s*"([A-Za-z]\w*)"', re.IGNORECASE),
    re.compile(r'\bconst\s+string\s+(?:Position|SectionName|\w*Section\w*)\s*=\s*"([A-Za-z][\w:]*)"'),
    re.compile(r'\bGet(?:OrDefault|Required)\(\s*"([A-Za-z]\w*)"'),  # a store's own settings: store.GetOrDefault("Key", ...), store.GetRequired("Key")
    re.compile(r'\bstore\[\s*"([A-Za-z]\w*)"\s*\]'),
)
ENUMERATED = re.compile(r'GetSection\(\s*"(\w+)"\s*\)\s*\.GetChildren\(\)')
PROPERTY = re.compile(r"\bpublic\s+[\w<>\[\]?,. ]+?\s+([A-Z]\w*)\s*\{\s*get;")
DICTIONARY_PROPERTY = re.compile(r"\bpublic\s+(?:I(?:Read[Oo]nly)?)?Dictionary<string,[^>]*>\??\s+([A-Z]\w*)\s*\{")


def source_files() -> list[Path]:
    return [p for p in (ROOT / "src").rglob("*.cs") if not {"bin", "obj"} & set(p.parts)]


def read_source() -> tuple[set[str], set[str]]:
    keys: set[str] = set()
    dynamic: set[str] = set()
    for path in source_files():
        text = path.read_text(encoding="utf-8")
        for pattern in LITERAL_READS:
            for literal in pattern.findall(text):
                keys.update(literal.split(":"))
        dynamic.update(ENUMERATED.findall(text))
        dynamic.update(DICTIONARY_PROPERTY.findall(text))
        if re.search(r"(Options|Settings)", path.name):
            keys.update(PROPERTY.findall(text))
    keys.update(dynamic)
    return keys, dynamic


def read_api_names() -> set[str]:
    names: set[str] = set()
    for path in (ROOT / "src").glob("*/PublicAPI.*.txt"):
        for line in path.read_text(encoding="utf-8").splitlines():
            names.update(re.findall(r"[A-Za-z_]\w*", line))
    return names


def read_package_ids() -> set[str]:
    ids: set[str] = set()
    for project in (ROOT / "src").glob("*/*.csproj"):
        match = re.search(r"<PackageId>(.*?)</PackageId>", project.read_text(encoding="utf-8"))
        ids.add(match.group(1) if match else project.stem)
    return ids


def read_allow() -> tuple[set[str], set[str]]:
    names: set[str] = set()
    keys: set[str] = set()
    if ALLOW.exists():
        for raw in ALLOW.read_text(encoding="utf-8").splitlines():
            entry = raw.split("#", 1)[0].strip()
            if entry.startswith("name:"):
                names.add(entry[5:].strip())
            elif entry.startswith("key:"):
                keys.add(entry[4:].strip())
    return names, keys


# --- documentation side -----------------------------------------------------------------------------------------------------


def doc_files() -> list[Path]:
    found: list[Path] = []
    for pattern in DOC_GLOBS:
        found.extend(sorted(ROOT.glob(pattern)))
    return found


def split_fences(text: str) -> tuple[list[tuple[int, str, str]], list[tuple[int, str]]]:
    """(blocks as (first line number, language, body), prose lines as (line number, text)). CommonMark fences."""
    blocks: list[tuple[int, str, str]] = []
    prose: list[tuple[int, str]] = []
    fence: re.Pattern[str] | None = None
    start = 0
    language = ""
    body: list[str] = []
    for number, line in enumerate(text.splitlines(), start=1):
        if fence is not None:
            if fence.match(line):
                blocks.append((start, language, "\n".join(body)))
                fence = None
            else:
                body.append(line)
            continue
        opener = FENCE_OPEN.match(line)
        if opener and not (opener[1][0] == "`" and "`" in opener[3]):
            fence = re.compile(rf"^[ \t]*{re.escape(opener[1][0])}{{{len(opener[1])},}}[ \t]*$")
            start, language, body = number + 1, opener[2].lower(), []
            continue
        prose.append((number, line))
    return blocks, prose


def parse_json(body: str):
    candidates = [body]
    stripped = re.sub(r"(?m)^\s*//.*$", "", body)
    stripped = re.sub(r"(?m)\s//[^\n\"]*$", "", stripped)
    stripped = re.sub(r"(?m)^\s*\.\.\.\s*,?\s*$", "", stripped)
    stripped = re.sub(r",(\s*[}\]])", r"\1", stripped)
    candidates.append(stripped)
    for candidate in candidates:
        try:
            return json.loads(candidate)
        except ValueError:
            continue
    return None


def walk(value, path: list[str], out: list[list[str]]) -> None:
    if isinstance(value, dict):
        for key, child in value.items():
            out.append([*path, key])
            walk(child, [*path, key], out)
    elif isinstance(value, list):
        for item in value:
            walk(item, path, out)


def is_placeholder(segment: str) -> bool:
    return segment == "*" or (segment.startswith("<") and segment.endswith(">"))


def unknown_segment(segments: list[str], keys: set[str], dynamic: set[str], allowed: set[str]) -> str | None:
    """The first segment of a configuration path that the source does not define, or None. The last segment decides for a JSON path
    (each prefix is checked by its own entry), so only the final one is tested here."""
    last = len(segments) - 1
    if is_placeholder(segments[last]):
        return None
    if last > 0 and (segments[last - 1] in dynamic or is_placeholder(segments[last - 1])):
        return None  # a name the user chooses (a store, a mailer, a server)
    segment = segments[last]
    if segment in keys or ":".join(segments) in allowed or segment in allowed:
        return None
    return segment


def main() -> int:
    strict = "--strict" in sys.argv[1:]
    keys, dynamic = read_source()
    api = read_api_names()
    packages = read_package_ids()
    allow_names, allow_keys = read_allow()

    findings: dict[str, list[tuple[str, int, str]]] = {"method": [], "package": [], "key": [], "root": []}
    seen: set[tuple[str, str, str]] = set()

    def add(kind: str, file: Path, line: int, text: str) -> None:
        identity = (kind, str(file), text)
        if identity not in seen:
            seen.add(identity)
            findings[kind].append((file.relative_to(ROOT).as_posix(), line, text))

    def check_path(file: Path, line: int, segments: list[str]) -> None:
        if not segments:
            return
        if len(segments) == 1:
            if segments[0] not in keys and segments[0] not in allow_keys:
                add("root", file, line, segments[0])
            return
        if segments[0] not in keys:
            return  # not a Naravel section (Logging, ConnectionStrings, ...), reported once as a root
        bad = unknown_segment(segments, keys, dynamic, allow_keys)
        if bad is not None:
            add("key", file, line, ":".join(segments) + f"  ('{bad}' is not read by the source)")

    for file in doc_files():
        text = file.read_text(encoding="utf-8")
        blocks, prose = split_fences(text)

        for number, line in enumerate(text.splitlines(), start=1):
            for name in re.findall(r"\b(Add[A-Z]\w*)\s*[<(]", line):
                if "Xxx" not in name and name not in api and name not in allow_names:
                    add("method", file, number, name)
            for package in re.findall(r"dotnet add package\s+([\w.]+)", line):
                if package.startswith("Naravel.") and package not in packages:
                    add("package", file, number, package)

        for start, language, body in blocks:
            if language in ("json", "jsonc"):
                data = parse_json(body)
                if data is None:
                    continue
                paths: list[list[str]] = []
                walk(data, [], paths)
                lines = body.splitlines()
                for segments in paths:
                    number = start + next((i for i, l in enumerate(lines) if f'"{segments[-1]}"' in l), 0)
                    check_path(file, number, segments)

        for number, line in prose:
            for dotted in re.findall(r"`([A-Z][A-Za-z0-9]*(?::[A-Za-z0-9<>*_.-]+)+)`", line):
                check_path(file, number, dotted.split(":"))
            for env in re.findall(r"\b([A-Z][A-Za-z0-9]*(?:__[A-Za-z0-9]+)+)\b", line):
                check_path(file, number, env.split("__"))

    titles = {
        "method": "Registration methods that no PublicAPI baseline and no allow entry defines",
        "package": "Package ids that no project under src/ defines",
        "key": "Configuration keys that the source does not read",
        "root": "Top-level names in JSON or paths that the source does not read (often not Naravel at all, for example Logging)",
    }
    total = sum(len(v) for k, v in findings.items() if k != "root")
    out = ["## Documentation names that the source does not define", ""]
    out.append(f"{total} finding(s) to check, {len(findings['root'])} top-level name(s) to glance at. Report only unless `--strict` is given.")
    out.append("")
    for kind in ("method", "package", "key", "root"):
        if findings[kind]:
            out.append(f"### {titles[kind]} ({len(findings[kind])})")
            out.extend(f"- `{file}:{line}` {text}" for file, line, text in sorted(findings[kind]))
            out.append("")
    report = "\n".join(out)
    print(report)
    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary:
        with open(summary, "a", encoding="utf-8") as handle:
            handle.write(report + "\n")
    return 1 if strict and total else 0


if __name__ == "__main__":
    sys.exit(main())
