# TODO

## Remaining Manual Validation

- [x] Verify out-of-range address handling and scroll limits with a real KV PLC.
- [x] Verify Host Link monitor read, write, CPU status, and CPU RUN/STOP behavior with a real KV PLC.
- [x] Verify TOYOPUC PC10G PC10 mode monitor read, write, CPU STOP, and CPU RUN behavior with a real PLC.
- [ ] Verify TOYOPUC CPU STOP, stop release, and scan resume with relay hops. Blocked: relay-hop environment is not currently available.
- [x] Verify Watch list visible-row-only reads while scrolling through a large watch list on a real KV PLC.
- [x] Verify optional communication trace logging during long-running communication with a real KV PLC.

## Future Work

- [x] Add initial automated UIA smoke tests for main window startup, monitor/watch surfaces, and start address editing.
- [x] Add automated UI tests for monitor scrolling and watch list scrolling.
- [x] Add automated UI tests for inline edit pause/resume behavior.
- [x] Verify Host Link runtime range catalog behavior in Scope against live KV hardware.
