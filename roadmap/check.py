#!/usr/bin/env python3
"""Validate roadmap stage files, IDs, dependencies, parity links, and NEXT."""

from __future__ import annotations

import re
import subprocess
import sys
from pathlib import Path, PurePosixPath


ROOT = Path(__file__).resolve().parents[1]
ROADMAP = ROOT / "ROADMAP.md"
MATRIX = ROOT / "roadmap" / "PARITY-MATRIX.md"
STAGES = ROOT / "roadmap" / "stages"
EXPECTED_IDS = {f"R{i:02}" for i in range(19)}


def fail(errors: list[str]) -> int:
    for error in errors:
        print(f"ERROR: {error}")
    return 1 if errors else 0


def main() -> int:
    errors: list[str] = []
    roadmap = ROADMAP.read_text(encoding="utf-8")
    matrix = MATRIX.read_text(encoding="utf-8")

    try:
        tracked_files = subprocess.run(
            ["git", "ls-files"], cwd=ROOT, check=True, capture_output=True, text=True
        ).stdout.splitlines()
    except (OSError, subprocess.CalledProcessError):
        tracked_files = None
        print("NOTICE: git not available or not a repository; tracked-artifact check skipped")
    if tracked_files is not None:
        generated_files = [
            path
            for path in tracked_files
            if {"bin", "obj", "BenchmarkDotNet.Artifacts", ".vscode"} & set(PurePosixPath(path).parts)
        ]
        if generated_files:
            errors.append(
                f"tracked build artifacts found (bin/, obj/, BenchmarkDotNet.Artifacts/, .vscode/): {generated_files}"
            )

    next_match = re.search(r"(?m)^NEXT:\s*(R\d{2})\s*$", roadmap)
    if not next_match:
        errors.append("ROADMAP.md must contain exactly one NEXT: R## pointer.")
        next_id = None
    else:
        next_id = next_match.group(1)
    if len(re.findall(r"(?m)^NEXT:", roadmap)) != 1:
        errors.append("ROADMAP.md must contain one and only one NEXT pointer.")

    rows: dict[str, tuple[set[str], str]] = {}
    for line in roadmap.splitlines():
        if not line.startswith("| R"):
            continue
        cells = [cell.strip() for cell in line.strip().strip("|").split("|")]
        if len(cells) != 5 or not re.fullmatch(r"R\d{2}", cells[0]):
            continue
        stage_id, _, dep_text, _, status = cells
        dependencies = set(re.findall(r"R\d{2}", dep_text))
        if stage_id in rows:
            errors.append(f"duplicate roadmap table row: {stage_id}")
        rows[stage_id] = (dependencies, status)

    if set(rows) != EXPECTED_IDS:
        errors.append(
            f"roadmap table IDs differ: missing={sorted(EXPECTED_IDS - set(rows))}, "
            f"unexpected={sorted(set(rows) - EXPECTED_IDS)}"
        )

    files_by_id: dict[str, list[Path]] = {}
    for path in STAGES.glob("R??-*.md"):
        match = re.match(r"(R\d{2})-", path.name)
        if match:
            files_by_id.setdefault(match.group(1), []).append(path)
    for stage_id in sorted(EXPECTED_IDS):
        files = files_by_id.get(stage_id, [])
        if len(files) != 1:
            errors.append(f"{stage_id} must have exactly one stage file; found {len(files)}")
            continue
        content = files[0].read_text(encoding="utf-8")
        if not re.search(rf"(?m)^# {stage_id}\b", content):
            errors.append(f"{files[0].relative_to(ROOT)} must have a matching {stage_id} heading")
        parity = re.search(r"(?m)^Parity section:\s*`(R\d{2})`", content)
        if not parity or parity.group(1) != stage_id:
            errors.append(f"{files[0].relative_to(ROOT)} must name its own parity section")
        elif not re.search(rf"(?m)^## {stage_id}\b", matrix):
            errors.append(f"PARITY-MATRIX.md is missing section {stage_id}")

    for stage_id, (dependencies, _) in rows.items():
        unknown = dependencies - set(rows)
        if unknown:
            errors.append(f"{stage_id} has unknown dependencies: {sorted(unknown)}")

    visiting: set[str] = set()
    visited: set[str] = set()

    def visit(stage_id: str) -> None:
        if stage_id in visiting:
            errors.append(f"dependency cycle includes {stage_id}")
            return
        if stage_id in visited:
            return
        visiting.add(stage_id)
        for dependency in rows.get(stage_id, (set(), ""))[0]:
            if dependency in rows:
                visit(dependency)
        visiting.remove(stage_id)
        visited.add(stage_id)

    for stage_id in rows:
        visit(stage_id)

    if next_id and next_id not in rows:
        errors.append(f"NEXT points to unknown stage {next_id}")
    elif next_id and rows[next_id][1].startswith("DONE"):
        errors.append(f"NEXT points to completed stage {next_id}")
    elif next_id and rows[next_id][1].startswith("BLOCKED"):
        reason = rows[next_id][1]
        if re.search(r"(?i)awaiting (PDR|GO) approval", reason):
            # Allowed by the PDR-batching rule in ROADMAP.md: informational only.
            print(f"NOTICE: NEXT={next_id} is {reason}: owner approval needed")
        else:
            errors.append(f"NEXT points to BLOCKED stage {next_id} without 'awaiting PDR/GO approval': {reason}")

    count_ratio = re.compile(r"(?<![\d/.])\d+\s*/\s*\d+(?![\d/])")
    count_tests = re.compile(r"(?i)\b\d+\s+tests?\b")
    for doc in (ROOT / "README.md", ROOT / "README.fa.md", ROOT / "AGENTS.md"):
        if not doc.exists():
            continue
        for number, line in enumerate(doc.read_text(encoding="utf-8").splitlines(), 1):
            if "check:allow-count" in line:
                continue
            mentions = re.search(r"(?i)test|passed|\u062a\u0633\u062a", line)
            if mentions and (count_ratio.search(line) or count_tests.search(line)):
                errors.append(
                    f"{doc.relative_to(ROOT)}:{number} contains a hand-typed test count; link to CI instead "
                    "(or add <!-- check:allow-count -->)"
                )

    # --- REVIEW-01 additions -------------------------------------------------
    satisfied = ("DONE", "DECLINED")
    if next_id in rows:
        open_deps = [d for d in rows[next_id][0] if not rows[d][1].startswith(satisfied)]
        if open_deps:
            errors.append(f"NEXT={next_id} has unfinished dependencies: {sorted(open_deps)}")
    for stage_id, (_, status) in rows.items():
        files = files_by_id.get(stage_id, [])
        if len(files) != 1:
            continue
        text = files[0].read_text(encoding="utf-8")
        unchecked = len(re.findall(r"(?m)^- \[ \]", text))
        if status.startswith("DONE") and unchecked:
            errors.append(f"{stage_id} is DONE but its stage file has {unchecked} unchecked task(s)")
        if status.startswith("TODO") and not unchecked:
            errors.append(f"{stage_id} is TODO but its stage file has no unchecked task")
        if not status.startswith(("TODO", "DOING", "DONE", "BLOCKED", "DECLINED")):
            errors.append(f"{stage_id} has invalid status '{status}'")

    slnx = (ROOT / "Naravel.slnx").read_text(encoding="utf-8").replace("\\", "/")
    for proj in list(ROOT.glob("src/**/*.csproj")) + list(ROOT.glob("tests/**/*.csproj")) + list(ROOT.glob("samples/**/*.csproj")):
        if proj.relative_to(ROOT).as_posix() not in slnx:
            errors.append(f"{proj.relative_to(ROOT).as_posix()} is not in Naravel.slnx")

    for kind in ("docs", "docs/pdr"):
        en = {p.name for p in (ROOT / kind / "en").glob("*.md")}
        fa = {p.name for p in (ROOT / kind / "fa").glob("*.md")}
        if en != fa:
            errors.append(f"{kind}: EN/FA file mismatch: {sorted(en ^ fa)}")

    fa_text = (ROOT / "ROADMAP.fa.md").read_text(encoding="utf-8")
    fa_next = re.search(r"(?m)^NEXT:\s*(R\d{2})\s*$", fa_text)
    if not fa_next or fa_next.group(1) != next_id:
        errors.append("ROADMAP.fa.md NEXT pointer differs from ROADMAP.md")

    # stage-file structure (STAGE-TEMPLATE.md)
    for stage_id, (_, status) in rows.items():
        files = files_by_id.get(stage_id, [])
        if len(files) != 1:
            continue
        text = files[0].read_text(encoding="utf-8")
        for key in ("Gate:", "Read:", "Touch:", "Out of scope:"):
            if not re.search(rf"(?m)^{re.escape(key)}", text):
                errors.append(f"{files[0].name} is missing a '{key}' line")
        if not status.startswith(("DONE", "DECLINED")) and stage_id != "R02":
            for line in re.findall(r"(?m)^- \[[ x]\] .*(?:\n  .*)*", text):
                if "**Accept:**" not in line:
                    errors.append(f"{files[0].name}: task without **Accept:** -> {line[:50]!r}")
            for tid in re.findall(r"\*\*(R\d{2}\.T\d{2})", text):
                if not tid.startswith(stage_id + "."):
                    errors.append(f"{files[0].name}: task id {tid} does not belong to {stage_id}")

    # default order must list each stage once and respect dependencies
    order_m = re.search(r"\*\*Default order:\*\*(.*?)\n\n", roadmap, re.S)
    if not order_m:
        errors.append("ROADMAP.md must contain a '**Default order:**' paragraph")
    else:
        first_line = order_m.group(1).split("Rationale")[0]
        order = re.findall(r"R\d{2}", first_line)
        pending = {sid for sid, (_, st) in rows.items() if not st.startswith("DONE")}
        if sorted(order) != sorted(pending):
            errors.append(f"Default order must list every non-DONE stage exactly once; diff={sorted(set(order) ^ pending)}")
        else:
            pos = {sid: i for i, sid in enumerate(order)}
            for sid, (deps, _) in rows.items():
                for d in deps:
                    if sid in pos and d in pos and pos[d] > pos[sid]:
                        errors.append(f"Default order puts {sid} before its dependency {d}")

    for od in range(1, 10):
        if f"OD-0{od}" not in roadmap:
            errors.append(f"ROADMAP.md is missing decision OD-0{od}")

    if errors:
        return fail(errors)
    print(f"Roadmap valid: {len(rows)} stages, {len(files_by_id)} stage files, NEXT={next_id}.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
