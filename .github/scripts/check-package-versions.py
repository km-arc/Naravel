#!/usr/bin/env python3
"""Enforce one lock-step NuGet version and central package management for the repository."""

from __future__ import annotations

import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OVERRIDE_PROPERTIES = {"Version", "VersionPrefix", "VersionSuffix", "PackageVersion", "VersionOverride"}


def properties(root: ET.Element, names: set[str]) -> list[tuple[str, str]]:
    values: list[tuple[str, str]] = []
    for group in root.findall("PropertyGroup"):
        for child in group:
            if child.tag in names:
                values.append((child.tag, (child.text or "").strip()))
    return values


def main() -> int:
    errors: list[str] = []
    build_props_path = ROOT / "Directory.Build.props"
    packages_props_path = ROOT / "Directory.Packages.props"
    build_props = ET.parse(build_props_path).getroot()
    packages_props = ET.parse(packages_props_path).getroot()

    shared_prefixes = [value for name, value in properties(build_props, {"VersionPrefix"}) if name == "VersionPrefix"]
    if len(shared_prefixes) != 1 or not shared_prefixes[0]:
        errors.append("Directory.Build.props must define exactly one non-empty VersionPrefix")
        expected_version = None
    else:
        expected_version = shared_prefixes[0]

    forbidden_global = [(name, value) for name, value in properties(build_props, {"Version", "VersionSuffix", "PackageVersion", "VersionOverride"}) if value]
    if forbidden_global:
        errors.append(f"Directory.Build.props contains competing version properties: {forbidden_global}")

    manage_centrally = [
        (child.text or "").strip().lower()
        for group in packages_props.findall("PropertyGroup")
        for child in group
        if child.tag == "ManagePackageVersionsCentrally"
    ]
    if manage_centrally != ["true"]:
        errors.append("Directory.Packages.props must set ManagePackageVersionsCentrally=true exactly once")

    central_versions: dict[str, str] = {}
    for item in packages_props.iter("PackageVersion"):
        package_id = item.attrib.get("Include") or item.attrib.get("Update")
        version = item.attrib.get("Version") or next(
            ((child.text or "").strip() for child in item if child.tag == "Version"),
            "",
        )
        if not package_id:
            errors.append("Directory.Packages.props contains PackageVersion without Include/Update")
            continue
        key = package_id.casefold()
        if key in central_versions:
            errors.append(f"duplicate central package version for {package_id}")
        central_versions[key] = version
        if not version:
            errors.append(f"central package {package_id} has no version")

    project_files = sorted(ROOT.rglob("*.csproj"))
    source_projects = [path for path in project_files if path.is_relative_to(ROOT / "src")]
    package_references = 0
    for project in project_files:
        relative = project.relative_to(ROOT).as_posix()
        try:
            document = ET.parse(project).getroot()
        except ET.ParseError as error:
            errors.append(f"{relative}: invalid project XML: {error}")
            continue

        if project in source_projects:
            overrides = [(name, value) for name, value in properties(document, OVERRIDE_PROPERTIES) if value]
            if overrides:
                errors.append(f"{relative}: project-level version overrides violate lock-step versioning: {overrides}")

        for reference in document.iter("PackageReference"):
            package_references += 1
            package_id = reference.attrib.get("Include") or reference.attrib.get("Update")
            attributes = {key.casefold(): value for key, value in reference.attrib.items()}
            inline_version = attributes.get("version") or attributes.get("versionoverride")
            nested_version = next(
                ((child.text or "").strip() for child in reference if child.tag in {"Version", "VersionOverride"}),
                "",
            )
            if inline_version or nested_version:
                errors.append(f"{relative}: PackageReference must not pin a version inline ({package_id})")
            if package_id and package_id.startswith("$"):
                continue
            if package_id and package_id.casefold() not in central_versions:
                errors.append(f"{relative}: package {package_id} is missing from Directory.Packages.props")

    print(
        f"checked {len(source_projects)} source projects, {len(project_files)} project files, "
        f"{package_references} package references and {len(central_versions)} central package pins; "
        f"shared VersionPrefix={expected_version or '(invalid)'}"
    )
    if errors:
        for error in errors:
            print(f"::error title=Package version consistency::{error}")
        return 1
    print("Package version configuration is consistent.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
