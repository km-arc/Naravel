#!/usr/bin/env python3
"""Fail when the NuGet publish job can run without a check that ci-ok requires.

ci-ok is the one check branch protection needs; the publish job runs on version tags, where no branch protection applies, so it
must wait for the same jobs itself (the service image pins included) and for ci-ok.
"""
import re
import sys
from pathlib import Path

text = Path(".github/workflows/ci.yml").read_text(encoding="utf8")


def needs_of(job: str) -> set[str]:
    """The `needs: [a, b]` list of a top-level job, read from its block (the job id sits at two spaces of indentation)."""
    block = re.search(rf"^  {re.escape(job)}:\n(.*?)(?=^  [\w-]+:\n|\Z)", text, re.M | re.S)
    if not block:
        print(f"::error title=Publish gates::job '{job}' was not found in ci.yml")
        sys.exit(1)
    match = re.search(r"^    needs:\s*(\[[^\]]*\]|[\w-]+)\s*$", block.group(1), re.M)
    if not match:
        print(f"::error title=Publish gates::job '{job}' has no 'needs:' list")
        sys.exit(1)
    return {item.strip() for item in match.group(1).strip("[]").split(",") if item.strip()}


required = needs_of("ci-ok")
publish = needs_of("publish")
missing = sorted((required | {"ci-ok"}) - publish)
print(f"ci-ok requires: {', '.join(sorted(required))}")
print(f"publish waits for: {', '.join(sorted(publish))}")
if missing:
    print(f"::error title=Publish gates::the publish job does not wait for: {', '.join(missing)}")
    sys.exit(1)
print("publish waits for every job ci-ok requires, and for ci-ok itself")
