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
              POST /register answers 202 (the event, the listener, the queue and the mail wiring is built and resolves). Then the
              application is started the way the docs say, with `dotnet run` from its folder (dev run), and the same is asked of it.
8. names:     the template is generated again with names that are not valid C# namespaces as they are (my-blog, 1st-blog, Acme.Blog).
              Every project reference must point to a file that was generated, the name must appear as it was given where a user reads
              it, and the first of these applications is built and its tests are run, so a path or a namespace that the template
              engine rewrote cannot go unnoticed.
9. matrix:    every value of every parameter of template.json once (--database, --cache, --queue, --mail, --no-tests), and a few
              combinations, are generated, checked for a half-wired result (a package without its call, a call without its package,
              a config that names a store that is not there) and built; the tests of each are run. None of them is started, because a
              server or a broker would be needed: only the default application is. The log says so.
10. items:    the item templates (naravel-job, -event, -listener, -mailable, -controller) are generated into the default application, in
              the folders the template names. Each file has the namespace of the project (or the one given with --namespace), no
              placeholder, conditional marker or #error is left, and the application is built with them.

Usage:
  template-smoke.py --local <directory-containing-nupkgs>   packages from `dotnet pack` (CI, before anything is pushed)
  template-smoke.py --nuget <version>                       the published pack on nuget.org (after a release): install, generate, build
  template-smoke.py --self-test                             the script's own checks (no dotnet needed)

Naravel.* is mapped to that directory only and every other package comes from nuget.org, so a Naravel package already published
under the same version cannot stand in for the one that was just packed. The global packages folder and the dotnet CLI home are fresh
directories, so nothing cached on the machine can hide a missing package or an installed template.
"""

from __future__ import annotations

import argparse
import contextlib
import fnmatch
import importlib.util
import io
import json
import os
import posixpath
import re
import shlex
import shutil
import signal
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
# The docs start the application with `dotnet run`. The smoke runs it from the application folder with -c Release --no-build: the
# application was just built in Release, and a plain `dotnet run` would build it again in Debug. Everything else is what a user does.
DEV_RUN = ["dotnet", "run", "-c", "Release", "--no-build"]
NAMESPACE_TOKEN = "AppRootNamespace"  # the derived symbol of template.json: the name given with -n, made safe for a C# namespace
PROJECT_NAME_TOKEN = "AppProjectName"  # the derived symbol of template.json: the name given with -n, as it was given (paths, display)
# (the name given with -n, the namespace the generated C# must have, whether that application is also built and its tests run). The
# expected values are written out here and not computed, so the template's own rules are checked against an independent answer.
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

# What each value of each parameter must write into a generated application: (package, registration call, using directive, the "Driver"
# written into config/). Written out here and not derived from template.json, so the template's own conditions are checked against an
# independent answer. None means the module has it built in. The Postgres driver is named "pgsql" in the source of Naravel.Database.Postgres.
ORACLE: dict[str, dict[str, tuple[str | None, str | None, str | None, str | None]]] = {
    "database": {
        "sqlite": ("Naravel.Database.Sqlite", "AddNaravelSqliteDatabase", None, "sqlite"),
        "postgres": ("Naravel.Database.Postgres", "AddNaravelPostgresDatabase", None, "pgsql"),
        "mysql": ("Naravel.Database.MySql", "AddNaravelMySqlDatabase", None, "mysql"),
        "sqlserver": ("Naravel.Database.SqlServer", "AddNaravelSqlServerDatabase", None, "sqlserver"),
        "none": (None, None, None, None),
    },
    "cache": {
        "memory": (None, None, None, "memory"),
        "redis": ("Naravel.Cache.Redis", "AddNaravelRedisCache", "Naravel.Cache.Redis", "redis"),
    },
    "queue": {
        "file": ("Naravel.Queue.File", "AddFileDriver", "Naravel.Queue.File", "file"),
        "memory": ("Naravel.Queue.Memory", "AddMemoryDriver", "Naravel.Queue.Memory", "memory"),
        "redis": ("Naravel.Queue.Redis", "AddRedisDriver", "Naravel.Queue.Redis", "redis"),
        "rabbitmq": ("Naravel.Queue.RabbitMQ", "AddRabbitMqDriver", "Naravel.Queue.RabbitMQ", "rabbitmq"),
        "kafka": ("Naravel.Queue.Kafka", "AddKafkaDriver", "Naravel.Queue.Kafka", "kafka"),
    },
    "mail": {
        "log": (None, None, None, "log"),
        "smtp": ("Naravel.Mail.Smtp", "AddNaravelMailSmtp", None, "smtp"),
    },
}
BASE_PACKAGES = {"Naravel.Routing", "Naravel.Cache", "Naravel.Queue", "Naravel.Events", "Naravel.Mail"}
# The key a driver cannot work without, in the store the template writes for it.
REQUIRED_KEY = {("database", "sqlite"): "Path", ("database", "postgres"): "ConnectionString", ("database", "mysql"): "ConnectionString",
                ("database", "sqlserver"): "ConnectionString", ("cache", "redis"): "ConnectionString", ("queue", "file"): "Path",
                ("queue", "redis"): "ConnectionString", ("queue", "rabbitmq"): "HostName", ("queue", "kafka"): "BootstrapServers",
                ("mail", "smtp"): "Host"}
# The environment variable the README names for a service the application needs: the path of that key in config/, with "__" for ":".
SERVICE_VARIABLES = {("database", "postgres"): "Database__Connections__main__ConnectionString",
                     ("database", "mysql"): "Database__Connections__main__ConnectionString",
                     ("database", "sqlserver"): "Database__Connections__main__ConnectionString",
                     ("cache", "redis"): "Cache__Stores__redis__ConnectionString",
                     ("queue", "redis"): "NaravelQueue__Stores__redis__ConnectionString",
                     ("queue", "rabbitmq"): "NaravelQueue__Stores__rabbitmq__HostName",
                     ("queue", "kafka"): "NaravelQueue__Stores__kafka__BootstrapServers",
                     ("mail", "smtp"): "Mail__Mailers__smtp__Host"}
STORE_SECTIONS = {"database": ("config/database.json", "Database", "Connections"), "cache": ("config/cache.json", "Cache", "Stores"),
                  "queue": ("config/queue.json", "NaravelQueue", "Stores")}
# Besides every value alone: combinations that put several drivers, a missing database and a missing tests project together.
COMBINATIONS: list[dict[str, str]] = [
    {"database": "postgres", "cache": "redis", "queue": "redis", "mail": "smtp"},
    {"database": "none", "queue": "memory"},
    {"database": "sqlserver", "queue": "rabbitmq", "no-tests": "true"},
]

# The item templates: what `dotnet new <short name> -n <class> -o <folder> <arguments>` is run with in the default application, the
# namespace the file must have ({app} is the root namespace of the application) and a line that must be in it. The folder of each
# template under templates/Naravel.Templates/content/ is named after its short name. Written out here and not computed, so the
# templates are checked against an independent answer.
ITEM_RUNS: list[tuple[str, str, str, list[str], str, str]] = [
    ("naravel-job", "SendReceipt", "app/Jobs", [], "{app}.App.Jobs", "class SendReceipt : Job"),
    ("naravel-event", "OrderShipped", "app/Events", [], "{app}.App.Events", "record OrderShipped()"),
    ("naravel-listener", "RecordUser", "app/Listeners", [], "{app}.App.Listeners", "IEventListener<UserRegistered>"),
    ("naravel-listener", "NotifyWarehouse", "app/Listeners", ["--event", "OrderShipped"], "{app}.App.Listeners", "IEventListener<OrderShipped>"),
    ("naravel-mailable", "ReceiptMail", "app/Mail", [], "{app}.App.Mail", "class ReceiptMail : Mailable"),
    ("naravel-controller", "OrdersController", "app/Http/Controllers", [], "{app}.App.Http.Controllers", "class OrdersController : ControllerBase"),
    ("naravel-job", "ExportBilling", "app/Jobs", ["--namespace", "Acme.Billing"], "Acme.Billing", "class ExportBilling : Job"),
]
ITEM_SOURCE_NAME = "NaravelItem"  # sourceName of every item template: its file is NaravelItem.cs and the class is named after the -n
ITEM_TOKENS = ("ItemNamespace", "ProjectRootNamespace", ITEM_SOURCE_NAME, "ItemEvent")  # replaced by the engine; none may survive
CONTENT_DIRECTORY = TEMPLATE_DIRECTORY.parent


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


def item_template_files() -> dict[str, list[str]]:
    """The folder of every item template under content/ (every folder but the application's) and the files in it."""
    return {folder.name: template_files(folder) for folder in sorted(CONTENT_DIRECTORY.iterdir())
            if folder.is_dir() and folder != TEMPLATE_DIRECTORY}


def generated_path(source_relative: str, name: str) -> str:
    return source_relative.replace(SOURCE_NAME, name)


def left_out(relative: str, choice: dict[str, str]) -> bool:
    """Whether the choices leave a source file out of the generated application (written out here, like the ORACLE)."""
    return ((choice.get("no-tests") == "true" and relative.startswith("tests/"))
            or (choice.get("database") == "none" and relative == "config/database.json"))


def expected_generated(sources: list[str], name: str, choice: dict[str, str] | None = None) -> list[str]:
    """What `dotnet new naravel -n <name>` must write: every source file but the template's own configuration, renamed."""
    choice = choice or {}
    return [generated_path(relative, name) for relative in sources
            if not relative.startswith(".template.config/") and not left_out(relative, choice)]


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
        for token in (NAMESPACE_TOKEN, PROJECT_NAME_TOKEN):
            if token in text or token in relative:
                problems.append(f"{relative}: still contains {token}")
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


def project_reference_problems(files: dict[str, str]) -> list[str]:
    """Every ProjectReference of a generated project file must point to a file that was generated.

    The template engine writes the name given with -n into a file name as it is (my-blog.csproj) but into the content of a file in its
    safe form (my_blog), so a path written with the source name points to a file that does not exist. NuGet skips such a reference
    without an error; the project then compiles without the application and fails far from the cause."""
    problems = []
    for relative, text in sorted(files.items()):
        if not relative.endswith(".csproj"):
            continue
        for include in re.findall(r'<ProjectReference\s+Include="([^"]+)"', text):
            target = posixpath.normpath(posixpath.join(posixpath.dirname(relative), include.replace("\\", "/")))
            if target not in files:
                problems.append(f"{relative}: the project reference '{include}' points to '{target}', which was not generated")
    return problems


def given_name_problems(files: dict[str, str], name: str, with_tests: bool = True) -> list[str]:
    """Where a user reads the name, it is the name that was given: the README title, the test command and the welcome page title."""
    problems = []
    readme = files.get("README.md", "")
    if not readme.startswith(f"# {name}\n"):
        problems.append(f"README.md does not start with '# {name}'")
    if with_tests and f"dotnet test tests/{name}.Tests" not in readme:
        problems.append(f"README.md does not say 'dotnet test tests/{name}.Tests'")
    if f"<title>{name}</title>" not in files.get("resources/views/welcome.html", ""):
        problems.append(f"resources/views/welcome.html does not have <title>{name}</title>")
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


# ---------------------------------------------------------------------------------------------------------------------------------
# The matrix (PDR-025 D9 step 3): every value of every parameter once, and a few combinations
# ---------------------------------------------------------------------------------------------------------------------------------

def template_parameters(template_json_text: str) -> dict[str, tuple[str, list[str]]]:
    """The choice and bool parameters of template.json as name -> (default, values), read from the file so that a value added to the
    template is in the matrix without being listed a second time."""
    found = {}
    for name, symbol in json.loads(template_json_text).get("symbols", {}).items():
        if symbol.get("type") != "parameter":
            continue
        if symbol.get("datatype") == "choice":
            found[name] = (symbol["defaultValue"], [c["choice"] for c in symbol["choices"]])
        elif symbol.get("datatype") == "bool":
            found[name] = (symbol.get("defaultValue", "false"), ["false", "true"])
    return found


def matrix_entries(parameters: dict[str, tuple[str, list[str]]], combinations: list[dict[str, str]]) -> list[dict[str, str]]:
    """Each non-default value of each parameter on its own, then the combinations. An entry holds only what differs from the default."""
    singles = [{name: value} for name, (default, values) in parameters.items() for value in values if value != default]
    return singles + [dict(combination) for combination in combinations]


def effective_choice(parameters: dict[str, tuple[str, list[str]]], entry: dict[str, str]) -> dict[str, str]:
    return {name: entry.get(name, default) for name, (default, _) in parameters.items()}


def entry_args(entry: dict[str, str]) -> list[str]:
    """The arguments of `dotnet new naravel` for an entry; a bool parameter is a flag."""
    return [argument for name, value in entry.items() for argument in ([f"--{name}"] if value == "true" else [f"--{name}", value])]


def entry_tag(entry: dict[str, str]) -> str:
    return " ".join(f"{name}={value}" for name, value in entry.items()) or "default"


def _config_documents(files: dict[str, str], problems: list[str]) -> dict[str, dict]:
    """Every config/*.json as parsed JSON; the files carry whole-line // comments, which the configuration system of ASP.NET Core accepts."""
    documents = {}
    for relative, text in sorted(files.items()):
        if relative.startswith("config/") and relative.endswith(".json"):
            try:
                documents[relative] = json.loads(re.sub(r"(?m)^\s*//.*$", "", text))
            except ValueError as error:
                problems.append(f"{relative} is not valid JSON once the comment lines are removed: {error}")
    return documents


def _has_path(document: dict, parts: list[str]) -> bool:
    for part in parts:
        if not isinstance(document, dict) or part not in document:
            return False
        document = document[part]
    return True


def wiring_problems(files: dict[str, str], name: str, choice: dict[str, str]) -> list[str]:
    """What is half-wired in an application generated with these choices (PDR-025 D4: the generator never emits a half-wired combination).

    Every value of a parameter changes four things together: the package, the registration call (and its using), the active store in
    config/, and what the README tells the user to set. This compares all four with the ORACLE, and finds the opposite as well: a package,
    a call or a store that belongs to a value that was not chosen."""
    problems: list[str] = []
    chosen = {parameter: ORACLE[parameter][choice[parameter]] for parameter in ORACLE}
    with_tests = choice.get("no-tests") != "true"
    uses_storage = choice["queue"] == "file" or choice["database"] == "sqlite"
    needs_service = any(choice[parameter] == value for parameter, value in SERVICE_VARIABLES)

    csproj = files.get(f"{name}.csproj", "")
    try:
        ET.fromstring(csproj)
    except ET.ParseError as error:
        problems.append(f"{name}.csproj is not well-formed XML: {error}")
    packages = set(re.findall(r'<PackageReference\s+Include="(Naravel\.[^"]+)"', csproj))
    expected = BASE_PACKAGES | ({"Naravel.Database"} if choice["database"] != "none" else set()) | {c[0] for c in chosen.values() if c[0]}
    if packages != expected:
        problems.append(f"{name}.csproj references {sorted(packages)}, expected {sorted(expected)}")

    program = files.get("Program.cs", "")
    for kind, index, present in (("call", 1, lambda value: re.search(rf"\.{re.escape(value)}\(", program)),
                                 ("using", 2, lambda value: f"using {value};" in program)):
        every = {entry[index] for values in ORACLE.values() for entry in values.values() if entry[index]}
        wanted = {entry[index] for entry in chosen.values() if entry[index]}
        for value in sorted(every):
            if bool(present(value)) != (value in wanted):
                problems.append(f"Program.cs {'lacks' if value in wanted else 'has'} the {kind} {value}, "
                                f"which the choices {'need' if value in wanted else 'do not use'}")
    if bool(re.search(r"\.AddNaravelDatabase\(", program)) != (choice["database"] != "none"):
        problems.append(f"Program.cs {'lacks' if choice['database'] != 'none' else 'has'} AddNaravelDatabase for database={choice['database']}")
    if ('"storage"' in program) != uses_storage:
        problems.append(f"Program.cs {'lacks' if uses_storage else 'has'} the creation of storage/ (queue={choice['queue']}, database={choice['database']})")

    documents = _config_documents(files, problems)
    for parameter, (path, section, stores) in STORE_SECTIONS.items():
        if choice[parameter] == "none":
            if path in files:
                problems.append(f"{path} was generated for {parameter}=none")
            continue
        block = documents.get(path, {}).get(section, {})
        active = block.get("Default")
        store = block.get(stores, {}).get(active)
        if store is None:
            problems.append(f"{path}: Default is {active!r}, which is not one of the {stores}")
            continue
        if store.get("Driver") != chosen[parameter][3]:
            problems.append(f"{path}: the store {active!r} has Driver {store.get('Driver')!r}, expected {chosen[parameter][3]!r}")
        if sorted(block[stores]) != [active]:
            problems.append(f"{path}: {stores} holds {sorted(block[stores])}, expected only the active {active!r}")
        required = REQUIRED_KEY.get((parameter, choice[parameter]))
        if required and not str(store.get(required, "")).strip():
            problems.append(f"{path}: the store {active!r} has no {required}")
    mail = documents.get("config/mail.json", {}).get("Mail", {})
    if choice["mail"] == "log":
        if mail.get("Default") != "log" or "Mailers" in mail:
            problems.append("config/mail.json: mail=log must have Default 'log' and no Mailers")
    else:
        mailer = mail.get("Mailers", {}).get("smtp", {})
        if mail.get("Default") != "smtp" or mailer.get("Driver") != "smtp" or not str(mailer.get("Host", "")).strip():
            problems.append("config/mail.json: mail=smtp must have Default 'smtp' and a mailer 'smtp' with Driver 'smtp' and a Host")
    for relative, text in sorted(files.items()):
        if relative.startswith("config/") and re.search(r'(?i)"(password|pwd|secret)"\s*:|(password|pwd)=', text):
            problems.append(f"{relative}: a password is written into a file that is committed; it belongs in the environment or user secrets")

    readme = files.get("README.md", "")
    if ("## Services" in readme) != needs_service:
        problems.append(f"README.md {'lacks' if needs_service else 'has'} the Services section")
    wanted_variables = {SERVICE_VARIABLES[(p, choice[p])] for p in ORACLE if (p, choice[p]) in SERVICE_VARIABLES}
    for variable in sorted(set(SERVICE_VARIABLES.values())):
        if (variable in readme) != (variable in wanted_variables):
            problems.append(f"README.md {'does not name' if variable in wanted_variables else 'names'} {variable} for these choices")
        elif variable in wanted_variables and not any(_has_path(document, variable.split("__")) for document in documents.values()):
            problems.append(f"README.md names {variable}, which is not a key of the generated config/")
    for fragment, wanted, label in (("| `storage/` |", uses_storage, "the storage/ row"), ("| `tests/` |", with_tests, "the tests/ row"),
                                    ("dotnet test", with_tests, "the test command")):
        if (fragment in readme) != wanted:
            problems.append(f"README.md {'lacks' if wanted else 'has'} {label}")

    for relative, text in sorted(files.items()):
        if re.search(r"(?m)^\s*(//|<!--)\s*#(if|elseif|else|endif)\b", text):
            problems.append(f"{relative}: a conditional line of the template survived the generation")
    return problems


def _condition(expression: str, names: dict[str, object]) -> bool:
    """A condition of the template engine for the subset the template uses: symbol names, == and != with a string, ! , && and ||.
    The expressions come from this repository's own template folder, never from outside."""
    python = re.sub(r"!(?!=)", " not ", expression.replace("||", " or ").replace("&&", " and ")).replace("-", "_")
    try:
        return bool(eval(python, {"__builtins__": {}}, names))  # noqa: S307 (the repository's own template, evaluated with no builtins)
    except (SyntaxError, NameError, TypeError) as error:
        raise ValueError(f"the condition {expression!r} of the template cannot be evaluated ({error}): a marker must be a whole line "
                         f"and name only the symbols of template.json") from error


def template_symbols(template_json_text: str, choice: dict[str, str]) -> dict[str, object]:
    """The values the engine gives the symbols of template.json for a choice: parameters (a bool as a bool) and computed symbols."""
    names: dict[str, object] = {name.replace("-", "_"): (value == "true" if value in ("true", "false") else value) for name, value in choice.items()}
    for name, symbol in json.loads(template_json_text)["symbols"].items():
        if symbol.get("type") == "computed":
            names[name] = _condition(symbol["value"], names)
    return names


def render_conditionals(text: str, names: dict[str, object]) -> str:
    """Resolve the `//#if (...)` and `<!--#if (...) -->` lines of a file the way the template engine does: a conditional marker is a whole
    line, the lines of a branch that is not taken are dropped, and so are the marker lines. (An inline marker is not supported by this
    function, and is not reliable in the engine either: the template does not use one.)"""
    marker = re.compile(r"^\s*(?://|<!--)\s*#(if|elseif|else|endif)\b\s*(.*?)\s*(?:-->)?\s*$")
    out: list[str] = []
    stack: list[list[bool]] = []  # per open #if: [the branch taken so far, the current branch is active]
    for line in text.splitlines(keepends=True):
        found = marker.match(line)
        if not found:
            if all(active for _, active in stack):
                out.append(line)
            continue
        kind, expression = found.groups()
        outer = all(active for _, active in stack)
        if kind == "if":
            taken = outer and _condition(expression, names)
            stack.append([taken, taken])
        elif kind == "elseif":
            taken, _ = stack[-1]
            now = all(active for _, active in stack[:-1]) and not taken and _condition(expression, names)
            stack[-1] = [taken or now, now]
        elif kind == "else":
            taken, _ = stack[-1]
            stack[-1] = [True, all(active for _, active in stack[:-1]) and not taken]
        else:
            stack.pop()
    return "".join(out)


def check_entry(files: dict[str, str], name: str, choice: dict[str, str], sources: list[str], version: str) -> list[str]:
    """Everything that can be checked on the files of a generated application before it is built."""
    expected = expected_generated(sources, name, choice)
    problems = [f"{path} was not generated" for path in expected if path not in files]
    problems += [f"{path} was generated, but the choices leave it out" for path in sorted(files) if path not in expected]
    problems += (leftover_placeholders(files, name) + project_reference_problems(files)
                 + given_name_problems(files, name, choice.get("no-tests") != "true") + wiring_problems(files, name, choice))
    versions = referenced_versions(files.get(f"{name}.csproj", ""))
    if versions != {version}:
        problems.append(f"{name}.csproj references Naravel packages at {sorted(versions)}, expected only {version}")
    return problems


def matrix_line(built: list[str]) -> str:
    return (f"Matrix: {len(built)} combinations were generated, checked for a half-wired result and built, and their tests were run "
            f"(a project made with no-tests has none): {'; '.join(built)}. None of them was started: each would need its server or "
            f"broker. Only the default application was started.")


def summary_line(version: str) -> str:
    return (f"Template smoke passed: {TEMPLATE_PACKAGE} {version}: the default application (cache memory, mail log, queue file, "
            f"database sqlite) was generated, built, tested, published and started; /up and / answered 200 and POST /register "
            f"answered 202. Only the default combination was started. Project names tried: "
            f"{', '.join(name + (' (built)' if built else '') for name, _, built in NAME_CASES)}.")


def item_problems(text: str, relative: str, class_name: str, namespace: str, must_contain: str) -> list[str]:
    """What is wrong with a generated item file: the #error of an unknown namespace, a conditional marker or a placeholder that
    survived, a namespace other than the expected one, no type of the given name, or the line that proves the arguments were used."""
    problems = []
    if re.search(r"^\s*#error\b", text, re.MULTILINE):
        problems.append(f"{relative}: the namespace was not found and the file holds the #error that says so (the project was not restored, "
                        f"or the output folder is outside it)")
    elif not re.search(rf"^namespace {re.escape(namespace)};[ \t]*$", text, re.MULTILINE):
        problems.append(f"{relative}: the namespace is not {namespace}")
    problems += [f"{relative}: still contains {token}" for token in ITEM_TOKENS if token in text]
    if re.search(r"^\s*//#(if|elseif|else|endif)\b", text, re.MULTILINE):
        problems.append(f"{relative}: a conditional marker of the template was not processed")
    if not re.search(rf"\b(class|record) {re.escape(class_name)}\b", text):
        problems.append(f"{relative}: no type named {class_name}")
    if must_contain not in text:
        problems.append(f"{relative}: does not contain {must_contain!r}")
    return problems


def render_item(text: str, class_name: str, namespace: str = "", project_namespace: str = "", event: str = "UserRegistered") -> str:
    """The replacements of the engine for an item template, as the real engine was seen to apply them: only `//#if (<symbol> != "")`,
    `//#elseif`, `//#else` and `//#endif` on their own lines, then each symbol replaces its token. Used by the self-test, which has no
    dotnet; the real engine is the proof in the run. Anything else in a condition raises, so a new kind of condition cannot pass unseen."""
    symbols = {"namespace": namespace, "ProjectNamespace": project_namespace}
    out: list[str] = []
    stack: list[tuple[bool, bool]] = []  # (the enclosing block is live, a branch of this block was already taken)

    def live() -> bool:
        return all(parent for parent, _ in stack)

    for line in text.splitlines():
        marker = re.match(r'^\s*//#(if|elseif|else|endif)\b\s*(?:\((\w+) != ""\))?\s*$', line)
        if line.lstrip().startswith("//#") and not marker:
            raise ValueError(f"an item template holds a conditional that render_item does not know: {line.strip()}")
        if not marker:
            if live():
                out.append(line)
            continue
        kind, symbol = marker.group(1), marker.group(2)
        if kind == "if":
            taken = bool(symbols[symbol])
            stack.append((taken, taken))
        elif kind == "elseif":
            _, was = stack.pop()
            taken = not was and bool(symbols[symbol])
            stack.append((taken, was or taken))
        elif kind == "else":
            _, was = stack.pop()
            stack.append((not was, True))
        else:
            stack.pop()
    return (("\n".join(out) + "\n").replace("ItemNamespace", namespace).replace("ProjectRootNamespace", project_namespace)
            .replace("ItemEvent", event).replace(ITEM_SOURCE_NAME, class_name))


def items_line() -> str:
    return (f"Items: {len(ITEM_RUNS)} files were generated into the default application with the item templates "
            f"({', '.join(sorted({run[0] for run in ITEM_RUNS}))}), each in the namespace of the project or the one given with --namespace, "
            f"and the application was built with them.")


# The pages that name the starter's commands. R34.T06: the docs name no command that this script did not run.
DOC_PAGES = ["README.md", "docs/en/getting-started.md", "docs/nuget/Naravel.Templates.md"]


def doc_commands(markdown: str) -> list[list[str]]:
    """The commands of the starter that a page names, each as its words: every `dotnet new install`, every `dotnet new naravel...` and a
    `dotnet run` without --project, in fenced blocks and in inline code. Other commands (`dotnet add package`, `dotnet new web`, `dotnet run
    --project samples/...`) belong to other parts of the docs and are not looked at."""
    spans: list[str] = []
    in_fence = False
    for line in markdown.splitlines():
        if line.lstrip().startswith("```"):
            in_fence = not in_fence
        elif in_fence:
            spans.append(line)
        else:
            spans += re.findall(r"`([^`\n]+)`", line)
    commands = []
    for span in spans:
        for part in span.split("#")[0].split("&&"):  # a comment after #, and `a && b` is two commands
            try:
                words = shlex.split(part)
            except ValueError:
                continue
            if words[:3] == ["dotnet", "new", "install"] or (words[:2] == ["dotnet", "new"] and len(words) > 2 and words[2].startswith("naravel")) \
                    or (words[:2] == ["dotnet", "run"] and "--project" not in words):
                commands.append(words)
    return commands


def doc_command_problems(words: list[str], parameters: dict[str, tuple[str, list[str]]],
                         combinations: list[dict[str, str]] = COMBINATIONS, runs: list = ITEM_RUNS) -> list[str]:
    """Why the smoke did not run this command, or [] when it did. `dotnet run` and the item templates must match what the smoke runs
    (a name given with -n or a namespace or an event may be any); the options of `dotnet new naravel` must be real parameters with real
    values, and what differs from the defaults must be inside one run of the matrix or its combinations."""
    text = " ".join(words)
    if words[:3] == ["dotnet", "new", "install"]:
        identity = words[3].split("::")[0] if len(words) > 3 else ""
        return [] if identity == TEMPLATE_PACKAGE else [f"`{text}`: the smoke installs {TEMPLATE_PACKAGE}, not {identity or 'nothing'}"]
    if words[:2] == ["dotnet", "run"]:
        extra = [word for word in words[2:] if word not in DEV_RUN]
        return [] if not extra else [f"`{text}`: the smoke runs `{' '.join(DEV_RUN)}`, which does not have {' '.join(extra)}"]
    short = words[2]
    flags = {name for name, (_, values) in parameters.items() if values == ["false", "true"]}
    options: dict[str, str] = {}
    index = 3
    while index < len(words):
        name = words[index]
        if not name.startswith("-"):
            return [f"`{text}`: {name!r} is not an option"]
        if name.lstrip("-") in flags:
            options[name], index = "true", index + 1
        else:
            options[name], index = (words[index + 1] if index + 1 < len(words) else ""), index + 2
    if short == "naravel":
        chosen: dict[str, str] = {}
        for name, value in options.items():
            key = name.lstrip("-")
            if name == "-n":
                continue
            if key not in parameters:
                return [f"`{text}`: {name} is not a parameter of the template"]
            default, values = parameters[key]
            if value not in values:
                return [f"`{text}`: {name} {value} is not one of {', '.join(values)}"]
            if value != default:
                chosen[key] = value
        if chosen and not any(all(entry.get(key) == value for key, value in chosen.items()) for entry in matrix_entries(parameters, combinations)):
            return ["`%s`: no run of the smoke has %s together" % (text, ", ".join("%s=%s" % pair for pair in chosen.items()))]
        return []
    candidates = [run for run in runs if run[0] == short]
    if not candidates:
        return [f"`{text}`: the smoke does not run {short}"]

    def covered(run: tuple, name: str, value: str) -> bool:
        return name == "-n" or (value == run[2] if name == "-o" else name in run[3])

    if not any(all(covered(run, name, value) for name, value in options.items()) for run in candidates):
        runs_text = "; ".join(" ".join(["-o", run[2], *run[3]]) for run in candidates)
        return [f"`{text}`: no run of the smoke has these options (the runs have {runs_text})"]
    return []


def docs_problems(root: Path, parameters: dict[str, tuple[str, list[str]]]) -> list[str]:
    """Every starter command of every page that the smoke did not run, a page that names none, and a getting-started table that does not
    list every option and value of template.json."""
    problems = []
    for page in DOC_PAGES:
        text = (root / page).read_text(encoding="utf-8")
        commands = doc_commands(text)
        if not commands:
            problems.append(f"{page}: names no command of the starter")
        for words in commands:
            problems += [f"{page}: {problem}" for problem in doc_command_problems(words, parameters)]
    text = (root / "docs/en/getting-started.md").read_text(encoding="utf-8")
    section = text.split("## Create an app", 1)[-1].split("\n## ", 1)[0]
    rows = {match.group(1): match.group(2) for match in re.finditer(r"^\| `--([\w-]+)` \| (.*?) \|", section, re.MULTILINE)}  # option -> its values cell
    for name, (_, values) in parameters.items():
        if name not in rows:
            problems.append(f"docs/en/getting-started.md: the Create an app table does not list --{name}")
            continue
        problems += [f"docs/en/getting-started.md: the values of --{name} in the Create an app table do not include {value}"
                     for value in values if values != ["false", "true"] and f"`{value}`" not in rows[name]]
    return problems


def nupkg_problems(names: list[str], nuspec_text: str, sources: list[str], item_sources: dict[str, list[str]] | None = None) -> list[str]:
    """What is wrong with the packed template: its package type, or a source file that is not packed (or a bin/obj file that is).
    item_sources maps the folder of each item template (content/<folder>/) to its files."""
    problems = []
    present = set(names)
    for folder, files in sorted((item_sources or {}).items()):
        gone = [relative for relative in files if f"content/{folder}/{relative}" not in present]
        if gone:
            problems.append(f"{len(gone)} of {len(files)} files of the item template {folder} are not in the package under content/{folder}/, "
                            f"for example content/{folder}/{gone[0]}")
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
    stray = sorted(name for name in names if name.startswith("content/") and re.search(r"/(bin|obj)/", name))
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

EXCERPTS: list[tuple[str, list[str]]] = []  # what annotate showed, for the job summary: GitHub keeps only 10 annotations per step


def annotate(phase: str, output: str) -> None:
    """Make the cause readable in the check annotations and the job summary; the raw log is not readable without authentication."""
    lines = [line.strip()[:900] for line in output.splitlines() if re.search(r"error|NU\d{4}|failed|exception|timed out|skipping", line, re.I)]
    lines = list(dict.fromkeys(lines))[:15]
    EXCERPTS.append((phase, lines))
    if not os.environ.get("GITHUB_ACTIONS"):
        return
    for line in lines:
        print(f"::error title=template-smoke ({phase})::{line}")


def absolute_directory(path: Path) -> Path:
    """The packed directory as an absolute path. Every dotnet call of the smoke runs in a temporary folder, where a relative
    --local ./nupkgs-template names nothing: `dotnet new install` then answers "is not supported" and a relative package source in
    the nuget.config would be read from the wrong folder."""
    return path.expanduser().resolve()


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


def stop_process(process: subprocess.Popen) -> None:
    """Stop the process and everything it started: `dotnet run` starts the application as a child, so the whole group is signalled."""
    leads_group = hasattr(os, "killpg")
    for sig, wait in ((signal.SIGTERM, 20), (signal.SIGKILL, None)):
        try:
            if leads_group:
                os.killpg(process.pid, sig)  # the process leads its own group (start_new_session in start_and_probe)
            elif sig == signal.SIGTERM:
                process.terminate()
            else:
                process.kill()
        except (ProcessLookupError, PermissionError):
            pass
        try:
            process.wait(timeout=wait)
            return
        except subprocess.TimeoutExpired:
            continue


def start_and_probe(folder: Path, name: str, env: dict[str, str], log_path: Path, command: list[str] | None = None) -> list[str]:
    """Start the application from `folder` (the publish output, or the application folder for `dotnet run`) and return what did not
    hold (empty when everything did). The command is `dotnet <name>.dll` unless it is given."""
    problems: list[str] = []
    port = free_port()
    base = f"http://127.0.0.1:{port}"
    run_env = dict(env, ASPNETCORE_URLS=base)
    with open(log_path, "w", encoding="utf-8") as log:
        process = subprocess.Popen(command or ["dotnet", f"{name}.dll"], cwd=folder, env=run_env, stdout=log, stderr=subprocess.STDOUT,
                                   start_new_session=True)
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
            stop_process(process)
    return problems


def write_summary(version: str, failed: list[str], notes: list[str], started: bool = True) -> None:
    path = os.environ.get("GITHUB_STEP_SUMMARY")
    if not path:
        return
    lines = ["### Template smoke", f"- Template pack: `{TEMPLATE_PACKAGE}` `{version}`",
             *(["- Combination started: default (cache memory, mail log, queue file, database sqlite)"] if started else []),
             f"- Result: {'passed' if not failed else 'FAILED in: ' + ', '.join(failed)}", *[f"- {note}" for note in notes], ""]
    for phase, excerpt in EXCERPTS:
        if excerpt:
            lines += [f"<details><summary>{phase}: the first {len(excerpt)} error lines</summary>", "", "```", *excerpt, "```", "</details>", ""]
    with open(path, "a", encoding="utf-8") as handle:
        handle.write("\n".join(lines))


def matrix_phase(runner, work: Path, template_json_text: str, sources: list[str], version: str) -> tuple[list[str], list[str]]:
    """Generate, check, build and test every entry of the matrix; none is started. Returns (problems, the tags of the entries that
    passed). Every entry is tried even when one fails, so a single run shows all of them."""
    parameters = template_parameters(template_json_text)
    problems: list[str] = []
    built: list[str] = []
    for index, entry in enumerate(matrix_entries(parameters, COMBINATIONS)):
        tag, choice = entry_tag(entry), effective_choice(parameters, entry)
        folder = work / "matrix" / str(index)
        folder.mkdir(parents=True)
        ok, output = runner.run(f"dotnet new naravel -n {APP_NAME} ({tag})", ["dotnet", "new", "naravel", "-n", APP_NAME, *entry_args(entry)],
                                folder, 180, True)
        project = folder / APP_NAME
        if not ok or not project.is_dir():
            problems.append(f"{tag}: dotnet new naravel did not generate a project")
            annotate("matrix", output)
            continue
        found = check_entry(read_text_files(project), APP_NAME, choice, sources, version)
        command = (["test", f"tests/{APP_NAME}.Tests/{APP_NAME}.Tests.csproj", "-c", "Release"] if choice.get("no-tests") != "true"
                   else ["build", f"{APP_NAME}.csproj", "-c", "Release"])
        built_ok, output = runner.dotnet(f"{command[0]} the application generated with {tag}", command, project)
        if not built_ok:
            found.append(f"it does not {command[0]}")
            annotate(f"matrix ({tag})", output)
        problems += [f"{tag}: {problem}" for problem in found]
        if not found:
            built.append(tag)
    return problems, built


def item_phase(runner, app: Path, name: str) -> list[str]:
    """Generate every ITEM_RUNS entry into the default application (already restored by the build) the way the docs say: from the
    project folder, with -o set to a folder of the application. Then build the application with the new files. Every run is tried
    even when one fails, so a single run shows all of them."""
    problems: list[str] = []
    for short, class_name, folder, arguments, namespace, must_contain in ITEM_RUNS:
        relative = f"{folder}/{class_name}.cs"
        ok, output = runner.run(f"dotnet new {short} -n {class_name} -o {folder} {' '.join(arguments)}".rstrip(),
                                ["dotnet", "new", short, "-n", class_name, "-o", folder, *arguments], app, 120, True)
        path = app / relative
        if not ok or not path.is_file():
            problems.append(f"{short}: dotnet new {short} did not generate {relative}")
            annotate("items", output)
            continue
        problems += [f"{short}: {problem}" for problem in
                     item_problems(path.read_text(encoding="utf-8", errors="replace"), relative, class_name,
                                   namespace.format(app=name), must_contain)]
    if not problems:
        built, output = runner.dotnet("build the application with the generated item files", ["build", f"{name}.csproj", "-c", "Release"], app)
        if not built:
            problems.append("the application does not build with the generated item files")
            annotate("items", output)
    return problems


def report_problems(name: str, problems: list[str], failed: list[str]) -> bool:
    """Print what a phase found; True when it found something (and then the phase is added to `failed`)."""
    if not problems:
        print(f"ok: {name}", flush=True)
        return False
    for problem in problems:
        print(f"ERROR: {name}: {problem}", file=sys.stderr, flush=True)
        if os.environ.get("GITHUB_ACTIONS"):
            print(f"::error title=template-smoke ({name})::{problem[:900]}")
    failed.append(name)
    return True


def published_smoke(runner, work: Path, version: str) -> tuple[list[str], list[str]]:
    """The pack as it is on nuget.org, after a release: install `Naravel.Templates::<version>`, generate the default application, check
    that it pins that version and has no placeholder, and build it. Nothing is read from the repository's template folder, because the
    repository may have moved on since the release; the packages come from nuget.org only. Returns (failed phases, notes)."""
    failed: list[str] = []
    notes: list[str] = []
    installed = False
    package = f"{TEMPLATE_PACKAGE}::{version}"
    try:
        ok, output = runner.run(f"install {package}", ["dotnet", "new", "install", package], work, 300, True)
        if not ok:
            annotate("install", output)
            return ["install"], notes
        installed = True
        ok, output = runner.run("dotnet new list naravel", ["dotnet", "new", "list", "naravel"], work, 120, True)
        if not ok or "Naravel application" not in output or not re.search(r"\bnaravel\b", output):
            annotate("install", output)
            return ["install"], notes
        ok, output = runner.run(f"dotnet new naravel -n {APP_NAME}", ["dotnet", "new", "naravel", "-n", APP_NAME], work, 180, True)
        app = work / APP_NAME
        if not ok or not app.is_dir():
            annotate("generate", output)
            return ["generate"], notes
        generated = read_text_files(app)
        problems = [] if f"{APP_NAME}.csproj" in generated else [f"{APP_NAME}.csproj was not generated"]
        problems += leftover_placeholders(generated, APP_NAME) + project_reference_problems(generated) + given_name_problems(generated, APP_NAME)
        versions = referenced_versions(generated.get(f"{APP_NAME}.csproj", ""))
        if versions != {version}:
            problems.append(f"{APP_NAME}.csproj references Naravel packages at {sorted(versions)}, expected only {version}")
        if report_problems("generate", problems, failed):
            return failed, notes
        ok, output = runner.dotnet("build the application", ["build", f"{APP_NAME}.csproj", "-c", "Release"], app)
        if not ok:
            annotate("build", output)
            return ["build"], notes
        print("ok: build", flush=True)
        notes.append(f"Published pack {version}: installed from nuget.org, {APP_NAME} generated with every Naravel package at {version}, and built.")
    finally:
        if installed:
            runner.run(f"dotnet new uninstall {TEMPLATE_PACKAGE}", ["dotnet", "new", "uninstall", TEMPLATE_PACKAGE], work, 120)
    return failed, notes


def smoke(local: Path, work: Path, extra_args: list[str]) -> tuple[list[str], list[str]]:
    """Return (failed phases, notes). Every phase prints its own output; a failed phase stops the phases that depend on it."""
    consumer = load_consumer_smoke()
    local = absolute_directory(local)
    failed: list[str] = []
    notes: list[str] = []
    installed = False
    version = consumer.packed_version(local, [TEMPLATE_PACKAGE])
    nupkg = local / f"{TEMPLATE_PACKAGE}.{version}.nupkg"
    sources = template_files(TEMPLATE_DIRECTORY)

    def phase_failed(name: str, problems: list[str]) -> bool:
        return report_problems(name, problems, failed)

    # 1. The packed template.
    with zipfile.ZipFile(nupkg) as package:
        entries = package.namelist()
        nuspec = next((package.read(n).decode("utf-8", "replace") for n in entries if n.endswith(".nuspec")), "")
        problems = nupkg_problems(entries, nuspec, sources, item_template_files())
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
        installed = True
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
        problems += leftover_placeholders(generated, APP_NAME) + project_reference_problems(generated) + given_name_problems(generated, APP_NAME)
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

        # 7b. The same application, started the way the docs say: `dotnet run` in its folder.
        problems = start_and_probe(app, APP_NAME, runner.env, work / "dev-run.log", DEV_RUN)
        if problems:
            tail = (work / "dev-run.log").read_text(encoding="utf-8", errors="replace")[-4000:]
            print("--- dotnet run output (last 4000 characters) ---\n" + tail, flush=True)
            annotate("dev run", tail)
        phase_failed("dev run", problems)  # a failure here is reported, and the other phases still run

        # Names that are not valid C# namespaces as they are (a hyphen, a leading digit): file names, paths and what a user reads keep
        # the name, the namespace is made safe. The first case is also built and its tests are run.
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
            problems += [f"-n {name}: {problem}" for problem in leftover_placeholders(files, name) + namespace_problems(files, name, expected)
                         + project_reference_problems(files) + given_name_problems(files, name)]
            if build:
                built, output = runner.dotnet(f"build the application and run the tests of a project named {name}",
                                              ["test", f"tests/{name}.Tests/{name}.Tests.csproj", "-c", "Release"], project)
                if not built:
                    problems.append(f"-n {name}: the generated application and its tests project do not build, or a test fails")
                    annotate("names", output)
        if not phase_failed("names", problems):
            notes.append("Project names generated: " + ", ".join(f"{name} (namespace {expected})" for name, expected, _ in NAME_CASES))

        # 9. The matrix: every value of every parameter once, and the combinations (see matrix_phase).
        problems, built = matrix_phase(runner, work, json_text, sources, version)
        if not phase_failed("matrix", problems):
            notes.append(matrix_line(built))
            print(matrix_line(built), flush=True)

        # 10. The item templates, in the default application (see item_phase).
        if not phase_failed("items", item_phase(runner, app, APP_NAME)):
            notes.append(items_line())
            print(items_line(), flush=True)
        if not failed:
            print(summary_line(version), flush=True)
    finally:
        if installed:  # leave the machine as it was; a failed install has nothing to remove
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
    parameters = template_parameters(text)
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

    def generate(name: str, namespace: str, choice: dict[str, str] | None = None) -> dict[str, str]:
        """The replacements of the engine, applied to the template folder as the real engine was seen to apply them: the source name
        becomes the name in a file name but its safe form inside a file; each derived symbol replaces its own token by its value; the
        conditional lines are resolved for the choice and the sources.modifiers of template.json leave files out."""
        names = template_symbols(text, effective_choice(parameters, choice or {}))
        excluded = [pattern for modifier in json.loads(text)["sources"][0]["modifiers"] if _condition(modifier["condition"], names)
                    for pattern in modifier["exclude"]]
        out = {}
        for relative in sources:
            if relative.startswith(".template.config/") or any(fnmatch.fnmatch(relative, pattern) for pattern in excluded):
                continue
            try:
                content = (TEMPLATE_DIRECTORY / relative).read_text(encoding="utf-8")
            except UnicodeDecodeError:
                continue
            if relative.endswith((".cs", ".csproj", ".json", ".md")):  # the engine does not resolve them in other files, html among them
                content = render_conditionals(content, names)
            out[generated_path(relative, name)] = (content.replace(SOURCE_NAME, namespace).replace(NAMESPACE_TOKEN, namespace)
                                                   .replace(PROJECT_NAME_TOKEN, name).replace(VERSION_PLACEHOLDER, "0.2.0"))
        return out

    for name, expected, _ in NAME_CASES:
        generated = generate(name, apply_forms(json_text_for_forms, name))
        expect(f"a generation with -n {name} has the namespace {expected}",
               namespace_problems(generated, name, expected) == [] and leftover_placeholders(generated, name) == [])
    expect("the plain replacement of the token by a hyphenated name is found",
           len(namespace_problems(generate("my-blog", "my-blog"), "my-blog", "my_blog")) > 3)
    contents = {relative: text for relative, text in read_text_files(TEMPLATE_DIRECTORY).items() if not relative.startswith(".template.config/")}
    expect("the template's files are read", len(contents) > 10 and ".gitignore" in contents)
    expect("no file of the template writes the source name in its content (only file names carry it; the template engine rewrites the "
           "name inside a file, so a path or a name written with it is wrong for a name such as my-blog)",
           not [relative for relative, text in contents.items() if SOURCE_NAME in text])
    project_symbol = json.loads(json_text_for_forms)["symbols"][PROJECT_NAME_TOKEN]
    expect("the derived symbol of the project name replaces its token with the name as it was given",
           project_symbol["replaces"] == PROJECT_NAME_TOKEN and project_symbol["valueSource"] == "name"
           and project_symbol["valueTransform"] == "identity")

    # The failure of the first names run: the engine wrote my_blog.csproj into the reference of the tests project, the file is
    # my-blog.csproj, NuGet skipped the reference and the tests project compiled without the application.
    for name, expected, _ in NAME_CASES:
        generated = generate(name, apply_forms(json_text_for_forms, name))
        expect(f"every project reference of a generation with -n {name} points to a generated file", project_reference_problems(generated) == [])
        expect(f"a generation with -n {name} shows the name as it was given", given_name_problems(generated, name) == [])
    reference = "tests/my-blog.Tests/my-blog.Tests.csproj"
    broken = dict(generate("my-blog", "my_blog"))
    broken[reference] = broken[reference].replace("my-blog.csproj", "my_blog.csproj")
    found = project_reference_problems(broken)
    expect("a reference to a file that was not generated is found and named", len(found) == 1 and "my_blog.csproj" in found[0] and reference in found[0])
    expect("a reference written with backslashes and parent folders is resolved",
           project_reference_problems({"a/b/b.csproj": '<ProjectReference Include="..\\a.csproj" />', "a/a.csproj": ""}) == [])
    expect("a project file without references has no problems", project_reference_problems({"a.csproj": "<Project />"}) == [])
    unsafe = dict(generate("my-blog", "my_blog"))
    unsafe["README.md"] = unsafe["README.md"].replace("my-blog", "my_blog")
    expect("a README that shows the safe form instead of the given name is found", len(given_name_problems(unsafe, "my-blog")) == 2)
    expect("a welcome page that shows the safe form is found",
           len(given_name_problems({**generate("my-blog", "my_blog"), "resources/views/welcome.html": "<title>my_blog</title>"}, "my-blog")) == 1)
    expect("a surviving project name token is found", len(leftover_placeholders({"a.md": "# AppProjectName"}, "Blog")) == 1)
    expect("the tests project references the application through the project name token",
           f"..\\..\\{PROJECT_NAME_TOKEN}.csproj" in (TEMPLATE_DIRECTORY / f"tests/{SOURCE_NAME}.Tests/{SOURCE_NAME}.Tests.csproj").read_text(encoding="utf-8"))
    for project in (f"{SOURCE_NAME}.csproj", f"tests/{SOURCE_NAME}.Tests/{SOURCE_NAME}.Tests.csproj"):
        expect(f"{project} sets RootNamespace from the token",
               f"<RootNamespace>{NAMESPACE_TOKEN}" in (TEMPLATE_DIRECTORY / project).read_text(encoding="utf-8"))


    # The matrix. The parameters are read from template.json and the oracle is written out here: a value that one has and the other
    # lacks fails here, and not as a surprise in CI.
    expect("template.json has the four choices and no-tests", set(parameters) == set(ORACLE) | {"no-tests"})
    for parameter, values in ORACLE.items():
        expect(f"the oracle knows every {parameter} value of template.json and no other", set(values) == set(parameters[parameter][1]))
        expect(f"the default of {parameter} is its first value", parameters[parameter][0] == parameters[parameter][1][0])
    entries = matrix_entries(parameters, COMBINATIONS)
    expect("every non-default value is an entry on its own",
           all({name: value} in entries for name, (default, values) in parameters.items() for value in values if value != default))
    expect("the default alone is not an entry, and there are 11 single values", {} not in entries and len(entries) == 11 + len(COMBINATIONS))
    expect("a value added to the template joins the matrix without being listed again",
           {"queue": "sqs"} in matrix_entries({"queue": ("file", ["file", "sqs"])}, []))
    expect("every combination uses parameters and values that exist",
           all(name in parameters and value in parameters[name][1] for combination in COMBINATIONS for name, value in combination.items()))
    expect("a choice is passed with its value and a bool as a flag",
           entry_args({"database": "postgres", "no-tests": "true"}) == ["--database", "postgres", "--no-tests"] and entry_tag({}) == "default")
    sample = ('//#if (queue == "redis")\nR\n//#elseif (queue == "kafka")\nK\n//#else\nF\n//#endif\n'
              '<!--#if (!no-tests) -->\nT\n<!--#endif -->\n')
    for choice, rendered in (({"queue": "kafka"}, "K\nT\n"), ({"queue": "redis", "no-tests": "true"}, "R\n"), ({}, "F\nT\n")):
        expect(f"the conditional lines resolve for {choice}", render_conditionals(sample, template_symbols(text, effective_choice(parameters, choice))) == rendered)
    nested = '<!--#if (NeedsService) -->\nA\n<!--#if (queue == "redis") -->\nB\n<!--#endif -->\n<!--#endif -->\n'
    for choice, rendered in (({}, ""), ({"queue": "redis"}, "A\nB\n"), ({"database": "postgres"}, "A\n")):
        expect(f"nested conditional lines resolve for {choice}",
               render_conditionals(nested, template_symbols(text, effective_choice(parameters, choice))) == rendered)
    for entry in [{}] + entries:
        choice = effective_choice(parameters, entry)
        found = check_entry(generate("Blog", "Blog", choice), "Blog", choice, sources, "0.2.0")
        expect(f"the template rendered for {entry_tag(entry)} has no problems ({found[:1]})", found == [])
    expect("a template without a tests project and without a README test command is accepted",
           given_name_problems({"README.md": "# Blog\n", "resources/views/welcome.html": "<title>Blog</title>"}, "Blog", with_tests=False) == [])

    def broken(entry: dict[str, str], path: str, old: str, new: str) -> list[str]:
        """The problems of the rendered template after one edit: each edit is a half-wired result that must be found."""
        choice = effective_choice(parameters, entry)
        files = dict(generate("Blog", "Blog", choice))
        if old not in files.get(path, ""):
            return [f"the mutation target {old!r} is not in {path}"]
        files[path] = files[path].replace(old, new)
        return check_entry(files, "Blog", choice, sources, "0.2.0")

    mutations = [
        ("a package that does not match the queue", {"queue": "redis"}, "Blog.csproj", "Naravel.Queue.Redis", "Naravel.Queue.File", "references"),
        ("a call that does not match the queue", {"queue": "redis"}, "Program.cs", ".AddRedisDriver(", ".AddFileDriver(", "lacks the call AddRedisDriver"),
        ("a using that does not match the queue", {"queue": "redis"}, "Program.cs", "using Naravel.Queue.Redis;", "using Naravel.Queue.File;",
         "lacks the using Naravel.Queue.Redis"),
        ("the driver postgres written instead of pgsql", {"database": "postgres"}, "config/database.json", '"pgsql"', '"postgres"', "expected 'pgsql'"),
        ("a default that names no store", {"queue": "kafka"}, "config/queue.json", '"Default": "kafka"', '"Default": "default"', "Default is 'default'"),
        ("a second store beside the active one", {"cache": "redis"}, "config/cache.json", '"Stores": {', '"Stores": { "memory": { "Driver": "memory" },',
         "expected only the active"),
        ("a password in a committed file", {"database": "postgres"}, "config/database.json", "Username=app", "Username=app;Password=x", "a password is written"),
        ("a conditional line that survived", {}, "Program.cs", "using Naravel.Events;", "//#if (x)\nusing Naravel.Events;", "conditional line"),
        ("a README without the Services section", {"cache": "redis"}, "README.md", "## Services", "## Service", "lacks the Services section"),
        ("a README that names a variable that is not a key", {"mail": "smtp"}, "README.md", "Mail__Mailers__smtp__Host", "Mail__Mailers__smtp__Server",
         "does not name Mail__Mailers__smtp__Host"),
        ("a missing storage/ creation", {}, "Program.cs", '"storage"', '"store"', "creation of storage/"),
        ("no registration of the database", {"database": "mysql"}, "Program.cs", ".AddNaravelMySqlDatabase(", ".AddNaravelDatabase(",
         "lacks the call AddNaravelMySqlDatabase"),
    ]
    for label, entry, path, old, new, fragment in mutations:
        expect(f"{label} is found", any(fragment in problem for problem in broken(entry, path, old, new)))
    with_tests_files = dict(generate("Blog", "Blog", effective_choice(parameters, {"no-tests": "true"})))
    with_tests_files["tests/Blog.Tests/X.cs"] = ""
    expect("a tests project that no-tests should have left out is found",
           any("the choices leave it out" in problem for problem in check_entry(with_tests_files, "Blog", effective_choice(parameters, {"no-tests": "true"}), sources, "0.2.0")))
    expect("a version that is not the packed one is found",
           any("expected only 9.9.9" in problem for problem in check_entry(generate("Blog", "Blog"), "Blog", effective_choice(parameters, {}), sources, "9.9.9")))
    expect("the matrix line names the count, says none was started and that only the default was",
           all(part in matrix_line(["a", "b"]) for part in ("2 combinations", "a; b", "None of them was started", "Only the default application was started")))

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
    relative = absolute_directory(Path("nupkgs-template"))
    expect("a relative packed directory becomes absolute, below the current directory", relative.is_absolute() and relative.name == "nupkgs-template"
           and relative.parent == Path.cwd().resolve())
    expect("an absolute packed directory stays as it is", absolute_directory(Path("/tmp")) == Path("/tmp").resolve())
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

    # The item templates (R34.T05): the folders under content/, their template.json, what the engine makes of them (rehearsed by
    # render_item; the real engine is the proof in the run) and the package that holds them.
    item_files = item_template_files()
    folders = sorted(item_files)
    expect("the item templates are the folders of content/ but the application's, and the table of runs names exactly those",
           folders == sorted({run[0] for run in ITEM_RUNS}) and len(folders) == 5)
    identities = set()
    for folder in folders:
        directory = CONTENT_DIRECTORY / folder
        document = json.loads((directory / ".template.config" / "template.json").read_text(encoding="utf-8"))
        symbols = document.get("symbols", {})
        identities.add(document.get("identity"))
        expect(f"{folder}: the short name is the folder, the type is item and the source name is {ITEM_SOURCE_NAME}",
               document.get("shortName") == folder and document.get("tags", {}).get("type") == "item" and document.get("sourceName") == ITEM_SOURCE_NAME)
        expect(f"{folder}: the namespace parameter is empty by default and replaces ItemNamespace",
               symbols.get("namespace", {}).get("type") == "parameter" and symbols["namespace"].get("defaultValue") == ""
               and symbols["namespace"].get("replaces") == "ItemNamespace")
        expect(f"{folder}: the root namespace of the project is bound from the msbuild property RootNamespace",
               symbols.get("ProjectNamespace", {}).get("type") == "bind" and symbols["ProjectNamespace"].get("binding") == "msbuild:RootNamespace"
               and symbols["ProjectNamespace"].get("defaultValue") == "" and symbols["ProjectNamespace"].get("replaces") == "ProjectRootNamespace")
        expect(f"{folder}: holds template.json and {ITEM_SOURCE_NAME}.cs and nothing else",
               item_files[folder] == [".template.config/template.json", f"{ITEM_SOURCE_NAME}.cs"])
        item_text = (directory / f"{ITEM_SOURCE_NAME}.cs").read_text(encoding="utf-8")
        for short, class_name, run_folder, arguments, namespace, must_contain in (run for run in ITEM_RUNS if run[0] == folder):
            options = dict(zip(arguments[::2], arguments[1::2]))
            label = f"{short} {class_name}"
            given, event = options.get("--namespace", ""), options.get("--event", "UserRegistered")
            try:
                rendered = render_item(item_text, class_name, given, "Blog", event)
            except ValueError as error:  # a conditional the rehearsal does not know: a failure of its own, not a traceback
                expect(f"{label}: {error}", False)
                continue
            expect(f"{label}: the file the engine makes from the template has no problem",
                   item_problems(rendered, f"{run_folder}/{class_name}.cs", class_name, namespace.format(app="Blog"), must_contain) == [])
            if not given:
                unknown = render_item(item_text, class_name, "", "", event)
                expect(f"{label}: a project that is not restored gives the #error and not a file without a namespace",
                       any("#error" in problem for problem in item_problems(unknown, "x.cs", class_name, namespace.format(app="Blog"), must_contain)))
                expect(f"{label}: a namespace that does not follow the table is found",
                       any("namespace is not" in problem for problem in item_problems(rendered.replace(".App.", ".Apps."), "x.cs", class_name,
                                                                                      namespace.format(app="Blog"), must_contain)))
    expect("every item template has an identity of its own", len(identities) == len(folders) and None not in identities)
    job_text = (CONTENT_DIRECTORY / "naravel-job" / "NaravelItem.cs").read_text(encoding="utf-8")
    expect("a --namespace wins over the project namespace, and is all a project that is not restored needs",
           all("namespace Acme.Billing;" in render_item(job_text, "X", "Acme.Billing", project) for project in ("Blog", "")))
    sample = render_item(job_text, "SendReceipt", "", "Blog")
    mutations = {
        "a surviving token": sample + "ItemNamespace",
        "a conditional marker that was not processed": '//#if (namespace != "")\n' + sample,
        "a missing type": sample.replace("SendReceipt", "Other"),
        "a missing line the arguments must produce": sample.replace(" : Job", ""),
    }
    for what, mutated in mutations.items():
        expect(f"item_problems finds {what}", bool(item_problems(mutated, "x.cs", "SendReceipt", "Blog.App.Jobs", "class SendReceipt : Job")))
    expect("item_problems accepts the file the engine makes", item_problems(sample, "x.cs", "SendReceipt", "Blog.App.Jobs", "class SendReceipt : Job") == [])
    try:
        render_item('//#if (other == "x")\n', "X")
        unknown_condition = False
    except ValueError:
        unknown_condition = True
    expect("a condition the rehearsal does not know is refused and not guessed", unknown_condition)

    with_items = [f"content/naravel/{s}" for s in sources] + [f"content/{f}/{r}" for f, files in item_files.items() for r in files] + ["x.nuspec"]
    expect("a package with the application and every item template has no problems", nupkg_problems(with_items, good_nuspec, sources, item_files) == [])
    expect("an item template that is not in the package is reported with its folder",
           any("naravel-job" in p for p in nupkg_problems([n for n in with_items if "/naravel-job/" not in n], good_nuspec, sources, item_files)))
    expect("a build output in an item folder is reported",
           len(nupkg_problems(with_items + ["content/naravel-job/obj/x.dll"], good_nuspec, sources, item_files)) == 1)
    expect("without item_sources the item folders are not looked at", nupkg_problems(with_items[:len(sources)] + ["x.nuspec"], good_nuspec, sources) == [])
    expect("the pack project packs everything below content/, so the item folders need no rule of their own",
           [item.get("Include") for item in content_items] == ["content\\**\\*"])
    expect("the items line names the count and every template", all(part in items_line() for part in (f"{len(ITEM_RUNS)} files", *folders)))

    # R34.T06: the docs name no command that the smoke did not run.
    parameters = template_parameters((TEMPLATE_DIRECTORY / ".template.config" / "template.json").read_text(encoding="utf-8"))
    found = docs_problems(ROOT, parameters)
    expect("every starter command of the docs is one that the smoke runs" + (": " + "; ".join(found) if found else ""), not found)
    for page in DOC_PAGES:
        expect(f"{page}: the parser finds starter commands in it", bool(doc_commands((ROOT / page).read_text(encoding="utf-8"))))
    sample = ("```sh\ndotnet new web -n Hello && cd Hello   # not the starter\ndotnet new install Naravel.Templates && dotnet new naravel -n Blog # a comment\n```\n\n"
              "Then `dotnet run`, not `dotnet run --project samples/X.csproj`, and not `--database postgres`.\n")
    expect("the parser takes the starter's commands from blocks and inline code, and leaves the others",
           doc_commands(sample) == [["dotnet", "new", "install", "Naravel.Templates"], ["dotnet", "new", "naravel", "-n", "Blog"], ["dotnet", "run"]])
    for command in ("dotnet new install Naravel.Templates", "dotnet new install Naravel.Templates::0.3.0", "dotnet new naravel -n Blog",
                    "dotnet new naravel -n Blog --database sqlite", "dotnet new naravel -n Blog --database postgres --cache redis --queue redis",
                    "dotnet new naravel -n Shop --no-tests --database sqlserver --queue rabbitmq", "dotnet new naravel-job -n SendWelcome -o app/Jobs",
                    "dotnet new naravel-job -n X -o app/Jobs --namespace Acme.X", "dotnet new naravel-listener -n X -o app/Listeners --event OrderShipped",
                    "dotnet run", "dotnet run -c Release"):
        expect(f"the smoke runs: {command}", doc_command_problems(shlex.split(command), parameters) == [])
    for command, why in {"dotnet new install ./Naravel/templates/Naravel.Templates/content/naravel": "a path, not the package the smoke installs",
                         "dotnet new naravel -n Blog --database oracle": "a value that is not there",
                         "dotnet new naravel -n Blog --cashe redis": "a parameter that is not there",
                         "dotnet new naravel -n Blog --database postgres --queue memory": "a combination no run has",
                         "dotnet new naravel -n Blog -o out": "an option the smoke does not pass",
                         "dotnet new naravel-widget -n X": "a template the smoke does not run",
                         "dotnet new naravel-job -n X -o app/Nowhere": "a folder no run has",
                         "dotnet new naravel-job -n X -o app/Jobs --event Y": "an option no run of that template has",
                         "dotnet run --launch-profile Dev": "a flag the smoke does not pass"}.items():
        expect(f"the smoke does not run ({why}): {command}", len(doc_command_problems(shlex.split(command), parameters)) == 1)

    def docs_with(change) -> list[str]:  # the three pages and the table of the real docs, with one change, in a temporary root
        with tempfile.TemporaryDirectory() as folder:
            for page in DOC_PAGES:
                (Path(folder) / page).parent.mkdir(parents=True, exist_ok=True)
                (Path(folder) / page).write_text(change(page, (ROOT / page).read_text(encoding="utf-8")), encoding="utf-8")
            return docs_problems(Path(folder), parameters)

    expect("docs that are the real docs have no problem", docs_with(lambda page, text: text) == [])
    expect("a value that the table of getting-started leaves out is found",
           any("rabbitmq" in problem for problem in docs_with(lambda page, text: text.replace("`rabbitmq`", "") if page.endswith("getting-started.md") else text)))
    expect("a command that the smoke does not run is found in a page",
           any("README.md" in problem and "together" in problem for problem in
               docs_with(lambda page, text: text.replace("dotnet new naravel -n Blog\n", "dotnet new naravel -n Blog --database postgres --queue memory\n") if page == "README.md" else text)))
    expect("a page without a starter command is found",
           any("names no command" in problem for problem in docs_with(lambda page, text: text.replace("dotnet", "dotnet-x") if page == "README.md" else text)))

    # The published pack (--nuget): what published_smoke asks of the machine, with a runner that does not run anything.
    class FakeRunner:
        def __init__(self, version: str, fail: str | None = None, pinned: str | None = None) -> None:
            self.version, self.fail, self.pinned, self.commands = version, fail, pinned or version, []

        def run(self, title, command, cwd, timeout, show_output=False):
            self.commands.append(command)
            if command[:3] == ["dotnet", "new", "install"]:
                return self.fail != "install", "error: no such package" if self.fail == "install" else "installed"
            if command[:3] == ["dotnet", "new", "list"]:
                return True, "Naravel application   naravel   [C#]   Naravel/Web"
            if command[:3] == ["dotnet", "new", "naravel"]:
                if self.fail == "generate":
                    return False, "error: the template engine failed"
                app = cwd / APP_NAME
                (app / "resources" / "views").mkdir(parents=True)
                (app / f"{APP_NAME}.csproj").write_text(f'<Project><ItemGroup><PackageReference Include="Naravel.Routing" Version="{self.pinned}" /></ItemGroup></Project>', encoding="utf-8")
                (app / "README.md").write_text(f"# {APP_NAME}\n\ndotnet test tests/{APP_NAME}.Tests\n", encoding="utf-8")
                (app / "resources" / "views" / "welcome.html").write_text(f"<title>{APP_NAME}</title>", encoding="utf-8")
            return True, ""

        def dotnet(self, title, args, cwd, timeout=0, show_output=False):
            self.commands.append(["dotnet", *args])
            return self.fail != "build", "error CS0246: missing" if self.fail == "build" else ""

    def published(fail: str | None = None, pinned: str | None = None) -> tuple[list[str], list[str], list[list[str]]]:
        saved = os.environ.pop("GITHUB_ACTIONS", None)  # the fake failures must not become annotations on a real run
        try:
            with tempfile.TemporaryDirectory() as folder, contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
                runner = FakeRunner("0.3.0", fail, pinned)
                failed, notes = published_smoke(runner, Path(folder), "0.3.0")
        finally:
            if saved is not None:
                os.environ["GITHUB_ACTIONS"] = saved
            EXCERPTS.clear()
        return failed, notes, runner.commands

    failed, notes, commands = published()
    expect("a published pack that installs, generates at its version and builds passes, and is uninstalled again",
           failed == [] and ["dotnet", "new", "install", f"{TEMPLATE_PACKAGE}::0.3.0"] in commands and ["dotnet", "build", f"{APP_NAME}.csproj", "-c", "Release"] in commands
           and commands[-1][:3] == ["dotnet", "new", "uninstall"] and any("0.3.0" in note for note in notes))
    failed, _, commands = published(fail="install")
    expect("a pack that cannot be installed stops there, and there is nothing to uninstall",
           failed == ["install"] and not any(command[:3] == ["dotnet", "new", "naravel"] for command in commands) and not any(command[:3] == ["dotnet", "new", "uninstall"] for command in commands))
    failed, _, commands = published(fail="generate")
    expect("a template that does not generate is a generate failure and the pack is uninstalled",
           failed == ["generate"] and commands[-1][:3] == ["dotnet", "new", "uninstall"])
    failed, _, commands = published(pinned="0.2.0")
    expect("an application that pins another version than the pack is found before anything is built",
           failed == ["generate"] and not any(command[:2] == ["dotnet", "build"] for command in commands))
    failed, _, commands = published(fail="build")
    expect("an application that does not build is a build failure and the pack is uninstalled",
           failed == ["build"] and commands[-1][:3] == ["dotnet", "new", "uninstall"])

    # start_and_probe, with a small server in place of the application: it must find what is wrong, and stop what it started,
    # including a child that a launcher such as `dotnet run` started.
    server = (
        "import http.server, os, sys, urllib.parse\n"
        "port = urllib.parse.urlparse(os.environ['ASPNETCORE_URLS']).port\n"
        "if len(sys.argv) > 2: open(sys.argv[2], 'w').write(str(os.getpid()))\n"
        "class H(http.server.BaseHTTPRequestHandler):\n"
        "    def log_message(self, *a): pass\n"
        "    def reply(self, code, body, kind='text/plain'):\n"
        "        data = body.encode(); self.send_response(code); self.send_header('Content-Type', kind)\n"
        "        self.send_header('Content-Length', str(len(data))); self.end_headers(); self.wfile.write(data)\n"
        "    def do_GET(self):\n"
        "        self.reply(500 if (self.path == '/up' and sys.argv[1] == 'down') else 200, '<title>Blog</title>', 'text/html')\n"
        "    def do_POST(self):\n"
        "        self.reply(202, '')\n"
        "http.server.HTTPServer(('127.0.0.1', port), H).serve_forever()\n")

    def probe(command: list[str]) -> list[str]:
        with tempfile.TemporaryDirectory() as folder:
            return start_and_probe(Path(folder), APP_NAME, os.environ.copy(), Path(folder) / "app.log", command)

    expect("an application that answers /up, / and /register is accepted", probe([sys.executable, "-c", server, "up"]) == [])
    expect("an application whose /up answers 500 is found", any("/up answered 500" in problem for problem in probe([sys.executable, "-c", server, "down"])))
    expect("an application that exits before it answers is found",
           any("exited with code 3" in problem for problem in probe([sys.executable, "-c", "import sys; sys.exit(3)"])))
    with tempfile.TemporaryDirectory() as folder:
        pid_file = Path(folder) / "child.pid"
        launcher = f"import subprocess, sys; subprocess.run([sys.executable, '-c', {server!r}, 'up', {str(pid_file)!r}])"
        launched = start_and_probe(Path(folder), APP_NAME, os.environ.copy(), Path(folder) / "app.log", [sys.executable, "-c", launcher])
        time.sleep(0.5)
        child = int(pid_file.read_text()) if pid_file.is_file() else 0
        try:  # a stopped process that nothing has collected yet (a zombie, as an orphan can be in a container) is not alive
            child_alive = Path(f"/proc/{child}/stat").read_text().rsplit(")", 1)[1].split()[0] not in ("Z", "X")
        except OSError:
            try:
                os.kill(child, 0)
                child_alive = not Path("/proc/self").exists()  # no /proc: the process answers, so it is alive; with /proc it was already gone
            except OSError:
                child_alive = False
        expect("the child that a launcher started is answered by the probes and is stopped with it", launched == [] and child > 0 and not child_alive)

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


def published_main(version: str, extra_args: list[str]) -> int:
    if not re.fullmatch(r"\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?", version):
        print(f"ERROR: {version!r} is not a version such as 0.3.0", file=sys.stderr)
        return 1
    consumer = load_consumer_smoke()
    work = Path(tempfile.mkdtemp(prefix="naravel-template-"))
    try:
        print(f"template smoke: {TEMPLATE_PACKAGE} {version} from nuget.org; working directory {work}", flush=True)
        runner = consumer.Runner(work, extra_args)
        runner.env["DOTNET_CLI_HOME"] = str(work / "cli-home")
        (work / "nuget.config").write_text(consumer.nuget_config(None), encoding="utf-8")
        failed, notes = published_smoke(runner, work, version)
    finally:
        shutil.rmtree(work, ignore_errors=True)
    write_summary(version, failed, notes, started=False)
    if failed:
        print(f"ERROR: template smoke of the published pack failed in: {', '.join(failed)}", file=sys.stderr)
        return 1
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument("--local", type=Path, metavar="DIR", help="directory with the .nupkg files from dotnet pack")
    source.add_argument("--nuget", metavar="VERSION", help="verify the pack published on nuget.org at this version: install, generate, build")
    source.add_argument("--self-test", action="store_true", help="run the script's own checks (no dotnet needed)")
    parser.add_argument("--keep", action="store_true", help="keep the working directory and print its path")
    parser.add_argument("--dotnet-arg", action="append", default=[], help="an extra argument for every dotnet restore/build/test/publish call")
    args = parser.parse_args()

    if args.self_test:
        return self_test()

    if args.nuget:
        return published_main(args.nuget, args.dotnet_arg)

    args.local = absolute_directory(args.local)
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
