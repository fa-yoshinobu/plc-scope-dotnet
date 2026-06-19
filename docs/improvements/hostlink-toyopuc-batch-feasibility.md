# Host Link / TOYOPUC Batch Feasibility

Status: Host Link implemented; TOYOPUC remains open investigation

Created: 2026-06-19

## Purpose

Determine whether watch-list cross-row batching can be safely extended beyond the completed SLMP batch I/O scope.

The current application already batches visible watch-list reads through `IPlcSession.ReadBatchAsync`, but only `SlmpSession` overrides it with protocol-specific random-read batching. Host Link and TOYOPUC currently use the default sequential cross-watch behavior.

This document is a feasibility task, not an implementation instruction. Do not add Host Link or TOYOPUC cross-watch batching until the API limits and failure behavior below are confirmed.

## Current Baseline

### Host Link

`HostLinkSession.ReadBlockAsync` is already optimized inside a single monitor block:

- word ranges use `ReadWordsAsync`;
- bit ranges try monitor-word registration first;
- then monitor-bit registration;
- then `ReadNamedAsync`;
- then legacy consecutive read;
- then sequential reads.

The missing optimization is not range/block reads. It is cross-watch batching for scattered visible watch rows such as `D100`, `D200`, `MR100`, and `B0`.

### TOYOPUC

`ToyopucSession.ReadBlockAsync` is already optimized inside a single monitor block:

- word ranges use `ReadWordsAsync`;
- bit ranges use direct reads or packed-word reads depending on the resolved device;
- relay hops are applied through `QueuedToyopucDeviceClient`.

The missing optimization is cross-watch batching for scattered visible watch rows, including relay-hop scenarios.

## Public API Inventory

Observed from `lib/plc-comm/net9.0` release DLL metadata, version `0.8.0`.

### Host Link Candidates

- `KvHostLinkClientExtensions.ReadNamedAsync(client, IEnumerable<string> addresses, CancellationToken)`
- `QueuedKvHostLinkClient` overload of `ReadNamedAsync`
- `RegisterMonitorWordsAsync` / `ReadMonitorWordsAsync`
- `RegisterMonitorBitsAsync` / `ReadMonitorBitsAsync`
- `ReadWordsAsync`, `ReadDWordsAsync`, `ReadTypedAsync`
- `WriteTypedAsync`
- `WriteBitInWordAsync`
- `WriteWordsChunkedAsync`, `WriteDWordsChunkedAsync`

Risk: there is no obvious direct equivalent of SLMP random read/write for arbitrary mixed watch rows. `ReadNamedAsync` looks like the main candidate, but point limits, suffix mixing, data-type mixing, and all-or-nothing failure behavior need confirmation.

### TOYOPUC Candidates

- `ToyopucDeviceClient.ReadManyAsync(IEnumerable<object> devices, CancellationToken)`
- `ToyopucDeviceClient.WriteManyAsync(IEnumerable<object> items, CancellationToken)`
- `ToyopucDeviceClient.RelayReadManyAsync(object hops, IEnumerable<object> devices, CancellationToken)`
- `ToyopucDeviceClient.RelayWriteManyAsync(object hops, IEnumerable<object> items, CancellationToken)`
- lower-level `ReadWordsMultiAsync`, `ReadBytesMultiAsync`, `ReadExtMultiAsync`
- lower-level `WriteWordsMultiAsync`, `WriteBytesMultiAsync`, `WriteExtMultiAsync`
- typed consecutive APIs such as `ReadDWordsAsync`, `ReadFloat32sAsync`, `WriteDWordsAsync`, and `WriteFloat32sAsync`

Risk: TOYOPUC has profile, unit, packed-bit, relay-hop, and PC10/Plus differences. `ReadManyAsync` / `WriteManyAsync` may be usable, but the accepted item shapes and error behavior need to be characterized before application use.

## Feasibility Matrix

| Protocol | Cross-watch batch read | Direct bit batch write | Initial judgment |
| --- | --- | --- | --- |
| Host Link | Implemented through `HostLinkSession.ReadBatchAsync` using `ReadNamedAsync`, with fallback to sequential reads on named-read failure. | Implemented for same-family consecutive direct bit-device runs through `WriteConsecutiveAsync`. Arbitrary scattered bit batch writes remain sequential. | Complete for the conservative scope. |
| TOYOPUC | Promising because `ReadManyAsync` and `RelayReadManyAsync` exist. | Promising but risky because `WriteManyAsync` item shape and bit safety must be proven. | Investigate with fake server and then real hardware. |

## Host Link Implementation (2026-06-19)

Implemented in `HostLinkSession`:

- `ReadBatchAsync` creates Host Link named-read plans for visible watch queries.
- Word-device watch rows are read as raw word addresses so existing `BlockReadResult` interpretation remains unchanged.
- Bit-device watch rows are read as named bit addresses and returned as `BitValues`.
- Named-read failures emit an error event and fall back to the existing sequential per-row reads.
- `WriteBitBatchAsync` batches only same-family consecutive direct bit-device requests through `WriteConsecutiveAsync`.
- Keyence bit-bank boundary crossings such as `MR015` to `MR100` remain sequential because that boundary was not part of the live write proof.
- Non-bit requests, word-bit requests, mixed families, and non-consecutive requests remain sequential.

Automated coverage:

- mixed Host Link watch batch read over word, DWord, `MR`, and `B` queries;
- named-read planning failure followed by sequential fallback;
- consecutive `MR` bit batch write;
- sequential fallback for Keyence bit-bank boundary write requests.

Implemented-session smoke check:

- Endpoint: `192.168.250.100:8501` over TCP
- PASS: `HostLinkSession.ReadBatchAsync` returned successful rows for `D1000`, `D1002` DWord, and `MR399900-MR399903`.
- PASS: `HostLinkSession.WriteBitBatchAsync` wrote `1010` to `MR399900-MR399903`.
- PASS: Original `MR399900-MR399903` values were restored and verified.

20-minute live validation:

- Date: 2026-06-19
- Endpoint: `192.168.250.100:8501` over TCP
- Scratch range: `MR399900-MR399915`
- Shape: 20 foreground chunks of 1 minute each.
- PASS: 4,720 mixed batch reads over `D` word/DWord/Float32 rows plus `MR` and `B` bit rows.
- PASS: 460 invalid-row isolation checks using `D99999` while valid `D1000` and `MR399900` rows continued to read.
- PASS: 220 same-family consecutive `MR` bit batch writes.
- PASS: 60 non-consecutive `MR` bit write fallback checks.
- PASS: All 20 chunks restored `MR399900-MR399915` to the original `0000000000000000` value.
- PASS: No `ErrorReceived` events, exceptions, write mismatches, or restore mismatches were logged.

## Live Host Link Read-Only Probe (2026-06-19)

Target:

- Endpoint: `192.168.250.100:8501` over TCP
- Library: `PlcComm.KvHostLink` `0.8.0`
- Operation type: read-only
- Writes performed: none

Result:

- PASS: TCP connection succeeded.
- PASS: CPU mode read returned `Run`.
- PASS: `ReadWordsAsync("D1000", 4)` returned four word values.
- PASS: `ReadNamedAsync(["D1000", "D1001"])` returned two word values.
- PASS: Colon data-type syntax worked:
  - `D1000:U`, `D1001:U`
  - `D1000:S`, `D1001:S`
  - `D1000:D`, `D1002:D`
  - `D1000:F`, `D1002:F`
- PASS: Word-bit reads worked with `D1000.0` and `D1000.1`.
- PASS: Bit-device reads worked with `MR000-MR003` and `B0-B3`.
- PASS: Mixed word/bit read worked with `D1000`, `MR000`, and `B0` in the same `ReadNamedAsync` call.
- PASS: Scattered word read counts worked for 10, 120, 121, 256, 512, and 1000 addresses starting at `D1000`.
- PASS: Invalid out-of-range address `D99999` failed the whole `ReadNamedAsync` call before returning any partial result.
- PASS: Dot suffixes such as `D1000.U` and `D1000.S` are not data-type suffixes for this API; they are interpreted as bit-in-word syntax and rejected. Use colon syntax for data types.

Observed samples:

```text
ReadNamed D1000|D1001 => D1000=0,D1001=0
ReadNamed D1000:U|D1001:U => D1000:U=0,D1001:U=0
ReadNamed D1000:S|D1001:S => D1000:S=0,D1001:S=0
ReadNamed D1000:D|D1002:D => D1000:D=0,D1002:D=0
ReadNamed D1000:F|D1002:F => D1000:F=0,D1002:F=0
ReadNamed D1000.0|D1000.1 => D1000.0=False,D1000.1=False
ReadNamed D1000|MR000|B0 => D1000=0,MR000=False,B0=False
ReadNamed 1000 D-word addresses => OK
ReadNamed D1000|D99999|D1001 => ERROR Device number out of range: D99999 (allowed: 0..65534)
```

Interpretation:

- This probe justified the conservative Host Link read-batch implementation.
- `ReadNamedAsync` is suitable for mixed visible watch-row reads when every address can be planned locally.
- Because invalid addresses fail the whole API call, the implementation falls back to the current sequential per-row behavior on named-read failure.
- Fake-server tests now capture the command behavior used by the implementation.

## Live Host Link Bit Write Probe (2026-06-19)

Target:

- Endpoint: `192.168.250.100:8501` over TCP
- Library: `PlcComm.KvHostLink` `0.8.0`
- Scratch devices used: `MR399900-MR399915`
- Writes performed: yes, limited to the scratch `MR` range above
- Restore policy: original values were read first and restored after each write test

Result:

- PASS: Original `MR399900-MR399915` values were read before writing.
- PASS: `WriteSetValueConsecutiveAsync` was rejected for `MR` before changing values: command `WSS` supports `C` and `T`, not `MR`.
- PASS: `WriteConsecutiveAsync("MR399900", [1,0,1,0])` wrote the four requested bit values; readback was `1010`.
- PASS: The four-bit `WriteConsecutiveAsync` test restored the original values; readback was `0000`.
- PASS: `ForcedSetConsecutiveAsync("MR399900", 4)` set the four target bits; readback was `1111`.
- PASS: `ForcedResetConsecutiveAsync("MR399900", 4)` reset the four target bits; readback returned to `0000`.
- PASS: `WriteConsecutiveAsync("MR399900", 16-bit alternating pattern)` wrote `1010101010101010`; restore readback returned to `0000000000000000`.
- PASS: Four-bit write to `MR399900-MR399903` did not change adjacent `MR399904-MR399907`; readback was `11110000`, then restore returned `00000000`.

Observed samples:

```text
ORIGINAL 0000
WriteSetValueConsecutiveAsync MR399900 x4 => ERROR Command 'WSS' does not support device type 'MR'. Supported types: C, T.
WriteConsecutive AFTER 1010
WriteConsecutive RESTORED 0000
ForcedSet AFTER 1111
ForcedReset AFTER 0000
ORIGINAL16 0000000000000000
AFTER16 1010101010101010
RESTORED16 0000000000000000
AFTER_WRITE4_READ8 11110000
NEIGHBORS_UNCHANGED=True
RESTORED8 00000000
```

Interpretation:

- Host Link bit batch write is feasible for consecutive direct bit-device runs using `WriteConsecutiveAsync`.
- `ForcedSetConsecutiveAsync` and `ForcedResetConsecutiveAsync` are usable for uniform consecutive set/reset runs, but `WriteConsecutiveAsync` is the better candidate because it supports per-bit values in one consecutive range.
- `WriteSetValueConsecutiveAsync` is not a general bit-device batch write API for `MR`; it appears timer/counter oriented in this library.
- The app should not try to batch arbitrary scattered bit writes unless a separate safe API is proven. A conservative implementation can group only adjacent direct bit-device write requests with the same Host Link device family and consecutive logical addresses, then fall back to sequential writes for everything else.
- Fake-server tests now cover the implemented command behavior. Source inspection may still be useful before broadening beyond the conservative consecutive-run scope.

## Required Questions

Host Link:

- What is the maximum stable point count for `ReadNamedAsync`? Live read-only probe confirmed at least 1000 scattered `D` word addresses.
- Can it mix word devices and bit devices in one call? Live read-only probe: yes for `D1000`, `MR000`, and `B0`.
- Can it mix typed suffixes for `UInt16`, `Int16`, `UInt32`, `Int32`, and `Float32`? Live read-only probe: yes with colon syntax such as `D1000:U`, `D1000:S`, `D1000:D`, and `D1000:F`.
- What happens when one address is invalid: one failed item, or whole request failure? Live read-only probe: invalid out-of-range address failed the whole `ReadNamedAsync` call.
- Is there a safe direct-bit batch write path, or should writes remain sequential? Live write probe: `WriteConsecutiveAsync` safely wrote and restored consecutive `MR` bit runs; arbitrary scattered bit writes remain unproven.

TOYOPUC:

- What exact object types does `ReadManyAsync` accept?
- What exact item types does `WriteManyAsync` accept?
- Can read-many mix word, bit, DWord, Float32, prefixed, and packed devices?
- Does relay-hop read-many use the same item behavior as direct read-many?
- What are the point count and payload limits?
- What happens when one address is invalid?
- Can direct bit writes be batched without changing non-target bits or devices?

## Investigation Plan

1. Reflection/API characterization
   - Record public signatures for the candidate APIs.
   - Identify item types accepted by TOYOPUC read-many/write-many APIs.
   - Identify whether Host Link `ReadNamedAsync` accepts typed suffixes and bit devices.

2. Fake-server characterization tests
   - Add tests that call candidate APIs through the current session clients where possible.
   - Capture actual protocol commands/frames sent for mixed read plans.
   - Confirm fallback behavior when the fake server rejects a batch command.

3. Session-level prototype behind conservative gates
   - Implement only in a branch or draft change after the above is known.
   - Keep `ReadBatchAsync` row order and row-isolated errors.
   - Fall back to the existing sequential default on unsupported plans or request failure.
   - Start with read batching only; write batching needs separate proof.

4. Real hardware validation
   - Host Link: verify on a real KV PLC with word, bit, DWord, Float32, invalid address, and scrolling watch-list cases.
   - TOYOPUC: verify direct and relay-hop paths where hardware is available.
   - Save original scratch values before any write test and restore them afterward.

## Acceptance Criteria

Host Link read batching may proceed only if:

- mixed watch rows can be read with fewer requests than sequential reads;
- unsupported or invalid rows do not stop other valid watch rows from updating, or the app can safely fall back to sequential reads;
- result order can be mapped back to input rows without ambiguity;
- existing monitor block optimizations are not regressed.

TOYOPUC read batching may proceed only if:

- direct and relay-hop behavior is understood;
- profile-specific and packed-bit behavior is safe;
- invalid rows can be isolated or safely trigger sequential fallback;
- DWord and Float32 results match the previous sequential path.

Any bit batch write may proceed only if:

- non-target bits and devices are proven unchanged;
- original values are restored in real-hardware tests;
- failed batch writes fall back or fail visibly without partial silent corruption.

## Stop And Ask

Stop before broadening the implemented Host Link scope, or before implementing
TOYOPUC batching, if any of these remain unknown:

- point count or payload limit;
- mixed-device behavior outside the currently validated Host Link named-read path;
- invalid-address failure behavior outside the current row-isolated fallback path;
- TOYOPUC read-many/write-many item shape;
- relay-hop behavior;
- bit-write safety outside same-family consecutive direct Host Link bit devices.

If those cannot be resolved from public DLL metadata and fake-server tests, the
next step is to inspect library source or run a small standalone hardware probe
before changing `ToyopucSession` or broadening `HostLinkSession` further.

## Non-Goals

- Do not modify `lib/plc-comm` DLLs.
- Do not change retry or timeout policy.
- Do not change project JSON or settings JSON formats.
- Do not replace the already-completed SLMP batch I/O implementation.
- Do not make Host Link or TOYOPUC batch support merge-blocking for the current SLMP batch scope.
