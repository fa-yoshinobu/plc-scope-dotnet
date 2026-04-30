# TODO

## Manual Validation

- [ ] Verify SLMP device range acquisition with a real PLC.
- [ ] Verify out-of-range address handling and scroll limits with a real PLC.
- [ ] Verify Host Link monitor, write, and CPU RUN/STOP behavior with a real PLC.
- [ ] Verify TOYOPUC monitor and write behavior on the target PLC profiles.
- [ ] Verify TOYOPUC CPU STOP, stop release, and scan resume behavior with and without relay hops.
- [ ] Verify TOYOPUC unsupported devices are hidden for each selected profile.
- [ ] Verify TOYOPUC prefixed address normalization for word and bit devices.
- [ ] Verify Monitor and Watch visual alignment in light and dark themes.
- [ ] Verify Watch list visible-row-only reads while scrolling through a large watch list.
- [ ] Verify Watch list type and format changes refresh the affected row immediately.
- [ ] Verify inline editing is not overwritten by periodic refresh.
- [ ] Verify optional communication trace logging during long-running communication.
- [ ] Verify error history persistence after application restart.

## Future Work

- [ ] Add automated UI tests for monitor scrolling and watch list scrolling.
- [ ] Add automated UI tests for inline edit pause/resume behavior.
- [ ] Add automated tests for project files that include watch list entries.
- [ ] Integrate more precise Host Link range data if the communication library exposes it.
