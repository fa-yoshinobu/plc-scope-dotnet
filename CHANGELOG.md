# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
