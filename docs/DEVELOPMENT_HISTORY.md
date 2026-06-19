# Development History

## 2026-06-11 Archived Refactor Plan

The older refactor planning notes now live under `docs/improvements/`.

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
