# TOYOPUC Relay-Hop Validation - 2026-06-12

This note preserves the plc-scope-specific result from the 2026-06-12
Toyopuc/ComputerLink relay investigation.

## Summary

`plc-scope-dotnet` now routes TOYOPUC monitor reads, writes, and CPU commands
through the configured relay hops consistently.

Validated relay path:

- host: `192.168.250.100`
- port: `1025`
- protocol: `tcp`
- profile: `toyopuc:nano-10gx:compatible`
- hops: `P1-L1:N2`

## Implementation Check

The TOYOPUC session now uses the queued relay-aware client path for:

- monitor word reads
- monitor bit reads
- writes
- bit-in-word writes
- CPU status
- CPU STOP / RUN operations

Regression tests cover relay-frame use for monitor read and write.

## Live Result

Underlying library checks:

- Python/.NET relay CPU status: OK
- `P1-D0000` relay read/write/readback: OK
- `P1-D0000` count probe `1/8/16/32/64/128/256`: OK
- 30-minute relay write/readback soak: 1029 iterations, 0 failures, final
  restore to `0x270F`

Application-level check:

- plc-scope TOYOPUC relay-hop CPU STOP / RUN was observed on real hardware.

## Remaining Classification

No plc-scope release-blocking TOYOPUC relay-hop issue remains from this
investigation.

The only observed limitation is target/path contention when multiple TCP
clients hit the same relay hop concurrently. A single client path remained
stable.
