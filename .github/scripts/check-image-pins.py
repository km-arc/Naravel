#!/usr/bin/env python3
"""Fail when a CI service image uses a floating tag (latest, major-only, or a minor line that keeps moving).

CI must be reproducible: a database or broker that silently changes version can turn a green main red with no code change.
Pin to an exact release and bump it deliberately in a reviewed change.
"""
import re
import sys
from pathlib import Path

RULES = {  # image -> regex the tag must match
    r"redis": r"\d+\.\d+\.\d+",
    r"rabbitmq": r"\d+\.\d+\.\d+",
    r"memcached": r"\d+\.\d+\.\d+",
    r"apache/kafka": r"\d+\.\d+\.\d+",
    r"adobe/s3mock": r"\d+\.\d+\.\d+",
    r"mysql": r"\d+\.\d+\.\d+",
    r"postgres": r"\d+\.\d+(\.\d+)?(-alpine(\d+\.\d+)?)?",
    r"mcr\.microsoft\.com/mssql/server": r"\d{4}-CU\d+(-ubuntu-\d+\.\d+)?",
}
# The CI workflow and the compose file developers use locally ("same images as the database job") are both checked.
FILES = [".github/workflows/ci.yml", "docker-compose.yml"]
bad = []
seen = 0
for file in FILES:
    for line_no, line in enumerate(Path(file).read_text(encoding="utf8").splitlines(), 1):
        for image, tag_rule in RULES.items():
            for match in re.finditer(rf"(?<![\w/.-]){image}:([\w.-]+)", line):
                seen += 1
                tag = match.group(1)
                if not re.fullmatch(tag_rule, tag):
                    bad.append(f"{file} line {line_no}: {image}:{tag} is not an exact version")
print(f"checked {seen} service image reference(s)")
if bad:
    for item in bad:
        print(f"::error title=Floating CI image tag::{item}")
    sys.exit(1)
