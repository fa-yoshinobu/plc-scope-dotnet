# perf-batch-io-instructions.md(通信一括化タスク / 2026-06-12)

plc-scope-dotnet の通信往復数削減タスクの指示書。実装担当モデル向けの完結した作業指示である。
実装前に本書全体と **ルートの `AGENTS.md`** を読むこと。

> **前提**:
> - 本書は `refactor-instructions.md`(第2サイクル)から分離された別タスクである。
>   **第2サイクル完了後に着手すること**(同じ `MainWindowViewModel` を触るため)。
>   着手時点の行番号アンカーはドリフトしている前提で、宣言名で探すこと。
> - 本タスクは**プロトコルセッション層の変更を伴い、実機 PLC での検証が必須**である。
>   実装担当モデルは実機検証ができないため、本タスクは
>   「実装 + 自動テスト + 実機検証チェックリスト作成」までで完結し、
>   **マージ可否の判断は人間の実機確認に委ねる**。
> - AGENTS.md の規則(UI のオプション/選択肢を勝手に消さない、「PLC が拒否する」は
>   UI 仕様変更の理由にならない、扱えない値は理由を表示してクラッシュしない)は
>   本タスクでも**そのまま有効**である。

---

## Objective

UI の見た目・操作・表示結果・エラー表示の単位を変えずに、PLC との往復数を削減する:

1. **ウォッチリスト可視行の一括読み**: 現在は可視 1 行 = 1 往復の逐次読み。
   プロトコルが複数点読みをサポートする場合に 1〜数往復へまとめる。
2. **ビットデバイス行へのワード値書込の一括化**: 現在は 1 ビット = 1 往復で 16/32 回。
   プロトコルがブロック書込をサポートする場合に 1 往復へまとめる。
3. サポートしないプロトコル・デバイスでは**現行の逐次パスをそのまま維持**する
   (機能を消さない・選択肢を減らさない)。

---

## Project Understanding(調査済みの事実)

- **現状のウォッチ読み**: `MainWindowViewModel.ReadWatchListAsync` が可視アイテムを
  1 件ずつ `IPlcSession.ReadBlockAsync(BlockQuery)` で読む(直列 await)。
  各アイテムは型により Word(1点)/ DWord・Float32(2点)/ Bit / word-bit(`D0.0`)の
  クエリになる。アイテムごとに try/catch があり、**無効アドレスや読取失敗は
  その行だけ `HasError` になる**(他の行を巻き込まない)。
- **現状のビット書込**: `MainWindowViewModel.WriteBitValuesAsync` がワード値をビット分解し、
  `IPlcSession.WriteAsync`(Bit)を 16/32 回直列に呼ぶ。
- **ライブラリの素地**: SLMP ライブラリ(`lib/plc-comm/net9.0/PlcComm.Slmp.dll`)は
  複数アドレス一括読み `ReadRandomAsync(wordDevices, dwordDevices, ct)` を公開しており、
  `SlmpSession.ReadRandomDWordValuesAsync`(LZ デバイス用、64 点チャンク)で使用実績がある。
  KV Host Link / TOYOPUC ライブラリの一括 API の有無は**未調査**。
- **セッションの直列化**: `PlcSessionBase.ExecuteSerializedAsync`(SemaphoreSlim)で
  全 I/O が直列化されている。一括化してもこの仕組みは維持する。
- **セッション実装**: `SlmpSession`(647 行)/ `HostLinkSession`(527 行)/
  `ToyopucSession`(299 行)。`IPlcSession` は `Core/Abstractions/IPlcSession.cs`。
- **テスト**: `tests/PlcScope.Core.Tests` にセッションの単体テスト
  (`SlmpSessionTests` / `HostLinkSessionTests` / `ToyopucSessionTests`)がある。
  実機なしで通る範囲のテストである。

---

## Behaviors To Preserve(絶対に壊さない既存挙動)

1. **行単位のエラー表示**: ウォッチで無効アドレス・読取失敗は該当行のみ
   `HasError` + 理由表示。一括化で「1 件の不良が他の行を巻き込んで全行エラー」に
   **なってはならない**。事前バリデーションで不良を一括対象から除外する、
   一括失敗時は逐次読みにフォールバックする、等で現行の単位を守ること。
2. **表示結果の同一性**: 値・Raw 表示・ビットセル・コメント・型正規化
   (`WatchDataTypePolicy`)は一括化前後で完全同一。
3. **書込のセマンティクス**: ビット一括書込でも「対象ビット以外を書き換えない」こと。
   ワードデバイスの word-bit 書込(`WriteBitInWordAsync`)の read-modify-write 挙動が
   ライブラリの一括 API で保証できない場合、その経路は一括化しない。
4. **可視行のみ読む省負荷設計**と編集中・スクロール中の読取抑制。
5. **UI 仕様**(AGENTS.md): プロトコルやデバイスが一括をサポートしないことを理由に
   選択肢・機能を削らない。逐次パスを温存する。
6. **セッション直列化**(`ExecuteSerializedAsync`)とトレース・エラーイベントの発火。
7. プロジェクト JSON / 設定 JSON / CSV の形式。

---

## Non-Negotiables(交渉不可の制約)

- 最初に `git status` を確認する。未コミット変更があれば混ぜず、報告して停止する。
- 編集前に Baseline Commands を実行し、結果を記録する。
- 変更は小さく戻しやすい単位。コミットはユーザーの指示があるまで行わない。
- `lib/` 配下の DLL を変更しない。ライブラリ側に必要な API が無い場合、
  ライブラリ改修は兄弟リポジトリ側の別作業であり、本タスクでは**調査結果の報告のみ**。
- `IPlcSession` の拡張は**既定実装(逐次フォールバック)を持つ形**にし、
  一括 API を実装しないセッションが無変更でも現行どおり動くこと。
- XAML を変更しない。VM の公開プロパティ/コマンド名を変えない。
- NuGet 依存を追加しない。
- 実機 PLC への接続・実機検証を自分では行わない(チェックリスト作成まで)。
- 正しさが不明な場合は実装を止め、「Stop And Ask」として質問を報告書に書く。

---

## Stop And Ask Conditions(即時停止して質問する条件)

- ライブラリの一括 API の**点数上限・デバイス種別の制約・混載可否**が
  ドキュメント/メタデータから判断できない(推測で実装しない)
- 一括化すると行単位エラーのセマンティクス(Behaviors 1)を保てない設計しか
  成立しないと判明した
- ビット一括書込で「対象外ビットを書き換えない」保証ができない(Behaviors 3)
- word-bit(`D0.0`)・bit デバイスのパック読み・DWord 専用ファミリ(LZ 等)の
  いずれかで、一括と逐次の表示結果が一致しないケースが見つかった
- 既存テストが落ちた(即巻き戻して報告)

---

## Baseline Commands

作業ディレクトリ: リポジトリルート。Windows 必須。実機 PLC 不要・接続禁止。

```powershell
git status
dotnet build .\PlcScopeDotNet.sln
dotnet test .\PlcScopeDotNet.sln -m:1
```

第2サイクル完了後の件数を baseline として記録すること。
既知のフレーク(`MonitorInlineValueFocus_TogglesInlineEditingState`、フォーカス依存)は
アイドル状態で単体再実行してから判定する。

---

## Implementation Phases

### Phase 0: 現状確認(変更なし)

1. `git status` 確認、Baseline Commands 実行・記録
2. `refactor-instructions.md` 第2サイクルが完了済みであることを確認
   (`docs/DEVELOPMENT_HISTORY.md` または git log)。未完了なら停止・報告

### Phase 1: ライブラリ API 調査(変更なし・報告必須)

1. 3 つの DLL の公開 API を調査する(リフレクション、`ildasm`/メタデータ、または
   兄弟リポジトリ `../plc-comm-slmp-dotnet` 等が存在すればそのソース):
   - 複数点読み(ランダム読み・複数ブロック読み)の有無、点数上限、
     ワード/DWord/ビットの混載可否
   - ビットのブロック書込・複数点書込の有無と保証
2. プロトコル × 機能のサポート表を作り、報告書に記載してから Phase 2 へ

### Phase 2: `IPlcSession` の拡張設計(逐次フォールバック内蔵)

1. 例: `ReadWatchBatchAsync(IReadOnlyList<BlockQuery>) → IReadOnlyList<Result|Error>` を
   `IPlcSession` に追加し、`PlcSessionBase` に**逐次実行する既定実装**を持たせる
   (結果は要素ごとに成功/失敗を保持し、行単位エラーを維持する)。
2. この時点で VM 側を新 API 経由に切り替えても、全プロトコルの動作は現行と同一
   (既定実装が逐次のため)。ここで一度全テストを通す。

### Phase 3: SLMP の一括読み実装

1. `SlmpSession` で `ReadRandomAsync` を用いた一括読みをオーバーライド実装:
   - ワード/DWord の混載、チャンク分割(既存実装の 64 点チャンクを参考に、
     Phase 1 で確認した上限に従う)
   - 事前バリデーションで解析不能アドレスを除外し、該当行はエラー結果として返す
   - 一括フレームが失敗した場合は逐次読みへフォールバックし、トレースに記録
   - bit デバイス・word-bit の扱いは Phase 1 の調査結果に従う
     (一括化できない種別は逐次のまま)
2. 単体テスト追加(モックで: 混載クエリの組み立て、チャンク分割、不良除外、
   フォールバック、結果の行対応)
3. 全テスト + 検証

### Phase 4: Host Link / TOYOPUC の一括読み(API がある場合のみ)

1. Phase 1 で一括 API が確認できたプロトコルのみ、Phase 3 と同じ手順で実装
2. API が無いプロトコルは既定実装(逐次)のまま。報告書にその旨を記載

### Phase 5: ビット一括書込(保証が確認できた場合のみ)

1. Phase 1 で「対象外ビットを書き換えない」保証が確認できたプロトコルのみ、
   `WriteBitValuesAsync` 経路を一括化(同様に既定実装は逐次)
2. 保証が確認できない場合は実装せず、調査結果と提案を報告書に記載

### Phase 6: 実機検証チェックリスト作成と停止

1. `TODO.md` の「Remaining Manual Validation」に実機検証項目を追記する:
   - 各実装プロトコルで: ウォッチ大量行(>50)のスクロール読み、混載型
     (Word/DWord/Float/Bit/word-bit)の値一致、無効アドレス行のエラー表示が
     他行を巻き込まないこと、一括失敗時のフォールバック、
     ビット一括書込の対象外ビット不変、長時間トレースの安定
2. 「実機確認が完了するまでマージ判断不可」と報告書に明記して停止

---

## Verification Requirements

各フェーズ完了時:

```powershell
dotnet build .\PlcScopeDotNet.sln
dotnet test .\PlcScopeDotNet.sln -m:1
```

最終フェーズでは追加で:

- テスト件数が baseline から増えていること
- `git diff --stat` で確認: XAML 無変更、`lib/` 無変更、`Directory.Packages.props` 無変更、
  `.github/` 無変更、VM 公開プロパティ/コマンド名 無変更
- 既定実装(逐次フォールバック)のテストにより、一括 API 未実装プロトコルの挙動が
  現行と同一であることが示されていること
- `build.bat Release` 成功

---

## Reporting Format

1. **Baseline 結果**
2. **Phase 1 サポート表**: プロトコル × (一括読み / ビット一括書込) × 上限・制約・根拠
3. **API 設計**: `IPlcSession` 拡張の署名と既定実装の説明
4. **実装一覧**: プロトコル別の実装内容・追加テスト・フォールバック条件
5. **最後に実行したコマンドと結果**(失敗を隠さない)
6. **Stop And Ask**: 発生した質問と停止範囲
7. **実機検証チェックリスト**(TODO.md への追記内容)と「マージは実機確認後」の明記

---

## Out-of-scope Items(やらないこと)

- UI 仕様・XAML・表示形式の変更
- 監視タブ(ブロック読み)の読取方式変更(既にブロック読みで 1〜数往復のため)
- `lib/` DLL の変更、ライブラリ自体の改修(兄弟リポジトリ側の別作業として提案のみ)
- 省負荷設計(可視行のみ・スクロール抑制・編集中停止)の変更
- プロジェクト JSON / 設定 JSON / CSV フォーマットの変更
- NuGet 依存の追加・更新
- 実機 PLC を使う検証の実施(チェックリスト作成まで)
- リトライ・タイムアウト等の通信ポリシー変更

---

## 実施結果(2026-06-19)

### できたこと

- 作業ブランチ `codex/perf-batch-io` で実装済み。ただし、この時点では未コミット。
- Phase 0:
  - `AGENTS.md` を確認。
  - `git status` が clean であることを確認してから着手。
  - refactor 第2サイクルが `d912c4e Complete refactor cycle 2` として完了済みであることを確認。
- Phase 1:
  - `lib/plc-comm/net9.0` の 3 DLL を reflection で調査。
  - SLMP は `ReadRandomAsync`、`WriteRandomBitsAsync`、`WriteRandomWordsAsync`、
    `WriteBlockAsync` を公開していることを確認。
  - KV Host Link は monitor / named / consecutive 系 API を公開していることを確認。
  - TOYOPUC は `ReadManyAsync` / `WriteManyAsync` などを公開していることを確認。
- Phase 2:
  - `IPlcSession.ReadBatchAsync(...)` を追加。
  - `IPlcSession.WriteBitBatchAsync(...)` を追加。
  - どちらも interface default implementation と `PlcSessionBase` virtual implementation で
    逐次 fallback を持つ形にした。
  - 一括 API 未実装 session でも既存挙動が維持されるようにした。
- Phase 3:
  - `SlmpSession.ReadBatchAsync(...)` を override。
  - watch 可視行の Word / DWord 系 query を `ReadRandomAsync` にまとめる実装を追加。
  - Word と DWord random operands の混載を実装。
  - 64 device ごとの chunk 分割を実装。
  - 解析不能・range validation 失敗は該当行だけ error result にする形にした。
  - random read frame が失敗した場合は逐次 read へ fallback する形にした。
  - bit device / word-bit / 未対応特殊 query は逐次 fallback のままにした。
- Phase 5:
  - `SlmpSession.WriteBitBatchAsync(...)` を override。
  - 直接 bit device への bit 書込のみ `WriteRandomBitsAsync` にまとめる実装を追加。
  - word-bit read-modify-write 経路は、対象外 bit 不変の保証を守るため一括化していない。
- Phase 6:
  - `TODO.md` の Remaining Manual Validation に実機検証チェックリストを追記。
  - `docs/perf-batch-io-report.md` に support table、実装内容、Stop And Ask、実機確認内容を記録。

### できていないこと / 残したこと

- 実機 PLC 検証は未実施。
  - 大量 watch 行の scroll read。
  - Word / DWord / Float32 / Bit / word-bit の値一致確認。
  - 無効 address が他行を巻き込まないことの実機確認。
  - random read 拒否時の逐次 fallback 確認。
  - bit 一括書込時に対象外 device / bit が変わらないことの確認。
  - 長時間 trace / error logging の安定確認。
- Host Link の cross-watch batch read / bit batch write は未実装。
  - DLL metadata だけでは cross-watch の点数上限・混載制約を確定できなかったため。
  - 既存 `ReadBlockAsync` 内の monitor / consecutive / named optimization は維持。
- TOYOPUC の cross-watch batch read / bit batch write は未実装。
  - DLL metadata だけでは `ReadMany` / relay / mixed-device 制約を確定できなかったため。
  - 既存 `ReadBlockAsync` 内の block / packed read は維持。
- SLMP の word-bit 書込一括化は未実装。
  - 対象外 bit を変更しない read-modify-write semantics を優先したため。
- `lib/` DLL の変更、兄弟リポジトリ改修、通信 retry / timeout policy 変更は未実施。

### 検証結果

- 着手時 baseline:
  - `dotnet build .\PlcScopeDotNet.sln`: 成功、0 warnings / 0 errors。
  - `dotnet test .\PlcScopeDotNet.sln -m:1`: 成功、274 tests。
    - Core: 200
    - App.UiTests: 33
    - App.Tests: 41
  - 最初に build / test を並列実行した際は MSBuild intermediate file locking が発生。
    `dotnet build-server shutdown` 後、直列で再実行して baseline 成功を確認。
- 実装後:
  - `dotnet build .\PlcScopeDotNet.sln`: 成功、0 warnings / 0 errors。
  - `dotnet test .\PlcScopeDotNet.sln -m:1`: 成功、282 tests。
    - Core: 206
    - App.UiTests: 33
    - App.Tests: 43
  - `build.bat Release`: 成功。
  - `git diff --check`: 空白エラーなし、CRLF 警告のみ。
- 追加した主な自動テスト:
  - default sequential batch read / write fallback。
  - watch tab の可視行 batch read。
  - batch read 内の行単位 error isolation。
  - SLMP mixed Word / DWord random read。
  - SLMP 64 device chunking。
  - SLMP random read failure から逐次 read への fallback。
  - SLMP random bit write。
- 禁止領域の確認:
  - XAML 変更なし。
  - `lib/` 変更なし。
  - `Directory.Packages.props` 変更なし。
  - `.github/` 変更なし。
  - VM の public property / command 名変更なし。

### マージ判断

この変更は実機 PLC 確認が完了するまで merge-ready としない。
