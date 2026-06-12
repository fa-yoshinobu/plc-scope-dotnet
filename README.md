# PLC Scope

![License](https://img.shields.io/badge/license-MIT-green)

[![.NET 9](https://img.shields.io/badge/.NET-9-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![C#](https://img.shields.io/badge/C%23-239120?logo=csharp&logoColor=white)](https://learn.microsoft.com/dotnet/csharp/)
[![Windows](https://img.shields.io/badge/Windows-0078D4?logo=windows&logoColor=white)](https://learn.microsoft.com/windows/)

PLC Scope is a Windows desktop tool for monitoring and writing PLC device values.

It is intended for live I/O checks: select a protocol, connect to a PLC, monitor a device range, and keep frequently used devices in a watch list.

## Requirements

- Windows
- .NET 9 Runtime for framework-dependent builds
- A PLC reachable by one of the supported protocols

## Supported Protocols

- Mitsubishi MELSEC `SLMP`
- KEYENCE KV `Host Link`
- JTEKT TOYOPUC `Computer Link`

## Main Features

- Monitor PLC device ranges with periodic refresh.
- Read only visible monitor rows to reduce PLC and UI load.
- Add monitor rows to the watch list from the right-click menu.
- Keep monitor and watch list views in separate tabs.
- Read only visible watch list rows.
- Reorder watch list rows by drag and drop.
- Import and export the watch list as CSV.
- Import comments from one or more external comment CSV files.
- Display values as `Dec` or `Hex`.
- Display word bits as clickable bit cells.
- Edit values inline and write with `Enter`.
- Pause refresh while a value is being edited so input is not overwritten.
- Clamp out-of-range integer input to the target type range before writing.
- Save and load project files as JSON.
- Switch light and dark themes.
- View recent error history and optional protocol trace logs.

## Basic Use

1. Open `Connection settings`.
2. Select the protocol and PLC connection settings.
3. Click `Connect`.
4. On the `Monitor` tab, choose `Device`, `Start address`, `Type`, and `Format`.
5. Use the `Watch list` tab for devices you want to keep visible.

## Monitor

The Monitor tab shows a device range starting at `Start address`.

Columns:

- `Address`: PLC device address.
- `Value`: editable value for writable numeric and bit rows.
- `Hex`: raw hexadecimal value where applicable.
- `Bits`: bit cells for word and packed bit displays.
- `Comment`: imported comment text.

For word bit expansion, child addresses such as `D0.0` are indented in the `Address` column. The other columns stay aligned with the parent word row.

## Watch List

Add an item from the Monitor tab by right-clicking a monitor row and selecting `Add to watch list`.

The Watch list supports:

- address, type, format, value, raw hex, bit cells, and comment columns
- `Dec` and `Hex` formats
- word bit addresses such as `D0.0`
- duplicate address prevention
- invalid address highlighting
- row removal from the right-click menu or the `Delete` key
- drag-and-drop row ordering
- CSV import and export
- immediate refresh when `Type` or `Format` changes

When the address is a normal word address such as `D0`, `Bit` is not offered as a type. Use a word bit address such as `D0.0` when you want to monitor a single bit inside a word.

## Writing Values

Inline editing is available in the Monitor and Watch list views.

- `Enter`: write the edited value
- `Esc`: cancel monitor inline edit
- Up / Down in a value cell: move to the value cell above or below

Periodic refresh pauses while a value cell is being edited.

Integer input above the target range is clamped before writing. Examples:

- `Bit`: values less than `1` become `0`; values `1` or greater become `1`
- `UInt16`: maximum `65535`
- `Int16`: maximum `32767`
- `UInt32`: maximum `4294967295`
- `Int32`: maximum `2147483647`

For bit cells, click the bit button to toggle the value when writing is supported by the selected protocol and device.

## Comments

Use `File` -> `Import comment CSV` to load comment text.

The application can read comments from multiple CSV files. If multiple comments match the same device, `Comment1` has priority when it is present.

Comment CSV files stay external to the project file. Project JSON stores the comment CSV paths, not the CSV contents.

## Projects And Settings

Projects are saved as JSON and include:

- connection settings
- monitor block settings
- watch list entries
- display settings used by the project
- optional comment CSV paths

Application settings are stored under `%LOCALAPPDATA%\PlcScope\settings.json`.

SLMP remote password is part of the connection settings. If it is entered, treat saved project files as sensitive.

## TOYOPUC Notes

TOYOPUC support uses the Computer Link library through the application protocol adapter.

- Device profiles are selectable in the connection settings.
- Unsupported devices for the selected PLC profile are hidden from the device list when range information is available.
- Prefixed TOYOPUC addresses such as `P1-D`, `P1-P`, `P1-S`, `P1-X`, and `P1-Y` are normalized by the application before reading.
- Bit device block monitoring uses packed word reads where appropriate, then expands the bits in the UI.
- CPU `RUN` / `STOP` is supported for TOYOPUC scan resume, scan stop release, and scan stop commands.

## CPU Control

Use the `CPU` menu to issue `CPU RUN` or `CPU STOP`.

For SLMP connections, `CPU RUN` sends Remote RUN with `clearMode = 0`, so device memory is not cleared when switching the CPU to RUN.
SLMP connections also show `CPU PAUSE`; other protocols only expose `CPU RUN` and `CPU STOP`.

When an SLMP remote password is set in `Connection settings`, the application unlocks the SLMP session after connecting and locks it before disconnecting. The same unlocked session is used for monitor reads, writes, and CPU commands.

Unsupported protocols disable CPU control. The status bar shows the latest CPU state when the protocol can read it.

## Logs

Open logs from the `Tools` menu.

- `Error history`: recent user-visible communication and validation errors
- `Trace log`: optional frame log for protocol troubleshooting

Logs are stored next to the executable when write permission is available:

- `trace.log.jsonl`
- `error.log.jsonl`

Each log keeps the latest 500 entries.

## Development

Build:

```powershell
dotnet build .\PlcScopeDotNet.sln
```

Test:

```powershell
dotnet test .\PlcScopeDotNet.sln -m:1
```

The solution includes FlaUI-based UI automation tests. Use `-m:1` for the
full solution test run so the UI test project runs in a stable serialized
desktop session.

Publish a Windows x64 single-file build:

```cmd
build.bat Release
```

Typical output:

```text
src\PlcScope.App\bin\Release\net9.0-windows\win-x64\publish\PlcScope.exe
```

## Documentation

- [Specification](docs/specification.md)
- [Development notes](docs/development.md)
- [TODO](TODO.md)

## License

MIT License. See [LICENSE](LICENSE).
