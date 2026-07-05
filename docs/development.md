# Development Notes

This file is for maintainers and contributors. The repository root [README](../README.md) is the operator-facing manual; build, test, release, dependency, and maintenance details should live here instead of in the user guide.

## Requirements

- Windows
- .NET 9 SDK for the PLC Scope app target
- .NET 8, 9, and 10 SDKs when building with the sibling PLC communication source repositories
- Visual Studio 2022 or another editor with WPF support

`PlcScope.App` is a WPF application, so the app project is intended to build and run on Windows.

## Solution Layout

- `src/PlcScope.App`
  WPF application, windows, XAML, and view models.
- `src/PlcScope.Core`
  Protocol-neutral models, formatting, range planning, and block data builders.
- `src/PlcScope.Infrastructure`
  PLC protocol adapters, JSON persistence, and log storage.
- `tests/PlcScope.Core.Tests`
  Unit tests for core services and infrastructure protocol behavior.

## Dependencies

Third-party package versions are centralized in [Directory.Packages.props](../Directory.Packages.props).

Main third-party libraries:

- `CommunityToolkit.Mvvm`
- `Microsoft.Extensions.DependencyInjection`

PLC communication libraries are consumed from sibling local source repositories:

- `../plc-comm-slmp-dotnet/src/PlcComm.Slmp/PlcComm.Slmp.csproj`
- `../plc-comm-hostlink-dotnet/src/PlcComm.KvHostLink/PlcComm.KvHostLink.csproj`
- `../plc-comm-computerlink-dotnet/src/Toyopuc/PlcComm.Toyopuc.csproj`

The checked-in `lib/plc-comm/net9.0/` DLLs are retained as release snapshots and are not the active project references for local development builds.

## Build

Build the full solution:

```powershell
dotnet build .\PlcScopeDotNet.sln
```

Build the app only:

```powershell
dotnet build .\src\PlcScope.App\PlcScope.App.csproj -c Release
```

## Test

Run all tests:

```powershell
dotnet test .\PlcScopeDotNet.sln -m:1
```

The solution test run includes FlaUI UI automation tests. Keep `-m:1` when
running the full solution so test projects execute serially and the UIA desktop
session stays stable.

Run the core test project:

```powershell
dotnet test .\tests\PlcScope.Core.Tests\PlcScope.Core.Tests.csproj
```

## Publish

`build.bat` creates a Windows x64 single-file build.

```cmd
build.bat
```

Specify configuration:

```cmd
build.bat Release
```

Typical output:

```text
src\PlcScope.App\bin\Release\net9.0-windows\win-x64\publish\PlcScope.exe
```

Manual publish:

```powershell
dotnet restore .\src\PlcScope.App\PlcScope.App.csproj -r win-x64
dotnet publish .\src\PlcScope.App\PlcScope.App.csproj -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false
```

## Versioning

The app version is defined in [PlcScope.App.csproj](../src/PlcScope.App/PlcScope.App.csproj).

For an application version bump, update all of:

- `Version`
- `AssemblyVersion`
- `FileVersion`
- `AssemblyInformationalVersion`
- `InformationalVersion`

## Manual Validation

Recommended checks before publishing:

- build the full solution
- run all tests
- connect to each available PLC protocol
- verify monitor visible-row refresh
- verify watch list visible-row refresh
- verify inline writes
- verify bit toggles
- verify CPU RUN/STOP for supported protocols and CPU PAUSE for SLMP
- verify SLMP remote password unlock on connect and lock on disconnect when configured
- verify dark theme readability
- verify error history and optional communication log behavior

## Maintainer Documentation Rules

- Keep the root `README.md` focused on installation, connection setup, operation, troubleshooting, and sample projects.
- Put build/test/release commands in this file.
- Put expected application behavior and protocol contracts in [specification.md](specification.md).
- Put completed investigation notes and implementation reports under [improvements/close](improvements/close/).
- Put hardware validation notes under [validation](validation/).
- Put reusable project JSON files under [samples](samples/).

## Release Checklist

Before publishing a release build:

1. Update the version fields in [PlcScope.App.csproj](../src/PlcScope.App/PlcScope.App.csproj).
2. Build the full solution.
3. Run the full test suite with `-m:1`.
4. Run the manual validation checklist that matches the changed protocols or UI areas.
5. Build the Windows x64 single-file package with `build.bat Release`.
6. Confirm the published `PlcScope.exe` starts and opens the connection dialog.
7. Update [CHANGELOG.md](../CHANGELOG.md) with the user-visible changes and validation summary.
