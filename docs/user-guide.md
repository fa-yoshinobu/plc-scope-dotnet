# User Guide

Full operator reference for PLC Scope. For the overview, safety notes, and
quick start, see the root [README](../README.md).

## Connection Settings

### Common

| Field | Meaning |
| --- | --- |
| `Protocol` | Selects SLMP, Host Link, or TOYOPUC. |
| `Transport` | Selects TCP or UDP when supported by the protocol adapter. |
| `Host` | PLC IP address or host name. |
| `Port` | PLC communication port. |
| `Timeout (s)` | Read/write timeout in seconds. |
| `Refresh interval ms` | Periodic monitor/watch refresh interval. |

Typical ports:

- SLMP: `1025`
- KEYENCE Host Link: `8501`
- TOYOPUC Computer Link: `1025`

### SLMP

SLMP settings include PLC profile, routing fields, monitoring timer, and optional remote password.
The selector shows canonical display names while project files store the
canonical profile value.

Selectable MELSEC profiles:

| Profile | Target |
| --- | --- |
| `melsec:iq-r` | MELSEC iQ-R (built-in) |
| `melsec:iq-r:rj71en71` | MELSEC iQ-R (RJ71EN71) |
| `melsec:iq-f` | MELSEC iQ-F (built-in) |
| `melsec:iq-l` | MELSEC iQ-L (built-in) |
| `melsec:mx-r` | MELSEC MX-R (built-in) |
| `melsec:mx-r:rj71en71` | MELSEC MX-R (RJ71EN71) |
| `melsec:mx-f` | MELSEC MX-F (built-in) |
| `melsec:qnudv` | MELSEC QnUDV (built-in) |
| `melsec:qnudv:qj71e71-100` | MELSEC QnUDV (QJ71E71-100) |
| `melsec:qnu` | MELSEC QnU (built-in) |
| `melsec:qnu:qj71e71-100` | MELSEC QnU (QJ71E71-100) |
| `melsec:qcpu:qj71e71-100` | MELSEC-Q (QJ71E71-100) |
| `melsec:lcpu` | MELSEC-L (built-in) |
| `melsec:lcpu:lj71e71-100` | MELSEC-L (LJ71E71-100) |

Routing defaults are suitable for common direct Ethernet connections:

- Network: `0`
- Station: `255`
- Module I/O: `OwnStation` (canonical module I/O target name; project files store the name, e.g. `"slmpModuleIo": "OwnStation"`)
- MultiDrop is fixed internally to `0x00`

If a remote password is entered, PLC Scope unlocks the session after connecting and locks it before disconnecting.

### Host Link

Host Link settings mainly select the KEYENCE PLC profile. The selected profile controls device notation and comment/address behavior.
The selector shows canonical display names while project files store the
canonical profile value.

Selectable KEYENCE profile families:

| Profile | Target |
| --- | --- |
| `keyence:kv-nano` | KEYENCE KV-NANO |
| `keyence:kv-nano-xym` | KEYENCE KV-NANO (XYM) |
| `keyence:kv-3000` | KEYENCE KV-3000 |
| `keyence:kv-3000-xym` | KEYENCE KV-3000 (XYM) |
| `keyence:kv-5000` | KEYENCE KV-5000 |
| `keyence:kv-5000-xym` | KEYENCE KV-5000 (XYM) |
| `keyence:kv-7000` | KEYENCE KV-7000 |
| `keyence:kv-7000-xym` | KEYENCE KV-7000 (XYM) |
| `keyence:kv-8000` | KEYENCE KV-8000 |
| `keyence:kv-8000-xym` | KEYENCE KV-8000 (XYM) |
| `keyence:kv-x500` | KEYENCE KV-X500 |
| `keyence:kv-x500-xym` | KEYENCE KV-X500 (XYM) |

### TOYOPUC

TOYOPUC settings select the PLC profile, relay hops, local port, retry count, and retry delay.

Relay hop example:

```text
P1-L1:N2
```

Use the profile that matches the target PLC or compatibility mode. Unsupported devices are hidden when profile range information is available.

When routing through a relay hop, keep to one client at a time. A single client path is stable, but several TCP clients reaching the same relay hop at once contend for the target path and reads or writes start failing. Close other software that talks through the same hop before connecting.

## Monitor Tab

The Monitor tab shows a generated device range.

Main controls:

- `Device`: device family such as `D`, `DM`, or `P1-D`
- `Start address`: first address to show
- `Type`: display/write interpretation, such as word, DWord, Float32, or bit mode
- `Format`: `Dec` or `Hex`

PLC Scope reads only visible monitor rows during periodic refresh. This keeps communication and UI work bounded by what is on screen instead of the full generated range.

Right-click a row to add it to the watch list.

## Watch List

The Watch list is for addresses you want to keep together while debugging.

The table contains:

- `Address`
- `Type`
- `Format`
- `Value`
- raw hexadecimal text where applicable
- bit cells where applicable
- `Comment`

Supported actions:

- add rows from the Monitor tab
- edit address/type/format
- remove rows from the right-click menu or `Delete`
- reorder rows by drag and drop
- import/export watch CSV
- refresh only visible rows
- isolate invalid rows so one bad address does not stop other visible rows from updating

When using a word address such as `D100`, select `UInt16`, `Int16`, `UInt32`, `Int32`, or `Float32`. For a single bit inside a word, use a word-bit address such as `D100.3`.

## Address Syntax

PLC Scope follows the shared high-level address convention used by the PLC communication libraries.

| Syntax | Meaning | Examples |
| --- | --- | --- |
| Plain device address | Normal word or bit device | `D100`, `DM100`, `P1-D0100`, `M1000`, `MR000` |
| `.` | Bit inside a word | `D100.3`, `DM100.A`, `P1-D0100.D` |
| `:` | Type/special view suffix used by lower-level libraries | `D100:D`, `DM100:F`, `P1-D0100:F` |

A dotted hex digit is a bit index. For example, `D100.D` means bit `0xD` / bit 13, not a DWord request.

## Value Types

| Type | Size | Notes |
| --- | --- | --- |
| `UInt16` | 1 word | Unsigned 16-bit value. |
| `Int16` | 1 word | Signed 16-bit value. |
| `UInt32` | 2 words | Unsigned 32-bit value. Use on word devices. |
| `Int32` | 2 words | Signed 32-bit value. Use on word devices. |
| `Float32` | 2 words | IEEE 754 single-precision value. Use on word devices. |
| `Bit` | 1 bit | Direct bit device or word-bit address. |

Some protocols and devices cannot represent every type. If a type is not valid for the selected address, the row shows an error or the type list excludes that option.

## Writing Values

Inline editing is available in Monitor and Watch list value cells when the selected protocol and device support writing.

Keyboard behavior:

- `Enter`: write the edited value
- `Esc`: cancel monitor inline edit
- Up / Down in a value cell: move to the value cell above or below

Periodic refresh pauses while a value cell is being edited.

Integer input outside the target range is clamped before writing:

- `Bit`: values less than `1` become `0`; values `1` or greater become `1`
- `UInt16`: `0` to `65535`
- `Int16`: `-32768` to `32767`
- `UInt32`: `0` to `4294967295`
- `Int32`: `-2147483648` to `2147483647`

Bit cells can be clicked to toggle the value when writing is supported.

## CPU Control

Use the `CPU` menu to issue supported CPU commands.

| Protocol | Commands |
| --- | --- |
| SLMP | `CPU RUN`, `CPU STOP`, `CPU PAUSE` |
| Host Link | `CPU RUN`, `CPU STOP` where supported by the target PLC |
| TOYOPUC | `CPU RUN`, `CPU STOP` mapped to TOYOPUC scan commands |

For SLMP, `CPU RUN` sends Remote RUN with `clearMode = 0`, so device memory is not cleared when switching the CPU to RUN.

The status bar shows the latest known CPU state when the protocol supports reading it.

## Comments

Use `File` -> `Import comment CSV` to load comment text.

PLC Scope can load multiple comment CSV files. If multiple comments match the same device, `Comment1` has priority when present.

Comment CSV files stay external to the project file. PLC Scope reads them only when you explicitly select `File` -> `Import comment CSV` in the current session. Project JSON stores neither the CSV paths nor CSV-derived comments, and opening a project does not reload a comment CSV automatically.

## Project Files

Projects are saved as JSON and include:

- connection settings
- monitor block settings
- watch list entries
- selected display settings

Application settings are stored under:

```text
%LOCALAPPDATA%\PlcScope\settings.json
```

## Debug Sample Projects

Sample projects are available under [samples](samples/). They are useful for watch-list layout, mixed-type display, batch-read behavior, and scroll testing.

| Sample | Purpose |
| --- | --- |
| [SLMP iQ-R 100-row watch](samples/slmp-iqr-100-watch.json) | Mixed SLMP watch list with word, DWord, Float32, word-bit, and direct bit rows. |
| [KEYENCE Host Link 100-row mixed debug watch](samples/keyence-hostlink-100-watch.json) | Mixed Host Link watch list with DM word values and MR direct bits. |
| [TOYOPUC 100-row mixed debug watch](samples/toyopuc-100-watch.json) | Mixed TOYOPUC watch list with `P1-D` word values and `P1-M` direct bits. |

Review and adjust the addresses before writing values on real equipment.

## Logs And Troubleshooting

Open logs from the `Tools` menu.

| Tool | Use |
| --- | --- |
| `Error history` | Recent user-visible communication and validation errors. |
| `Trace log` | Optional protocol frame log for troubleshooting. |

Log files are stored next to the executable when write permission is available:

- `trace.log.jsonl`
- `error.log.jsonl`

Each log keeps the latest 500 entries.

Common checks:

- Confirm protocol, transport, host, and port.
- Confirm PLC-side Ethernet / Host Link / Computer Link settings.
- Confirm the selected PLC profile matches the connected PLC.
- For SLMP routing issues, try the routing defaults first.
- For TOYOPUC relay routing, confirm the relay hop string, such as `P1-L1:N2`.
- If a watch row is red, check that address and type combination first; other rows may still be updating normally.
- If values change while editing, confirm the value cell is actually in edit mode; refresh pauses only during inline edit.
