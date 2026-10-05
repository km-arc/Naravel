# Stage file template (required by check.py)

```markdown
# Rxx — Title

Parity section: `Rxx` in [../PARITY-MATRIX.md](../PARITY-MATRIX.md).
Gate: <none | PDR-nnn approved | GO> (deps live in the ROADMAP table only)
Read: <exact files the agent may read; code first, docs only if listed>
Touch: <exact projects/paths the agent may edit>
Out of scope: <what must not be changed here, with the owning stage>
Decisions: <OD-nn / PDR-nnn that already settle questions>

## Tasks
- [ ] **Rxx.Tnn — Title (D-nn):** what to do. **Accept:** observable condition (test name, command, file).

## Exit evidence
<what the status cell must record>
```

Rules: every task has `**Accept:**`; ≤ 8 tasks per stage; tasks are ordered and each leaves the solution building.
