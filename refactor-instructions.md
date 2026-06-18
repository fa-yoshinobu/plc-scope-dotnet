# refactor-instructions.md(第2サイクル / 2026-06-12)

plc-scope-dotnet のリファクタリング指示書。実装担当モデル向けの完結した作業指示である。
実装前に本書全体と **ルートの `AGENTS.md`** を読むこと(本書は AGENTS.md の規則を引き継ぐ)。

> **前提**: これは第2サイクルである。第1サイクルの指示書は `docs/DEVELOPMENT_HISTORY.md`
> (2026-06-11 Archived Refactor Plan)にアーカイブ済みで、計画 4 候補のうち
> 「ウォッチリストの型候補規則」のみが `Core/Services/WatchDataTypePolicy.cs` として
> 抽出完了している(commit `fb6c4c8`)。本書は残りの負債を、調査済みの証拠付きで
> より小さく安全な単位に分解したものである。
>
> このアプリは GitHub Releases で配布される Windows デスクトップアプリ(WPF / .NET 9)で、
> 実機 PLC(MELSEC SLMP / KEYENCE KV Host Link / TOYOPUC Computer Link)での手動検証記録
> (`TODO.md`)に紐づく。レイヤ構造(Core / Infrastructure / App)は健全で、負債は
> **`MainWindowViewModel.cs`(2,556 行)への責務集中**と少数の微細欠陥に集約される。
>
> AGENTS.md の規則(UI のオプション/メニュー/ボタン/可視フィールドを勝手に消さない、
> 「PLC が拒否する」は UI 仕様変更の理由にならない、既存プロジェクトファイル内の
> デバイス値は安全に扱う)は本タスクでも**そのまま有効**である。

---

## Objective

UI の見た目・操作・XAML バインディング・保存ファイル互換・プロトコル挙動を一切壊さずに:

1. 証拠のある**微細欠陥(重複行・誤インデント)を除去**する
2. `MainWindowViewModel` 内の**明白な重複を統合**する(move-only)
3. UI に依存しない**純粋ロジックを Core へ抽出**し、ユニットテストを追加する
4. **実態と乖離したドキュメントと CI**を現状に合わせる(`docs/development.md` の
   依存関係記述、`release.yml` の未使用 clone ステップ削除 — Q3 承認済み)
5. **承認済みの修正**を指定範囲でのみ実施する: 誤流用エラーメッセージ 2 箇所(Q1)、
   VM 単体テストの専用プロジェクト分割(Q2)
6. **承認済みのパフォーマンス改善**(Q4)を実施する: ウォッチ書込後の再読範囲縮小(D9)、
   リフレッシュ毎の再計算キャッシュ(D10)、エラーログトリムのヒステリシス(D11)、
   行 VM の in-place 更新(D12・特性テスト前提)
7. 通信往復数の一括化(ウォッチ一括読み・ビット一括書込)は**本書の対象外**。
   別指示書 `perf-batch-io-instructions.md` で扱う(実機検証必須のため)
8. それ以外の判断が必要な項目(大きな構造変更、サブ VM 分割等)は
   **実装せず提案・質問に留める**

---

## Project Understanding

### 何のアプリか

PLC のデバイス値をライブ監視・書込する Windows ツール。監視タブ(可視行のみ読む省負荷設計)、
ウォッチリスト(D&D 並べ替え・CSV 入出力・重複禁止)、コメント CSV 取込(複数ファイル可)、
インライン編集(編集中はリフレッシュ一時停止、Enter で書込、範囲クランプ)、
プロジェクト JSON 保存、CPU RUN/STOP(SLMP のみ PAUSE)、SLMP リモートパスワード、
ライト/ダークテーマ、エラー履歴・トレースログ(各 500 件、jsonl)。

### レイヤ構成と健全度

| プロジェクト | 内容 | 状態 |
|---|---|---|
| `src/PlcScope.Core` | モデル、`IPlcSession` 等の抽象、純粋サービス(`MonitorRangePlanner` / `BlockDataBuilder` / `NumericFormatter` / `WatchListCsvSerializer` / `CommentCsvImporter` / `DeviceAddressRangeProvider` / `ProtocolCatalog` / `WatchDataTypePolicy`) | 健全・テスト充実 |
| `src/PlcScope.Infrastructure` | プロトコルセッション(`SlmpSession` 647 行 / `HostLinkSession` 527 行 / `ToyopucSession` 299 行、共通基底 `PlcSessionBase`)、JSON ストア、`FileLogStore` | 健全・テストあり。**プロトコルセッションと JSON ストアは触らない**。`FileLogStore` のみ D11 の範囲で変更可 |
| `src/PlcScope.App` | WPF。**`MainWindowViewModel.cs` 2,556 行**、`MainWindow.xaml.cs` 629 行(UI グルーとして正当)、DI 構成は `App.xaml.cs` | ここが負債 |

### 依存

- `lib/plc-comm/net9.0/` の自家製 PLC 通信 DLL(PlcComm.Slmp / KvHostLink / Toyopuc)を
  `<Reference>` + HintPath で直接参照。**変更禁止**。
- NuGet は `Directory.Packages.props` で集中管理(CommunityToolkit.Mvvm、MS.DI、FlaUI 等)。
- CI は `.github/workflows/release.yml`(タグ push で単一 EXE publish + GitHub Release)。

### テスト(既存の安全網)

- `tests/PlcScope.Core.Tests`(net9.0): 純粋サービスとセッションの単体テスト。**149 件**。
- `tests/PlcScope.App.UiTests`(net9.0-windows): **79 件**。FlaUI による UIA スモーク
  (起動、監視/ウォッチ面、スクロール、インライン編集 pause/resume)**と**、
  WPF 型を要するため同居している VM レベル単体テスト
  (`MainWindowViewModelWatchTests` / `MainWindowViewModelCommentCsvTests` /
  `ConnectionDialogViewModelTests` / `CommentAddressKeyProviderTests` /
  `MonitorRowRefreshComparerTests`)。`InternalsVisibleTo("PlcScope.App.UiTests")` あり。
- **既知のフレーク**: `MainWindowUiTests.MonitorInlineValueFocus_TogglesInlineEditingState` は
  UIA フォーカス依存。デスクトップが他作業中だと落ちることがある(2026-06-12 に確認:
  バックグラウンド実行で失敗、アイドルなフォアグラウンド再実行で成功)。
  落ちたらまず**アイドル状態で単体再実行**してから判断すること。

---

## Behaviors To Preserve(絶対に壊さない既存挙動)

1. **UI 仕様全体**(AGENTS.md): オプション・メニュー・ボタン・列・デバイス選択肢・
   ユーザー可視文字列の追加/削除/変更をしない(Stop And Ask の承認がある場合を除く)。
2. **XAML バインディングパス**: `MainWindow.xaml` / `App.xaml` / 各 Window が参照する
   ViewModel の公開プロパティ名・コマンド名・`UiAutomationStateText` の書式を 1 つも変えない。
   UI テストが `AutomationId` と `UiAutomationStateText` をパースしている。
3. **保存ファイル互換**: プロジェクト JSON(`CommentCsvPath` 単数/`CommentCsvPaths` 複数の
   後方互換を含む)・設定 JSON(`%LOCALAPPDATA%\PlcScope\settings.json`)・
   ウォッチリスト CSV・コメント CSV の読み書き形式。古いファイル内のデバイス値も安全に扱う。
4. **省負荷設計**(実機検証済み・`TODO.md`): 可視行のみ読む
   (`UpdateVisibleRowRange` / `UpdateVisibleWatchRange`)、スクロール中の読取抑制と
   300ms 後再開(`NotifyScrollActivity`)、インライン編集中・編集セルのリフレッシュ抑制
   (`_isInlineEditing` / `ShouldKeepExistingRowDuringRefresh`)。
5. **書込セマンティクス**: `NumericFormatter.ParseByType` による解析と範囲クランプ、
   bit デバイスへの word 値書込(`WriteBitValuesAsync` のビット分解順序)、
   ウォッチの word-bit アドレス(`D0.0`)書込パス。
6. **CPU 制御ガード**: `CanIssueCpuControl` / `CanIssueCpuPauseControl`(PAUSE は SLMP のみ)、
   確認ダイアログ(`RequestCpuCommandConfirmationAsync`)、キャンセル時のエラーメッセージ。
7. **プロトコルセッション**(`SlmpSession` / `HostLinkSession` / `ToyopucSession` /
   `PlcSessionBase`)の読み書き・直列化(`ExecuteSerializedAsync`)・CPU 状態キャッシュ挙動。
   本タスクでは変更しない。
8. **コメント解決の優先順位**: セッション由来コメント > CSV コメント、複数 CSV の
   後勝ちマージ、MELSEC タイマ/カウンタ別名(`TN0`→`T0` 等)の展開順序
   (`CommentAddressKeyProvider`)。D10 のキャッシュ導入後も解決結果は完全同一であること。
9. **ログビューアの表示挙動**: エラー履歴・トレースログは最新 500 件表示
   (`LoadRecentTraceAsync` / `LoadRecentErrorsAsync` の maxCount=500)。
   D11 はファイル内部のトリム発動タイミングのみ変更し、ビューアの表示件数・内容・順序は
   変えない。

---

## Non-Negotiables(交渉不可の制約)

- 最初に `git status` を確認する。未コミット変更があれば混ぜず、報告して停止する。
- 編集前に Baseline Commands をすべて実行し、結果(テスト件数含む)を記録する。
- 変更は小さく戻しやすい単位(1 フェーズ = 独立に revert 可能)。
  コミットはユーザーの指示があるまで行わない。
- 無関係な整形・「ついで」リファクタリングをしない(本書に列挙された欠陥行の修正を除く)。
- NuGet 依存を追加しない(`Directory.Packages.props` のバージョン変更も禁止。
  D2c の新テストプロジェクトは既存の集中管理バージョンのみ参照する)。
  `lib/` 配下を変更しない。`.github/workflows/` の変更は D7 で指定した
  1 ステップの削除のみ。
- 抽出は **move-only + 委譲**: ロジックの分岐・順序・例外型・例外メッセージを変えない。
- 既存挙動を変えてよいのは本書で承認済みと明記した箇所のみ:
  D1b の文言 2 箇所、D9 の書込後再読範囲、D11 のトリム発動タイミング、
  D12 の行更新方式(表示結果は同一)。
- XAML ファイルを一切変更しない(リソース・テンプレート・バインディング・AutomationId)。
- ユーザー可視文字列(エラーメッセージ、ステータス、ツールチップ)を変更しない。
  唯一の例外は D1b で指定した 2 箇所・指定文言。それ以外は誤りに見えても
  現状の文字列を維持したまま移動し、報告書で提案する。
- UI テストが実行できない環境では、その旨を報告書に明記し、Core.Tests のみで完了判断しない
  (人間の手動確認に委ねる項目を列挙する)。
- 正しさが不明な場合は実装を止め、「Stop And Ask」として質問を報告書に書く。

---

## Stop And Ask Conditions(即時停止して質問する条件)

- 抽出対象が `Dispatcher` / `DispatcherTimer` / WPF 型 / `ObservableCollection` の
  変更通知順序に依存しており、純粋化すると挙動が変わりうると判明した
- XAML 変更・公開プロパティ名変更なしには分離できない構造だと判明した
- プロジェクト JSON / 設定 JSON / CSV のシリアライズ結果が変わる可能性が生じた
- 既存テストが自分の変更後に落ちた ⇒ まずフレーク既知の
  `MonitorInlineValueFocus_TogglesInlineEditingState` ならアイドル状態で単体再実行。
  それでも落ちる、または他のテストなら**即座に巻き戻して報告**
- 承認済みの範囲(D1b の 2 箇所、D2c の分割条件、D7 の 1 ステップ、
  D9〜D12 の指定範囲)を超える変更が必要になった
- D12 で、行 VM の mutable 化が XAML バインディングパス・インライン編集保持・
  `MonitorRowRefreshComparer` の判定セマンティクスのいずれかを壊さずには
  実現できないと判明した(⇒ D12 を中止して報告)
- 本書の Debt Map に無い大きな問題を発見した(報告のみ。勝手に直さない)

### Open Questions(全件回答済み・承認内容の記録)

- **Q1: 誤流用エラーメッセージの修正可否。 → 承認済み(2026-06-12)。**
  Debt Map の **D1b** として実装可。本書で指定した 2 箇所・指定文言のみ変更してよい。
  それ以外のユーザー可視文字列は引き続き変更禁止。
- **Q2: VM 単体テストの専用プロジェクト分割。 → 承認済み(2026-06-12)。**
  Debt Map の **D2c** として実装可。`tests/PlcScope.App.Tests` を新設し、
  FlaUI 非依存の VM 単体テストを移す。詳細条件は D2c を参照。
- **Q3: `release.yml` の死んだ CI ステップ削除。 → 承認済み(2026-06-12)。**
  Debt Map の **D7** として実装可。削除してよいのは
  「Checkout protocol dependencies」ステップ(兄弟リポジトリ 3 つの clone)**のみ**。
  `release.yml` の他のステップ・トリガー・権限は変更しない。
  `docs/development.md` の依存関係記述の修正も D7 に含む。
- **Q4: パフォーマンス改善の採否。 → 承認済み(2026-06-12)。**
  調査で挙がった 7 候補のうち、ウォッチ書込後の再読範囲縮小(D9)、
  再計算キャッシュ(D10)、ログトリムのヒステリシス(D11)、
  行 VM の in-place 更新(D12)を本書で実施する。
  通信往復の一括化 2 件(ウォッチ一括読み・ビット一括書込)は実機検証が必須のため
  本書から分離し、別指示書 `perf-batch-io-instructions.md` で扱う。

承認済み 4 件以外の新たな判断事項が生じた場合は、従来どおり停止して質問すること。

---

## Baseline Commands

作業ディレクトリ: リポジトリルート。**Windows 必須**(WPF / net9.0-windows)。
実機 PLC 不要・接続禁止。UI テストは対話的デスクトップセッション必須。

```powershell
git status                          # クリーンであることを確認
dotnet build .\PlcScopeDotNet.sln   # 期待: 0 警告 0 エラー
dotnet test .\PlcScopeDotNet.sln -m:1   # -m:1 必須(UI テスト直列化)
```

2026-06-12 時点の基準値: Core.Tests **149 件成功**、App.UiTests **79 件成功**
(うち `MonitorInlineValueFocus_TogglesInlineEditingState` はフォーカス依存フレークあり)。

UiTests が環境的に実行不能な場合は
`dotnet test .\tests\PlcScope.Core.Tests\PlcScope.Core.Tests.csproj` を baseline とし、
UiTests 未実施を報告書に明記する。

---

## Debt Map

行番号は調査時点(main, commit `3d4eeb9`)のアンカー。ドリフトしていたら宣言名で探すこと。

### D1. 証拠のある微細欠陥 【実装可・最優先】

- **根拠**(すべて `src/PlcScope.App/ViewModels/MainWindowViewModel.cs`):
  - 393–394 行: `ErrorText = string.Empty;` が 2 行連続(`ImportCommentCsvAsync` 内)。
    片方は死コード。
  - 620–622 行: `DisconnectAsync` 内 `if (_session is null)` ブロックの
    `StatusText = "Disconnected";` のインデントが崩れている(挙動は正しい)。
  - 2053–2061 行: `ToggleDWordBitAsync` 内 `ErrorText` 代入 2 箇所のインデントが崩れている。
- **改善案**: 重複行 1 行の削除と、当該行のみのインデント修正。文字列・分岐は変えない
  (誤メッセージ文言は D1b で別途修正)。
- **検証**: ビルド + 全テスト。`git diff` が上記行のみであること。

### D1b. 誤流用エラーメッセージの修正 【実装可・Q1 承認済み】

- **根拠**: 同名メッセージが正誤混在で 5 箇所にある(調査時点の grep 結果):
  - `MainWindowViewModel.cs:445` `LoadDeviceRangeCatalogAsync` —
    「Connect to the PLC before opening device ranges.」**正しい文脈。変更しない**
  - `MainWindowViewModel.cs:898` `ReadWatchItemAsync` — 同上の文言だが、ウォッチ読取の
    文脈に合わないコピペ。**修正対象**
  - `MainWindowViewModel.cs:1375` `WriteBitValuesAsync` —
    「The bit write target address could not be parsed.」解析失敗の文脈で**正しい。変更しない**
  - `MainWindowViewModel.cs:2054` `ToggleDWordBitAsync` の `IsSlmpDWordOnlyFamily()` 分岐 —
    解析失敗ではなく「このデバイスはビット書込非対応」の文脈。**修正対象**
  - `MainWindowViewModel.cs:2060` `ToggleDWordBitAsync` の解析失敗分岐 — **正しい。変更しない**
- **指定文言**(この 2 箇所のみ、この文言どおりに変更する。独自の文言を発明しない):
  - 898 行: `"Connect to the PLC before reading the watch list."`
  - 2054 行: `"Bit writes are not supported for this device."`
- **影響範囲**: テストはどちらの文字列も固定していない(2026-06-12 に grep で確認済み)。
  例外型(`InvalidOperationException`)と分岐は変えない。
- **検証**: ビルド + 全テスト。`git diff` で変更が指定 2 行のみであること。

### D2. `MainWindowViewModel` 内の重複ロジック 【実装可】

- **D2a. ウォッチ読取エラー処理の重複**: `ReadWatchListAsync`(842–866 行)のループ本体と
  `RefreshWatchItemAsync`(869–893 行)の try/catch が完全に同一。
  → private ヘルパー `RefreshSingleWatchItemAsync(item)` に統合(VM 内 move-only)。
- **D2b. テストダブルの重複**: `InMemorySettingsStore` / `NullLogStore` が
  `MainWindowViewModelWatchTests.cs` と `MainWindowViewModelCommentCsvTests.cs` の
  両方に private 定義されている。→ 共有ファイル(例 `TestDoubles.cs`)へ
  internal として統合(置き場所は D2c の新プロジェクト)。
- **D2c. VM 単体テストの専用プロジェクト分割【Q2 承認済み】**:
  FlaUI デスクトップテストと高速な VM 単体テストが `PlcScope.App.UiTests` に同居している。
  `tests/PlcScope.App.Tests` を新設して分離する。条件:
  - 新プロジェクトは `net9.0-windows`、参照は既存の集中管理バージョンのみ
    (xunit / xunit.runner.visualstudio / Microsoft.NET.Test.Sdk / coverlet.collector と
    `PlcScope.App` への ProjectReference)。**FlaUI を参照しない**。
  - 移動対象 = FlaUI(`FlaUI.*` の using)に依存しないテスト:
    `MainWindowViewModelWatchTests` / `MainWindowViewModelCommentCsvTests` /
    `ConnectionDialogViewModelTests` / `CommentAddressKeyProviderTests` /
    `MonitorRowRefreshComparerTests`(+ D2b の共有テストダブル)。
    `AppThemeResourceTests` は FlaUI / WPF Application 依存の有無を確認して判断し、
    判断根拠を報告書に書く。FlaUI 依存テスト(`MainWindowUiTests` /
    `MonitorLayoutTests`)は UiTests に残す。
  - `src/PlcScope.App/Properties/AssemblyInfo.cs` に
    `[assembly: InternalsVisibleTo("PlcScope.App.Tests")]` を追加(既存の UiTests 向けは残す)。
  - ソリューションに新プロジェクトを追加。テスト名・アサーションは変更しない(move-only)。
  - なお `CommentAddressKeyProviderTests` は Phase 4(D4)で対象クラスごと Core.Tests へ
    移る予定。二度手間を避けるため、**Phase 4 を実施するなら D2c では移動対象から外し、
    Phase 4 で直接 Core.Tests へ移してよい**(実施順の判断を報告書に書く)。
- **検証**: ビルド + `dotnet test .\PlcScopeDotNet.sln -m:1`。
  全体のテスト件数が baseline(149 + 79 = 228)から減っていないこと。挙動変更なし。

### D3. 純粋フォーマッタ群の VM 同居 【実装可】

- **根拠**: 以下はすべて static 純関数で UI 非依存(同ファイル内):
  `FormatInt16` / `FormatInt32`(1228–1236 行)、`FormatInputError(ValueDataType, Exception)`
  (2111 行)、`FormatConnectionError` / `FormatConnectionContext`(2139–2151 行)、
  `FormatReadOperation` / `FormatReadContext`(2153–2163 行)、`FormatCpuStateText`
  (2911 行)、`TranslateCpuCommand`(2928 行)、`FormatSelectedPlcModel` +
  `FormatSlmpPlcFamily` / `FormatHostLinkPlcModel` / `FormatToyopucDeviceProfile`
  (2871–2900 行)、`ToRawWord` / `ToRawDWord`(1248–1262 行)、`PackBits`(1017 行)、
  `CombineWords`(1293 行)、`GetProjectCommentCsvPaths` / `NormalizeCommentCsvPaths`
  (2368–2383 行)。
- **なぜ負債か**: ロジック単位のテストができず、VM の行数と認知負荷を押し上げている。
- **改善案**: Core の新規 static クラスへ move-only 抽出(例:
  `Core/Services/StatusTextFormatter.cs`(接続/CPU/読取コンテキスト文字列)、
  `Core/Services/RawValueConverter.cs`(ToRaw/PackBits/CombineWords/FormatInt16/32))。
  VM 側は委譲または using static。**出力文字列は 1 文字も変えない**。
  各抽出先に Core.Tests の特性テスト(期待値 = 現実装の出力)を追加。
- **変更リスク**: 低(純関数の移動)。ユーザー可視文字列を含むため、テストで文字列を固定する。
- **検証**: ビルド + 全テスト + 追加テスト。

### D4. コメント CSV マージロジックの VM 同居 【実装可・慎重に】

- **根拠**: `LoadCommentCsvFilesAsync` の後勝ちマージ(2353–2366 行)、
  `AddCommentCsvComments` のキー展開マージ(2324 行)、`ApplyCsvComments` の
  セッションコメント優先の合成(2385 行)、`UpdateWatchCommentsFromCsv`(2335 行)。
  キー展開は `App/ViewModels/CommentAddressKeyProvider.cs`(Core 型のみに依存する
  internal static、87 行)。
- **改善案**:
  1. `CommentAddressKeyProvider` を `Core/Services/` へ移動(public 化または
     Core の internal + InternalsVisibleTo 追加はしない → public でよい。
     名前空間変更のみ、XAML 参照なし)。対応するテスト
     `CommentAddressKeyProviderTests` を Core.Tests へ移す。
  2. マージ/合成の純粋部分を Core サービス(例 `CommentCsvMergePolicy`)へ move-only 抽出。
     VM は `_commentCsvComments` 辞書の保持と UI 反映のみ残す。
- **変更リスク**: 中。複数 CSV の優先順位(後勝ち)とセッションコメント優先を
  特性テストで先に固定してから移動すること。
- **検証**: ビルド + 全テスト(`MainWindowViewModelCommentCsvTests` が安全網)+ 追加テスト。

### D5. ウォッチ読取結果の解釈ロジック 【実装可・慎重に】

- **根拠**: `ReadWatchItemAsync`(895–980 行)と `ReadWatchBitDeviceItem`(982–1012 行)は
  「`BlockQuery` の組み立て」「`BlockReadResult` → (ValueText, RawText, ビット列) への解釈」
  という純粋計算と、セッション I/O・`item` への代入が混在している。
- **改善案**: 解釈部分のみ Core へ抽出(例 `WatchValueInterpreter`:
  入力 = `BlockReadResult` + `ValueDataType` + `DisplayRadix` + family、
  出力 = ValueText / RawText / ビット値配列のレコード)。クエリ組み立ても
  純粋なので同様に抽出可(`WatchDataTypePolicy` の隣に置く)。
  `SetWatchBits` 系の `ObservableCollection` 再利用判定は WPF 通知順序に関わるため **VM に残す**。
- **変更リスク**: 中。word-bit パス(`D0.0`)、bit デバイスのパック表示、
  Int32/UInt32/Float32 の組み合わせを特性テストで固定してから移動すること。
- **検証**: ビルド + 全テスト + 追加テスト。

### D6. 行 ViewModel 生成の二重 switch 【実装可・特性テスト前提】

- **根拠**: `CreateRowViewModel`(1963–2018 行)と `CreateReadOnlyRowViewModel`
  (2790–2838 行)は 7 行種 × (編集可 / 読取専用) のほぼ同一 switch(約 90 行の重複)。
  差分は canEdit/canToggle フラグとトグルコールバックの有無のみ。
- **なぜ負債か**: 行種追加時に 2 箇所の同期修正が必要で、片方だけ直すバグの温床。
- **改善案**: 先に**特性テストを書く**(全行種 × 両モードで、生成された VM の
  Address/ValueText/HexText/CanEdit/ビットの CanToggle・コールバック有無を比較固定)。
  テストが現挙動を完全に固定できた場合のみ、`canEdit` パラメータ付きの単一メソッドへ統合。
  固定しきれない差分が見つかったら**統合せず報告**。
- **変更リスク**: 中〜高(監視表示の中核)。テストなしの統合は禁止。
- **検証**: 特性テスト + 全テスト + UiTests(MonitorLayoutTests が表示崩れを検出する)。

### D7. ドキュメントと CI の実態乖離 【実装可・Q3 承認済み】

- **根拠**: `docs/development.md` 34 行目「兄弟リポジトリがあれば project reference /
  なければ NuGet」は実態(無条件で `lib/plc-comm` DLL 参照)と乖離。
  `release.yml` の「Checkout protocol dependencies」ステップ(調査時点 24–29 行、
  兄弟リポジトリ 3 つの clone)は、commit `2c8c3f4` の DLL 直接参照化以降
  どの csproj からも使われていない。
- **改善案**:
  1. development.md の依存記述を現状(`lib/plc-comm` の DLL を直接参照、
     更新手順は `lib/plc-comm/README.md`)に合わせて修正。
  2. `release.yml` から「Checkout protocol dependencies」ステップ**のみ**を削除。
     トリガー・権限・publish/package/release/VirusTotal の各ステップは変更しない。
- **変更リスク**: 低。ただしリリースパイプラインはローカルで実行確認できないため、
  削除前に `git grep` 等で兄弟リポジトリパス(`plc-comm-slmp-dotnet` 等)への参照が
  リポジトリ内に存在しないことを再確認し、結果を報告書に記録する。
- **検証**: YAML 構文の目視確認 + `build.bat Release` 成功(publish コマンド自体は
  CI と同等のものがローカルで通ること)。次回タグ push 時のリリース成否は人間が確認する
  旨を報告書に明記。

### D8. 現状維持(報告のみ)と判断したもの

- **`MainWindow.xaml.cs`(629 行)**: スクロール連携・D&D・フォーカス移動の UI グルー。
  置き場として正当で UiTests が挙動を押さえている。分離は利益薄。
- **セッション 3 実装の構造類似**: プロトコル固有の実機検証記録に紐づくため触らない。
- **`PersistUiSettingsAsync` の握りつぶし catch / `OnTraceReceived` の空 catch**:
  設定保存・トレース失敗で通信を止めない意図的設計(コメントあり)。変更しない。
- **VM の `async void` タイマーハンドラ**: WPF の DispatcherTimer 慣習に沿っており、
  再入ガード(`_refreshInFlight`)も実装済み。変更しない。
- **サブ ViewModel 分割(Monitor/Watch の VM 分離)**: XAML バインディングパス変更を
  伴うため本サイクルも提案のみ。

### D9. ウォッチ書込後の可視全行再読 【実装可・Q4 承認済み・挙動変更】

- **根拠**: `WriteWatchDirectBitAsync`(1160–1174 行)、`WriteWatchBitAsync`
  (1176–1190 行)、`WriteWatchItemAsync`(1192–1226 行)は書込成功後に
  `ReadWatchListAsync()` を呼び、1 点の書込確認のために可視 N 行ぶんの
  PLC 往復(各 1 往復)が直列に発生する。
- **改善案**: 書込した該当アイテムのみ再読する。`SetWatchDirectBit` / `SetWatchWordBit` /
  `SetWatchBits` のトグルコールバック生成時に `item` をキャプチャし、書込後は
  D2a で導入する `RefreshSingleWatchItemAsync(item)` を呼ぶ。
- **承認済みの挙動変更**: 書込直後の「他の可視行の即時更新」が次の自動リフレッシュ周期
  (既定 500ms)での更新に変わる。これ以外(該当行の即時反映、エラー表示、
  編集状態の解除)は不変。
- **検証**: VM テストを追加(`CapturingSession` 系のテストダブルで、書込後の読取が
  該当アイテム 1 件分のクエリのみであることをアサート)。既存ウォッチテスト全パス。

### D10. リフレッシュ毎の再計算(コメント解決・ファミリ解決) 【実装可・Q4 承認済み】

- **根拠**:
  - `ApplyCsvComments`(2385 行)→ `GetCommentAddressKeys`(2409 行)→
    `CommentAddressKeyProvider.GetKeys` が、可視アドレスごとに毎リフレッシュ
    (既定 500ms)デバイスファミリ列挙 + `OrderByDescending` + アドレス解析を再実行する。
  - `ResolveDeviceFamilyForAddress`(1264 行)も呼出しごとに
    `GetDeviceFamilies(...).OrderByDescending(...)` を再構築する(ウォッチ各行 × 毎読み)。
- **改善案**:
  - 「アドレス → 解決済みコメント(無ければ無し)」のキャッシュ辞書を導入。
    **無効化条件を明記して実装する**: `_commentCsvComments` の変更時
    (`SetCommentCsv` / `AddCommentCsvComments` / `NewProject` / クリア)、
    `SelectedProtocol` 変更時、`ConnectionSettings.KeyenceDeviceMode` 変更時。
  - ファミリ解決用に「プロトコル + KeyenceDeviceMode → コード長降順ソート済み配列」を
    キャッシュ。無効化条件は同上。
- **制約**: 解決結果(コメント文字列・選ばれるファミリ)は現実装と完全同一であること。
  キャッシュ導入前に特性テストで現挙動を固定する。挙動変更ではなく純粋な計算削減。
- **検証**: 特性テスト + 既存コメント CSV テスト(`MainWindowViewModelCommentCsvTests`)全パス。

### D11. エラーログトリムの毎回フルリライト 【実装可・Q4 承認済み・挙動変更】

- **根拠**: `FileLogStore.WriteLinesCoreAsync`(`FileLogStore.cs:191–211`)は追記後に
  `recordCount > MaxLogRecords(500)` で `TrimLogFileAsync`(全行読み → 全行書き)を呼ぶ。
  500 件到達後は **append のたびに**この条件が成立し、毎回ファイル全体を読み書きする。
  PLC 切断中に自動リフレッシュが有効だと 500ms ごとにエラー → 毎回フルリライトになる。
- **改善案**: トリム発動閾値と保持件数を分離するヒステリシスを導入。
  例: 件数が **600 を超えたら最新 500 件に切り詰める**(定数 2 つで表現)。
  これによりフルリライトは約 100 append に 1 回になる。
- **承認済みの挙動変更**: ログファイルが一時的に最大 600 件保持する。
  ビューアは `maxCount=500` で読むため**表示挙動は不変**(Behaviors To Preserve 9)。
  `README.md` / `docs/specification.md` の「最新 500 件を保持」の記述が不正確になる場合は
  1 行程度の文言調整を行ってよい(例: 「ビューアは最新 500 件を表示。ファイルは
  超過時に最新 500 件へ切り詰め」)。
- **制約**: 変更してよいのは `FileLogStore` のトリム発動条件と関連定数のみ。
  jsonl 形式・複数行 JSON 許容(`ReadJsonRecords`)・読み取り側 API・
  トレースの 5 秒バッチは変えない。
- **検証**: `FileLogStoreTests` を確認し、トリム挙動のテストを閾値に合わせて追加
  (500 件以下では切り詰めない / 601 件目で 500 件になる / ビューア読取は 500 件のまま)。

### D12. 行 ViewModel のリフレッシュ毎再生成 【実装可・Q4 承認済み・特性テスト前提・最後に実施】

- **根拠**: `ReplaceRows`(1399–1416 行)は値が変化した行を `CreateRowViewModel` で
  丸ごと新規生成して置換する(`MonitorRowCollection.Replace` → WPF の Replace 通知 →
  行テンプレート再生成)。1 行あたり BitCellViewModel 16〜32 個 + クロージャを毎回確保し、
  描画コストと GC churn を生む。無変化行は `MonitorRowRefreshComparer` が置換を
  抑止済み(この仕組みは維持する)。
- **改善案**: 同一行(Address・行種・編集可否・ビット構成が一致)の場合、
  置換ではなく既存 VM の値プロパティ(Value/ValueText/HexText/ビットの IsOn 等)を
  in-place 更新する。`SetWatchBits`(1071–1158 行)が既に同じ再利用パターンを
  実装しており、設計の参考にできる。
- **必須の制約**:
  - **XAML を変更しない**: 行 VM の公開プロパティ名は XAML が参照しているため、
    名前・型を変えない。setter 追加 + `INotifyPropertyChanged` 化のみ許可。
  - **インライン編集保持**(`ShouldKeepExistingRowDuringRefresh`)のセマンティクスを
    維持する: 編集中の行は in-place 更新もしない。
  - `MonitorRowRefreshComparer` は「置換要否」から「更新要否 + 置換要否」判定に
    役割が変わる。既存テスト(`MonitorRowRefreshComparerTests`)を壊さず拡張する。
  - **特性テストを先に書く**: リフレッシュ後の表示値・CanEdit・CanToggle・
    ビットトグルのコールバック先・選択状態維持を、置換方式の現挙動で固定してから着手。
    固定できない差分があれば D12 を中止して報告。
- **承認済みの挙動変更**: 行の更新方式(オブジェクト置換 → プロパティ更新)。
  表示結果・操作結果は同一であること。
- **変更リスク**: 中〜高(監視表示の中核)。他のすべてのフェーズが green に
  なってから単独フェーズで実施し、失敗したら丸ごと巻き戻す。
- **検証**: 特性テスト + 全テスト + UiTests
  (`MonitorLayoutTests` と inline edit pause/resume テストが安全網)。

---

## Implementation Phases

各フェーズ完了ごとに Verification Requirements の基本コマンドを実行し、
通ってから次へ進む。落ちたら当該フェーズを巻き戻して報告。

### Phase 0: 現状確認(変更なし)

1. `git status` 確認(クリーンでなければ停止・報告)
2. Baseline Commands を実行し、結果(件数・UiTests 実行可否・フレーク発生有無)を記録

### Phase 1: 微細欠陥・メッセージ修正・ドキュメント・CI(D1, D1b, D7)

1. 重複 `ErrorText` 行の削除、欠陥 3 箇所のインデント修正(文字列・分岐は不変)
2. D1b の指定 2 箇所を指定文言どおりに修正(それ以外の同名文字列は変更しない)
3. `docs/development.md` の依存関係記述を実態に合わせて修正
4. `release.yml` の「Checkout protocol dependencies」ステップを削除
   (削除前に兄弟リポジトリパスへの参照が無いことを `git grep` で再確認)
5. 検証

### Phase 2: テストプロジェクト分割と重複統合(D2)

1. `tests/PlcScope.App.Tests` を新設し、FlaUI 非依存の VM 単体テストを move-only で移動
   (D2c の条件に従う。`InternalsVisibleTo` 追加、ソリューション登録を含む)
2. テストダブルを新プロジェクト内の共有ファイルへ統合(D2b)
3. `RefreshSingleWatchItemAsync` ヘルパーへの統合(D2a、move-only)
4. 検証(全体件数が 228 件から減っていないことを確認)

以降のフェーズで追加する VM レベルの特性テストは、新設した `PlcScope.App.Tests` に置く。
Core へ抽出したロジックのテストは `PlcScope.Core.Tests` に置く。

### Phase 3: 純粋フォーマッタの Core 抽出(D3)

1. 1 クラスずつ move-only 抽出 → VM は委譲に置換 → 特性テスト追加 → 検証、を繰り返す
2. ユーザー可視文字列はテストで原文固定

### Phase 4: コメント CSV ロジックの Core 抽出(D4)

1. 特性テストで優先順位を固定
2. `CommentAddressKeyProvider` を Core へ移動(テストも Core.Tests へ)
3. マージ/合成の純粋部分を抽出
4. 検証

### Phase 5: ウォッチ読取解釈の Core 抽出(D5)

1. 特性テストで解釈結果を固定
2. クエリ組み立てと結果解釈を move-only 抽出(`SetWatchBits` 系は VM に残す)
3. 検証

### Phase 6: 行 VM 生成の統合(D6・条件付き)

1. 特性テストを先に作成。全行種 × 両モードを固定できなければ**統合せず報告へ**
2. 固定できた場合のみ単一メソッドへ統合
3. 検証(MonitorLayoutTests を含む全テスト)

### Phase 7: 低リスクパフォーマンス改善(D9, D10, D11)

1. D9: ウォッチ書込後の再読を該当アイテムのみに変更 + VM テスト追加
2. D10: 特性テストで現挙動を固定 → コメント解決キャッシュとファミリ解決キャッシュを導入
   (無効化条件を D10 の記載どおりに実装)
3. D11: `FileLogStore` のトリムにヒステリシスを導入 + `FileLogStoreTests` 拡張 +
   必要なら README / specification の文言を 1 行調整
4. 検証

### Phase 8: 行 VM の in-place 更新(D12・条件付き)

1. 特性テストを先に作成。現挙動を固定できなければ**中止して報告へ**
2. 固定できた場合のみ、`MonitorRowRefreshComparer` 拡張 + 行 VM のプロパティ更新化を実施
3. 検証(MonitorLayoutTests・inline edit テストを含む全テスト)。失敗したら丸ごと巻き戻し

### Phase 9: 最終検証と報告

1. Verification Requirements を全実行
2. 承認済み項目(D1b / D2c / D7 / D9〜D12)の実施結果と、提案事項(サブ VM 分割、
   セッション統合の非推奨理由)を報告書に記載

時間や環境の制約でフェーズを完遂できない場合、**完了したフェーズまでで止めて報告**する。
途中状態のフェーズを残さない。

---

## Verification Requirements

各フェーズ完了時に最低限:

```powershell
dotnet build .\PlcScopeDotNet.sln
dotnet test .\PlcScopeDotNet.sln -m:1
```

最終フェーズでは追加で:

- テスト件数が baseline(149 + 79)から増えていること(Phase 3〜8 の追加分)
- `git diff --stat` で確認: XAML 全ファイル無変更、`lib/` 無変更、
  `Directory.Packages.props` 無変更、VM の公開プロパティ/コマンド名 無変更
  (D12 での setter 追加は可、名前変更は不可)、
  `.github/` の変更が D7 の 1 ステップ削除のみ、
  `src/PlcScope.Infrastructure` の変更が `FileLogStore.cs` のみ(D11)、
  ユーザー可視文字列の変更が D1b の指定 2 箇所のみ
- `build.bat Release` が成功すること(単一 EXE publish)
- フレークした UI テストはアイドル状態で単体再実行してから判定
- UiTests 未実施の場合: 人間が手動確認すべき項目(接続 → 監視 → インライン編集 →
  ウォッチ操作 → コメント CSV 取込 → プロジェクト保存/読込)を報告書に列挙

---

## Reporting Format

作業完了時(または中断時)に以下を Markdown で報告する:

1. **Baseline 結果**: 実行コマンドと結果(テスト件数、UiTests 実行可否、フレーク有無)
2. **フェーズ別サマリ**: 実施内容、移動したロジック、追加したテスト、検証結果
3. **最後に実行したコマンドと結果**(失敗を隠さない)
4. **Stop And Ask**: 発生した質問と停止範囲
5. **承認済み項目の実施結果**: D1b の文言修正、D2c の分割(移動したテスト一覧と
   `AppThemeResourceTests` の判断根拠)、D7 の CI ステップ削除と `git grep` 確認結果、
   D9〜D11 の実施内容、D12 の実施可否判断(特性テストで固定できたか)と結果
6. **提案事項**: 実装しなかった改善案(サブ VM 分割等)
7. **未実施事項**: スキップしたフェーズとその理由、人間の手動確認チェックリスト

---

## Out-of-scope Items(やらないこと)

- XAML の変更(バインディング・テンプレート・リソース・レイアウト・AutomationId)
- ユーザー可視文字列の変更(D1b で指定した 2 箇所を除く)
- サブ ViewModel への分割(提案のみ)
- UI 仕様の変更全般(AGENTS.md)
- プロトコルセッション(`SlmpSession` / `HostLinkSession` / `ToyopucSession` /
  `PlcSessionBase`)と `lib/` 配下 DLL の変更
- `FileLogStore` の D11 指定範囲(トリム発動条件と関連定数)以外の Infrastructure 変更
- 通信往復数の一括化(ウォッチ一括読み・ビットデバイスへの一括書込)
  — 別指示書 `perf-batch-io-instructions.md` で扱う。本書では着手しない
- 省負荷設計そのものの変更(スクロール中の読取停止・300ms 再開、可視行のみ読む設計、
  リフレッシュ最小間隔 100ms、CPU 状態の 1 秒スロットル、トレースの 5 秒バッチ)
- プロジェクト JSON / 設定 JSON / CSV フォーマットの変更
- NuGet 依存の追加・更新、.NET ターゲット変更
- `.github/workflows/` の変更(D7 で指定した 1 ステップの削除を除く)
- D2c で指定した以外のテストプロジェクト構成変更
- `MainWindow.xaml.cs` の Behavior 化(提案のみ)
- 実機 PLC を使う検証、兄弟リポジトリの変更

---

## 実施結果(2026-06-19)

### できたこと

- 第2サイクルの refactor は完了済み。コミット `d912c4e Complete refactor cycle 2` として
  `codex/refactor-cycle-2` に反映し、`origin/codex/refactor-cycle-2` へ push 済み。
- D1 / D1b / D7:
  - 重複していた `ErrorText` 設定や不要なインデントを整理。
  - 指定されたユーザー可視文言 2 箇所のみ調整。
  - release workflow の clone step 削除を実施。
- D2 / D2c:
  - `tests/PlcScope.App.Tests` を追加し、ViewModel 系テストを UIA テストから分離。
  - `AppThemeResourceTests` は WPF Application / FlaUI 依存が不要なため App.Tests 側へ移動。
  - `MainWindowUiTests` / `MonitorLayoutTests` は UiTests 側に残した。
- D3 / D4 / D5:
  - `RawValueConverter`、`StatusTextFormatter`、`ProjectCommentCsvPathPolicy`、
    `PlcProfileDisplayFormatter`、`CommentCsvMergePolicy`、
    `WatchReadQueryBuilder`、`WatchValueInterpreter` を Core 側へ分離。
  - `CommentAddressKeyProvider` を Core へ移動し、関連テストを追加。
- D6 / D9 / D10 / D11 / D12:
  - 行 VM 生成ロジックを整理し、factory 系テストを追加。
  - watch 書込後の再読込を対象 item のみへ縮小。
  - コメント解決 cache / sorted family cache を追加し、設定・CSV・protocol 変更時に無効化。
  - `FileLogStore` の trim 発動を 600 件、保持を 500 件へ調整。
  - `MonitorRowRefreshComparer` と行 VM を更新し、条件が合う行は in-place 更新するようにした。

### できていないこと / やらないこととして残したこと

- サブ ViewModel 分割は提案事項のまま。今回の scope では実装していない。
- `MainWindow.xaml.cs` の Behavior 化は提案事項のまま。今回の scope では実装していない。
- 実機 PLC 検証は未実施。自動テストと release publish までで完了。
- 通信往復数の一括化は本指示書の out-of-scope のため未実施。
  後続の `perf-batch-io-instructions.md` 側で扱う。

### 検証結果

- refactor 着手前 baseline:
  - `dotnet build .\PlcScopeDotNet.sln`: 成功、0 warnings / 0 errors。
  - `dotnet test .\PlcScopeDotNet.sln -m:1`: 成功、236 tests。
- refactor 完了後:
  - `dotnet build .\PlcScopeDotNet.sln`: 成功、0 warnings / 0 errors。
  - `dotnet test .\PlcScopeDotNet.sln -m:1`: 成功、274 tests。
    - Core: 200
    - App.UiTests: 33
    - App.Tests: 41
  - `build.bat Release`: 成功。
  - `git diff --check`: 空白エラーなし、CRLF 警告のみ。
- 禁止領域の確認:
  - XAML 変更なし。
  - `lib/` 変更なし。
  - `Directory.Packages.props` 変更なし。
  - `.github/` 変更は D7 の release workflow clone step 削除のみ。
  - Infrastructure 変更は D11 の `FileLogStore.cs` のみ。
