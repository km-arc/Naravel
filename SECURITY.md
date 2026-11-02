# Security policy

## Reporting a vulnerability

Please **do not open a public issue** for a security problem.

Report it privately through GitHub: open the repository's **Security** tab and choose **Report a vulnerability**
(<https://github.com/km-arc/Naravel/security/advisories/new>). The report stays private between you and the maintainers
until a fix is released.

If that button is not available, open a public issue that says only that you need a private channel for a security report,
and **put no details in it**. English or Persian is fine.

A useful report has the package and version, what an attacker can do, and the smallest reproduction you can give. Please
give us reasonable time to fix the problem before you disclose it.

## Supported versions

Naravel is pre-1.0 (`0.x`). Only the **latest published version** is supported: a fix ships as a new version of the
packages (they share one version), so please reproduce on the latest version before reporting.

## What counts

Examples of what we treat as a vulnerability in Naravel's own code:

- a path escaping a local storage disk's root (`Naravel.Filesystem`);
- a queue or cache payload that makes the application resolve or run an unintended type (payloads are resolved
  through an explicit alias registry only, never from a stored CLR type name);
- a query-builder input that reaches SQL as something other than a bound value or a validated identifier;
- encryption or signing that can be bypassed, or key material or plaintext that leaks into logs, telemetry or exceptions.

Not Naravel's to fix, though a heads-up is welcome: a vulnerability in a dependency (report it to that project), and an
application's own misconfiguration. Dependencies are audited on every restore (`NuGetAudit`, all severities), so a published
advisory against a package Naravel uses normally surfaces at restore (as an error under `src/`, where warnings are errors).
