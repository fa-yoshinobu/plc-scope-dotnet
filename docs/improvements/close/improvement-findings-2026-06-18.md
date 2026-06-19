# plc-scope-dotnet 改善点・問題点調査報告

調査日: 2026-06-18  
対象ブランチ: main (commit 98be138)
更新: 2026-06-19 main で完了済み項目にチェックを反映。残件 6 件を小改善で修正し、1-F は仕様としてクローズ。

---

## 調査概要

コードベース全体を静的に調査し、以下の観点で問題点・改善機会を整理した。

- 確認済みの不具合・欠陥（コード上で証拠あり）
- エラーメッセージの誤流用
- コード重複・設計負債
- パフォーマンス改善機会
- 潜在的な問題（軽微・エッジケース）
- ドキュメント・CI との乖離

> **注意**: `close/refactor-instructions.md`（第2サイクル）および `close/perf-batch-io-instructions.md` に
> すでに定義・承認された項目（D1〜D12、一括IO）は末尾の「既承認タスク一覧」にまとめた。
> 本文では未文書化の新規発見を中心に記述する。

---

## チェックリスト

### 1. 確認済みの不具合・欠陥

- [x] **1-A** `ImportCommentCsvAsync` の死コード行 — `MainWindowViewModel.cs:393-394`
- [x] **1-B** `DisconnectAsync` インデント崩れ — `MainWindowViewModel.cs:621`
- [x] **1-C** `ToggleDWordBitAsync` インデント崩れ（2箇所） — `MainWindowViewModel.cs:2054,2060`
- [x] **1-D** `NumericFormatter` 符号あり/なしのオーバーフロー処理の非対称 — `NumericFormatter.cs:154-157`
- [x] **1-E** `ParseByType` Float32 のエラー処理なし — `NumericFormatter.cs:71`
- [x] **1-F** `NormalizeDataType` ビットデバイスへの非 Bit 型素通り — `WatchDataTypePolicy.cs:31-32`

### 2. エラーメッセージの誤流用

- [x] **2-A** `ReadWatchItemAsync` のメッセージが文脈と不一致 — `MainWindowViewModel.cs:898`
- [x] **2-B** `ToggleDWordBitAsync` 1箇所目のメッセージが文脈と不一致 — `MainWindowViewModel.cs:2054`

### 3. コード重複・設計負債

- [x] **3-A** ウォッチ読取エラー処理の完全重複 — `MainWindowViewModel.cs:842-893`
- [x] **3-B** 行 VM 生成の二重 switch — `MainWindowViewModel.cs:1963, ~2790`
- [x] **3-C** テストダブルの重複定義 — `MainWindowViewModelWatchTests.cs` / `CommentCsvTests.cs`
- [x] **3-D** 純粋フォーマッタ関数の VM 同居 — `MainWindowViewModel.cs`（約20関数）
- [x] **3-E** FlaUI テストと VM 単体テストの混在 — `tests/PlcScope.App.UiTests`

### 4. パフォーマンス改善機会

- [x] **4-A** ログトリムの毎回フルリライト（高頻度・実測影響あり） — `FileLogStore.cs:191-211`
- [x] **4-B** コメント解決の毎リフレッシュ再計算 — `MainWindowViewModel.cs: ApplyCsvComments`
- [x] **4-C** ファミリ解決の毎回再構築 — `MainWindowViewModel.cs: ResolveDeviceFamilyForAddress`
- [x] **4-D** ウォッチ書込後の全行再読 — `MainWindowViewModel.cs: WriteWatchXxxAsync`
- [x] **4-E** 監視行 VM の毎回新規生成（GC負荷） — `MainWindowViewModel.cs: ReplaceRows`
- [x] **4-F** ウォッチ一括読み（通信往復数削減・大規模改善） — `SlmpSession` + VM

### 5. 潜在的な問題（軽微・エッジケース）

- [x] **5-A** `ExpandedBitMonitorRow` のアドレス分割に `Split` を使用 — `MainWindowViewModel.cs:2012,2016`
- [x] **5-B** `BuildPackedBits` が `BuildBitRows` の switch に含まれていない（デッドコード候補） — `BlockDataBuilder.cs:111-128`
- [x] **5-C** `NormalizeNumericText` が 10進モードでも "0X" を除去 — `NumericFormatter.cs:92`
- [x] **5-D** `CountLogRecordsAsync` の非効率な全行読み（初回起動時） — `FileLogStore.cs:275-282`

### 6. ドキュメント・CI との乖離

- [x] **6-A** `docs/development.md` 依存関係記述が実態と不一致
- [x] **6-B** `release.yml` に使われていないステップが残存

### 7. 既承認タスク（close/refactor-instructions.md 第2サイクル）

- [x] **D1** 重複行削除・インデント修正（L393, L621, L2053-2061）
- [x] **D1b** 誤流用エラーメッセージ修正 L898, L2054
- [x] **D2a** ウォッチ読取エラー処理の重複統合
- [x] **D2b** テストダブルの共有ファイルへ統合
- [x] **D2c** VM 単体テストを PlcScope.App.Tests へ分離
- [x] **D3** 純粋フォーマッタ群を Core へ抽出
- [x] **D4** コメント CSV マージロジックを Core へ抽出
- [x] **D5** ウォッチ読取解釈ロジックを Core へ抽出
- [x] **D6** 行 VM 生成の二重 switch 統合
- [x] **D7** `docs/development.md` 修正 + CI ステップ削除
- [x] **D9** ウォッチ書込後の全行再読 → 1 行のみ再読
- [x] **D10** コメント解決・ファミリ解決のキャッシュ導入
- [x] **D11** ログトリムのヒステリシス導入
- [x] **D12** 行 VM の in-place 更新（特性テスト前提）
- [x] **perf-batch** ウォッチ一括読み・ビット一括書込（第2サイクル完了後）

---

## 1. 確認済みの不具合・欠陥

### 1-A. `ImportCommentCsvAsync` の死コード行
- [x] 対応完了

**ファイル**: `src/PlcScope.App/ViewModels/MainWindowViewModel.cs`  
**行**: 393–394

```csharp
ErrorText = string.Empty;
ErrorText = string.Empty;   // ← 同一行が連続。片方は死コード
```

**影響**: 動作は正しいが、読み手に意図的なロジックがあるかのような誤解を与える。  
**対応**: 1行削除。

---

### 1-B. `DisconnectAsync` 内のインデント崩れ
- [x] 対応完了

**ファイル**: `src/PlcScope.App/ViewModels/MainWindowViewModel.cs`  
**行**: 619–622

```csharp
if (_session is null)
{
StatusText = "Disconnected";   // ← インデント崩れ（ブロック内なのに左寄せ）
    return;
}
```

**影響**: 挙動は正しいが、PR レビューやツールの diff で構造誤認のリスクがある。

---

### 1-C. `ToggleDWordBitAsync` のインデント崩れ（2箇所）
- [x] 対応完了

**ファイル**: `src/PlcScope.App/ViewModels/MainWindowViewModel.cs`  
**行**: 2053–2061

```csharp
if (IsSlmpDWordOnlyFamily())
{
            ErrorText = "...";   // ← 過剰インデント
    return Task.CompletedTask;
}

if (!DeviceAddressRangeProvider.TryParseAddress(...))
{
            ErrorText = "...";   // ← 過剰インデント
    return Task.CompletedTask;
}
```

**影響**: 動作は正しい。可読性の問題のみ。

---

### 1-D. `NumericFormatter` — 符号あり/なしのオーバーフロー処理の非対称性
- [x] 対応完了

**ファイル**: `src/PlcScope.Core/Services/NumericFormatter.cs`  
**行**: 136–139 (unsigned), 154–157 (signed)

**unsigned の場合**（`ParseUnsignedWithUpperClamp`）:
```csharp
// decimal.TryParse 失敗後
if (normalized.All(char.IsDigit))   // 超大正数 → maxValue にクランプ
    return maxValue;

return ulong.Parse(normalized, ...); // ← 非数字文字を含む → 必ず FormatException
```

**signed の場合**（`ParseSignedWithRangeClamp`）:
```csharp
if (normalized.All(char.IsDigit))   // 超大正数 → maxValue にクランプ
    return maxValue;

return long.Parse(normalized, ...); // ← '-' を含む超大負数もここで例外
```

**問題点**:
- 超大正数（例: "9" × 30桁）→ maxValue にクランプ（安全）
- 超大負数（例: "-9" × 30桁）→ `decimal.TryParse` 失敗 + `.All(char.IsDigit)` = false（'-' が含まれる）→ `long.Parse` も失敗 → **FormatException**
- 正負でクランプ挙動が非対称。ユーザーが非常に大きな負数を入力したとき、正数と異なる例外になる。

**影響**: 実用上は入力フィールドが通常の数値範囲を大きく超えることは稀。ただし設計の非一貫性。

**対応結果**: signed integer の巨大正数/巨大負数をそれぞれ max/min に clamp するよう修正し、テストを追加。

---

### 1-E. `NumericFormatter.ParseByType` — Float32 のエラー処理なし
- [x] 対応完了

**ファイル**: `src/PlcScope.Core/Services/NumericFormatter.cs`  
**行**: 71

```csharp
ValueDataType.Float32 => float.Parse(normalized, CultureInfo.InvariantCulture),
```

**問題点**: 整数型は `decimal.TryParse` + クランプで安全に処理されるが、Float32 だけ `float.Parse` を直接呼ぶ。ユーザーが "abc" などを入力すると FormatException がスローされる。呼び出し元の VM 側でキャッチされるので致命的ではないが、他の型と挙動が揃っていない。  
**比較**: ビット型は行 64 で `_` ケースの `throw new FormatException(...)` を明示的に持つ。Float32 は暗黙的に例外が伝播する。

**対応結果**: `TryParse` ベースの finite Float32 parser に変更し、invalid / NaN / Infinity のテストを追加。

---

### 1-F. `WatchDataTypePolicy.NormalizeDataType` の素通りパス
- [x] 対応完了（仕様としてクローズ）

**ファイル**: `src/PlcScope.Core/Services/WatchDataTypePolicy.cs`  
**行**: 31–32

```csharp
if (family.Kind == DeviceKind.Bit)
    return dataType == ValueDataType.Bit ? ValueDataType.Bit : dataType;
//                                                              ↑ Bit以外がそのまま通る
```

**問題点**: ビットデバイスファミリに対して `Float32` や `Int32` が渡されても、`Bit` に正規化されず素通りする。関数名「Normalize」から期待される動作（ビットデバイスなら必ず `Bit` に丸める）と異なる。

**判断結果**: これは現在の仕様。`M` / `L` / `B` などの bit device を `UInt16` / `UInt32` / `Float32` として packed watch する機能に必要なため、コード変更せず仕様としてクローズ。

**現状影響**: 上位の UI / VM 側が `GetAvailableDataTypes`（同ファイル L13）で型候補を絞り込んでいるため、通常フローでは `Float32` がビットデバイスに渡ることは稀。ただし将来の API 拡張時にサイレントバグの温床になる可能性がある。

---

## 2. エラーメッセージの誤流用

### 2-A. `ReadWatchItemAsync` のメッセージが文脈と不一致
- [x] 対応完了

**ファイル**: `src/PlcScope.App/ViewModels/MainWindowViewModel.cs`  
**行**: 898

```csharp
if (_session is null)
    throw new InvalidOperationException("Connect to the PLC before opening device ranges.");
//                                        ↑ ウォッチ読取の文脈なのにデバイスレンジの文言
```

**正しい文言**: `"Connect to the PLC before reading the watch list."`  
（`close/refactor-instructions.md` D1b に承認済み）

---

### 2-B. `ToggleDWordBitAsync` の 1 箇所目のメッセージが文脈と不一致
- [x] 対応完了

**ファイル**: `src/PlcScope.App/ViewModels/MainWindowViewModel.cs`  
**行**: 2054

```csharp
if (IsSlmpDWordOnlyFamily())
{
    ErrorText = "The bit write target address could not be parsed.";
//               ↑ 「ビット書込非対応デバイス」の文脈なのに「アドレス解析失敗」の文言
```

**正しい文言**: `"Bit writes are not supported for this device."`  
（`close/refactor-instructions.md` D1b に承認済み）

---

## 3. コード重複・設計負債

### 3-A. ウォッチ読取エラー処理の完全重複
- [x] 対応完了

**ファイル**: `src/PlcScope.App/ViewModels/MainWindowViewModel.cs`  
**行**: 842–866 (`ReadWatchListAsync` ループ本体) と 869–893 (`RefreshWatchItemAsync`)

try/catch の内容・例外処理・ビット初期化がほぼ完全に同一。行数にして約 20 行の重複。  
→ 共通ヘルパー `RefreshSingleWatchItemAsync(item)` への統合が可能。  
（`close/refactor-instructions.md` D2a に定義済み）

---

### 3-B. 行 VM 生成の二重 switch
- [x] 対応完了

**ファイル**: `src/PlcScope.App/ViewModels/MainWindowViewModel.cs`  
**行**: 1963–2018 (`CreateRowViewModel`) と 推定 2790 付近 (`CreateReadOnlyRowViewModel`)

7 行種 × 2 バリアント（書込可 / 読取専用）でほぼ同一の switch 文。差分はフラグとコールバック有無のみ。  
行種追加時に 2 箇所の同期修正が必要で、片方だけ修正するバグの温床。  
（`close/refactor-instructions.md` D6 に定義済み）

---

### 3-C. テストダブルの重複定義
- [x] 対応完了

**ファイル**: `tests/PlcScope.App.UiTests/` 以下の複数ファイル

`InMemorySettingsStore` / `NullLogStore` が `MainWindowViewModelWatchTests.cs` と `MainWindowViewModelCommentCsvTests.cs` の両方に private クラスとして定義されている。  
（`close/refactor-instructions.md` D2b に定義済み）

---

### 3-D. 純粋フォーマッタ関数の VM 同居
- [x] 対応完了

**ファイル**: `src/PlcScope.App/ViewModels/MainWindowViewModel.cs`

UI 非依存の静的純関数（`FormatInt16`, `FormatInt32`, `FormatConnectionError`, `FormatCpuStateText`, `ToRawWord`, `ToRawDWord`, `PackBits`, `CombineWords` など）が 2,556 行の ViewModel に混在している。  
Core への抽出でユニットテストが追加可能になる。  
（`close/refactor-instructions.md` D3 に定義済み）

---

### 3-E. FlaUI テストと VM 単体テストの混在
- [x] 対応完了

**プロジェクト**: `tests/PlcScope.App.UiTests`

UIA スモークテスト（実デスクトップセッション必須）と高速な VM 単体テストが同一プロジェクトに混在。  
分離することで CI の高速化と、デスクトップセッション不要環境での単体テスト実行が可能になる。  
（`close/refactor-instructions.md` D2c に承認済み）

---

## 4. パフォーマンス改善機会

### 4-A. ログトリムの毎回フルリライト（高頻度・実測影響あり）
- [x] 対応完了

**ファイル**: `src/PlcScope.Infrastructure/Storage/FileLogStore.cs`  
**行**: 191–211

```csharp
// WriteLinesCoreAsync 内
if (recordCount > MaxLogRecords)   // MaxLogRecords = 500
    recordCount = await TrimLogFileAsync(path, cancellationToken);
// → ファイル全行読み込み → 最新500行だけ書き直し
```

**問題**: ログが 500 件に達した後は**追記のたびに**条件が成立し、毎回ファイル全体を読み書きする。  
PLC 切断中に自動リフレッシュが動いていると 500ms ごとにエラーログが増加し、**500ms 周期でフルリライト**が繰り返される。

**改善案**: ヒステリシス導入。例: 600 件超えたときだけ 500 件に切り詰める。  
→ フルリライトは約 100 append に 1 回になる（20倍削減）。  
（`close/refactor-instructions.md` D11 に承認済み）

---

### 4-B. コメント解決の毎リフレッシュ再計算
- [x] 対応完了

**ファイル**: `src/PlcScope.App/ViewModels/MainWindowViewModel.cs`  
**箇所**: `ApplyCsvComments` → `GetCommentAddressKeys` → `CommentAddressKeyProvider.GetKeys`

500ms 周期のリフレッシュごとに、可視アドレス×デバイスファミリの全組み合わせで  
`GetDeviceFamilies(...).OrderByDescending(...).ThenByDescending(...)` が再構築される。

**改善案**: 「アドレス → 解決済みコメント」のキャッシュ辞書を導入。  
無効化条件: CSV 変更時・プロトコル変更時・KeyenceDeviceMode 変更時のみ。  
（`close/refactor-instructions.md` D10 に承認済み）

---

### 4-C. ファミリ解決の毎回再構築
- [x] 対応完了

**ファイル**: `src/PlcScope.App/ViewModels/MainWindowViewModel.cs`  
**行**: 推定 1264 (`ResolveDeviceFamilyForAddress`)

ウォッチ各行の読取時に毎回 `GetDeviceFamilies(...).OrderByDescending(...)` を再構築している。  
100 行表示時 × 500ms 周期 = 1秒に約 200 回の無駄なリスト再生成。

**改善案**: 「プロトコル + KeyenceDeviceMode → ソート済みファミリ配列」のキャッシュ。  
（`close/refactor-instructions.md` D10 に含む）

---

### 4-D. ウォッチ書込後の全行再読
- [x] 対応完了

**ファイル**: `src/PlcScope.App/ViewModels/MainWindowViewModel.cs`  
**箇所**: `WriteWatchDirectBitAsync`, `WriteWatchBitAsync`, `WriteWatchItemAsync`

1 点書き込み後に `ReadWatchListAsync()` を呼ぶ → 可視 N 行全員分の PLC 往復（直列）が発生する。  
特に可視行が多いと書込確認が完了するまで UI がブロックされる時間が長い。

**改善案**: 書き込んだ該当アイテムのみを再読する。  
（`close/refactor-instructions.md` D9 に承認済み）

---

### 4-E. 監視行 VM の毎回新規生成（GC負荷）
- [x] 対応完了

**ファイル**: `src/PlcScope.App/ViewModels/MainWindowViewModel.cs`  
**行**: 推定 1399–1416 (`ReplaceRows`)

値が変化した行を `CreateRowViewModel` で丸ごと新規生成して置換する。  
1 行あたり `BitCellViewModel` 16〜32 個 + クロージャを 500ms ごとに確保・破棄する。  
無変化行は `MonitorRowRefreshComparer` が置換を抑止しているが、変化行は毎回フル再確保。

**改善案**: 同一アドレス・同一行種の既存 VM をプロパティ in-place 更新する。  
`SetWatchBits`（推定 L1071–1158）で同パターンの再利用が既に実装されており参考にできる。  
（`close/refactor-instructions.md` D12 に承認済み・特性テスト前提）

---

### 4-F. ウォッチ一括読み（通信往復数削減）— 大規模改善
- [x] 対応完了

**現状**: 可視 1 行 = 1 往復の逐次読み（100行可視時 = 100往復直列）  
**改善案**: SLMP `ReadRandomAsync` を活用し複数アドレスを 1〜数往復でまとめて読む。  
→ SLMP の対応が確認済み（`SlmpSession.ReadRandomDWordValuesAsync` で既に使用実績あり）。  
KV Host Link / TOYOPUC は API 調査が必要。

**注意**: 行単位エラーのセマンティクス（1 行失敗が他行を巻き込まない）の維持が必須。  
（`close/perf-batch-io-instructions.md` に詳細定義済み・実施済み）

---

## 5. 潜在的な問題（軽微・エッジケース）

### 5-A. `ExpandedBitMonitorRow` のアドレス分割に `Split` を使用
- [x] 対応完了

**ファイル**: `src/PlcScope.App/ViewModels/MainWindowViewModel.cs`  
**行**: 2012, 2016

```csharp
expandedBit.Address.Split('.')[0]   // "D100.5" → "D100"
```

`Address` が空文字列や `.` を含まない場合に `[0]` は安全だが、期待するワードアドレス部分が取れるかは形式依存。  
`expandedBit.WordAddress` のような専用プロパティを持つ方が堅牢。

**対応結果**: `LastIndexOf('.')` ベースの helper に置き換え、toggle callback も同じ word address を共有するよう修正。

---

### 5-B. `BuildPackedBits` が `BuildBitRows` の switch に含まれていない
- [x] 対応完了

**ファイル**: `src/PlcScope.Core/Services/BlockDataBuilder.cs`  
**行**: 30–38, 111–128

`BuildPackedBits` メソッド（L111-128）は定義されているが、`BuildBitRows` の switch（L30-38）に対応するケースが存在しない。  
`BuildSingleBits`（BitExpand）と `BuildBitWordRows`（Word）はあるが、PackedBit 専用の `BlockDisplayMode` が将来追加されるなら、このメソッドは現状デッドコード。  
もし使用予定がないなら削除が望ましい。

**対応結果**: 未使用の `BuildPackedBits` を削除。既存の bit-device Word / DWord / Float32 rows は現行メソッドで維持。

---

### 5-C. `NormalizeNumericText` が "0X" プレフィックスを常に除去
- [x] 対応完了

**ファイル**: `src/PlcScope.Core/Services/NumericFormatter.cs`  
**行**: 92

```csharp
.Replace("0X", string.Empty, StringComparison.OrdinalIgnoreCase)
```

16進数モード（`isHex: true`）以外のとき（10進モードで）も "0X" を除去する。  
ユーザーが "0X1A" と入力した場合、16進モードでは "1A" → 26 として正しく処理されるが、  
10進モードでも "0X" が消えて "1A" が残り、`decimal.TryParse` 失敗 → 例外になる。  
呼び出し元が `radix` 引数を正しく渡している前提では問題ないが、API として normalize が isHex に依存しないのは混乱を招く。

**対応結果**: `0x` prefix は Hex mode の integer parse でのみ除去するよう変更し、Dec mode では format error になるテストを追加。

---

### 5-D. `FileLogStore.CountLogRecordsAsync` の非効率な全行読み
- [x] 対応完了

**ファイル**: `src/PlcScope.Infrastructure/Storage/FileLogStore.cs`  
**行**: 275–282

```csharp
private static async Task<int> CountLogRecordsAsync(string path, ...)
{
    var lines = await File.ReadAllLinesAsync(path, ...);
    return ReadJsonRecords(lines).Count();
}
```

**対応結果**: `StreamReader` で 1 行ずつ読みながら JSON record 数を数える実装に変更し、初回 count で全行配列を作らないようにした。

`_knownRecordCounts` が未登録（初回起動時など）のときに呼ばれる。ファイル全体を読んでカウントするため、既存ログが大きい場合に起動時コストが高い。  
`File.ReadAllLines` の代わりに行数だけカウントする軽量な手段（`StreamReader` でカウントのみ）への変更も選択肢の一つ。現実的には 500 行上限なので深刻ではない。

---

## 6. ドキュメント・CI との乖離

### 6-A. `docs/development.md` の依存関係記述が実態と不一致
- [x] 対応完了

**行**: 34 付近

「兄弟リポジトリがあれば project reference / なければ NuGet」と書かれているが、  
実態は無条件で `lib/plc-comm/net9.0/` の DLL を `<Reference>` + HintPath で参照している。  
（`close/refactor-instructions.md` D7 に承認済み）

---

### 6-B. `release.yml` に使われていないステップが残存
- [x] 対応完了

`.github/workflows/release.yml` の "Checkout protocol dependencies" ステップ（兄弟リポジトリ3つのclone）は commit `2c8c3f4` 以降どの csproj からも使われていない。  
（`close/refactor-instructions.md` D7 に承認済み）

---

## 7. 既承認タスク一覧（close/refactor-instructions.md 第2サイクル）

| チェック | ID | 内容 |
|:---:|----|----|
| - [x] | D1 | 重複行削除・インデント修正（L393, L621, L2053-2061） |
| - [x] | D1b | 誤流用エラーメッセージ修正 L898, L2054 |
| - [x] | D2a | ウォッチ読取エラー処理の重複統合 |
| - [x] | D2b | テストダブルの共有ファイルへ統合 |
| - [x] | D2c | VM 単体テストを PlcScope.App.Tests へ分離 |
| - [x] | D3 | 純粋フォーマッタ群を Core へ抽出 |
| - [x] | D4 | コメント CSV マージロジックを Core へ抽出 |
| - [x] | D5 | ウォッチ読取解釈ロジックを Core へ抽出 |
| - [x] | D6 | 行 VM 生成の二重 switch 統合 |
| - [x] | D7 | `docs/development.md` 修正 + CI ステップ削除 |
| - [x] | D9 | ウォッチ書込後の全行再読 → 1 行のみ再読 |
| - [x] | D10 | コメント解決・ファミリ解決のキャッシュ導入 |
| - [x] | D11 | ログトリムのヒステリシス導入 |
| - [x] | D12 | 行 VM の in-place 更新（特性テスト前提） |
| - [x] | perf-batch | ウォッチ一括読み・ビット一括書込（第2サイクル完了後） |

---

## 優先度サマリ

| 優先度 | 項目 | 理由 |
|--------|------|------|
| 高 | 4-A ログトリムヒステリシス (D11) | PLC切断時に500ms周期でフルI/Oが発生 |
| 高 | 4-B/C コメント・ファミリ解決キャッシュ (D10) | 500ms周期の無駄な再計算 |
| 高 | 2-A/B エラーメッセージ修正 (D1b) | ユーザーに誤情報を提示 |
| 中 | 1-D 符号付き負数クランプ非対称 | 実用上は稀だが設計上の非一貫性 |
| 中 | 4-D ウォッチ書込後全行再読 (D9) | 書込応答が遅く感じる場面がある |
| 中 | 4-F ウォッチ一括読み | 大幅な往復数削減（実機検証必須） |
| 低 | 1-A〜C 書式欠陥 (D1) | 動作に影響なし・可読性のみ |
| 低 | 5-B BuildPackedBits デッドコード | 機能影響なし |

---

*調査者: Claude Sonnet 4.6 (自動静的解析)*
