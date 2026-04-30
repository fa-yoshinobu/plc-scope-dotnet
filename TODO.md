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
- [ ] Integrate more precise Host Link range data if the communication library exposes it.

## Completed

- [x] Implement Watch list tab and monitor-to-watch registration.
- [x] Prevent duplicate Watch list addresses.
- [x] Remove Watch list rows from the context menu and the `Delete` key.
- [x] Highlight invalid Watch list addresses.
- [x] Align Monitor and Watch visual styling.
- [x] Refresh the affected Watch row immediately when `Type` or `Format` changes.
- [x] Read only the active tab: Monitor tab reads monitor rows, Watch tab reads watch rows.
- [x] Read only visible Monitor rows.
- [x] Read only visible Watch rows.
- [x] Pause reads while scrolling.
- [x] Avoid replacing unchanged visible Monitor rows during refresh.
- [x] Reuse Watch bit cells instead of recreating them on each refresh.
- [x] Add TOYOPUC CPU STOP, scan stop release, and scan resume command mapping.
- [x] Verify TOYOPUC CPU STOP and RUN command frames with unit tests.
- [x] Verify TOYOPUC CPU STOP and stop release on the connected PLC without relay hops.
- [x] Use TOYOPUC profile range data to hide unsupported devices.
- [x] Verify TOYOPUC profile range and split-range handling with unit tests.
- [x] Normalize TOYOPUC prefixed addresses for word and bit devices.
- [x] Verify TOYOPUC prefixed address parsing and range width handling with unit tests.
- [x] Verify TOYOPUC PC10G UI operation for 2 minutes on a connected PLC: Monitor/Watch tab switching, monitor scrolling, monitor-to-watch registration, connection/RUN status retention, and inline edit pause during refresh.
- [x] Persist Watch list entries in project files.
- [x] Verify project JSON round-trip with Watch list entries.
- [x] Persist error history immediately and keep the latest 500 records.
- [x] Verify error log persistence, trimming, and clearing with unit tests.
- [x] Buffer communication trace logs and flush them when loaded or cleared.
- [x] Verify trace log buffering and clearing with unit tests.
- [x] Rewrite README and docs in ASCII English.
- [x] Bump app version to `0.1.2`.
- [x] Verify SLMP device range acquisition and live monitor reads with an iQ-R PLC at `192.168.250.100:1025`.
- [x] Verify SLMP monitor start-address keyboard entry against a live iQ-R PLC.
- [x] Remove display padding from SLMP decimal device addresses in Monitor rows and scroll-updated Start address text.
- [x] Verify SLMP decimal address display rules with unit tests for D/R/RD/SD/M families.
