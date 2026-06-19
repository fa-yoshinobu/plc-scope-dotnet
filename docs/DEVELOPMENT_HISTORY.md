# Development History

## 2026-06-19 Host Link Batch I/O Follow-Up

Host Link watch-list batching was added after the SLMP batch I/O work.

### Scope

- Added `HostLinkSession.ReadBatchAsync` using `ReadNamedAsync` for visible watch rows.
- Preserved row-level behavior by falling back to the existing sequential read path when named reads fail.
- Added `HostLinkSession.WriteBitBatchAsync` for same-family consecutive direct bit-device writes through `WriteConsecutiveAsync`.
- Kept non-consecutive bit writes, word-bit writes, and unproven Keyence bit-bank boundary cases on the existing sequential write path.
- Left TOYOPUC cross-watch batching as an open investigation.

### Verification

- Host Link live read-only probe passed on `192.168.250.100:8501`.
- Host Link live bit-write probe passed on `MR399900-MR399915`; original values were restored.
- Implemented `HostLinkSession` smoke check passed on `192.168.250.100:8501` for mixed batch read and `MR399900-MR399903` batch write/restore.
- Fake Host Link session tests cover mixed watch read batching, fallback behavior, consecutive bit writes, and bit-bank boundary fallback.

## 2026-06-19 Batch I/O And Improvement Closure

The SLMP batch I/O work and the open improvement findings were completed and merged to `main`.

### Scope

- Added visible-row watch-list batch reads through `IPlcSession.ReadBatchAsync`.
- Added SLMP random-read batching for supported word-device watch queries.
- Added SLMP direct bit-device batch writes through `WriteBitBatchAsync`.
- Preserved row-level watch errors so an invalid watch address does not stop other visible rows from updating.
- Kept Host Link and TOYOPUC cross-watch batching out of the initial SLMP batch I/O merge until library/source or protocol-limit details were available.
- Closed the remaining improvement findings; the bit-device data-type policy item was kept as the intended packed-read/write behavior.
- Moved completed improvement plans and reports into `docs/improvements/close/`.

### Verification

- `dotnet test .\PlcScopeDotNet.sln -m:1` passed with Core, App, and FlaUI UI test projects.
- SLMP iQ-R hardware validation passed at `192.168.250.100:1025` using `D`, `W`, `M`, `L`, and `B` scratch ranges while avoiding `X`, `Y`, and `G`.
- A 1-hour extended iQ-R pattern check completed with 10,546 iterations, 1,728,038 trace events, 0 error events, and all original scratch values restored.
- SLMP QnUDV smoke validation passed at `192.168.250.100:1025` for word, DWord, word-bit, random bit batch, and mixed batch reads.
- App-level 100-row watch UI scrolling was checked with `docs/slmp-iqr-100-watch.json`.

### Notes

- Live random-read rejection could not be reproduced because the available iQ-R and QnUDV targets accepted random-read frames. The sequential fallback path remains covered by automated fake-SLMP tests.
- Batch I/O is complete for the current scope and no longer blocks merge decisions.
- Current validation status is recorded in `TODO.md`; completed implementation and verification detail is archived under `docs/improvements/close/`.

## 2026-06-11 Archived Refactor Plan

The older refactor planning notes now live under `docs/improvements/close/`.

### Scope

- App: `plc-scope-dotnet`, a WPF/desktop PLC monitoring tool.
- Primary task: extract UI-independent logic from `MainWindowViewModel` into Core services and add unit tests.
- Large sub-ViewModel splitting that would alter XAML binding paths was proposal-only.

### Contracts To Preserve

- UI options, menus, buttons, columns, device choices, and interaction behavior documented by the app guidance.
- Existing XAML binding paths in `MainWindow.xaml` and `App.xaml`.
- Project JSON, settings JSON, watch-list CSV, and related file compatibility.
- Low-load design: visible-row polling behavior, watch range updates, throttling, and cancellation semantics.
- SLMP, Host Link, and Computer Link session read/write semantics.
- Write-range clamping and CPU control guard conditions.

### Debt Notes

- D1: `MainWindowViewModel.cs` was a large god ViewModel containing connection lifecycle, monitoring refresh, command guards, persistence, and UI-facing state.
- Only logic that could move behind the existing ViewModel facade was implementation-safe.
- D2: `MainWindow.xaml.cs` code-behind was sizable but acceptable for UI event glue; report-only.
- D3: session implementation similarity was report-only.

### Planned Verification

- Record baseline `dotnet build` and `dotnet test` results, including UiTests availability.
- Audit candidate extraction areas for UI type, dispatcher, and event-order dependencies before editing.
- Extract one responsibility at a time into Core, keep the ViewModel facade and XAML bindings stable, and add characterization unit tests.
- Run `dotnet build` and `dotnet test PlcScopeDotNet.sln -m:1` after each extraction.
- Revert the current extraction if UiTests failed.

### Out Of Scope

- XAML binding changes.
- Large sub-ViewModel reshaping.
- File-format changes.
- Protocol behavior changes.
- UI redesign or unrelated refactoring.
