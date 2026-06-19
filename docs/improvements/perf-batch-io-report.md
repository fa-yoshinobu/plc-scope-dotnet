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
- Live random-read rejection fallback has not been reproduced on the real iQ-R used for automatic checks because the PLC accepted the random-read frames. The fallback path remains covered by fake-SLMP automated tests.

## Manual Validation

`TODO.md` now contains the remaining real PLC validation checklist. Merge judgment for this batch I/O change must wait until those items are completed on real hardware.

### Automatic Live PLC Check (2026-06-19)

Target:

- PLC: SLMP iQ-R profile
- Endpoint: `192.168.250.100:1025` over TCP
- Scratch devices used: `D1000-D1005` and `M1000-M1031`

Result:

- Connected successfully. CPU state reported `Run`, raw `0x0000`.
- Saved original scratch values before writes.
- PASS: Mixed watch reads for Word, DWord, Float32, Bit, and word-bit matched the previous sequential read results.
- PASS: Invalid address `DXYZ` produced an error only for that row; valid `D1000` and `D1002` rows still succeeded.
- PASS: Random bit write changed target bits only; non-target `M1016-M1031` bits stayed unchanged.
- PASS: 30 read/write cycles completed with `NewErrorEvents=0` and `TraceEvents=222`.
- PASS: Original `D1000-D1005` and `M1000-M1031` values were restored and verified.
- SKIP: Live random-read rejection fallback. The PLC accepted the random-read frames; fallback remains covered by automated fake-SLMP tests.

### Extended Live PLC Pattern Check (2026-06-19)

Target:

- PLC: SLMP iQ-R profile
- Endpoint: `192.168.250.100:1025` over TCP
- Duration: about 1 hour (`01:00:00.0107279`)
- Devices intentionally not used: `X`, `Y`, `G`
- Scratch ranges used:
  - `D1000-D1127`
  - `W1000-W103F`
  - `M1000-M1063`
  - `L1000-L1063`
  - `B1000-B103F`

Result:

- Active device ranges: `D`, `W`, `M`, `L`, and `B` were all readable/writable on the target PLC.
- PASS: Scalar word writes and reads across `D` / `W` patterns.
- PASS: `UInt32` and `Float32` values on `D` devices read back through sequential and batch paths.
- PASS: `WriteBitInWordAsync` changed only the requested bit in `D` word devices.
- PASS: Random bit writes across `M`, `L`, and `B` changed target bits only and preserved non-target bits.
- PASS: Mixed sequential reads and `ReadBatchAsync` results matched across word, DWord, Float32, and bit-device queries.
- PASS: 70 single-word query batches crossed the 64-device random-read chunk boundary correctly.
- PASS: Invalid address checks remained row-isolated during the long run.
- PASS: Long run completed with `iterations=10546`, `TraceEvents=1728038`, and `ErrorEvents=0`.
- PASS: Original values for all scratch ranges were restored and verified.

Notes:

- Session-level `>50` row batch behavior is covered by the 70-query checks above.
- App-level 100-row watch UI scrolling was checked with `docs/slmp-iqr-100-watch.json`.
- Live random-read rejection fallback is still not reproduced on this iQ-R because random-read frames are accepted.

### Light QnUDV Live PLC Smoke Check (2026-06-19)

Target:

- PLC: SLMP QnUDV profile (`melsec:qnudv`)
- Endpoint: `192.168.250.100:1025` over TCP
- Scratch devices used: `D1000-D1003` and `M1000-M1015`

Result:

- Connected successfully. CPU state reported `Run`, raw `0x0000`.
- Saved original scratch values before writes.
- PASS: Word writes to `D1000` / `D1001` read back correctly.
- PASS: DWord write to `D1002` read back as `D1002=0xCDEF`, `D1003=0x89AB`.
- PASS: Word-bit write `D1000.3` read back correctly.
- PASS: Random bit batch write over `M1000-M1015` read back correctly.
- PASS: Mixed batch read over Word, DWord, and Bit queries returned 3 successful rows.
- PASS: Original `D1000-D1003` and `M1000-M1015` values were restored and verified.

### What To Confirm On Real Hardware

The real PLC check is not just a speed check. It must confirm that batching preserves the old read/write results and safety boundaries:

- Large SLMP watch lists, especially more than 50 visible/scrolling rows, continue updating correctly. Checked with the 100-row watch UI project.
- Word, DWord, Float32, Bit, and word-bit addresses such as `D0.0` display the same values as the previous sequential read path.
- Invalid addresses affect only their own watch row; other valid rows continue to update.
- If the PLC or route rejects random read, the sequential fallback still updates valid rows.
- Word/DWord/Float writes through bit-device rows do not change non-target devices or bits.
- Long-running trace and error logging remains stable during batch reads and random bit writes.

Do not treat this change as merge-ready until those hardware checks pass.
