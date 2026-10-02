# 変更内容の確認 (Walkthrough) - v0.1.11.0

## 変更の概要
HDM (`Enceladeum/HDM`) および A Quest Reborn (AQR) のアーキテクチャ解析に基づき、スポーン時の「自キャラの見た目でスポーンしてしまう不具合」および「NPC・モンスター選択時にギズモしか表示されない不具合」を根本解決しました。また、テンプレート保存処理（`Save to Chara`）における全情報の完全保存と復元を保証しました。

---

## 主な変更点

### 1. Two Index Spaces Trap の完全解決 (`ActorManager.cs`, `CharacterModels.cs`)
- **問題**: `ClientObjectManager.CreateBattleCharacter()` が返す COM インデックス（0, 1...）を Glamourer や Penumbra に渡していたため、スロット 0 がプレイヤー自キャラ（ObjectTable[0]）と誤認され、自キャラの外見が上書きされていました。
- **解決策**:
  - `SpawnedActorData` に `GlobalIndex`（ushort）と `ComIndex`（ushort）を分離定義。
  - `objectTable.CreateObjectReference((nint)nativeChara)` からグローバルな `ObjectIndex`（GPose/カットシーン予約枠 ~200-244）を解決し、Glamourer / Penumbra IPC に渡すように改修しました。

### 2. Glamourer Identity スタンプ (`ActorManager.cs`)
- **問題**: `CreateBattleCharacter` で作成された BattleNpc は、Glamourer の `ActorIdentifierFactory` によって無効（Invalid）と判定され、外見適用がサイレントに失敗して自キャラクローンが残る状態でした（HDM The 0.8.44 Bug）。
- **解決策**:
  - スポーン直後に `nativeChara->NameId = 0`、`nativeChara->HomeWorld = meNative->HomeWorld`、および一意な SE 有効姓名（`Cs Aa`, `Cs Ab`...）を `NextPuppetName()` で付与。
  - Glamourer の Player Rescue ブランチを確実に通過させ、外見データが 100% 適用されるようにしました。

### 3. Draw-When-Ready 2フェーズ待機キュー (`ActorManager.cs`)
- **問題**: スポーン直後のフレームでは DrawObject が未生成のため、外見適用が反映されませんでした。
- **解決策**:
  - `ReadyJob` クラスを導入し、毎フレームの `UpdateFrame` で 2 フェーズポーリングを実施：
    - **Phase 1**: `IsReadyToDraw()` を待機して `EnableDraw()` を実行。
    - **Phase 2**: `DrawObject != null && DrawObject->IsVisible`（実際に描画オブジェクトが可視状態）になった瞬間に `ApplyExternalAppearance` を発火。

### 4. モンスター・非人型 NPC の描画保証 (`ActorManager.cs`)
- **問題**: 空の BattleNpc は描画骨格を持たず、不可視（ギズモのみ）となっていました。
- **解決策**:
  - HDM & Brio の黄金パターンに準拠し、モンスター・NPC を問わず、まず自キャラからダブルコピー（`WeaponHiding` ＋ `None`）を行って完全な drawable 状態を確立。
  - その後、モンスターの場合は `ModelContainer.ModelCharaId = template.ModelCharaId` を書き込み、武器を非表示にして Redraw（Penumbra Redraw または DisableDraw/EnableDraw）を実行。確実にモンスター・モブモデルが表示されるようにしました。

### 5. テンプレート情報の保存・復元・表示保証 (`CharacterLibraryTab.cs`)
- **保存の保証**: `SaveModalTemplate` において、MCDF の場合はパース結果を確実に `target.GlamourerDesignString` に代入。Glamourer の場合も GUID またはデザイン名を確実に代入。
- **復元の保証**: `OpenEditCharacterModal` で、保存されている GUID や名前から Glamourer のデザイン選択状態を正確に復元。
- **詳細表示の強化**: 右ペインの `Template Details` に、保存されている全属性（SourceType, DataId, ModelCharaId, Glamourer Design名/GUID, Penumbra Collection, McdfFilePath）を明瞭に表示。
- **詳細ログ**: 保存時およびスポーン時に、全パラメータの内容をログ（Logタブ）に出力。

---

## 修正箇所のファイル一覧
1. `Models/CharacterModels.cs`: `GlobalIndex` と `ComIndex` の追加
2. `Managers/ActorManager.cs`: HDM & AQR アーキテクチャへの全面改修
3. `UI/CharacterLibraryTab.cs`: 保存・復元・詳細表示の強化
4. `package.json`: バージョン更新 (`0.1.11`)
5. `CharacterSpawn.json`: AssemblyVersion 更新 (`0.1.11.0`)
6. `CharacterSpawn.csproj`: Version/AssemblyVersion/FileVersion 更新 (`0.1.11.0`)
7. `repo.json`: AssemblyVersion 更新 (`0.1.11.0`)
8. `CHANGELOG.md`: 0.1.11 変更内容追記
9. `docs/appearance_and_model_spawn_fixes/`: `task.md`, `implementation_plan.md`, `walkthrough.md` 同期
