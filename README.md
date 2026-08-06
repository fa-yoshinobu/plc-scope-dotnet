# PLC Scope

[![Release](https://github.com/fa-yoshinobu/plc-scope-dotnet/actions/workflows/release.yml/badge.svg)](https://github.com/fa-yoshinobu/plc-scope-dotnet/actions/workflows/release.yml)
[![Version](https://img.shields.io/badge/version-2.2.0-blue)](src/PlcScope.App/PlcScope.App.csproj)
[![.NET](https://img.shields.io/badge/.NET-10.0_LTS-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![C#](https://img.shields.io/badge/language-C%23-239120?logo=csharp&logoColor=white)](https://learn.microsoft.com/dotnet/csharp/)
[![Platform](https://img.shields.io/badge/platform-Windows-0078D4?logo=windows&logoColor=white)](https://learn.microsoft.com/windows/)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)

PLC Scope is a Windows desktop tool for live PLC I/O checks. It can connect to a PLC, monitor device ranges, keep important devices in a watch list, write values, import comments, and save the setup as a project file.

It is two tools in one:

- **A PLC monitor.** Connect, watch live device values, edit them inline, and keep a debugging layout as a project file.
- **A zero-code verification app for the plc-comm .NET libraries.** Every connection, read, and write goes through the same [PlcComm packages](#built-on-the-plc-comm-libraries) you would reference in your own application, so a working PLC Scope session proves the library, the connection settings, and the PLC-side setup before you write any code.

This README is the entrance: overview, safety, and first connection. The full
operator reference lives in the [User Guide](docs/user-guide.md).

## Safety

PLC Scope can write live PLC devices and issue CPU commands. Before writing or changing CPU state, confirm that the selected address range is safe for the connected equipment.

Project files can store connection settings, including an SLMP remote password when one is entered. Treat project files as sensitive when a password is configured.

Creating a new project closes the PLC connection first, and closing PLC Scope releases the session, so an SLMP remote password is locked again on exit. Both were manual steps before v2.1.0.

## Requirements

- Windows
- A PLC reachable over the selected protocol
- .NET 10 Runtime when using a framework-dependent build

## Supported Protocols

| Protocol | Main Use | Notes |
| --- | --- | --- |
| MELSEC `SLMP` | MELSEC Ethernet device monitoring and writing | Supports remote password, CPU RUN / STOP / PAUSE, device range discovery, visible watch batching, and direct bit-device batch writes. |
| KEYENCE KV `Host Link` | KEYENCE KV Ethernet Host Link access | Supports monitor reads, writes, comments, CPU RUN / STOP, visible watch named reads, and consecutive direct bit writes where safe. |
| JTEKT TOYOPUC `Computer Link` | TOYOPUC Computer Link access | Supports selectable PLC profiles, relay hops, monitor reads, writes, CPU RUN / STOP, visible watch read-many batching, and direct bit write-many batching. |

## Built On The plc-comm Libraries

PLC Scope does not implement any PLC protocol itself. All communication goes
through the plc-comm family of .NET libraries:

| Protocol | Library | Library docs |
| --- | --- | --- |
| MELSEC SLMP | [PlcComm.Slmp](https://www.nuget.org/packages/PlcComm.Slmp/) | [Getting started](https://fa-yoshinobu.github.io/plc-comm-docs-site/slmp/dotnet/GETTING_STARTED/) |
| KEYENCE Host Link | [PlcComm.KvHostLink](https://www.nuget.org/packages/PlcComm.KvHostLink/) | [Getting started](https://fa-yoshinobu.github.io/plc-comm-docs-site/hostlink/dotnet/GETTING_STARTED/) |
| TOYOPUC Computer Link | [PlcComm.Toyopuc](https://www.nuget.org/packages/PlcComm.Toyopuc/) | [Getting started](https://fa-yoshinobu.github.io/plc-comm-docs-site/computerlink/dotnet/GETTING_STARTED/) |

The PLC profiles, address syntax, typed access, and batching you see in
PLC Scope are the libraries' own behavior. When a connection works here, the
same settings — protocol, host, port, profile, addresses — carry over directly
to your own application code.

Full library documentation for all languages and protocols:
[PLC Communication Libraries](https://fa-yoshinobu.github.io/plc-comm-docs-site/).

## Quick Start

1. Open `Connection settings`.
2. Select `Protocol`.
3. Set `Transport`, `Host`, `Port`, `Timeout (s)`, and `Refresh interval ms`.
4. Set the protocol-specific options.
5. Click `OK`, then click `Connect`.
6. On the `Monitor` tab, select the device, start address, display type, and format.
7. Add important rows to the `Watch list` from the monitor row right-click menu.
8. Save the setup with `File` -> `Save project`.

Connection settings per protocol, monitor and watch usage, address syntax,
value types, writing, CPU control, comments, project files, samples, and
troubleshooting are all covered in the [User Guide](docs/user-guide.md).

## Documentation

- [User Guide](docs/user-guide.md)
- [Changelog](CHANGELOG.md)
- [Security notes](SECURITY.md)
- [PLC Communication Libraries documentation site](https://fa-yoshinobu.github.io/plc-comm-docs-site/)

Maintainer material (specification, development notes, improvement plans) is
indexed in [docs/README.md](docs/README.md).

## License

| Item | Value |
| --- | --- |
| License | [MIT](LICENSE) |
