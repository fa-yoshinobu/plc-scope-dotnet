# TODO

## Remaining Manual Validation

- [ ] Verify out-of-range address handling and scroll limits with a real PLC.
- [ ] Verify Host Link monitor, write, and CPU RUN/STOP behavior with a real PLC.
- [ ] Verify TOYOPUC monitor and write behavior across all target PLC profiles.
- [ ] Verify TOYOPUC CPU STOP, stop release, and scan resume with relay hops.
- [ ] Verify Watch list visible-row-only reads while scrolling through a large watch list.
- [ ] Verify optional communication trace logging during long-running communication with a real PLC.

## Future Work

- [ ] Add automated UI tests for monitor scrolling and watch list scrolling.
- [ ] Add automated UI tests for inline edit pause/resume behavior.
- [ ] Verify Host Link runtime range catalog behavior in Scope against live KV hardware.
