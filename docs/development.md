# Development Notes

## Requirements

- Windows
- .NET 9 SDK
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

Package versions are centralized in [Directory.Packages.props](../Directory.Packages.props).

Main libraries:

- `PlcComm.Slmp`
- `PlcComm.KvHostLink`
- `PlcComm.Toyopuc`
- `CommunityToolkit.Mvvm`
- `Microsoft.Extensions.DependencyInjection`

When sibling PLC communication repositories exist next to this repository, project references are used for local development. Otherwise NuGet package references are used.

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
dotnet test .\PlcScopeDotNet.sln
```

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
- verify CPU RUN/STOP for supported protocols
- verify dark theme readability
- verify error history and optional communication log behavior
