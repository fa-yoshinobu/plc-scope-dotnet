# 開発・ビルド手順

## 必要環境

- Windows
- .NET 9 SDK

WPF アプリのため、macOS / Linux では `PlcScope.App` をビルドできません。

## ソリューション構成

- `src/PlcScope.App`
  WPF アプリ本体
- `src/PlcScope.Core`
  モデル、表示変換、アドレス範囲、ブロックデータ構築
- `src/PlcScope.Infrastructure`
  PLC 通信アダプタ、JSON 保存、ログ保存
- `tests/PlcScope.Core.Tests`
  Core / Infrastructure の単体テスト

## 使用ライブラリ

- `PlcComm.Slmp` `0.1.11`
- `PlcComm.KvHostLink` `0.1.3`
- `PlcComm.Toyopuc` `0.1.3`
- `CommunityToolkit.Mvvm` `8.4.0`

パッケージの集中管理は [Directory.Packages.props](../Directory.Packages.props) で行っています。

## ビルド

```powershell
dotnet restore .\src\PlcScope.App\PlcScope.App.csproj
dotnet build .\src\PlcScope.App\PlcScope.App.csproj -c Release
```

ソリューション全体を Visual Studio で開く場合は [PlcScopeDotNet.sln](../PlcScopeDotNet.sln) を使用します。

## 単一 EXE 発行

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

## テスト

```powershell
dotnet test .\tests\PlcScope.Core.Tests\PlcScope.Core.Tests.csproj
```

## 現在の制約

- `TOYOPUC` は現状 CPU RUN / STOP 未対応で、CPU 状態表示のみです。
- 実 PLC との最終動作確認は Windows 環境で実施してください。
