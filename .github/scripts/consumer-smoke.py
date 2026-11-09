#!/usr/bin/env python3
"""Install the Naravel packages the way a user does, then build and run against them.

Every test project in this repository references Naravel through ProjectReference, so nothing in CI ever consumes a packed
.nupkg: a missing dependency in a nuspec, a framework reference that does not flow, a version range that does not resolve or an
assembly that does not load would only show up after a version is on nuget.org, where it cannot be edited. This script builds a
consumer outside the repository (so Directory.Build.props and central package management do not apply) in two steps:

1. install-all: one project that references every packable package under src/ at one exact version, restores and builds it, then
   loads every Naravel assembly and its public types. This finds packages that do not install together (a downgrade, a violated
   dependency constraint), a missing runtime dependency and a framework reference that does not flow.
2. run: the program of samples/Naravel.AotSmoke (its runtime checks across Cache, Events, Mail, Queue, Encryption, Filesystem,
   Scheduling, Pagination and Database) restored from the packages instead of ProjectReference, run on the JIT.

Usage:
  consumer-smoke.py --local <directory-containing-nupkgs>   packages from `dotnet pack` (CI, before anything is pushed)
  consumer-smoke.py --nuget <version>                       the published packages on nuget.org (after a release)
  consumer-smoke.py --nuget <version> --exclude <id>        the same, without a package that is not part of that release
  consumer-smoke.py --self-test                             the script's own parsing checks, no dotnet needed

With --local, Naravel.* is mapped to that directory only and every other package comes from nuget.org, so a Naravel package
already published under the same version cannot stand in for the one that was just packed. The global packages folder is a fresh
directory, so nothing cached on the machine can hide a missing package.
"""

from __future__ import annotations

import argparse
import os
import re
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SMOKE_SAMPLE = ROOT / "samples" / "Naravel.AotSmoke"
NUGET_ORG = "https://api.nuget.org/v3/index.json"
TIMEOUT_RESTORE_BUILD = 900
TIMEOUT_RUN = 180


def package_ids() -> list[str]:
    """Every packable project under src/, by package id (PackageId when set, else the project name)."""
    ids = []
    for csproj in sorted((ROOT / "src").glob("*/*.csproj")):
        tree = ET.parse(csproj).getroot()
        ids.append(next((n.text.strip() for n in tree.iter("PackageId") if n.text), csproj.stem))
    return ids


def without_excluded(ids: list[str], excluded: list[str]) -> list[str]:
    """The ids that remain when the named ones are left out; exits when a name is not a packable project under src/.

    A published version has only the packages that existed when it was released, while src/ lists today's. Leaving a package out is
    therefore something the person who starts the run names, and the run says so, so that a package that a release forgot to
    publish is never skipped without notice."""
    unknown = sorted(set(excluded) - set(ids))
    if unknown:
        raise SystemExit(f"ERROR: --exclude names packages that are not packable projects under src/: {unknown}")
    return [package_id for package_id in ids if package_id not in set(excluded)]


def central_version(name: str) -> str:
    tree = ET.parse(ROOT / "Directory.Packages.props").getroot()
    for node in tree.iter("PackageVersion"):
        if node.get("Include") == name:
            return node.get("Version", "")
    raise SystemExit(f"ERROR: {name} has no PackageVersion in Directory.Packages.props")


def smoke_references() -> tuple[list[str], list[tuple[str, str]]]:
    """(Naravel package ids, (other package id, central version)) the smoke sample references."""
    tree = ET.parse(SMOKE_SAMPLE / "Naravel.AotSmoke.csproj").getroot()
    naravel = [Path(n.get("Include", "").replace("\\", "/")).stem for n in tree.iter("ProjectReference")]
    others = [(n.get("Include", ""), central_version(n.get("Include", ""))) for n in tree.iter("PackageReference")]
    return naravel, others


def packed_version(directory: Path, ids: list[str]) -> str:
    """The one version every id has in the directory; exits when an id is missing, doubled or at a different version."""
    versions: dict[str, str] = {}
    problems = []
    for package_id in ids:
        # "Naravel.Queue" must not claim "Naravel.Queue.Redis.0.1.1.nupkg": a version has to follow the id.
        pattern = re.compile(rf"^{re.escape(package_id)}\.(\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?)\.nupkg$")
        found = [m.group(1) for p in directory.glob("*.nupkg") if (m := pattern.match(p.name))]
        if len(found) != 1:
            problems.append(f"expected exactly one {package_id} .nupkg in {directory}, found {len(found)}")
        else:
            versions[package_id] = found[0]
    if len(set(versions.values())) > 1:
        problems.append(f"the packages are not all at one version: {sorted(set(versions.values()))}")
    if problems:
        for problem in problems:
            print(f"ERROR: {problem}", file=sys.stderr)
        raise SystemExit(1)
    return next(iter(versions.values()))


def nuget_config(local_directory: Path | None) -> str:
    if local_directory is None:
        return f"""<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="{NUGET_ORG}" />
  </packageSources>
</configuration>
"""
    return f"""<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="packed" value="{local_directory.resolve()}" />
    <add key="nuget.org" value="{NUGET_ORG}" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="packed"><package pattern="Naravel.*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
"""


def project_file(exact: list[tuple[str, str]], minimum: list[tuple[str, str]] = (), extra_properties: str = "") -> str:
    """exact: the Naravel packages, pinned to the one version under test; minimum: anything else, as a user would write it."""
    items = "\n".join(
        [f'    <PackageReference Include="{name}" Version="[{version}]" />' for name, version in exact]
        + [f'    <PackageReference Include="{name}" Version="{version}" />' for name, version in minimum])
    return f"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <InvariantGlobalization>true</InvariantGlobalization>
    <!-- A downgrade or a violated dependency constraint means the packages do not install together. -->
    <WarningsAsErrors>$(WarningsAsErrors);NU1605;NU1608</WarningsAsErrors>{extra_properties}
  </PropertyGroup>
  <ItemGroup>
{items}
  </ItemGroup>
</Project>
"""


def install_program(ids: list[str]) -> str:
    names = ",\n    ".join(f'"{package_id}"' for package_id in ids)
    return f"""using System.Reflection;

var failures = 0;
foreach (var name in new[]
{{
    {names}
}})
{{
    try
    {{
        var types = Assembly.Load(name).GetExportedTypes();
        Console.WriteLine($"ok   {{name}} ({{types.Length}} public types)");
    }}
    catch (Exception ex)
    {{
        failures++;
        Console.WriteLine($"FAIL {{name}}: {{ex.GetType().Name}}: {{ex.Message}}");
    }}
}}

Console.WriteLine(failures == 0 ? "install smoke: every package loaded" : $"install smoke: {{failures}} package(s) failed");
return failures == 0 ? 0 : 1;
"""


class Runner:
    def __init__(self, work: Path, extra_args: list[str]) -> None:
        self.work = work
        self.extra = extra_args
        self.env = os.environ.copy()
        self.env["NUGET_PACKAGES"] = str(work / "packages")
        self.env["DOTNET_NOLOGO"] = "1"
        self.env["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
        self.env["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1"

    def run(self, title: str, command: list[str], cwd: Path, timeout: int, show_output: bool = False) -> tuple[bool, str]:
        print(f"::group::{title}" if os.environ.get("GITHUB_ACTIONS") else f"--- {title}", flush=True)
        try:
            done = subprocess.run(command, cwd=cwd, env=self.env, capture_output=True, text=True, timeout=timeout, check=False)
            output = (done.stdout or "") + (done.stderr or "")
            ok = done.returncode == 0
        except subprocess.TimeoutExpired as exc:
            output = f"timed out after {timeout} s\n{exc.stdout or ''}"
            ok = False
        lines = output.splitlines()
        shown = lines if (show_output or not ok) else lines[-8:]
        print("\n".join(shown), flush=True)
        if os.environ.get("GITHUB_ACTIONS"):
            print("::endgroup::", flush=True)
        print(f"{'ok' if ok else 'FAILED'}: {title}", flush=True)
        return ok, output

    def dotnet(self, title: str, args: list[str], cwd: Path, timeout: int = TIMEOUT_RESTORE_BUILD, show_output: bool = False):
        return self.run(title, ["dotnet", *args, *self.extra], cwd, timeout, show_output)


def consume(local: Path | None, version: str, ids: list[str], work: Path, extra_args: list[str], generate_only: bool) -> list[str]:
    """Return the list of failed phases (empty when everything held)."""
    failed: list[str] = []
    (work / "nuget.config").write_text(nuget_config(local), encoding="utf-8")
    naravel_refs = [(package_id, version) for package_id in ids]

    install = work / "install-all"
    install.mkdir()
    (install / "InstallAll.csproj").write_text(project_file(naravel_refs), encoding="utf-8")
    (install / "Program.cs").write_text(install_program(ids), encoding="utf-8")

    smoke_naravel, smoke_others = smoke_references()
    missing = [name for name in smoke_naravel if name not in ids]
    if missing:
        raise SystemExit(f"ERROR: {SMOKE_SAMPLE.name} references projects that are not packable under src/ or are left out of this run: {missing}")
    run_dir = work / "run"
    run_dir.mkdir()
    (run_dir / "ConsumerSmoke.csproj").write_text(
        project_file([(name, version) for name in smoke_naravel], smoke_others), encoding="utf-8")
    shutil.copy(SMOKE_SAMPLE / "Program.cs", run_dir / "Program.cs")

    if generate_only:
        print(f"generated the consumer projects in {work}")
        return failed

    runner = Runner(work, extra_args)
    ok, _ = runner.dotnet("install-all: restore", ["restore", "InstallAll.csproj"], install)
    ok = ok and runner.dotnet("install-all: build", ["build", "InstallAll.csproj", "-c", "Release", "--no-restore"], install)[0]
    if ok:
        loaded, output = runner.run("install-all: load every assembly", ["dotnet", "bin/Release/net10.0/InstallAll.dll"], install, TIMEOUT_RUN, True)
        ok = loaded and "FAIL " not in output
    if not ok:
        failed.append("install-all")

    ok, _ = runner.dotnet("run: restore", ["restore", "ConsumerSmoke.csproj"], run_dir)
    ok = ok and runner.dotnet("run: build", ["build", "ConsumerSmoke.csproj", "-c", "Release", "--no-restore"], run_dir)[0]
    if ok:
        ran, output = runner.run("run: the smoke program against the packages", ["dotnet", "bin/Release/net10.0/ConsumerSmoke.dll"],
                                 run_dir, TIMEOUT_RUN, True)
        ok = ran and "all checks passed" in output and not re.search(r"^FAIL ", output, re.M)
    if not ok:
        failed.append("run")
    return failed


def write_summary(mode: str, version: str, count: int, failed: list[str], excluded: list[str]) -> None:
    path = os.environ.get("GITHUB_STEP_SUMMARY")
    if not path:
        return
    lines = [
        "### Consumer smoke",
        f"- Source: {mode}",
        f"- Version: `{version}`",
        f"- Packages installed together: {count}",
        *([f"- Left out by request (not part of this version): {', '.join(excluded)}"] if excluded else []),
        f"- Result: {'passed' if not failed else 'FAILED in: ' + ', '.join(failed)}",
        "",
    ]
    with open(path, "a", encoding="utf-8") as handle:
        handle.write("\n".join(lines))


def self_test() -> int:
    failures = []

    def expect(name: str, condition: bool) -> None:
        if not condition:
            failures.append(name)

    ids = package_ids()
    expect("35 or more packable projects", len(ids) >= 35 and len(set(ids)) == len(ids))
    expect("Foundation is packable", "Naravel.Foundation" in ids)
    naravel, others = smoke_references()
    expect("the smoke sample references only packable projects", all(name in ids for name in naravel))
    expect("the smoke sample's other packages have central versions", all(version for _, version in others))

    with tempfile.TemporaryDirectory() as tmp:
        directory = Path(tmp)
        for package_id in ["Naravel.Queue", "Naravel.Queue.Redis", "Naravel.Cache"]:
            (directory / f"{package_id}.0.1.1.nupkg").write_bytes(b"")
            (directory / f"{package_id}.0.1.1.snupkg").write_bytes(b"")
        expect("a version follows the id, so Queue does not claim Queue.Redis",
               packed_version(directory, ["Naravel.Queue", "Naravel.Queue.Redis", "Naravel.Cache"]) == "0.1.1")

    every = ["Naravel.A", "Naravel.B", "Naravel.C"]
    expect("excluding nothing keeps every id", without_excluded(every, []) == every)
    expect("an excluded id is left out and the others keep their order", without_excluded(every, ["Naravel.B"]) == ["Naravel.A", "Naravel.C"])
    expect("excluding the same id twice is the same as once", without_excluded(every, ["Naravel.C", "Naravel.C"]) == ["Naravel.A", "Naravel.B"])
    try:
        without_excluded(every, ["Naravel.Nope"])
        failures.append("an id that is not a packable project must be refused")
    except SystemExit:
        pass

    version_pattern = r"\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?"
    expect("the version check accepts releases and previews",
           all(re.fullmatch(version_pattern, v) for v in ("0.1.1", "1.0.0-preview.2")))
    expect("the version check refuses anything that could change the project file",
           not re.fullmatch(version_pattern, '1.0.0"/><Evil') and not re.fullmatch(version_pattern, "1.0"))

    for xml_text in (nuget_config(None), nuget_config(Path("/tmp/x")), project_file([("A.B", "1.2.3")])):
        try:
            ET.fromstring(xml_text.split("?>", 1)[-1])
        except ET.ParseError as exc:
            failures.append(f"generated XML is not well-formed: {exc}")
    program = install_program(["Naravel.A", "Naravel.B"])
    expect("the install program lists every id", '"Naravel.A"' in program and '"Naravel.B"' in program)
    expect("the local config maps Naravel.* to the packed directory only",
           '<package pattern="Naravel.*" />' in nuget_config(Path("/tmp/x")) and 'key="packed"' in nuget_config(Path("/tmp/x")))

    for failure in failures:
        print(f"FAIL {failure}", file=sys.stderr)
    print(f"consumer-smoke self-test: {'FAILED' if failures else 'all cases passed'}")
    return 1 if failures else 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument("--local", type=Path, metavar="DIR", help="directory with the .nupkg files from dotnet pack")
    source.add_argument("--nuget", metavar="VERSION", help="verify the version published on nuget.org")
    source.add_argument("--self-test", action="store_true", help="run the script's own checks (no dotnet needed)")
    parser.add_argument("--exclude", action="append", default=[], metavar="ID",
                        help="with --nuget: a package id that is not part of that published version, for example one that was added to "
                             "src/ after the release; may be repeated, and the run reports every package it leaves out")
    parser.add_argument("--generate-only", action="store_true", help="write the consumer projects and stop (no restore, build or run)")
    parser.add_argument("--keep", action="store_true", help="keep the working directory and print its path")
    parser.add_argument("--dotnet-arg", action="append", default=[], help="an extra argument for every dotnet restore/build call")
    args = parser.parse_args()

    if args.self_test:
        return self_test()

    ids = package_ids()
    if args.exclude and args.local is not None:
        print("ERROR: --exclude is only for --nuget; a packed directory has to hold every package", file=sys.stderr)
        return 1
    excluded = sorted(set(args.exclude))
    ids = without_excluded(ids, excluded)
    if args.local is not None:
        if not args.local.is_dir():
            print(f"ERROR: {args.local} is not a directory", file=sys.stderr)
            return 1
        version = packed_version(args.local, ids)
        mode = f"packed in {args.local}"
    else:
        version = args.nuget
        if not re.fullmatch(r"\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?", version):
            print(f"ERROR: {version!r} is not a version such as 0.1.1 or 0.2.0-preview.1", file=sys.stderr)
            return 1
        mode = "nuget.org"

    work = Path(tempfile.mkdtemp(prefix="naravel-consumer-"))
    if ROOT in work.resolve().parents:
        print(f"ERROR: the working directory {work} is inside the repository; the consumer must be outside it", file=sys.stderr)
        return 1
    try:
        left_out = f"; left out by request: {', '.join(excluded)}" if excluded else ""
        print(f"consumer smoke: {len(ids)} packages at {version} from {mode}{left_out}; working directory {work}", flush=True)
        failed = consume(args.local, version, ids, work, args.dotnet_arg, args.generate_only)
    finally:
        if args.keep or args.generate_only:
            print(f"working directory kept: {work}")
        else:
            shutil.rmtree(work, ignore_errors=True)

    if args.generate_only:
        return 0
    write_summary(mode, version, len(ids), failed, excluded)
    if failed:
        print(f"ERROR: consumer smoke failed in: {', '.join(failed)}", file=sys.stderr)
        return 1
    note = f" (left out by request: {', '.join(excluded)})" if excluded else ""
    print(f"Consumer smoke passed: {len(ids)} packages at {version} installed together and the smoke program ran against them{note}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
