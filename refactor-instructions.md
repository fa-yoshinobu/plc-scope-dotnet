# refactor-instructions.md

plc-scope-dotnet のリファクタリング指示書。
この文書は実装担当モデル向けの完結した作業指示である。実装前にこの文書全体と
**ルートの `AGENTS.md`** を読むこと(本書は AGENTS.md の規則を引き継ぐ)。

> **最重要の前提**: これは GitHub Releases で配布される Windows デスクトップアプリ
> (WPF / .NET 9)であり、実機 PLC(KEYENCE KV / MELSEC / TOYOPUC)での手動検証記録
> (`TODO.md`)に紐づく。レイヤ構造(Core / Infrastructure / App)は既に健全で、
> 負債は **`MainWindowViewModel.cs`(2,596 行)への責務集中**にほぼ集約される。
>
> AGENTS.md の規則(UI のオプション/メニュー/ボタン/可視フィールドを勝手に消さない、
> 「PLC が拒否する」は UI 仕様変更の理由にならない、既存プロジェクトファイル内の
> デバイス値は安全に扱う)は本タスクでも**そのまま有効**である。

---

## Objective

UI の見た目・操作・バインディング・保存ファイル互換を一切壊さずに:

1. **`MainWindowViewModel` から UI に依存しないロジックを Core サービスへ抽出する**
   (テスト可能化が目的。VM は薄い委譲ファサードとして残す)
2. 抽出した各ロジックに**ユニットテストを追加**する
3. 大きなサブ ViewModel 分割(バインディングパス変更を伴うもの)は**提案に留める**

---

## Project Understanding

### 何のアプリか

PLC のデバイス値をライブ監視・書込する Windows ツール。プロトコルは
SLMP(MELSEC)/ Host Link(KEYENCE KV)/ Computer Link(TOYOPUC)。
監視タブ(可視行のみ読む省負荷設計)、ウォッチリスト(D&D 並べ替え・CSV 入出力)、
コメント CSV 取込、インライン編集(編集中はリフレッシュ一時停止)、
プロジェクト JSON 保存、ライト/ダークテーマ、エラー履歴・トレースログ。

### レイヤ構成

| プロジェクト | 内容 | 健全度 |
|---|---|---|
| `PlcScope.Core` | モデル、`IPlcSession` 等の抽象、純粋サービス(`MonitorRangePlanner` / `BlockDataBuilder` / `NumericFormatter` / `WatchListCsvSerializer` / `CommentCsvImporter` / `DeviceAddressRangeProvider` / `ProtocolCatalog`) | 健全・テスト充実 |
| `PlcScope.Infrastructure` | プロトコルセッション(`SlmpSession` 647 行 / `HostLinkSession` 527 行 / `ToyopucSession` 299 行、`lib/` の自家製ライブラリ DLL を参照)、JSON ストア、ファイルログ | 健全・テストあり |
| `PlcScope.App` | WPF。**`MainWindowViewModel.cs` 2,596 行**、`MainWindow.xaml` 717 行 + code-behind 629 行 | ここが負債 |

### 依存ライブラリ

`lib/` 配下の自社 PLC 通信ライブラリ(PlcComm 一族のリリース DLL)。変更禁止。

### テスト(既存の安全網)

- `tests/PlcScope.Core.Tests`(約 13 ファイル): 純粋サービスとセッションの単体テスト
- `tests/PlcScope.App.UiTests`: UIA ベースの UI スモーク(起動、監視/ウォッチ面、
  スクロール、インライン編集の pause/resume)。**Windows デスクトップセッション必須**
- 実行: `dotnet test PlcScopeDotNet.sln -m:1`

### 検証コマンド

`dotnet build PlcScopeDotNet.sln` / `dotnet test PlcScopeDotNet.sln -m:1` /
`build.bat`(単一 EXE publish)。

---

## Behaviors To Preserve(絶対に壊さない既存挙動)

1. **UI 仕様全体**(AGENTS.md): オプション・メニュー・ボタン・列・デバイス選択肢の
   追加/削除/並べ替えをしない。
2. **XAML バインディングパス**: `MainWindow.xaml`(717 行)・`App.xaml` が参照する
   ViewModel のプロパティ名・コマンド名を 1 つも変えない(VM をファサードとして残す理由)。
3. **保存ファイル互換**: プロジェクト JSON・設定 JSON・ウォッチリスト CSV・
   コメント CSV の読み書き形式。既存ファイルに入っている古いデバイス値も安全に扱う
   (AGENTS.md)。
4. **省負荷設計**: 可視行のみ読む(`UpdateVisibleRowRange` / `UpdateVisibleWatchRange`)、
   インライン編集中のリフレッシュ一時停止、スクロール中の読取抑制
   (`NotifyScrollActivity`)。実機検証済みの挙動(`TODO.md`)。
5. **プロトコルセッションの読み書きセマンティクス**(`SlmpSession` / `HostLinkSession` /
   `ToyopucSession`)。本タスクでは触らない。
6. **書込前の範囲クランプ**、CPU 制御(RUN/STOP/PAUSE)のガード条件
   (`CanIssueCpuControl` 等)。

---

## Non-Negotiables(交渉不可の制約)

- 最初に `git status` を確認する。未コミット変更があれば混ぜず、報告して停止する。
- 編集前に Baseline Commands をすべて実行し、結果(テスト件数含む)を記録する。
- 変更は小さく戻しやすい単位(1 責務 = 1 抽出)。コミットはユーザーの指示があるまで行わない。
- 無関係な整形・「ついで」リファクタリングをしない。
- NuGet 依存を追加しない(`Directory.Packages.props` を変更しない)。`lib/` の DLL を触らない。
- 抽出は **move-only + 委譲**: ロジックの分岐・順序・例外処理を変えない。
- XAML ファイルを変更しない(リソース・テンプレート・バインディングすべて)。
- UI テスト(UiTests)が実行できない環境では、その旨を報告書に明記し、
  Core.Tests のみで判断しない(人間の手動確認に委ねる項目を列挙する)。
- 正しさが不明な場合は実装を止め、「Stop And Ask」として質問を報告書に書く。

---

## Stop And Ask Conditions(即時停止して質問する条件)

- 抽出しようとしたロジックが `Dispatcher` / UI スレッド / WPF 型(`ObservableCollection`
  の変更通知順序を含む)に依存しており、純粋化すると挙動が変わりうる
- バインディングパス・XAML の変更無しには分離できない構造だと判明した
- プロジェクト JSON / 設定 JSON のシリアライズ結果が 1 バイトでも変わる可能性がある
- 既存テスト(Core.Tests / UiTests)が自分の変更後に落ちた ⇒ 即座に巻き戻して報告
- AGENTS.md の規則と本書の指示が矛盾して見えた(AGENTS.md を優先しつつ質問)
- 本書の Debt Map に無い大きな問題を発見した(報告のみ)

---

## Baseline Commands

作業ディレクトリ: リポジトリルート。**Windows 必須**(WPF / net9.0-windows)。
実機 PLC 不要・接続禁止。

```powershell
git status                          # クリーンであることを確認
dotnet build PlcScopeDotNet.sln
dotnet test PlcScopeDotNet.sln -m:1 # テスト件数を記録。UiTests はデスクトップセッション必須
```

UiTests が環境的に実行不能な場合は
`dotnet test tests\PlcScope.Core.Tests\PlcScope.Core.Tests.csproj` を baseline とし、
UiTests 未実施を報告書に明記する。

---

## Debt Map

行番号は調査時点(main, commit `0c0ceb7`)のアンカー。ドリフトしていたら宣言名で探すこと。

### D1. `MainWindowViewModel.cs`(2,596 行)の god ViewModel 【一部実装可】

- **根拠**: 接続ライフサイクル(`ConnectAsync` 566 行〜)、監視リフレッシュ
  (`ReadOnceAsync` 630 行〜)、ウォッチリスト管理(718〜877 行)、インライン編集
  (`BeginInlineEdit` / `CommitInlineEditAsync` 499〜564 行)、プロジェクト永続化
  (310〜398 行)、CSV 入出力(400〜429 行)、トレース/エラーログ(430〜440 行)、
  CPU 制御、可視範囲管理が 1 クラスに同居。
- **なぜ負債か**: 単体テストは VM 経由の統合的なものに限られ(`MainWindowViewModelWatchTests`
  等)、ロジック単位の検証ができない。変更影響の見積りも困難。
- **影響範囲**: App 層全体。XAML バインディングが全プロパティを参照。
- **変更リスク**: 高(UI 挙動)。だから **VM ファサード維持 + ロジックのみ Core へ** に限定する。
- **改善案**(実装可の範囲):
  1. **UI 非依存ロジックの Core サービス化(move-only)**。候補(依存を確認しながら):
     - インライン編集の状態遷移判定(pause/resume の条件)→ 純粋な状態判定クラス
     - 書込値の解析・クランプロジック(`WritePanelAsync` / `CommitInlineEditAsync` 内)
     - ウォッチリストの型候補更新規則(`UpdateWatchAvailableDataTypes`)
     - プロジェクト適用ロジック(`ApplyProjectAsync` のうちモデル変換部分)
  2. 各抽出先に Core.Tests のユニットテストを追加
- **サブ ViewModel への分割(MonitorViewModel / WatchViewModel 等)は XAML 変更を伴うため
  提案のみ**(Out-of-scope)。
- **検証**: 全テスト + (可能なら)UiTests。`git diff` で XAML 無変更を確認。

### D2. `MainWindow.xaml.cs`(629 行)の code-behind 【提案のみ】

- **根拠**: スクロール連携・D&D・ウィンドウイベントが code-behind に集中。
- **なぜ負債か(軽度)**: UI 操作の実装としては正当な置き場であり、UiTests が挙動を
  押さえている。無理に Behavior 化する利益が薄い。
- **改善案**: 現状維持。分離案は報告のみ。

### D3. セッション 3 実装の構造類似 【現状維持 / 報告のみ】

- `SlmpSession` / `HostLinkSession` / `ToyopucSession` は `PlcSessionBase` を共有しつつ
  プロトコル差分を各自実装。類似はあるが、プロトコル固有の検証記録に紐づくため触らない。

---

## Implementation Phases

### Phase 0: 現状確認

1. `git status` 確認(クリーンでなければ停止・報告)
2. Baseline Commands を実行し、結果を記録(UiTests 実行可否も記録)

### Phase 1: 抽出対象の依存調査(変更なし)

1. D1 の候補 4 つについて、UI 型・Dispatcher・イベント順序への依存を読み取り、
   「純粋化可能 / 不可能」を判定する(不可能なものはスキップして報告)
2. 判定結果を報告書に記録してから Phase 2 へ

### Phase 2: 1 責務ずつの抽出(D1)

1. 候補 1 つを Core へ move-only 抽出 → VM は委譲呼び出しに置換
2. 抽出先のユニットテストを追加(現挙動の特性テスト。期待値は現在の実装出力)
3. `dotnet build` + `dotnet test PlcScopeDotNet.sln -m:1` → 通ったら次の候補へ
4. 1 つでも UiTests が落ちたら即巻き戻し

### Phase 3: 検証と報告

1. 全 Verification Requirements を最終実行
2. サブ VM 分割の提案(やる場合の手順とリスク)を報告書に書く(実装しない)

---

## Verification Requirements

各フェーズ完了時に最低限:

```powershell
dotnet build PlcScopeDotNet.sln
dotnet test PlcScopeDotNet.sln -m:1
```

最終フェーズでは追加で:

- テスト件数が baseline から増えていること(Phase 2 の追加分)
- `git diff` で確認: XAML 全ファイル無変更、`lib/` 無変更、
  `Directory.Packages.props` 無変更、VM の公開プロパティ/コマンド名 無変更
- `build.bat` が成功すること(単一 EXE publish)
- UiTests 未実施の場合: 人間が手動確認すべき項目(接続→監視→インライン編集→
  ウォッチ操作→プロジェクト保存/読込)を報告書に列挙

---

## Reporting Format

作業完了時(または中断時)に以下を Markdown で報告する:

1. **Baseline 結果**: 実行コマンドと結果(テスト件数、UiTests 実行可否)
2. **Phase 1 判定表**: 候補 × 純粋化可否 × 理由
3. **抽出一覧**: 移動したロジック、移動先クラス、追加したテスト
4. **各フェーズの検証結果**: 最後に実行したコマンドと結果(失敗を隠さない)
5. **サブ VM 分割の提案**(実装はしない)
6. **Stop And Ask**: 発生した質問と停止範囲
7. **未実施事項**: UiTests 未実施等と、人間の手動確認チェックリスト

---

## Out-of-scope Items(やらないこと)

- XAML の変更(バインディング・テンプレート・リソース・レイアウト)
- サブ ViewModel への分割(提案のみ)
- UI 仕様の変更全般(AGENTS.md)
- プロトコルセッション(`SlmpSession` / `HostLinkSession` / `ToyopucSession`)と
  `lib/` 配下 DLL の変更
- プロジェクト JSON / 設定 JSON / CSV フォーマットの変更
- NuGet 依存の追加・更新、.NET ターゲット変更
- `MainWindow.xaml.cs` の Behavior 化(提案のみ)
- 実機 PLC を使う検証
- 兄弟リポジトリの変更
