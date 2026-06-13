# SLMP Manual Audit Reflection - 2026-06-13

This repository records the plc-scope effect of the SLMP manual audit and .NET
SLMP library API cleanup.

Source decisions:

- SLMP library profile selection is canonicalized around `SlmpPlcProfile`.
- Public profile text uses canonical `melsec:...` names.
- Frame type and compatibility mode are derived from the selected profile.
- Remote Stop no longer has a force branch in the high-level SLMP API.
- Manual point-limit checks and TCP_NODELAY are handled by the SLMP library.

## plc-scope Reflection

- The bundled `PlcComm.Slmp.dll` was refreshed.
- Connection settings now use `SlmpPlcProfileName`.
- SLMP profile UI values use canonical strings such as `melsec:iq-r` and
  `melsec:qnudv`.
- `SlmpSession` constructs the new `SlmpClient` with `SlmpPlcProfile` and no
  direct frame/compatibility mutation.
- Display formatting maps canonical profile strings back to user-friendly labels.

## Verification

```text
dotnet build
passed

dotnet test tests\PlcScope.App.UiTests\PlcScope.App.UiTests.csproj --filter "FullyQualifiedName~ConnectionDialogViewModelTests"
6 passed
```

Full UI test execution had two UI Automation wait timeouts unrelated to the SLMP
connection profile migration:

- `MainWindowUiTests.MonitorAndWatchScrolling_UpdateUiAutomationState`
- `MainWindowUiTests.MonitorInlineValueFocus_TogglesInlineEditingState`

## Notes

- Compatibility with old saved `SlmpPlcFamilyName` settings is intentionally not
  preserved. The current shape is the canonical `SlmpPlcProfileName` model.
