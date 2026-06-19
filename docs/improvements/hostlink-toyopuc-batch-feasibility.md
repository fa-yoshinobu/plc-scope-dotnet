# Host Link / TOYOPUC Batch Feasibility

Status: open investigation

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
| Host Link | Possible only if `ReadNamedAsync` safely handles the needed mixed address/type set. | Unknown; no direct arbitrary bit batch API is visible. | Investigate, likely read-only first. |
| TOYOPUC | Promising because `ReadManyAsync` and `RelayReadManyAsync` exist. | Promising but risky because `WriteManyAsync` item shape and bit safety must be proven. | Investigate with fake server and then real hardware. |

## Required Questions

Host Link:

- What is the maximum stable point count for `ReadNamedAsync`?
- Can it mix word devices and bit devices in one call?
- Can it mix typed suffixes for `UInt16`, `Int16`, `UInt32`, `Int32`, and `Float32`?
- What happens when one address is invalid: one failed item, or whole request failure?
- Is there a safe direct-bit batch write path, or should writes remain sequential?

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

Stop before implementation if any of these remain unknown:

- point count or payload limit;
- mixed-device behavior;
- invalid-address failure behavior;
- TOYOPUC read-many/write-many item shape;
- relay-hop behavior;
- bit-write safety.

If those cannot be resolved from public DLL metadata and fake-server tests, the next step is to inspect library source or run a small standalone hardware probe before changing `HostLinkSession` or `ToyopucSession`.

## Non-Goals

- Do not modify `lib/plc-comm` DLLs.
- Do not change retry or timeout policy.
- Do not change project JSON or settings JSON formats.
- Do not replace the already-completed SLMP batch I/O implementation.
- Do not make Host Link or TOYOPUC batch support merge-blocking for the current SLMP batch scope.
