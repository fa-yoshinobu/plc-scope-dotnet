# TODO

## Remaining Manual Validation

- [x] Verify out-of-range address handling and scroll limits with a real KV PLC.
- [x] Verify Host Link monitor read, write, CPU status, and CPU RUN/STOP behavior with a real KV PLC.
- [x] Verify TOYOPUC PC10G PC10 mode monitor read, write, CPU STOP, and CPU RUN behavior with a real PLC.
- [x] Verify TOYOPUC CPU STOP, stop release, and scan resume from the app with relay hops. Relay path `P1-L1:N2` to Nano 10GX is available, basic Python/.NET relay read/write/stress checks pass, plc-scope session read/write has relay frame coverage, and app-level RUN/STOP was observed on real hardware.
- [x] Verify Watch list visible-row-only reads while scrolling through a large watch list on a real KV PLC.
- [x] Verify optional communication trace logging during long-running communication with a real KV PLC.
- [x] Verify SLMP watch-list batch reads on a real PLC with more than 50 visible/scrolling watch rows. Session-level 70-row batch reads were checked during a 1-hour iQ-R hardware run, and app-level 100-row watch UI scrolling was checked with `docs/slmp-iqr-100-watch.json`.
- [x] Verify SLMP mixed watch values (Word, DWord, Float32, Bit, and word-bit addresses) match the previous sequential read results on real hardware. Checked on iQ-R at `192.168.250.100:1025` using `D1000-D1005` and `M1000-M1031`; original values were restored.
- [x] Verify invalid SLMP watch addresses show an error only on the affected row while other rows continue updating. Checked with invalid `DXYZ` between valid `D1000` / `D1002` reads.
- [ ] Verify SLMP random-read failure fallback by using an unsupported route/device scenario and confirming sequential reads still update valid rows. Live iQ-R accepted the random-read frames; fallback remains covered by automated fake-SLMP tests.
- [x] Verify SLMP bit-device word/dword/float writes leave non-target devices unchanged when random bit write is used. Checked with `M1000-M1031`; target bits changed, non-target bits stayed unchanged, then original values were restored.
- [x] Verify long-running SLMP trace/error logging remains stable during batch reads and random bit writes. Checked with 30 read/write cycles; no new error events.
- [x] Verify extended SLMP read/write patterns on real hardware without `X` / `Y` / `G` devices. Checked for 1 hour on iQ-R at `192.168.250.100:1025` using `D1000-D1127`, `W1000-W103F`, `M1000-M1063`, `L1000-L1063`, and `B1000-B103F`; 10,546 iterations, 1,728,038 trace events, 0 error events, and all original values restored.
- [x] Verify a light SLMP QnUDV hardware smoke test. Checked QnUDV profile `melsec:qnudv` at `192.168.250.100:1025` using `D1000-D1003` and `M1000-M1015`; Word, DWord, word-bit, random bit batch, and mixed batch read passed, then original values were restored.
- [ ] Do not make a merge decision for the batch I/O changes until the real PLC validation above is complete.

## Future Work

- [x] Add initial automated UIA smoke tests for main window startup, monitor/watch surfaces, and start address editing.
- [x] Add automated UI tests for monitor scrolling and watch list scrolling.
- [x] Add automated UI tests for inline edit pause/resume behavior.
- [x] Verify Host Link runtime range catalog behavior in Scope against live KV hardware.
