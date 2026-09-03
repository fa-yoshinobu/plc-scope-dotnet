# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [2.4.0] - 2026-09-03

### Changed

- Updated the PLC communication packages to `PlcComm.KvHostLink` `4.2.0`, `PlcComm.Slmp` `5.2.0`, and `PlcComm.Toyopuc` `4.2.0`.
- Migrated SLMP word reads from the removed raw-read API to the current `ReadWordsAsync` API.
- Migrated Host Link, SLMP, and TOYOPUC operations to the protocol libraries' dedicated typed, single-request, DWord, and CPU-state APIs where available.

### Fixed

- Split SLMP direct word/bit reads, random reads, and random bit writes by the selected PLC profile's operation-specific request limits instead of fixed 64-point chunks.
- Split long-timer and long-retentive-timer state/current-value reads by their wire-point cost under the selected SLMP profile's direct word-read limit.
- Routed SLMP `LCS` and `LCC` block reads through the supported typed long-counter state API.

## [2.3.0] - 2026-08-27

### Changed

- Updated the public PLC communication packages to `PlcComm.KvHostLink` `4.1.0`, `PlcComm.Slmp` `5.1.0`, and `PlcComm.Toyopuc` `4.1.0`.
- Migrated contiguous Host Link and TOYOPUC word reads and SLMP bit writes to the packages' explicit single-request APIs.
- Isolated package restore to nuget.org and a repository-local cache so an unpublished package with the same identity cannot be reused from the machine-wide NuGet cache.

## [2.2.0] - 2026-08-07

### Changed

- Updated the PLC communication packages to `PlcComm.KvHostLink` `4.0.0`, `PlcComm.Toyopuc` `4.0.0`, and `PlcComm.Slmp` `5.0.0` from public NuGet.
- Migrated Host Link bit writes to the library's native Boolean single-value and consecutive-write APIs.
- Migrated the SLMP and TOYOPUC adapters from the removed external queue wrappers to the libraries' built-in serialized clients and lifecycle APIs, including direct SLMP CPU/password commands and route-aware TOYOPUC client construction. PLC Scope project files, saved settings, route selection, and operator workflow remain compatible.

## [2.1.0] - 2026-07-31

- Corrected the documented location of `settings.json`: it sits next to the executable, not under `%LOCALAPPDATA%`. Documented the theme, font, and always-on-top controls, the watch-list CSV menu items, and the message shown when a project written by a newer PLC Scope is opened. Corrected the project-file contract in the specification.
- Replaced the 2026-06 Host Link/TOYOPUC batching investigation note with a maintainer rule: the note characterised package version 0.8.0, and PlcComm.KvHostLink 3.2.1 has since changed out-of-range handling, direct-bit packing, and consecutive-read splitting, so its conclusions no longer describe the shipped library.
- Documented the TOYOPUC relay-hop limitation in the user guide: only one client at a time should route through a relay hop, because several TCP clients on the same hop contend for the target path.
- Migrated the test projects from xUnit 2.9.3, which NuGet reports as deprecated, to xUnit v3. Test counts are unchanged (Core 247, App 70, UI automation 37) and the build keeps zero warnings; the analyzer's cancellation-token findings were fixed rather than suppressed.
- Added project file schema validation: opening a project now checks `projectVersion`, accepts a missing or blank value and any version 1 file, and rejects a file written by a newer PLC Scope (or with an unreadable version) with an explicit "cannot open" message instead of silently loading partially understood data.
- Added a `CI` workflow that restores, builds, and tests the solution on Windows for every push to `main`, every pull request against `main`, and manual dispatch, so build and test regressions are caught before a release tag. The FlaUI desktop-automation tests stay a local gate.
- Added `global.json` pinning the .NET SDK to `10.0.202` with `latestFeature` roll-forward, so local and CI builds use the same toolchain.
- Fixed low-contrast text: themed the fixed WPF defaults for ToolTip, Hyperlink, and ListView backgrounds; scoped TextBlock styles inside Button and ComboBoxItem templates so accent/disabled foregrounds actually apply; darkened the light-theme muted/hex/comment colors one step. All rendered text pairs now meet WCAG AA (4.5:1) or the 3:1 component threshold in both themes.
- Restructured README as a user-facing entrance (dual role: PLC monitor and zero-code verification app for the plc-comm .NET libraries) and moved the full operator reference to docs/user-guide.md.
- Updated PLC communication libraries: PlcComm.Slmp 4.0.1, PlcComm.KvHostLink 3.2.1, PlcComm.Toyopuc 3.2.1 (bug-fix releases, no API changes).
- Fixed New project leaving the previous connection open: the session is now closed and released before the connection settings are reset, so auto-refresh polling, writes, and CPU commands can no longer reach the previous PLC.
- Fixed the PLC session not being released when the application exits: closing the main window now waits for an exactly-once shutdown (with a fallback on application exit), so SLMP remote passwords are locked again instead of staying unlocked.
- Fixed project saving truncating the target file before serializing: projects are written to a temporary file and then moved over the previous file, so an interrupted save can no longer leave an empty or corrupted project behind.

## [2.0.0] - 2026-07-18

### BREAKING
- Moved all projects and Windows publish paths to .NET 10 LTS. .NET 9 builds and runtimes are no longer supported.
- Removed `commentCsvPath` and `commentCsvPaths` from project JSON. Comment CSV files are now imported explicitly for the current session only; legacy fields are ignored without migration or fallback, and CSV-derived comments are not copied into saved watch items.

### Changed
- Bumped the application package and assembly version to `2.0.0` for the breaking project-format and runtime changes.
- Updated PLC communication package references to `PlcComm.Slmp` `4.0.0`, `PlcComm.KvHostLink` `3.2.0`, and `PlcComm.Toyopuc` `3.2.0`.
- Migrated SLMP, Host Link, and TOYOPUC sessions to the latest explicit profile, route, device, and queue APIs while preserving relay-aware TOYOPUC reads and writes.
- Added `melsec:mx-r:rj71en71` (MELSEC MX-R (RJ71EN71)) to the SLMP PLC profile selector.
- Host Link raw-frame tracing remains available. SLMP and TOYOPUC no longer emit raw-frame trace entries because the current package APIs do not expose public trace hooks.

## [1.0.3] - 2026-07-07

### BREAKING
- Replaced the SLMP Module I/O hex-number setting with the canonical 13-name module I/O target vocabulary. Project/settings JSON now stores `slmpModuleIo` as the canonical name (e.g. `"OwnStation"`) instead of a number, and the connection dialog uses a fixed-choice selector instead of a hex text box. Older project files with a numeric `slmpModuleIo` are not migrated.
- Removed the SLMP MultiDrop setting from connection settings and project/settings JSON. SLMP requests now always use MultiDrop station number `0x00`; older project files containing `slmpMultidrop` are not migrated.

### Changed
- Updated PLC communication package references to `PlcComm.Slmp`, `PlcComm.KvHostLink`, and `PlcComm.Toyopuc` `2.0.0` (canonical module I/O vocabulary release).
- Updated PLC profile display labels to use the communication-library display-name APIs instead of app-local duplicated label tables.
- Updated the SLMP Module I/O selector to show user-facing labels such as `Multiple CPU No. 2` instead of raw enum member names.

## [1.0.2] - 2026-07-05

### Changed
- Bumped the application package metadata and README version badge to `1.0.2`.
- Switched PLC communication libraries to the latest NuGet packages: `PlcComm.Slmp`, `PlcComm.KvHostLink`, and `PlcComm.Toyopuc` `1.2.0`.
- Updated `Microsoft.NET.Test.Sdk` to `18.7.0`.
- Updated SLMP, KV Host Link, and TOYOPUC profile selectors and status labels to use canonical communication-library display names while preserving saved canonical profile IDs.
- Updated SLMP PLC profile choices for the local MELSEC profile set, including RJ71EN71, LJ71E71-100, and QJ71E71-100 unit profiles.
- Expanded KEYENCE Host Link profile labels to show the model families covered by the selected profile.
- Added UI tests covering canonical profile values, display labels, legacy/unknown profile preservation, and TOYOPUC profile selection.

## [1.0.1] - 2026-06-29

### Changed
- Bumped the application package metadata and README version badge to `1.0.1`.
- Updated PLC communication package references to `PlcComm.Slmp` and `PlcComm.KvHostLink` `1.1.1`, and `PlcComm.Toyopuc` `1.1.0`.
- Updated PLC read/write calls for the latest communication library APIs while keeping user-facing PLC address input unchanged.

### Fixed
- Updated Host Link bit read/write handling so bit devices use the raw Host Link API path required by the latest library.
- Preserved monitor and watch-list address display as normal PLC addresses such as `D0`, `M0`, and `D0.3`.

## [1.0.0] - 2026-06-24

### Changed
- Bumped the application package metadata and README version badge to `1.0.0`.
- Updated PLC communication package references to `PlcComm.Slmp`, `PlcComm.KvHostLink`, and `PlcComm.Toyopuc` `1.0.0`.
- Condensed `TODO.md` to active open items only and pointed completed-work references to the changelog and closed validation records.

### Fixed
- Updated Host Link session creation for the `PlcComm.KvHostLink` `1.0.0` constructor by passing the selected PLC profile name.

## [0.5.1] - 2026-06-19

### Added
- Added TOYOPUC visible-row batch reads through `ToyopucSession.ReadBatchAsync`.
- Added TOYOPUC direct bit-device batch writes through `ToyopucSession.WriteBitBatchAsync`.
- Added Host Link visible-row batch reads through `HostLinkSession.ReadBatchAsync`.
- Added Host Link same-family consecutive direct bit-device batch writes through `HostLinkSession.WriteBitBatchAsync`.
- Added visible-row watch-list batch reads through `IPlcSession.ReadBatchAsync`.
- Added SLMP random-read batching for supported word-device watch queries.
- Added SLMP direct bit-device batch writes through `WriteBitBatchAsync`.

### Changed
- Preserved row-level watch errors so invalid rows do not stop other visible rows from updating.
- Kept word-bit writes, non-bit writes, typed word writes, non-consecutive Host Link bit writes, and unproven Keyence bit-bank boundary cases on the existing sequential paths.
- Closed the remaining improvement findings and moved completed improvement plans and reports into `docs/improvements/close/`.

### Fixed
- Added sequential-read fallback paths for Host Link and TOYOPUC batch reads when grouped requests fail.
- Isolated local TOYOPUC batch-planning errors so invalid rows can fail independently.

## [0.5.0] - 2026-06-11

### Changed
- Archived the first refactor plan under `docs/improvements/close/`, preserving the UI, file-format, polling, and protocol compatibility contracts for later work.
