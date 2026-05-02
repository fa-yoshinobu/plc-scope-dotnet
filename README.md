# PLC Scope

PLC Scope is a Windows PLC I/O monitor for reading and writing device values from a desktop UI.

## Supported Protocols

- Mitsubishi MELSEC `SLMP`
- KEYENCE KV `Host Link`
- JTEKT TOYOPUC `Computer Link`

## Main Features

- Monitor visible PLC device ranges with periodic refresh.
- Read only the visible monitor rows to reduce PLC and UI load.
- Add monitor addresses to a watch view from the monitor context menu.
- Watch list and monitor view are separated by tabs.
- Watch list reads only the visible watch rows.
- Edit values inline and write with `Enter`.
- Toggle writable bit cells from the monitor and watch views.
- Display Word, DWord, Float32, Bit, decimal, hexadecimal, and binary formats.
- Import comments from one or more external CSV files.
- Save and load projects as JSON.
- Switch light and dark themes.
- View recent error history.

## TOYOPUC Notes

TOYOPUC support uses the Computer Link library through the application protocol adapter.

- Device profiles are selectable in the connection settings.
- Unsupported devices for the selected PLC profile are hidden from the device list when range information is available.
- Prefixed TOYOPUC addresses such as `P1-D`, `P1-P`, `P1-S`, `P1-X`, and `P1-Y` are normalized by the application before reading.
- Bit device block monitoring uses packed word reads where appropriate, then expands the bits in the UI.
- CPU `RUN` / `STOP` is supported for TOYOPUC scan resume, scan stop release, and scan stop commands.

## CPU Control

Use the `CPU` menu to issue `CPU RUN` or `CPU STOP`.

Unsupported protocols disable CPU control. The status bar shows the latest CPU state when the protocol can read it.

## Watch List

Add an item from the Monitor tab by right-clicking a monitor row and selecting `Add to watch list`.

The Watch tab supports:

- address, type, format, value, raw hex, bit cells, and comment columns
- duplicate address prevention
- invalid address highlighting
- row removal from the context menu or the `Delete` key
- immediate refresh when `Type` or `Format` changes

## Writing Values

Inline editing is available in the monitor and watch views.

- `Enter`: write the edited value
- `Esc`: cancel monitor inline edit
- periodic refresh pauses while editing to avoid overwriting input

For bit cells, click the bit button to toggle the value when writing is supported by the protocol and device.

## Logs

Open logs from the `Tools` menu.

- `Error history`: recent user-visible communication and validation errors
- `Trace log`: optional frame log for protocol troubleshooting

Logs are stored next to the executable when write permission is available:

- `trace.log.jsonl`
- `error.log.jsonl`

Each log keeps the latest 500 entries.

## Projects And Settings

Projects are saved as JSON and include:

- connection settings
- monitor block settings
- watch list entries
- display settings used by the project
- optional comment CSV paths; comment text stays in the external CSV files

Application settings are stored under `%LOCALAPPDATA%\PlcScope\settings.json`.

## Development

Build:

```powershell
dotnet build .\PlcScopeDotNet.sln
```

Test:

```powershell
dotnet test .\PlcScopeDotNet.sln
```

Publish a Windows x64 folder build:

```powershell
dotnet publish .\src\PlcScope.App\PlcScope.App.csproj -c Release -r win-x64 --self-contained false
```

## Documentation

- [Specification](docs/specification.md)
- [Development notes](docs/development.md)
- [TODO](TODO.md)

## Version

Current version: `0.1.3`

## License

MIT License. See [LICENSE](LICENSE).
