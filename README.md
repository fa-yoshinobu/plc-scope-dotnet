# plc-scope-dotnet

Windows 向けの PLC I/O モニターです。  
`.NET 9` / `WPF` で実装しており、PLC のデバイス値を周期読込みしながら、画面上で直接確認・書込みできます。

## 対応プロトコル

- Mitsubishi MELSEC `SLMP`
- KEYENCE KV `Host Link`
- JTEKT TOYOPUC `Computer Link`

接続先、プロトコル、通信条件は接続設定画面で管理します。トップ画面では監視対象のデバイス、先頭アドレス、表示形式、表示基数、読込み間隔を操作します。

## 主な機能

- 日本語 UI
- ライト / ダークテーマ切替
- 文字サイズ切替
- JSON 形式のプロジェクト保存 / 読込み
- 初期プロジェクト名は `タイトルなし`
- CPU 状態表示
- CPU メニューからの RUN / STOP 操作
- 通信ログ / エラー履歴表示
- 下部ステータスバーへの状態、最終読込み、応答時間、通信回数、CPU 状態、エラー表示

## 監視画面

先頭アドレスを指定すると、それ以降のアドレスが自動で表示されます。点数指定や手動の読込みボタンはなく、画面に見えている行だけを周期読込みします。

負荷軽減のため、スクロール中は通信を一時停止し、スクロール停止後に通信を再開します。自動更新は常に有効で、読込み間隔だけを変更できます。

## 表示形式と書込み

値の書込みは一覧のセルに直接入力して行います。

- `Enter`: 入力値を書込み
- `Esc`: 入力を取り消し
- 入力中は周期読込みを一時停止

Word デバイスでは、表示形式により以下の扱いになります。

- `Word`: 1 ワードを数値表示 / 書込み
- `DWord`: 2 ワードを 32 bit 整数として表示 / 書込み
- `Float32`: 2 ワードを IEEE754 単精度浮動小数点として表示 / 書込み
- `BitExpand`: 1 ワードを `b0` から `b15` まで展開表示

Bit デバイスでは、表示形式により以下の扱いになります。

- `BitExpand`: `M0`, `M1`, `M2` のように個別アドレスとして表示 / 書込み
- `Word`: 16 点を 1 ワードとして表示 / 書込み
- `DWord`: 32 点を 32 bit 整数として表示 / 書込み
- `Float32`: 32 点を IEEE754 単精度浮動小数点として表示 / 書込み

`Float32` 表示で数値として扱えない値は `N/A` と表示します。`-0` は `0` と表示します。浮動小数点の入力は、表示基数に関係なく通常の小数表記で入力します。

## ソリューション構成

- `src/PlcScope.App`
  WPF アプリ本体
- `src/PlcScope.Core`
  モデル、表示変換、アドレス範囲、ブロックデータ構築
- `src/PlcScope.Infrastructure`
  PLC 通信アダプタ、JSON 保存、ログ保存
- `tests/PlcScope.Core.Tests`
  Core の単体テスト

## 使用ライブラリ

- `PlcComm.Slmp` `0.1.5`
- `PlcComm.KvHostLink` `0.1.3`
- `PlcComm.Toyopuc` `0.1.3`
- `CommunityToolkit.Mvvm` `8.4.0`

パッケージの集中管理は [Directory.Packages.props](Directory.Packages.props) で行っています。

## 必要環境

- Windows
- .NET 9 SDK

WPF アプリのため、macOS / Linux では `PlcScope.App` をビルドできません。

## ビルド

```powershell
dotnet restore .\src\PlcScope.App\PlcScope.App.csproj
dotnet build .\src\PlcScope.App\PlcScope.App.csproj -c Release
```

ソリューション全体を Visual Studio で開く場合は [PlcScopeDotNet.sln](PlcScopeDotNet.sln) を使用します。

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

テストでは、主にアドレス範囲、表示形式、数値変換、ブロックデータ構築を検証しています。

## 現在の制約

- `TOYOPUC` は現状 CPU RUN / STOP 未対応で、CPU 状態表示のみです
- 実 PLC との最終動作確認は Windows 環境で実施してください
