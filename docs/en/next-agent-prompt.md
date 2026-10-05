# Prompt for the next agent (copy everything below the line)

---

You are working on **Naravel**: Laravel's simplicity with .NET's speed, as a monorepo built by one solution (`Naravel.slnx`).

1. Read `AGENTS.md`, then `ROADMAP.md`, then **only** `roadmap/stages/<NEXT>-*.md` (`NEXT` is in `ROADMAP.md`). Read nothing else
   unless that stage's `Read:` line lists it. Do not read `PROGRESS*.md`, FA files or other stages.
2. Follow "How to run one pass" in `ROADMAP.md` exactly: check the gate, do the tasks in order, tick a task only when its
   **Accept:** condition holds, stop after the stage (or after the tasks you finished, leaving it `DOING`).
3. Edit only the paths in the stage's `Touch:` line. A defect outside them goes into `roadmap/AUDIT.md`.
4. Run `dotnet restore Naravel.slnx && dotnet build Naravel.slnx -c Release && dotnet test Naravel.slnx -c Release` and
   `/usr/bin/python3 roadmap/check.py`. If you cannot run something, write `NOT RUN: <reason>`; never claim an unrun pass.
5. Final report: what changed, what was verified, what was not, and the new `NEXT`.
