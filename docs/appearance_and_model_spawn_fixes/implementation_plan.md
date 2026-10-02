# 実装計画: 外見適用およびモデルスポーンの根本改修 (v0.1.13)

## 1. 課題と根本原因の特定

### A. MCDF が読み込めず前キャラ（または自キャラ）のままになる
- **原因**: 従来のコードは生バイト列から `"MCDF"` ヘッダを探していたが、現代の MCDF ファイル（`testruma.mcdf` 等）は **全体が LZ4 圧縮ストリーム** として格納されている（AQR の `McdfCharaFileManager.cs` 380行目: `new LZ4Stream(unwrapped, LZ4StreamMode.Decompress, LZ4StreamFlags.HighCompression)` 参照）。
- **影響**: 生ファイルを開くと LZ4 圧縮バイナリが JSON の途中に混ざり `unexpected character` 例外でパース落ちし、`GlamourerDesignString` が空（長さ 0）のまま保存されていたため、直前のキャラ（Ruma等）や自キャラの姿が残っていた。

### B. Penumbra Collection が反映されない
- **原因**: `Penumbra.Api.dll` (v1.7.2.1) の CIL 逆アセンブル解析の結果、`Penumbra.SetCollectionForObject.V5` のシグネチャは、
  - 第2引数が `Guid` ではなく **`Guid?` (`Nullable<Guid>`)**
  - 戻り値が `(int, Guid)` ではなく **`(int, (Guid, string)?)` すなわち `(PenumbraApiEc, (Guid, string)?)`**
  であることが判明した。
- **影響**: 誤った型（`int` や `(int, Guid)`）で購読していたため、呼び出し時に型変換例外（`converting from ValueTuple 2 to System.Int32`）が発生し、Penumbra コレクションの適用が全て失敗していた。

### C. Penumbra Collection と Glamourer の二重 Redraw 不足
- **原因**: AQR では Penumbra コレクション設定直後に一度 Redraw を行い、その後に Glamourer 外見を適用し、再度 Redraw を行うシーケンスを採用している。CharacterSpawn ではこれが不足していたため Mod 服や装飾が反映されにくかった。

---

## 2. アーキテクチャ改修方針

### 1. MCDF LZ4 圧縮ストリーム解凍パーサーの導入 (`Services/McdfParser.cs`)
- `lz4net` ライブラリを導入し、ストリーム全体を `LZ4.LZ4Stream` でオンザフライ解凍。
- 解凍後のストリームから `"MCDF"` 4バイトヘッダ、バージョン (1 byte)、データ長 (int32) を読み取り、UTF-8 JSON ペイロードを取得。
- JSON 内の `"GlamourerData"` プロパティから Base64 外見文字列（1100文字超）を確実に抽出して保持。

### 2. Penumbra IPC 正確な V5 型定義の実装 (`Services/PenumbraIpc.cs`)
- `ICallGateSubscriber<int, Guid?, bool, bool, (int, (Guid, string)?)>` を最優先で購読。
- Legacy 向けには `ICallGateSubscriber<int, string, bool, bool, (int, string)>` を実装。
- 戻り値タプルの第1要素が `0`（Success）であることを検証し、確実にコレクションをバインド。

### 3. Penumbra & Glamourer 二重 Redraw フロー (`Managers/ActorManager.cs`)
- AQR 準拠: コレクション設定 -> 第1回 Penumbra Redraw -> Glamourer 外見適用 -> 第2回 Penumbra Redraw のパイプラインを確立。

---

## 3. 検証・品質保証
- `testruma.mcdf` (22KB) および `test.mcdf` (19KB) からそれぞれ 1104 文字、1144 文字の Glamourer Base64 データの抽出成功を確認。
- Penumbra IPC の V5 正確なタプル通信の型安全性を CIL レベルで確認。
- 各種モデルスポーン（MCDF、Penumbra Collection、Glamourer Design）の完全適用。
