# Development Notes

This file is for maintainers and contributors. The repository root [README](../README.md) is the operator-facing manual; build, test, release, dependency, and maintenance details should live here instead of in the user guide.

## Requirements

- Windows
- .NET 10 SDK for the PLC Scope app target
- Visual Studio 2022 or another editor with WPF support

[global.json](../global.json) pins the SDK to `10.0.202` with `latestFeature` roll-forward, so any installed 10.0 SDK from that feature band or newer is used and older bands are rejected.

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

PLC communication libraries are restored from the centrally managed NuGet package versions in `Directory.Packages.props`:

- `PlcComm.Slmp`
- `PlcComm.KvHostLink`
- `PlcComm.Toyopuc`

The historical DLL snapshots under `lib/plc-comm/net9.0/` are not active project references and are not used by local or release builds.

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

## Continuous Integration

[ci.yml](../.github/workflows/ci.yml) restores, builds, and tests the solution on
`windows-latest` for every push to `main`, every pull request that targets `main`,
and manual dispatch. It skips
`PlcScope.App.UiTests.MainWindowUiTests`, because those FlaUI tests drive the real
window with synthetic input and reset the PLC Scope app-data directory, which a hosted
runner cannot do reliably. Run them locally with the full solution test command above.

[release.yml](../.github/workflows/release.yml) still runs only on a `v*` tag or manual
dispatch and publishes the single-file build.

Avoid pushing to `main` directly; land changes through a pull request so CI reports
before the merge. To enforce this on GitHub, open *Settings > Branches > Add branch
protection rule* (or *Settings > Rules > Rulesets*), target `main`, and enable
"Require a pull request before merging" plus "Require status checks to pass" with the
`build-and-test` check selected.

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
src\PlcScope.App\bin\Release\net10.0-windows\win-x64\publish\PlcScope.exe
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
- Keep a closed investigation note under [improvements/close](improvements/close/) only when it explains a design decision the code does not; otherwise record the outcome in the changelog and delete it.
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
