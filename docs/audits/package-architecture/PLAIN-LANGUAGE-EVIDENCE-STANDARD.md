# Plain-Language Evidence Standard for Architecture Reviews

## Purpose

Every audit finding must be understandable to a developer or product owner who did not write the package. Technical precision is required, but unexplained implementation vocabulary is not acceptable.

## Required explanation for every finding

Write each finding in this order:

1. **In everyday words:** one or two sentences explaining what goes wrong without relying on framework or provider jargon.
2. **A realistic example:** show a small, concrete sequence a user might perform and the surprising or harmful result.
3. **What we checked:** name the exact source file and method, the relevant test or documentation, and the actual behavior seen. Include line numbers when available; if line numbers may move, include the method name as well.
4. **Why that proves the issue:** connect the observed code path to the example. Do not jump from a code smell to a claim of production failure.
5. **What is not yet proven:** clearly state whether the behavior was reproduced by a test, only traced in source, or checked against an external library contract. Do not describe a proposed test as an executed test.
6. **What should change:** state the required outcome in user-visible terms before naming implementation options.
7. **How we will prove the fix:** list regression tests using plain language, including the failure case and the expected safe result.

## Language rules

- Prefer short sentences and familiar words. Explain a technical term the first time it appears.
- Avoid unexplained phrases such as *ExecutionContext*, *fencing token*, *cardinality*, *lease*, *ambient transaction*, *parameter expansion*, *data source*, *reference counting*, or *provider pooling*. If the term is needed, immediately define it in everyday words.
- Use a small example when it makes the problem clearer. Examples must be derived from the actual API and source, not invented API names.
- Separate facts from conclusions and recommendations. Label source observations, runtime reproductions, test results, external-library behavior, and assumptions distinctly.
- State the user consequence: wrong data, unexpected sharing, lost error, delayed operation, leaked resource, missing telemetry, or documentation confusion.
- Explain severity by describing both how the problem can happen and what harm it can cause. Do not use the severity label as its own explanation.
- A "no findings" report must still explain in plain language what was checked, what guarantees were confirmed from source, and which real services/tests were not run.
- Keep code identifiers, package IDs, paths, API names and error names exact. Explain them; do not translate or rename them.
- All repository documentation remains English-only. Use plain, direct English; do not add Persian translations or duplicate localized files.

## Verification language

Use these labels consistently:

- **Seen in source:** the code path was inspected; no runtime test has proven it yet.
- **Reproduced by test:** a named test actually ran and showed the behavior.
- **Checked against library documentation/contract:** an external library's documented behavior supports the conclusion.
- **Not run:** no claim of success or failure may be made.
- **Confirmed fixed:** only after the regression test and required CI checks pass.

A report must never imply that source inspection, a test recommendation, or a skipped integration test is equivalent to a passing runtime test.

## Backfill policy

Apply this standard to every existing report in `reports/`, including reports with no findings. When revisiting an older report, preserve its original evidence and status; add plain-language explanations rather than silently changing what was or was not verified. Keep the audit on the existing `audit/package-architecture-revalidation` branch.
