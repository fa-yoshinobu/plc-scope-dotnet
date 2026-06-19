# PLC Scope

[![Release](https://github.com/fa-yoshinobu/plc-scope-dotnet/actions/workflows/release.yml/badge.svg)](https://github.com/fa-yoshinobu/plc-scope-dotnet/actions/workflows/release.yml)
[![Version](https://img.shields.io/badge/version-0.5.1-blue)](src/PlcScope.App/PlcScope.App.csproj)
[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![C#](https://img.shields.io/badge/language-C%23-239120?logo=csharp&logoColor=white)](https://learn.microsoft.com/dotnet/csharp/)
[![Platform](https://img.shields.io/badge/platform-Windows-0078D4?logo=windows&logoColor=white)](https://learn.microsoft.com/windows/)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)

PLC Scope is a Windows desktop tool for live PLC I/O checks. It can connect to a PLC, monitor device ranges, keep important devices in a watch list, write values, import comments, and save the setup as a project file.

Use this README as the operator guide. Build, test, release, and maintainer notes live in [docs/development.md](docs/development.md).

## Safety

PLC Scope can write live PLC devices and issue CPU commands. Before writing or changing CPU state, confirm that the selected address range is safe for the connected equipment.

Project files can store connection settings, including an SLMP remote password when one is entered. Treat project files as sensitive when a password is configured.

## Requirements

- Windows
- A PLC reachable over the selected protocol
- .NET 9 Runtime when using a framework-dependent build

For development or local builds, see [Development notes](docs/development.md).

## Supported Protocols

| Protocol | Main Use | Notes |
| --- | --- | --- |
| MELSEC `SLMP` | MELSEC Ethernet device monitoring and writing | Supports remote password, CPU RUN / STOP / PAUSE, device range discovery, visible watch batching, and direct bit-device batch writes. |
| KEYENCE KV `Host Link` | KEYENCE KV Ethernet Host Link access | Supports monitor reads, writes, comments, CPU RUN / STOP, visible watch named reads, and consecutive direct bit writes where safe. |
| JTEKT TOYOPUC `Computer Link` | TOYOPUC Computer Link access | Supports selectable PLC profiles, relay hops, monitor reads, writes, CPU RUN / STOP, visible watch read-many batching, and direct bit write-many batching. |

## Quick Start

1. Open `Connection settings`.
2. Select `Protocol`.
3. Set `Transport`, `Host`, `Port`, `Timeout (s)`, and `Refresh interval ms`.
4. Set the protocol-specific options.
5. Click `OK`, then click `Connect`.
6. On the `Monitor` tab, select the device, start address, display type, and format.
7. Add important rows to the `Watch list` from the monitor row right-click menu.
8. Save the setup with `File` -> `Save project`.

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

Routing defaults are suitable for common direct Ethernet connections:

- Network: `0`
- Station: `255`
- Module I/O: `0x03FF`
- Multidrop: `0x00`

If a remote password is entered, PLC Scope unlocks the session after connecting and locks it before disconnecting.

### Host Link

Host Link settings mainly select the KEYENCE PLC profile. The selected profile controls device notation and comment/address behavior.

### TOYOPUC

TOYOPUC settings select the PLC profile, relay hops, local port, retry count, and retry delay.

Relay hop example:

```text
P1-L1:N2
```

Use the profile that matches the target PLC or compatibility mode. Unsupported devices are hidden when profile range information is available.

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

Comment CSV files stay external to the project file. Project JSON stores the CSV paths, not the CSV contents.

## Project Files

Projects are saved as JSON and include:

- connection settings
- monitor block settings
- watch list entries
- selected display settings
- optional comment CSV paths

Application settings are stored under:

```text
%LOCALAPPDATA%\PlcScope\settings.json
```

## Debug Sample Projects

Sample projects are available under [docs/samples](docs/samples/). They are useful for watch-list layout, mixed-type display, batch-read behavior, and scroll testing.

| Sample | Purpose |
| --- | --- |
| [SLMP iQ-R 100-row watch](docs/samples/slmp-iqr-100-watch.json) | Mixed SLMP watch list with word, DWord, Float32, word-bit, and direct bit rows. |
| [KEYENCE Host Link 100-row mixed debug watch](docs/samples/keyence-hostlink-100-watch.json) | Mixed Host Link watch list with DM word values and MR direct bits. |
| [TOYOPUC 100-row mixed debug watch](docs/samples/toyopuc-100-watch.json) | Mixed TOYOPUC watch list with `P1-D` word values and `P1-M` direct bits. |

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

## Documentation

- [Documentation index](docs/README.md)
- [Specification](docs/specification.md)
- [Development and maintainer notes](docs/development.md)
- [Development history](docs/DEVELOPMENT_HISTORY.md)
- [Security notes](SECURITY.md)
- [Improvement plans and archive](docs/improvements/README.md)
- [Batch I/O report](docs/improvements/close/perf-batch-io-report.md)
- [TOYOPUC relay validation](docs/validation/toyopuc-relay-hop-validation-2026-06-12.md)
- [Validation checklist](TODO.md)

## License

| Item | Value |
| --- | --- |
| License | [MIT](LICENSE) |
