# 実装計画: 外見適用およびモデルスポーン不具合の根本解決 (v0.1.11.0)

## 1. 概要
- **ユーザーからの報告課題**:
  1. Glamourer design / Penumbra Collection を選択してスポーンしても自キャラの見た目になってしまう。
  2. MCDF を選択してスポーンしても自キャラの見た目になってしまう。
  3. NPC やモンスターを選択してスポーンしても、ギズモしか表示されずキャラが表示されない。
  4. 保存した際にきちんと情報が保存されているか確認してほしい。
- **方針**:
  - Glamourer / Penumbra / MCDF 外見適用は **A Quest Reborn (AQR)** の方式に準拠。
  - NPC / モンスターモデルスポーンおよびアクター生成・描画ライフサイクルは **HDM (https://github.com/Enceladeum/HDM)** に準拠。

## 2. 根本原因の特定と設計方針

### (1) 自キャラ化の真因（Two Index Spaces Trap & Glamourer Identity）
- **Two Index Spaces Trap**:
  `ClientObjectManager.CreateBattleCharacter()` が返すのは COM 内部インデックス（0, 1, 2...）。しかし Dalamud の `IObjectTable` や Glamourer / Penumbra IPC が受け取る `actorIndex` は **グローバル ObjectTable インデックス（GPose/カットシーン予約枠 ~200-244）** であった。COM#0 を渡すとプレイヤー自キャラ（ObjectTable[0]）と誤認され、Glamourer が自キャラに適用されてしまう。
  → `objectTable.CreateObjectReference((nint)nativeChara)` から `actor.ObjectIndex`（GlobalIndex）を取得して IPC に渡す。
- **Glamourer Identity の罠 (The 0.8.44 Bug)**:
  `CreateBattleCharacter` で作成した BattleNpc は `NameId == 0` かつ名前が適切でないと、Glamourer の `ActorIdentifierFactory` が `CreateNpc(BattleNpc, 0)` を呼び出し、無効（Invalid）と判定されて `GetState` / `ApplyDesign` がサイレントに何もせず自キャラクローンのまま残る。
  → スポーン直後に `NameId = 0`、`HomeWorld = meNative->HomeWorld`、ユニークな "Forename Surname" 形式の名前（例: `"Cs Aa"`, `"Cs Ab"`）をスタンプする。
- **Draw-When-Ready（2フェーズ待機キュー）**:
  スポーン直後のフレームでは DrawObject は未生成。Glamourer は DrawObject が実際に **VISIBLE（可視）** になって初めて外見を適用できる。
  → フェーズ 1: `IsReadyToDraw()` で `EnableDraw()`。
  → フェーズ 2: `DrawObject != null && DrawObject->IsVisible` を確認した瞬間に `ApplyExternalAppearance` を実行。

### (2) ギズモのみ表示の真因（素の BattleNpc の不可視問題）
- 素の `SetupBNpc(0)` や空の BattleNpc は drawable な人間骨格を持たず、不可視となる。
- HDM & Brio の黄金パターン:
  すべてのスポーン（モンスター・NPC含む）において、まず自キャラからダブルコピー（`sourceNative -> newChara (WeaponHiding)`, `newChara -> newChara (None)`）を行い、drawable な描画骨格を確立する。
  その上でモンスターの場合、`ModelContainer.ModelCharaId = template.ModelCharaId` を書き込み、武器を非表示にして Redraw（Penumbra Redraw または DisableDraw/EnableDraw）を行うことで、確実にモンスターモデルが構築・描画される。

### (3) 保存内容の保証
- `SaveModalTemplate` において、MCDF の場合はパース結果を確実に `target.GlamourerDesignString` に代入。Glamourer の場合も GUID またはデザイン名を確実に代入。
- 保存結果の詳細を `logManager.Info` に記録し、右ペインの `Template Details` にも全項目を明瞭に表示。
- `OpenEditCharacterModal` で保存されている GUID／名前／MCDFパス／モンスター・NPC 情報を正確に復元。

## 3. 実装ステップ
1. `Models/CharacterModels.cs`: `GlobalIndex` と `ComIndex` を追加。
2. `Managers/ActorManager.cs`:
   - `ReadyJob` クラスと `readyJobs` リストを追加。
   - `NextPuppetName()` によるユニーク名生成。
   - `SpawnCharacter` での自キャラからのダブルコピー、Glamourer Identity スタンプ、グローバルインデックス解決。
   - `UpdateFrame` での 2フェーズポーリング。
   - `ApplyExternalAppearance` でのグローバルインデックス適用とモデル設定・Redraw。
   - `DespawnCharacter` での `GetIndexByObject` による動的 COM 解決。
3. `UI/CharacterLibraryTab.cs`:
   - `SaveModalTemplate`、`OpenEditCharacterModal`、`DrawRightPane` の強化。
4. バージョン更新（0.1.11 / 0.1.11.0）、CHANGELOG 更新、Git コミット & プッシュ。
