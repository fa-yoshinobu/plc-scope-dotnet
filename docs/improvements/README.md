# Improvements

This directory is the workspace for improvement plans, investigation notes, and completion reports.

## Current Status

No active improvement memo is currently open. Completed items are archived under [`close/`](close/).

## Closed Items

- [Refactor instructions](close/refactor-instructions.md): completed refactor planning and follow-up implementation notes.
- [Performance batch I/O instructions](close/perf-batch-io-instructions.md): completed implementation plan for watch-list batch reads and SLMP bit batch writes.
- [Performance batch I/O report](close/perf-batch-io-report.md): completed implementation and real PLC validation report.
- [Improvement findings 2026-06-18](close/improvement-findings-2026-06-18.md): completed improvement checklist; remaining small fixes were closed on 2026-06-19.
- [Host Link / TOYOPUC batch feasibility](close/hostlink-toyopuc-batch-feasibility.md): conservative batching is implemented for both Host Link and TOYOPUC.

## Reading Order

For the current state of the project, start with:

1. [`../../README.md`](../../README.md)
2. [`../DEVELOPMENT_HISTORY.md`](../DEVELOPMENT_HISTORY.md)
3. [`close/hostlink-toyopuc-batch-feasibility.md`](close/hostlink-toyopuc-batch-feasibility.md)
4. [`close/perf-batch-io-report.md`](close/perf-batch-io-report.md)
5. [`../../TODO.md`](../../TODO.md)

New improvement work should be added as a new Markdown file in this directory. Move it to `close/` only after the implementation, verification, and follow-up notes are complete.
