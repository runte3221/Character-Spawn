# タスクリスト: モンスター・NPCスポーン不具合の完全修正 (v0.1.23)

## 課題概要
1. **モンスター（モブ）モデル不一致**:
   - ルーインランナーをスポーンすると別のモブが出現する問題（推測用 `BNpcLink.csv` の不正確さが原因）。
2. **人型NPC（ゴントラン、ミューヌ等）の外見反映失敗**:
   - NPCを選択してスポーンしても自キャラの姿（Ruma / testruma）で表示されてしまう問題（Dalamud ログ調査で `ApplyNpcAppearance: GetState returned null for actor #200` が判明。メインスレッドでの `Thread.Sleep(16)` によるゲームループ停止が原因）。
3. **非人型NPC（レターモーグリ等）の透明化**:
   - Demihuman（McType 2）のNPCでギズモしか表示されない問題（`NpcEquip` 由来のモデルIDの未適用が原因）。

---

## タスク進捗

- [x] **原因調査・ログ解析**
  - [x] `dalamud.log` から Glamourer の `GetState` が null になっている事実を特定
  - [x] HDM (`Enceladeum/HDM`) の `HumanGuise.cs`, `MobIndex.cs`, `GuiseService.cs` を徹底分析
  - [x] HDM 公式データ `Data/mob-model-index.csv` (16,243体) を入手

- [x] **実装修正**
  - [x] `Resources/mob-model-index.csv` をプロジェクトに配置
  - [x] `GameDataService.cs` の `BuildMonsterCache` を `mob-model-index.csv` ベースに刷新（Lumina `BNpcName` から日本語名を解決、重複排除）
  - [x] `CharacterModels.cs` の `CharacterTemplate` に `McType` を追加
  - [x] `GlamourerIpc.cs` のブロッキングスリープを撤廃し、非ブロッキング `TryApplyNpcAppearance` を実装
  - [x] `ActorManager.cs` に HDM 準拠の非同期フレームポーリングキュー `PendingNpcJob` を実装（最大 120 フレーム）
  - [x] Demihuman NPC（レターモーグリ等）の `EquipmentModelIds` 適用と `IsHatHidden = false` 設定
  - [x] スポーン時のデータ自動補完強化

- [x] **バージョン更新 & デプロイ準備**
  - [x] `package.json` -> 0.1.23
  - [x] `CharacterSpawn.csproj` -> 0.1.23.0
  - [x] `CharacterSpawn.json` -> 0.1.23.0
  - [x] `repo.json` -> 0.1.23.0
  - [x] `CHANGELOG.md` 追記
  - [x] `docs/npc_and_monster_spawn_fixes` ドキュメント同期
  - [ ] GitHub リモートへコミット & プッシュ
  - [ ] GitHub Actions ビルド成果物を全バージョンフォルダに同期
