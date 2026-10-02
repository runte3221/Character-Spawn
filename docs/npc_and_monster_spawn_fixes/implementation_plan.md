# 実装計画: モンスター・NPCスポーン不具合の完全修正 (v0.1.23)

## 1. 根本原因の特定と解決策

### A. モンスターのモデル不一致（ルーインランナー問題）
- **原因**: 以前の実装では簡易マッピング `BNpcLink.csv` から推測して紐付けていたため、ルーインランナー（NameId 2）が誤った ModelCharaId に結びついていた。
- **解決策**: HDM公式の `mob-model-index.csv` (16,243体) をリソースとして直接読み込み。
  - ルーインランナー: `BaseId: 16642, NameId: 2, ruins runner, ModelCharaId: 1281, McType: 3, Scale: 1.1` を正確にセット。
  - 日本語名は Lumina の `BNpcName` シートから優先取得。

### B. 人型NPCが自キャラ（Ruma / testruma）になる問題
- **原因**: `dalamud.log` より、`ApplyNpcAppearance: GetState returned null for actor #200` を確認。メインスレッド内で `Thread.Sleep(16)` を繰り返したため、ゲームループが止まり Glamourer がスポーンされたパペットを認識・登録できずタイムアウトしていた。その結果、自キャラクローンのまま Penumbra Redraw されてしまっていた。
- **解決策**: HDM `HumanGuise.cs` に完全準拠した非ブロッキング非同期フレームポーリングキュー `PendingNpcJob` を実装。
  - スレッドを一切停止させず、毎フレームの `UpdateFrame` 内で `glamourerIpc.TryApplyNpcAppearance` をポーリング（最大 120 フレーム）。
  - Glamourer が認識して State が取れた瞬間に、`Customize` (36項目) + `Equipment` (10スロット) を注入し、`Parameters` と `Materials` を剥離して自キャラの肌色・シェーダー汚染を排除。
  - 適用後に `penumbraIpc.Redraw(globalIndex)` を呼んで NPC の容姿を確定。

### C. レターモーグリ（Demihuman）が透明になる問題
- **原因**: レターモーグリは `ModelChara.Type == 2`（Demihuman）。Demihuman は単なる ModelCharaId スワップだけではパーツが存在せず透明になる。`NpcEquip` シートから装備パーツをロードし、`DrawData.EquipmentModelIds` に書き込み、`IsHatHidden = false` を維持する必要がある。
- **解決策**: `GameDataService.GetNpcAppearanceData` で Demihuman 用の `EquipmentModelIds` を返し、`ActorManager` の描画処理で確実にセットする。

---

## 2. 変更対象ファイル
1. `CharacterSpawn/Models/CharacterModels.cs`: `McType` プロパティ追加
2. `CharacterSpawn/Services/GameDataService.cs`: `mob-model-index.csv` 読み込みと日本語名解決
3. `CharacterSpawn/Services/GlamourerIpc.cs`: 非ブロッキング `TryApplyNpcAppearance` の実装
4. `CharacterSpawn/Managers/ActorManager.cs`: `PendingNpcJob` キューの実装、Demihuman 装備書き込み
5. `CharacterSpawn/UI/CharacterLibraryTab.cs`: `Scale` / `McType` の保存対応
6. `package.json`, `CharacterSpawn.csproj`, `CharacterSpawn.json`, `repo.json`: バージョン 0.1.23 / 0.1.23.0
