#!/usr/bin/env python3
"""Require the cross-instance Redis tests to execute and pass in Provider integration CI.

ServiceFact tests are reported as skipped (not failed) when their service environment variable is
missing. A green dotnet test exit code alone therefore does not prove that these integration tests ran.
"""
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

REQUIRED = {
    "QueueCoordination/QueueCoordination.trx": (
        "Redis_lock_stops_two_workers_from_running_the_same_key_at_once",
        "Redis_gate_rejects_a_duplicate_dispatched_from_another_worker",
    ),
    "Scheduling/Scheduling.trx": (
        "Two_instances_over_one_Redis_skip_the_overlap_and_the_running_instance_keeps_the_lock_through_a_renewal",
        "A_lock_that_is_cleared_in_Redis_is_lost_at_the_next_renewal_and_the_running_task_is_cancelled",
    ),
}


def local(tag):
    return tag.rsplit("}", 1)[-1]


def validate(results_dir):
    errors = []
    for relative_path, required_names in REQUIRED.items():
        path = results_dir / relative_path
        if not path.is_file():
            errors.append(f"{relative_path}: TRX file is missing; the test project did not produce results")
            continue

        try:
            root = ET.parse(path).getroot()
        except ET.ParseError as exc:
            errors.append(f"{relative_path}: invalid TRX XML: {exc}")
            continue

        results = [
            node
            for node in root.iter()
            if local(node.tag) == "UnitTestResult"
        ]
        for required_name in required_names:
            matches = [
                node for node in results
                if required_name in node.attrib.get("testName", "")
            ]
            if not matches:
                errors.append(f"{relative_path}: required Redis test was not discovered: {required_name}")
                continue
            if len(matches) != 1:
                errors.append(f"{relative_path}: expected one result for {required_name}, found {len(matches)}")
                continue

            result = matches[0]
            outcome = result.attrib.get("outcome", "")
            if outcome != "Passed":
                reason = next(
                    (
                        child.text.strip()
                        for child in result.iter()
                        if local(child.tag) == "Message" and child.text and child.text.strip()
                    ),
                    "",
                )
                suffix = f" ({reason})" if reason else ""
                errors.append(f"{relative_path}: {required_name} was {outcome or 'missing an outcome'}{suffix}")

    return errors


def main():
    if len(sys.argv) != 2:
        print(f"usage: {Path(sys.argv[0]).name} <provider-trx-directory>", file=sys.stderr)
        return 2

    errors = validate(Path(sys.argv[1]))
    if errors:
        for error in errors:
            print(f"::error title=Required Redis integration test::{error}")
        return 1

    required_count = sum(len(names) for names in REQUIRED.values())
    print(f"All {required_count} required cross-instance Redis integration tests passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
