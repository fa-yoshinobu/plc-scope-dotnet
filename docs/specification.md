# PLC Scope Specification

## Monitor View

The Monitor tab displays a generated address range from the selected device and start address.

Only visible rows are read during periodic refresh. Refresh pauses while the user scrolls, then resumes after scrolling stops. This keeps PLC traffic and UI work bounded by the viewport instead of the full device range.

Start addresses are normalized before use. For example, lowercase input is converted to the protocol's canonical notation. When device range data is available, unsupported devices are hidden and out-of-range addresses are rejected or moved into the valid range before monitor rows are generated.

## Watch View

The Watch tab contains user-selected addresses. Items are added from the Monitor tab context menu.

The Watch tab displays:

- address
- type
- format
- value
- raw hexadecimal text
- bit cells
- comment

Duplicate addresses are not allowed. Invalid addresses are shown as errors on the address cell. Rows can be removed from the context menu or with the `Delete` key.

Only visible watch rows are read. Type and format changes refresh the affected row immediately.

## Display And Write Modes

Word devices support:

- `UInt16` / `Int16`: one word
- `UInt32` / `Int32`: two words
- `Float32`: two words interpreted as IEEE 754 single precision
- `Bit`: bit-style display for supported bit targets

Bit devices support:

- single bit display
- packed 16-bit display
- packed 32-bit display
- bit toggling when the protocol supports writing that target

Inline value editing pauses periodic refresh so user input is not overwritten.

Keyboard behavior:

- `Enter`: write the edited value
- `Esc`: cancel monitor inline edit
- `Delete`: remove the selected watch item

## Protocol Behavior

### SLMP

SLMP supports device range acquisition based on the selected PLC family and settings. The Device Range window shows support status, point count, lower bound, upper bound, and display notation.

Long timer/counter families that are 32-bit by definition are displayed as DWord-style values.

When an SLMP remote password is configured, the application sends Remote Password Unlock after opening the connection and sends Remote Password Lock before disconnecting. Monitor reads, writes, device range reads, CPU state reads, and CPU commands share that unlocked session.

SLMP CPU control exposes `CPU RUN`, `CPU STOP`, and `CPU PAUSE`. `CPU PAUSE` is shown only when SLMP is the active protocol. `CPU RUN` sends Remote RUN with `clearMode = 0`.

### Host Link

Host Link supports monitor reads, writes, and CPU mode operations where the library and PLC model allow them.

Visible watch-list reads use Host Link named reads where possible. Invalid batch requests fall back to the existing per-row read path so valid rows can continue updating.

Consecutive direct bit-device writes can be grouped into one Host Link consecutive write when all requested bits are in the same device family and safe contiguous range. Non-consecutive bit writes, word-bit writes, and unproven bank-boundary cases use the existing sequential write path.

### TOYOPUC

TOYOPUC uses the Computer Link protocol adapter.

The selected TOYOPUC device profile controls which device families are available. Unsupported devices are removed from the device list when profile range data is available.

Prefixed addresses such as `P1-D`, `P1-P`, `P1-S`, `P1-X`, and `P1-Y` are normalized before read and write operations.

Packed bit reads are used for TOYOPUC bit block monitoring where that matches the protocol library. The application expands the packed word data into visible bit cells.

Visible watch-list reads use TOYOPUC read-many batching where possible. Invalid local planning failures remain isolated to the affected row; batch request failures fall back to the existing per-row read path.

Direct bit-device writes can be grouped through TOYOPUC write-many batching. Word-bit writes and non-bit writes use the existing typed or bit-in-word write paths.

CPU control maps to TOYOPUC scan commands:

- `CPU STOP`: scan stop
- `CPU RUN`: scan stop release followed by scan resume

Relay hops are applied when configured.

## CPU State

The status bar shows the latest known CPU state, including RUN, STOP, PAUSE, or PROGRAM where supported, when the active protocol can read it. Unsupported protocols show an unknown state and disable CPU control commands.

## Logs

The application provides:

- error history
- optional communication trace log

Error history is written immediately. Trace logs are batched to reduce disk load.

Log files:

- `error.log.jsonl`
- `trace.log.jsonl`

Each log keeps the latest 500 entries.

## Project Files

Project JSON files contain:

- project version
- connection settings
- monitor block definitions
- watch list entries

Watch list entries persist only address, type, format, enable flag, and comment. Value, raw hex text, bit cells, and error state are runtime display fields and are not saved.

Comment CSV files are session-only external inputs. They are loaded only through an explicit `Import comment CSV` action. Project JSON does not store their paths or CSV-derived comments, and project loading never accesses a comment CSV automatically. Watch-list-owned comments remain part of the watch list and are independent from comment CSV data.

Project compatibility is best-effort across early `0.1.x` releases.
