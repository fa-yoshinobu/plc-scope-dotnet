# plc-scope-dotnet

[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4)](https://dotnet.microsoft.com/download/dotnet/9.0)

PLC Scope is a Windows desktop PLC I/O monitor built with .NET WPF.

## Version

- `0.1.0`

## License

MIT License. See [LICENSE](LICENSE).

## Overview

Windows 向けの PLC I/O モニターです。  
`.NET 9` / `WPF` で実装しており、PLC のデバイス値を周期読込みしながら、画面上で確認・書込みできます。

## Supported Protocols

- Mitsubishi MELSEC `SLMP`
- KEYENCE KV `Host Link`
- JTEKT TOYOPUC `Computer Link`

## Main Features

- 日本語 UI
- ライト / ダークテーマ切替
- 文字サイズ切替
- JSON 形式のプロジェクト保存 / 読込み
- CPU 状態表示
- CPU RUN / STOP 操作
- デバイス範囲表示
- 通信ログ / エラー履歴表示、コピー、履歴削除
- 画面に見えている行だけを周期読込み
- 先頭アドレスの大文字正規化と範囲内移動
- デバイス範囲内に制限したスクロール
- `LTN` / `LSTN` / `LCN` の 32 bit 現在値表示

## Documentation

細かい仕様は [docs/specification.md](docs/specification.md) を参照してください。

残件は [TODO.md](TODO.md) に記録しています。

## Project Layout

- `src/PlcScope.App`
  WPF アプリ本体
- `src/PlcScope.Core`
  モデル、表示変換、アドレス範囲、ブロックデータ構築
- `src/PlcScope.Infrastructure`
  PLC 通信アダプタ、JSON 保存、ログ保存
- `tests/PlcScope.Core.Tests`
  Core の単体テスト

## Libraries

- `PlcComm.Slmp` `0.1.5`
- `PlcComm.KvHostLink` `0.1.3`
- `PlcComm.Toyopuc` `0.1.3`
- `CommunityToolkit.Mvvm` `8.4.0`

パッケージの集中管理は [Directory.Packages.props](Directory.Packages.props) で行っています。

## Requirements

- Windows
- .NET 9 SDK

WPF アプリのため、macOS / Linux では `PlcScope.App` をビルドできません。

## Build

```powershell
dotnet restore .\src\PlcScope.App\PlcScope.App.csproj
dotnet build .\src\PlcScope.App\PlcScope.App.csproj -c Release
```

ソリューション全体を Visual Studio で開く場合は [PlcScopeDotNet.sln](PlcScopeDotNet.sln) を使用します。

## Publish Single EXE

`build.bat` で `win-x64` の自己完結型 single-file EXE を作成できます。

```cmd
build.bat
```

または構成を指定します。

```cmd
build.bat Release
```

出力先:

```text
src\PlcScope.App\bin\Release\net9.0-windows\win-x64\publish\PlcScope.App.exe
```

ビルドログ:

```text
build.log
```

手動で発行する場合は、先に Runtime Identifier 付きで restore してください。これを行わないと `NETSDK1047` が出る場合があります。

```powershell
dotnet restore .\src\PlcScope.App\PlcScope.App.csproj -r win-x64
dotnet publish .\src\PlcScope.App\PlcScope.App.csproj -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false
```

## Test

```powershell
dotnet test .\tests\PlcScope.Core.Tests\PlcScope.Core.Tests.csproj
```

## Current Limitations

- `TOYOPUC` は現状 CPU RUN / STOP 未対応で、CPU 状態表示のみです。
- 実 PLC との最終動作確認は Windows 環境で実施してください。
