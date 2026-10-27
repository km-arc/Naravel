#!/usr/bin/env python3
"""Markdown report of executed and skipped tests from TRX files, for a job summary and a PR check annotation.

Usage: test-report.py <title> <trx-directory>
Writes markdown to stdout (append it to $GITHUB_STEP_SUMMARY) and one ::notice line to stderr so the totals show in the checks list.
A skipped test is listed with its reason, because "skipped" is not "passed" and a reader must be able to tell what was NOT RUN.
"""
import sys
import xml.etree.ElementTree as ET
from collections import Counter
from pathlib import Path

title, directory = sys.argv[1], Path(sys.argv[2])


def local(tag):
    return tag.rsplit("}", 1)[-1]


totals = Counter()
skipped = []
failed = []
for trx in sorted(directory.rglob("*.trx")):
    root = ET.parse(trx).getroot()
    for node in root.iter():
        if local(node.tag) != "UnitTestResult":
            continue
        outcome = node.attrib.get("outcome", "")
        name = node.attrib.get("testName", "?")
        totals[outcome] += 1
        if outcome == "Failed":
            failed.append(name)
        elif outcome in ("NotExecuted", "Skipped"):
            reason = next((n.text or "" for n in node.iter() if local(n.tag) == "Message"), "").strip()
            skipped.append((name, reason or "no reason recorded"))

executed = totals["Passed"] + totals["Failed"]
print(f"### {title}")
print(f"- Executed: **{executed}** (passed {totals['Passed']}, failed {totals['Failed']})")
print(f"- Skipped / NOT RUN: **{len(skipped)}**")
if not totals:
    print("- No TRX results found: nothing was executed.")
if failed:
    print("\n**Failed**\n")
    for name in failed[:50]:
        print(f"- `{name}`")
if skipped:
    reasons = Counter(reason for _, reason in skipped)
    print("\n**Skipped, grouped by reason**\n")
    print("| Reason | Tests |")
    print("| --- | ---: |")
    for reason, count in reasons.most_common():
        print(f"| {reason.replace('|', '/')[:200]} | {count} |")
print()
print(f"::notice title=Tests: {title}::executed {executed}, failed {totals['Failed']}, skipped {len(skipped)}", file=sys.stderr)
