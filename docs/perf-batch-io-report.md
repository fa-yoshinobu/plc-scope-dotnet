# Perf Batch I/O Report (2026-06-19)

## Baseline

- Branch base: `codex/refactor-cycle-2` at `d912c4e Complete refactor cycle 2`.
- `git status`: clean before implementation.
- `dotnet build .\PlcScopeDotNet.sln`: passed, 0 warnings, 0 errors.
- `dotnet test .\PlcScopeDotNet.sln -m:1`: passed, 274 tests.
  - Core: 200
  - App.UiTests: 33
  - App.Tests: 41

An initial parallel build/test attempt hit MSBuild intermediate-file locking. `dotnet build-server shutdown` was run and the baseline was re-run sequentially.

## Phase 1 Support Table

| Protocol | Batch read | Bit batch write | Limits / constraints | Basis |
| --- | --- | --- | --- | --- |
| SLMP | Implemented for word-device watch queries via `ReadRandomAsync`. Word and DWord random operands can be mixed. Bit-device reads and unsupported special devices use sequential fallback. | Implemented for direct bit devices via `WriteRandomBitsAsync`. Word-bit read-modify-write paths remain sequential. | 64 random devices per frame in this app implementation. Random read failure falls back to per-query sequential reads. | `PlcComm.Slmp.dll` public API reflection plus existing `SlmpSession.ReadRandomDWordValuesAsync` 64-point chunk precedent. |
| KV Host Link | Cross-watch batch not implemented. Existing `ReadBlockAsync` already uses monitor/consecutive/named optimized paths inside a single block read. | Not implemented. | Metadata exposes monitor/named/consecutive APIs, but cross-watch point limits and mixed-device constraints were not fully determinable from DLL metadata. | `PlcComm.KvHostLink.dll` reflection and existing `HostLinkSession`. |
| TOYOPUC | Cross-watch batch not implemented. Existing `ReadBlockAsync` already uses block/packed reads inside a single block read. | Not implemented. | Metadata exposes `ReadManyAsync` / `WriteManyAsync`, but point limits, relay behavior, and mixed-device constraints were not fully determinable from DLL metadata. | `PlcComm.Toyopuc.dll` reflection and existing `ToyopucSession`. |

## API Design

- `IPlcSession.ReadBatchAsync(IReadOnlyList<BlockQuery>, CancellationToken)` returns one `BlockReadBatchItemResult` per input query.
- `BlockReadBatchItemResult` stores either a `BlockReadResult` or an `Exception`, preserving row-level watch errors.
- `IPlcSession.WriteBitBatchAsync(IReadOnlyList<WriteRequest>, CancellationToken)` batches bit writes where supported.
- Both interface methods have sequential default implementations. `PlcSessionBase` also provides virtual sequential implementations, so sessions without overrides keep existing behavior.

## Implementation

- Watch-list visible rows now build read plans and call `ReadBatchAsync` once per visible read cycle.
- Single-row refresh and post-write refresh still use the existing single-row path.
- SLMP overrides `ReadBatchAsync`:
  - normal word queries use random word operands;
  - DWord-addressed `LZ` and long counter current `LCN` use random DWord operands;
  - unsupported queries, bit-device queries, and oversized plans use sequential fallback;
  - invalid query planning returns an error only for that row;
  - random-frame failure emits an error and falls back to sequential reads.
- SLMP overrides `WriteBitBatchAsync` for direct bit-device writes and chunks random bit writes at 64 entries.
- Host Link and TOYOPUC use the default sequential cross-watch behavior pending library/source confirmation of limits.

## Verification

- `dotnet test .\PlcScopeDotNet.sln -m:1`: passed, 282 tests.
  - Core: 206
  - App.UiTests: 33
  - App.Tests: 43

Added tests cover:

- default sequential batch read/write behavior;
- watch tab use of batch reads for visible rows;
- row-isolated batch read errors;
- SLMP mixed word/DWord random read;
- SLMP 64-device random-read chunking;
- SLMP random-read fallback to sequential read;
- SLMP random bit write.

## Stop And Ask

- Host Link cross-watch batching needs library source or protocol documentation for point limits and mixed-device constraints before implementation.
- TOYOPUC cross-watch batching needs library source or protocol documentation for `ReadMany` / relay / mixed-device constraints before implementation.
- Real PLC validation has not been performed by the implementation agent.

## Manual Validation

`TODO.md` now contains the remaining real PLC validation checklist. Merge judgment for this batch I/O change must wait until those items are completed on real hardware.

### What To Confirm On Real Hardware

The real PLC check is not just a speed check. It must confirm that batching preserves the old read/write results and safety boundaries:

- Large SLMP watch lists, especially more than 50 visible/scrolling rows, continue updating correctly.
- Word, DWord, Float32, Bit, and word-bit addresses such as `D0.0` display the same values as the previous sequential read path.
- Invalid addresses affect only their own watch row; other valid rows continue to update.
- If the PLC or route rejects random read, the sequential fallback still updates valid rows.
- Word/DWord/Float writes through bit-device rows do not change non-target devices or bits.
- Long-running trace and error logging remains stable during batch reads and random bit writes.

Do not treat this change as merge-ready until those hardware checks pass.
