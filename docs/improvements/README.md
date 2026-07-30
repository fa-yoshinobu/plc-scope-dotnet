# Improvements

This directory is the workspace for improvement plans and investigation notes.

## Current Status

No active improvement memo is currently open.

## Retained Design Notes

- [Host Link / TOYOPUC batch feasibility](close/hostlink-toyopuc-batch-feasibility.md):
  why the conservative batching strategy was chosen for Host Link and TOYOPUC.
  Kept because the reasoning is not recoverable from the code.

Completed instruction documents and closed investigation checklists are not kept
here. Their outcome lives in the [changelog](../../CHANGELOG.md) and in the code,
and the documents themselves remain in the git history. Real-PLC validation
records and release decisions are kept under [../validation](../validation/).

## Adding New Work

Add a new Markdown file in this directory while the work is open. When it is
finished, record the outcome in the changelog and delete the working document,
unless it explains a design decision that the code alone does not.
