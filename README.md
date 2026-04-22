# plc-scope-dotnet

Windows 向けの PLC I/O モニターです。  
`.NET 9` / `WPF` で実装しており、以下の 3 プロトコルに対応します。

- Mitsubishi MELSEC `SLMP`
- KEYENCE KV `Host Link`
- JTEKT TOYOPUC `Computer Link`

UI は日本語で、接続、読込み、書込み、CPU 状態表示、プロジェクト保存、通信ログ表示を 1 画面中心で扱います。

## 使用ライブラリ

- `PlcComm.Slmp` `0.1.5`
- `PlcComm.KvHostLink` `0.1.3`
- `PlcComm.Toyopuc` `0.1.3`
- `CommunityToolkit.Mvvm` `8.4.0`

パッケージの集中管理は [Directory.Packages.props](Directory.Packages.props) で行っています。

## ソリューション構成

- `src/PlcScope.App`
  WPF アプリ本体
- `src/PlcScope.Core`
  モデル、契約、表示変換、アドレス入力補助
- `src/PlcScope.Infrastructure`
  各 PLC 通信アダプタ、JSON 保存、ログ保存
- `tests/PlcScope.Core.Tests`
  Core / Infrastructure の単体テスト

## 主な機能

- プロトコル別接続設定
- デバイス種別候補つきアドレス入力
- Word / DWord / Float / Bit 展開表示
- 手動読込み / 自動更新
- 書込みパネルとビットトグル
- 書込みロックと確認ダイアログ
- CPU 状態表示
- `SLMP` の CPU RUN / STOP
- `Host Link` の RUN / PROGRAM 切替
- `TOYOPUC` の CPU 状態読取り
- JSON 形式のプロジェクト保存 / 読込み
- 通信ログ / エラー履歴表示

## ビルド

WPF アプリのビルドは Windows 上で実行してください。

```powershell
dotnet build .\src\PlcScope.App\PlcScope.App.csproj -c Release
```

ソリューション全体を Visual Studio で開く場合は [PlcScopeDotNet.sln](PlcScopeDotNet.sln) を使用します。

## 発行

`win-x64` の framework-dependent publish profile を同梱しています。

```powershell
dotnet publish .\src\PlcScope.App\PlcScope.App.csproj -c Release -r win-x64 --self-contained false
```

出力先:

```text
src\PlcScope.App\bin\Release\net9.0-windows\publish\win-x64\
```

Publish profile は [src/PlcScope.App/Properties/PublishProfiles/FolderProfile.pubxml](src/PlcScope.App/Properties/PublishProfiles/FolderProfile.pubxml) です。

## テスト

Core / Infrastructure の検証:

```powershell
dotnet build .\src\PlcScope.Core\PlcScope.Core.csproj
dotnet build .\src\PlcScope.Infrastructure\PlcScope.Infrastructure.csproj
dotnet test .\tests\PlcScope.Core.Tests\PlcScope.Core.Tests.csproj
```

## 現在の制約

- `PlcScope.App` は `Microsoft.NET.Sdk.WindowsDesktop` を使うため、macOS ではビルドできません
- `TOYOPUC` は現状 CPU RUN / STOP 未対応で、CPU 状態表示のみです
- 実 PLC との最終動作確認は Windows 環境で実施してください

## 参考資料

- [ui-spec.md](ui-spec.md)
- [ui-mockup.html](ui-mockup.html)
