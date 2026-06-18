# TODO

## Remaining Manual Validation

- [x] Verify out-of-range address handling and scroll limits with a real KV PLC.
- [x] Verify Host Link monitor read, write, CPU status, and CPU RUN/STOP behavior with a real KV PLC.
- [x] Verify TOYOPUC PC10G PC10 mode monitor read, write, CPU STOP, and CPU RUN behavior with a real PLC.
- [x] Verify TOYOPUC CPU STOP, stop release, and scan resume from the app with relay hops. Relay path `P1-L1:N2` to Nano 10GX is available, basic Python/.NET relay read/write/stress checks pass, plc-scope session read/write has relay frame coverage, and app-level RUN/STOP was observed on real hardware.
- [x] Verify Watch list visible-row-only reads while scrolling through a large watch list on a real KV PLC.
- [x] Verify optional communication trace logging during long-running communication with a real KV PLC.
- [ ] Verify SLMP watch-list batch reads on a real PLC with more than 50 visible/scrolling watch rows.
- [ ] Verify SLMP mixed watch values (Word, DWord, Float32, Bit, and word-bit addresses) match the previous sequential read results on real hardware.
- [ ] Verify invalid SLMP watch addresses show an error only on the affected row while other rows continue updating.
- [ ] Verify SLMP random-read failure fallback by using an unsupported route/device scenario and confirming sequential reads still update valid rows.
- [ ] Verify SLMP bit-device word/dword/float writes leave non-target devices unchanged when random bit write is used.
- [ ] Verify long-running SLMP trace/error logging remains stable during batch reads and random bit writes.
- [ ] Do not make a merge decision for the batch I/O changes until the real PLC validation above is complete.

## Future Work

- [x] Add initial automated UIA smoke tests for main window startup, monitor/watch surfaces, and start address editing.
- [x] Add automated UI tests for monitor scrolling and watch list scrolling.
- [x] Add automated UI tests for inline edit pause/resume behavior.
- [x] Verify Host Link runtime range catalog behavior in Scope against live KV hardware.
