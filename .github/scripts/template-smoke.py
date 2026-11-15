#!/usr/bin/env python3
"""Install the `Naravel.Templates` pack the way a user does, generate the default application from it and run that application.

The template content under templates/Naravel.Templates/content/ is never compiled in this repository: it is packed as it is. A
template.json that the template engine rejects, a placeholder that is not replaced, a Naravel version that does not resolve, C# that
does not compile against the packages, a test that fails, a file that the publish step leaves out: each of these would only show
up when a user runs `dotnet new naravel`, after the pack is on nuget.org. This script does what the user does, outside the repository
(so Directory.Build.props and central package management do not apply):

1. nupkg:     the packed template has package type Template and holds every file of the template folder.
2. install:   `dotnet new install <the .nupkg>` into an isolated CLI home, and `dotnet new list naravel` shows the template.
3. generate:  `dotnet new naravel -n Blog` next to a nuget.config that maps Naravel.* to the packed directory only. No placeholder is
              left, every source file is there under its new name, and the Naravel version written is the version of the pack.
4. build:     `dotnet build` of the application.
5. test:      `dotnet test` of the generated tests project (the fakes, and the environment-over-files rule of the configuration).
6. publish:   `dotnet publish`; the output holds config/*.json and resources/views/welcome.html.
7. run:       the published application is started from its own folder; /up answers 200, / answers the welcome page, and
              POST /register answers 202 (the event, the listener, the queue and the mail wiring is built and resolves).

Usage:
  template-smoke.py --local <directory-containing-nupkgs>   packages from `dotnet pack` (CI, before anything is pushed)
  template-smoke.py --self-test                             the script's own checks (no dotnet needed)

Naravel.* is mapped to that directory only and every other package comes from nuget.org, so a Naravel package already published
under the same version cannot stand in for the one that was just packed. The global packages folder and the dotnet CLI home are fresh
directories, so nothing cached on the machine can hide a missing package or an installed template.
"""

from __future__ import annotations

import argparse
import importlib.util
import json
import os
import re
import shutil
import socket
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TEMPLATE_PACKAGE = "Naravel.Templates"
TEMPLATE_DIRECTORY = ROOT / "templates" / "Naravel.Templates" / "content" / "naravel"
SOURCE_NAME = "NaravelApp"  # sourceName in template.json: replaced by the name the user passes with -n
VERSION_PLACEHOLDER = "NARAVEL_VERSION"
APP_NAME = "Blog"
NAMESPACE_TOKEN = "AppRootNamespace"  # the derived symbol of template.json: the name given with -n, made safe for a C# namespace
# (the name given with -n, the namespace the generated C# must have, whether that application is also compiled). The expected values are
# written out here and not computed, so the template's own rules are checked against an independent answer.
NAME_CASES = [("my-blog", "my_blog", True), ("Acme.Blog", "Acme.Blog", False), ("1st-blog", "_1st_blog", False)]
NAMESPACE_FILE = "app/Events/UserRegistered.cs"
START_TIMEOUT = 60

# What a generated application must hold whatever else is added to the template (paths with the name already replaced).
MUST_HAVE = [
    ".gitignore",
    "README.md",
    "{name}.csproj",
    "Program.cs",
    "bootstrap/ConfigurationSetup.cs",
    "routes/WebRoutes.cs",
    "config/cache.json",
    "config/database.json",
    "config/logging.json",
    "config/mail.json",
    "config/queue.json",
    "resources/views/welcome.html",
    "tests/{name}.Tests/{name}.Tests.csproj",
]
# What the publish step must carry, because the application reads them from its own folder at run time.
PUBLISH_MUST_HAVE = ["config/cache.json", "config/database.json", "config/logging.json", "config/mail.json", "config/queue.json",
                     "resources/views/welcome.html"]


def load_consumer_smoke():
    """The helpers of consumer-smoke.py (the nuget.config, the packed-version lookup and the process runner)."""
    path = ROOT / ".github" / "scripts" / "consumer-smoke.py"
    spec = importlib.util.spec_from_file_location("consumer_smoke", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


# ---------------------------------------------------------------------------------------------------------------------------------
# Pure helpers (these are what --self-test covers)
# ---------------------------------------------------------------------------------------------------------------------------------

def template_files(directory: Path) -> list[str]:
    """Every file of a template folder, as a path relative to it with forward slashes; bin and obj of a hand-built copy are left out."""
    found = []
    for path in sorted(directory.rglob("*")):
        relative = path.relative_to(directory).as_posix()
        if path.is_file() and not re.search(r"(^|/)(bin|obj)/", relative):
            found.append(relative)
    return found


def generated_path(source_relative: str, name: str) -> str:
    return source_relative.replace(SOURCE_NAME, name)


def expected_generated(sources: list[str], name: str) -> list[str]:
    """What `dotnet new naravel -n <name>` must write: every source file but the template's own configuration, renamed."""
    return [generated_path(relative, name) for relative in sources if not relative.startswith(".template.config/")]


def default_version(template_json_text: str) -> str | None:
    """The default of the NaravelVersion symbol, or None when it is missing (comments in the file are not allowed to matter)."""
    try:
        document = json.loads(template_json_text)
        return document["symbols"]["NaravelVersion"]["defaultValue"]
    except (ValueError, KeyError, TypeError):
        return None


def leftover_placeholders(files: dict[str, str], name: str) -> list[str]:
    """Where a placeholder survived the generation: the source name (unless the chosen name contains it) or the version placeholder."""
    problems = []
    for relative, text in sorted(files.items()):
        if VERSION_PLACEHOLDER in text:
            problems.append(f"{relative}: still contains {VERSION_PLACEHOLDER}")
        if NAMESPACE_TOKEN in text or NAMESPACE_TOKEN in relative:
            problems.append(f"{relative}: still contains {NAMESPACE_TOKEN}")
        if SOURCE_NAME not in name and (SOURCE_NAME in text or SOURCE_NAME in relative):
            problems.append(f"{relative}: still contains {SOURCE_NAME}")
    return problems


def referenced_versions(csproj_text: str) -> set[str]:
    """The Version of every Naravel.* PackageReference of a project file."""
    return set(re.findall(r'<PackageReference\s+Include="Naravel\.[^"]+"\s+Version="([^"]+)"', csproj_text))


def raw_name_in_namespaces(files: dict[str, str], name: str) -> list[str]:
    """The C# files in which the name is written into a namespace or using directive as it was given (a hyphen is not an identifier)."""
    pattern = re.compile(rf"^\s*(namespace|using)\s+[^;{{]*{re.escape(name)}", re.M)
    return sorted(relative for relative, text in files.items() if relative.endswith(".cs") and pattern.search(text))


def namespace_problems(files: dict[str, str], name: str, expected: str) -> list[str]:
    """What is wrong with the namespace of an application generated with -n <name>: it must be <expected>, in the C# and in RootNamespace."""
    problems = []
    if f"namespace {expected}.App.Events;" not in files.get(NAMESPACE_FILE, ""):
        problems.append(f"{NAMESPACE_FILE} does not declare 'namespace {expected}.App.Events;'")
    if f"<RootNamespace>{expected}</RootNamespace>" not in files.get(f"{name}.csproj", ""):
        problems.append(f"{name}.csproj does not set <RootNamespace>{expected}</RootNamespace>")
    if name != expected:
        problems.extend(f"{relative}: the name {name!r} is written into a namespace or using directive as it was given"
                        for relative in raw_name_in_namespaces(files, name))
    return problems


def apply_forms(template_json_text: str, name: str) -> str | None:
    """The value of the AppRootNamespace symbol for a name, computed from the forms of template.json with Python's regular expressions.

    This is a rehearsal of what the template engine does with them, not the engine: \\p{L} and \\p{Nd} are written as the ASCII and the
    Arabic letters and the digits, which is enough for the names tried here. The real proof is the generation in the run."""
    document = json.loads(template_json_text)
    symbol = document["symbols"].get("AppRootNamespace")
    if not symbol or symbol.get("type") != "derived" or symbol.get("valueSource") != "name":
        return None
    forms = document.get("forms", {})

    def transform(form_name: str, value: str) -> str:
        form = forms[form_name]
        if form["identifier"] == "chain":
            for step in form["steps"]:
                value = transform(step, value)
            return value
        pattern = form["pattern"].replace("\\p{L}", "A-Za-z\u0600-\u06FF").replace("\\p{Nd}", "0-9")
        return re.sub(pattern, form["replacement"], value)

    return transform(symbol["valueTransform"], name)


def summary_line(version: str) -> str:
    return (f"Template smoke passed: {TEMPLATE_PACKAGE} {version}: the default application (cache memory, mail log, queue file, "
            f"database sqlite) was generated, built, tested, published and started; /up and / answered 200 and POST /register "
            f"answered 202. Only the default combination was started. Project names tried: "
            f"{', '.join(name + (' (built)' if built else '') for name, _, built in NAME_CASES)}.")


def nupkg_problems(names: list[str], nuspec_text: str, sources: list[str]) -> list[str]:
    """What is wrong with the packed template: its package type, or a source file that is not packed (or a bin/obj file that is)."""
    problems = []
    if not re.search(r'<packageType\s+name="Template"', nuspec_text):
        problems.append('the nuspec does not have <packageType name="Template" />')
    packed = {name for name in names if name.startswith("content/naravel/")}
    missing = [relative for relative in sources if f"content/naravel/{relative}" not in packed]
    if missing:
        # One problem, not one per file: a packing rule that is wrong drops most files, and twenty annotations hide the cause.
        shown = ", ".join(f"content/naravel/{relative}" for relative in missing[:4])
        problems.append(f"{len(missing)} of {len(sources)} files of the template folder are not in the package under content/naravel/, "
                        f"for example {shown}")
    # NuGet appends the folder of each file to PackagePath by itself; a PackagePath that names that folder too repeats it.
    repeated = sorted(name for name in packed if re.match(r"^content/naravel/(?P<d>(?:.*/)?)naravel/(?P=d)[^/]+$", name))
    if repeated:
        problems.append(f"{len(repeated)} entries repeat the template folder, for example {repeated[0]}: NuGet already appends the folder of "
                        f"each file to PackagePath, so PackagePath in Naravel.Templates.csproj must be content\\ and nothing more")
    stray = sorted(name for name in packed if re.search(r"/(bin|obj)/", name))
    problems.extend(f"{name} is a build output and must not be packed" for name in stray)
    return problems


def entry_listing(names: list[str], limit: int = 150) -> str:
    """The entries of the package as they are stored, one per line, so a wrong packing rule can be seen and not guessed."""
    entries = sorted(names)
    lines = [f"  {name}" for name in entries[:limit]]
    if len(entries) > limit:
        lines.append(f"  ... and {len(entries) - limit} more")
    return f"the package holds {len(entries)} entries:\n" + "\n".join(lines)


def trx_problems(trx_text: str, required_test: str) -> list[str]:
    """What is wrong with a test result file: it is unreadable, no test ran, a test failed, or the named test did not pass.

    The console summary of `dotnet test` changes between SDK versions; the result file does not, and a run that executed no test at
    all must not count as a pass."""
    try:
        root = ET.fromstring(trx_text)
    except ET.ParseError as error:
        return [f"the test result file is not valid XML: {error}"]
    counters = next((node for node in root.iter() if node.tag.endswith("Counters")), None)
    if counters is None:
        return ["the test result file has no counters"]
    problems = []
    executed = int(counters.get("executed", "0"))
    failed = int(counters.get("failed", "0"))
    if executed < 1:
        problems.append("the tests project ran no test")
    if failed:
        problems.append(f"{failed} test(s) failed")
    named = [node for node in root.iter() if node.tag.endswith("UnitTestResult") and (node.get("testName") or "").endswith("." + required_test)]
    if not named:
        problems.append(f"the test {required_test} did not run")
    elif any(node.get("outcome") != "Passed" for node in named):
        problems.append(f"the test {required_test} did not pass")
    return problems


# ---------------------------------------------------------------------------------------------------------------------------------
# The run
# ---------------------------------------------------------------------------------------------------------------------------------

def annotate(phase: str, output: str) -> None:
    """Make the cause readable in the check annotations; the raw log is not readable without authentication."""
    if not os.environ.get("GITHUB_ACTIONS"):
        return
    lines = [line for line in output.splitlines() if re.search(r"error|NU\d{4}|failed|exception|timed out", line, re.I)]
    for line in list(dict.fromkeys(lines))[:15]:
        print(f"::error title=template-smoke ({phase})::{line.strip()[:900]}")


def free_port() -> int:
    with socket.socket() as sock:
        sock.bind(("127.0.0.1", 0))
        return sock.getsockname()[1]


def http(method: str, url: str, body: dict | None = None, timeout: int = 15) -> tuple[int, str, str]:
    """(status, content type, body); a proxy variable of the machine must not stand between this script and 127.0.0.1."""
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    headers = {"Content-Type": "application/json"} if body is not None else {}
    request = urllib.request.Request(url, data=json.dumps(body).encode() if body is not None else None, method=method, headers=headers)
    try:
        with opener.open(request, timeout=timeout) as response:
            return response.status, response.headers.get("Content-Type", ""), response.read().decode("utf-8", "replace")
    except urllib.error.HTTPError as error:
        return error.code, error.headers.get("Content-Type", ""), error.read().decode("utf-8", "replace")


def read_text_files(directory: Path) -> dict[str, str]:
    """Every text file of the generated application, bin, obj and publish output left out."""
    files = {}
    for relative in template_files(directory):
        try:
            files[relative] = (directory / relative).read_text(encoding="utf-8")
        except UnicodeDecodeError:
            continue
    return files


def start_and_probe(publish: Path, name: str, env: dict[str, str], log_path: Path) -> list[str]:
    """Start the published application from its own folder and return what did not hold (empty when everything did)."""
    problems: list[str] = []
    port = free_port()
    base = f"http://127.0.0.1:{port}"
    run_env = dict(env, ASPNETCORE_URLS=base)
    with open(log_path, "w", encoding="utf-8") as log:
        process = subprocess.Popen(["dotnet", f"{name}.dll"], cwd=publish, env=run_env, stdout=log, stderr=subprocess.STDOUT)
        try:
            deadline = time.monotonic() + START_TIMEOUT
            up = None
            while time.monotonic() < deadline:
                if process.poll() is not None:
                    problems.append(f"the application exited with code {process.returncode} before it answered /up")
                    break
                try:
                    up = http("GET", f"{base}/up", timeout=5)
                    break
                except (urllib.error.URLError, OSError):
                    time.sleep(0.5)
            if up is None and not problems:
                problems.append(f"the application did not answer /up within {START_TIMEOUT} s")
            if up is not None:
                if up[0] != 200:
                    problems.append(f"GET /up answered {up[0]}, expected 200")
                status, content_type, body = http("GET", f"{base}/")
                if status != 200 or "text/html" not in content_type or f"<title>{name}</title>" not in body:
                    problems.append(f"GET / answered {status} ({content_type}); expected the welcome page for {name}")
                status, _, body = http("POST", f"{base}/register", {"email": "ada@example.com", "name": "Ada"})
                if status != 202:
                    problems.append(f"POST /register answered {status}, expected 202: {body[:300]}")
        finally:
            process.terminate()
            try:
                process.wait(timeout=20)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait()
    return problems


def write_summary(version: str, failed: list[str], notes: list[str]) -> None:
    path = os.environ.get("GITHUB_STEP_SUMMARY")
    if not path:
        return
    lines = ["### Template smoke", f"- Template pack: `{TEMPLATE_PACKAGE}` `{version}`",
             "- Combination started: default (cache memory, mail log, queue file, database sqlite)",
             f"- Result: {'passed' if not failed else 'FAILED in: ' + ', '.join(failed)}", *[f"- {note}" for note in notes], ""]
    with open(path, "a", encoding="utf-8") as handle:
        handle.write("\n".join(lines))


def smoke(local: Path, work: Path, extra_args: list[str]) -> tuple[list[str], list[str]]:
    """Return (failed phases, notes). Every phase prints its own output; a failed phase stops the phases that depend on it."""
    consumer = load_consumer_smoke()
    failed: list[str] = []
    notes: list[str] = []
    version = consumer.packed_version(local, [TEMPLATE_PACKAGE])
    nupkg = local / f"{TEMPLATE_PACKAGE}.{version}.nupkg"
    sources = template_files(TEMPLATE_DIRECTORY)

    def phase_failed(name: str, problems: list[str]) -> bool:
        if not problems:
            print(f"ok: {name}", flush=True)
            return False
        for problem in problems:
            print(f"ERROR: {name}: {problem}", file=sys.stderr, flush=True)
            if os.environ.get("GITHUB_ACTIONS"):
                print(f"::error title=template-smoke ({name})::{problem[:900]}")
        failed.append(name)
        return True

    # 1. The packed template.
    with zipfile.ZipFile(nupkg) as package:
        entries = package.namelist()
        nuspec = next((package.read(n).decode("utf-8", "replace") for n in entries if n.endswith(".nuspec")), "")
        problems = nupkg_problems(entries, nuspec, sources)
    json_text = (TEMPLATE_DIRECTORY / ".template.config" / "template.json").read_text(encoding="utf-8")
    if default_version(json_text) != version:
        problems.append(f"template.json: NaravelVersion defaults to {default_version(json_text)!r}, the packed version is {version!r}")
    if phase_failed("nupkg", problems):
        print(entry_listing(entries), flush=True)
        return failed, notes

    runner = consumer.Runner(work, extra_args)
    runner.env["DOTNET_CLI_HOME"] = str(work / "cli-home")
    (work / "nuget.config").write_text(consumer.nuget_config(local), encoding="utf-8")

    try:
        # 2. Install.
        ok, output = runner.run(f"install {nupkg.name}", ["dotnet", "new", "install", str(nupkg)], work, 300, True)
        if not ok:
            annotate("install", output)
            return failed + ["install"], notes
        ok, output = runner.run("dotnet new list naravel", ["dotnet", "new", "list", "naravel"], work, 120, True)
        if not ok or "Naravel application" not in output or not re.search(r"\bnaravel\b", output):
            annotate("install", output)
            return failed + ["install"], notes

        # 3. Generate the default application.
        ok, output = runner.run(f"dotnet new naravel -n {APP_NAME}", ["dotnet", "new", "naravel", "-n", APP_NAME], work, 180, True)
        app = work / APP_NAME
        if not ok or not app.is_dir():
            annotate("generate", output)
            return failed + ["generate"], notes
        generated = read_text_files(app)
        problems = [f"{path} was not generated" for path in expected_generated(sources, APP_NAME) if not (app / path).is_file()]
        problems += [f"{path} was not generated" for path in (p.format(name=APP_NAME) for p in MUST_HAVE) if not (app / path).is_file()]
        problems += leftover_placeholders(generated, APP_NAME)
        versions = referenced_versions(generated.get(f"{APP_NAME}.csproj", ""))
        if versions != {version}:
            problems.append(f"{APP_NAME}.csproj references Naravel packages at {sorted(versions)}, expected only {version}")
        if phase_failed("generate", problems):
            return failed, notes

        # 4 to 6. Restore, build, test, publish: the packages come from the packed directory (nuget.config in the parent folder).
        ok, output = runner.dotnet("build the application", ["build", f"{APP_NAME}.csproj", "-c", "Release"], app)
        if not ok:
            annotate("build", output)
            return failed + ["build"], notes
        print("ok: build", flush=True)

        results = work / "test-results"
        tests_ok, output = runner.dotnet(
            "test the generated tests project",
            ["test", f"tests/{APP_NAME}.Tests", "-c", "Release", "--logger", "trx;LogFileName=tests.trx", "--results-directory", str(results)],
            app)
        if not tests_ok:
            annotate("test", output)
            failed.append("test")
        else:
            trx = results / "tests.trx"
            problems = (trx_problems(trx.read_text(encoding="utf-8", errors="replace"), "Environment_variables_override_config_files")
                        if trx.is_file() else ["dotnet test wrote no test result file"])
            tests_ok = not phase_failed("test", problems)

        publish = work / "publish"
        ok, output = runner.dotnet("publish the application", ["publish", f"{APP_NAME}.csproj", "-c", "Release", "-o", str(publish)], app)
        if not ok:
            annotate("publish", output)
            return failed + ["publish"], notes
        problems = [f"the publish output does not hold {path}" for path in PUBLISH_MUST_HAVE if not (publish / path).is_file()]
        if phase_failed("publish", problems):
            return failed, notes

        # 7. Start it.
        problems = start_and_probe(publish, APP_NAME, runner.env, work / "app.log")
        if problems:
            tail = (work / "app.log").read_text(encoding="utf-8", errors="replace")[-4000:]
            print("--- application output (last 4000 characters) ---\n" + tail, flush=True)
            annotate("run", tail)
        if phase_failed("run", problems):
            return failed, notes

        # Names that are not valid C# namespaces as they are (a hyphen, a leading digit): the file names and the README keep the name,
        # the namespace is made safe. The first case is also compiled, tests project included.
        problems = []
        for index, (name, expected, build) in enumerate(NAME_CASES):
            folder = work / "names" / str(index)
            folder.mkdir(parents=True)
            ok, output = runner.run(f"dotnet new naravel -n {name}", ["dotnet", "new", "naravel", "-n", name], folder, 180, True)
            project = folder / name
            if not ok or not project.is_dir():
                problems.append(f"dotnet new naravel -n {name} did not generate a project")
                annotate("names", output)
                continue
            files = read_text_files(project)
            problems += [f"-n {name}: {problem}" for problem in leftover_placeholders(files, name) + namespace_problems(files, name, expected)]
            if build:
                built, output = runner.dotnet(f"build a project named {name}",
                                              ["build", f"tests/{name}.Tests/{name}.Tests.csproj", "-c", "Release"], project)
                if not built:
                    problems.append(f"-n {name}: the generated application and its tests project do not build")
                    annotate("names", output)
        if not phase_failed("names", problems):
            notes.append("Project names generated: " + ", ".join(f"{name} (namespace {expected})" for name, expected, _ in NAME_CASES))
        if not failed:
            print(summary_line(version), flush=True)
    finally:
        runner.run(f"dotnet new uninstall {TEMPLATE_PACKAGE}", ["dotnet", "new", "uninstall", TEMPLATE_PACKAGE], work, 120)
    return failed, notes


def self_test() -> int:
    failures: list[str] = []

    def expect(name: str, condition: bool) -> None:
        if not condition:
            failures.append(name)

    consumer = load_consumer_smoke()
    sources = template_files(TEMPLATE_DIRECTORY)
    expect("the template folder has files", len(sources) > 10)
    expect("template.json is in the template folder", ".template.config/template.json" in sources)
    expect("no build output is listed", not any(re.search(r"(^|/)(bin|obj)/", s) for s in sources))
    for required in MUST_HAVE:
        expect(f"the template holds {required}", required.format(name=SOURCE_NAME) in sources)
    for required in PUBLISH_MUST_HAVE:
        expect(f"the template holds {required}, which the publish output must carry", required in sources)

    renamed = expected_generated(sources, "Blog")
    expect("the template's own configuration is not generated", not any(p.startswith(".template.config/") for p in renamed))
    expect("the source name is replaced in file names", "Blog.csproj" in renamed and "tests/Blog.Tests/Blog.Tests.csproj" in renamed)
    expect("no generated path keeps the source name", not any(SOURCE_NAME in p for p in renamed))

    text = (TEMPLATE_DIRECTORY / ".template.config" / "template.json").read_text(encoding="utf-8")
    shipped = default_version(text)
    expect("template.json declares the NaravelVersion default", bool(shipped) and re.fullmatch(r"\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?", shipped))
    expect("a template.json without the symbol is reported", default_version('{"symbols": {}}') is None)
    expect("a template.json that is not JSON is reported", default_version("{") is None)
    expect("the application project uses the placeholder for every Naravel package",
           referenced_versions((TEMPLATE_DIRECTORY / f"{SOURCE_NAME}.csproj").read_text(encoding="utf-8")) == {VERSION_PLACEHOLDER})

    expect("a clean generation has no leftovers",
           leftover_placeholders({"Blog.csproj": '<PackageReference Include="Naravel.Cache" Version="0.2.0" />'}, "Blog") == [])
    expect("a surviving version placeholder is found",
           len(leftover_placeholders({"Blog.csproj": 'Version="NARAVEL_VERSION"'}, "Blog")) == 1)
    expect("a surviving source name is found in contents and in paths",
           len(leftover_placeholders({"a.cs": "namespace NaravelApp;", "NaravelApp.cs": ""}, "Blog")) == 2)
    expect("a name that contains the source name is not reported",
           leftover_placeholders({"a.cs": "namespace MyNaravelApp;"}, "MyNaravelApp") == [])
    expect("only Naravel references are read",
           referenced_versions('<PackageReference Include="xunit" Version="2.9.3" /><PackageReference Include="Naravel.Mail" Version="0.2.0" />')
           == {"0.2.0"})

    expect("a raw name in a namespace is found",
           raw_name_in_namespaces({"a.cs": "namespace my-blog.App;", "b.cs": "using System;"}, "my-blog") == ["a.cs"])
    expect("a name that appears only in a string is not a namespace problem",
           raw_name_in_namespaces({"a.cs": 'var x = "my-blog";'}, "my-blog") == [])
    expect("a surviving namespace token is found", len(leftover_placeholders({"a.cs": "namespace AppRootNamespace;"}, "Blog")) == 1)

    # The namespace of the generated application. The template's forms are rehearsed with Python's regular expressions (the engine is
    # the real proof, in the run); a plain replacement of the token by the name is what the first version did, and must be found.
    json_text_for_forms = (TEMPLATE_DIRECTORY / ".template.config" / "template.json").read_text(encoding="utf-8")
    table = [("Blog", "Blog"), ("my-blog", "my_blog"), ("my blog", "my_blog"), ("Acme.Blog", "Acme.Blog"), ("1st-blog", "_1st_blog"),
             ("a.2b", "a._2b"), ("a--b", "a__b"), ("وبلاگ-من", "وبلاگ_من")]
    table += [(name, expected) for name, expected, _ in NAME_CASES]
    expect("template.json derives AppRootNamespace from the name", apply_forms(json_text_for_forms, "Blog") == "Blog")
    for name, expected in table:
        expect(f"the namespace form turns {name!r} into {expected!r}", apply_forms(json_text_for_forms, name) == expected)
    symbol = json.loads(json_text_for_forms)["symbols"]["AppRootNamespace"]
    expect("the derived symbol replaces the token the C# files use", symbol["replaces"] == NAMESPACE_TOKEN)

    def generate(name: str, namespace: str) -> dict[str, str]:
        """The plain replacements of the engine, applied to the template folder."""
        out = {}
        for relative in sources:
            if relative.startswith(".template.config/"):
                continue
            try:
                text = (TEMPLATE_DIRECTORY / relative).read_text(encoding="utf-8")
            except UnicodeDecodeError:
                continue
            out[generated_path(relative, name)] = (
                text.replace(SOURCE_NAME, name).replace(NAMESPACE_TOKEN, namespace).replace(VERSION_PLACEHOLDER, "0.2.0"))
        return out

    for name, expected, _ in NAME_CASES:
        generated = generate(name, apply_forms(json_text_for_forms, name))
        expect(f"a generation with -n {name} has the namespace {expected}",
               namespace_problems(generated, name, expected) == [] and leftover_placeholders(generated, name) == [])
    expect("the plain replacement of the token by a hyphenated name is found",
           len(namespace_problems(generate("my-blog", "my-blog"), "my-blog", "my_blog")) > 3)
    expect("no C# file of the template writes the source name into a namespace",
           not any(SOURCE_NAME in (TEMPLATE_DIRECTORY / relative).read_text(encoding="utf-8") for relative in sources if relative.endswith(".cs")))
    for project in (f"{SOURCE_NAME}.csproj", f"tests/{SOURCE_NAME}.Tests/{SOURCE_NAME}.Tests.csproj"):
        expect(f"{project} sets RootNamespace from the token",
               f"<RootNamespace>{NAMESPACE_TOKEN}" in (TEMPLATE_DIRECTORY / project).read_text(encoding="utf-8"))

    good_nuspec = '<package><metadata><packageTypes><packageType name="Template" /></packageTypes></metadata></package>'
    packed = [f"content/naravel/{s}" for s in sources] + ["Naravel.Templates.nuspec"]
    expect("a complete package has no problems", nupkg_problems(packed, good_nuspec, sources) == [])
    expect("a wrong package type is reported", len(nupkg_problems(packed, good_nuspec.replace("Template", "Dependency"), sources)) == 1)
    expect("a missing file is reported", len(nupkg_problems(packed[1:], good_nuspec, sources)) == 1)
    only_dot_files = [f"content/naravel/{s}" for s in sources if s.startswith(".")] + ["Naravel.Templates.nuspec"]
    dropped = nupkg_problems(only_dot_files, good_nuspec, sources)
    expect("a package that kept only the dot files is one problem that counts the files",
           len(dropped) == 1 and f"{len(sources) - len(only_dot_files) + 1} of {len(sources)} files" in dropped[0])
    # The listing the first CI run printed for PackagePath="content\\%(RecursiveDir)\\": every folder is written twice.
    doubled = ["Naravel.Templates.nuspec", "content/naravel/naravel/Program.cs", "content/naravel/naravel/.gitignore",
               "content/naravel/app/Events/naravel/app/Events/UserRegistered.cs",
               "content/naravel/.template.config/naravel/.template.config/template.json"]
    repeats = nupkg_problems(doubled, good_nuspec, sources)
    expect("a package that repeats the template folder is reported with the cause",
           any("4 entries repeat the template folder" in p and "must be content" in p for p in repeats))
    expect("a package with the right paths does not report a repeated folder", not any("repeat" in p for p in nupkg_problems(packed, good_nuspec, sources)))
    import xml.etree.ElementTree as ET
    pack_project = ROOT / "templates" / "Naravel.Templates" / "Naravel.Templates.csproj"
    content_items = [item for item in ET.parse(pack_project).getroot().iter("None") if item.get("Include", "").startswith("content\\")]
    expect("the template project packs the content folder with PackagePath content\\ and without RecursiveDir",
           len(content_items) == 1 and content_items[0].get("PackagePath") == "content\\")
    listing = entry_listing(["b.txt", "a.txt", "c.txt"], limit=2)
    expect("the entry listing is sorted, counted and cut", "3 entries" in listing and listing.index("a.txt") < listing.index("b.txt")
           and "c.txt" not in listing and "1 more" in listing)
    expect("a packed build output is reported",
           len(nupkg_problems(packed + ["content/naravel/bin/Debug/x.dll"], good_nuspec, sources)) == 1)

    def trx(executed: int, failed: int, outcome: str, test: str = "Blog.Tests.ConfigurationTests.Environment_variables_override_config_files") -> str:
        return (f'<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>'
                f'<UnitTestResult testName="{test}" outcome="{outcome}" /></Results><ResultSummary outcome="Completed">'
                f'<Counters total="{executed}" executed="{executed}" passed="{executed - failed}" failed="{failed}" /></ResultSummary></TestRun>')

    required = "Environment_variables_override_config_files"
    expect("a passing result file has no problems", trx_problems(trx(6, 0, "Passed"), required) == [])
    expect("a failing test is reported", any("failed" in p for p in trx_problems(trx(6, 1, "Failed"), required)))
    expect("a run without tests is reported", any("no test" in p for p in trx_problems(trx(0, 0, "Passed"), required)))
    expect("a missing named test is reported", any("did not run" in p for p in trx_problems(trx(6, 0, "Passed", "Blog.Tests.Other.Test"), required)))
    expect("a named test that did not pass is reported", any("did not pass" in p for p in trx_problems(trx(6, 0, "NotExecuted"), required)))
    expect("an unreadable result file is reported", len(trx_problems("not xml", required)) == 1)

    line = summary_line("0.2.0")
    expect("the result line names the version and the combination",
           "0.2.0" in line and "cache memory, mail log, queue file, database sqlite" in line and "Only the default" in line)

    with tempfile.TemporaryDirectory() as tmp:
        directory = Path(tmp)
        for package_id in (TEMPLATE_PACKAGE, "Naravel.Queue"):
            (directory / f"{package_id}.0.3.0.nupkg").write_bytes(b"")
        expect("the template version is read from the file name", consumer.packed_version(directory, [TEMPLATE_PACKAGE]) == "0.3.0")
        expect("an empty directory is refused", _refused(lambda: consumer.packed_version(Path(tmp) / "nowhere", [TEMPLATE_PACKAGE])))

    expect("the nuget.config maps Naravel.* to the packed directory only",
           'key="packed"' in consumer.nuget_config(Path("/tmp/x")) and '<package pattern="Naravel.*" />' in consumer.nuget_config(Path("/tmp/x")))

    status = http_selftest()
    expect("the http helper reads a status, a content type and a body", status)

    for failure in failures:
        print(f"FAIL {failure}", file=sys.stderr)
    print(f"template-smoke self-test: {'FAILED' if failures else 'all cases passed'}")
    return 1 if failures else 0


def _refused(action) -> bool:
    """True when the action exits; its message on stderr is the expected one, so it is not shown."""
    import contextlib
    import io

    with contextlib.redirect_stderr(io.StringIO()):
        try:
            action()
        except SystemExit:
            return True
    return False


def http_selftest() -> bool:
    """A local server answers; the helper must reach it and read the status of both a 200 and an error response."""
    import http.server as http_server
    import threading

    class Handler(http_server.BaseHTTPRequestHandler):
        def do_GET(self):  # noqa: N802
            code = 200 if self.path == "/ok" else 404
            body = b"<title>x</title>"
            self.send_response(code)
            self.send_header("Content-Type", "text/html")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def log_message(self, *args):  # silence
            pass

    server = http_server.HTTPServer(("127.0.0.1", 0), Handler)
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    try:
        base = f"http://127.0.0.1:{server.server_port}"
        ok = http("GET", f"{base}/ok")
        missing = http("GET", f"{base}/missing")
        return ok[0] == 200 and "text/html" in ok[1] and "<title>" in ok[2] and missing[0] == 404
    finally:
        server.shutdown()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument("--local", type=Path, metavar="DIR", help="directory with the .nupkg files from dotnet pack")
    source.add_argument("--self-test", action="store_true", help="run the script's own checks (no dotnet needed)")
    parser.add_argument("--keep", action="store_true", help="keep the working directory and print its path")
    parser.add_argument("--dotnet-arg", action="append", default=[], help="an extra argument for every dotnet restore/build/test/publish call")
    args = parser.parse_args()

    if args.self_test:
        return self_test()

    if not args.local.is_dir():
        print(f"ERROR: {args.local} is not a directory", file=sys.stderr)
        return 1
    work = Path(tempfile.mkdtemp(prefix="naravel-template-"))
    if ROOT in work.resolve().parents:
        print(f"ERROR: the working directory {work} is inside the repository; the generated application must be outside it", file=sys.stderr)
        return 1
    version = "unknown"
    try:
        print(f"template smoke: {TEMPLATE_PACKAGE} from {args.local}; working directory {work}", flush=True)
        failed, notes = smoke(args.local, work, args.dotnet_arg)
        version = load_consumer_smoke().packed_version(args.local, [TEMPLATE_PACKAGE])
    finally:
        if args.keep:
            print(f"working directory kept: {work}")
        else:
            shutil.rmtree(work, ignore_errors=True)

    write_summary(version, failed, notes)
    if failed:
        print(f"ERROR: template smoke failed in: {', '.join(failed)}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
