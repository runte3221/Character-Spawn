# 修正内容の確認 (Walkthrough): モンスター・NPCスポーン不具合の完全修正 (v0.1.23)

## 実施した変更内容

### 1. HDM 公式 `mob-model-index.csv` (16,243体) の統合
- **ファイル**: `Services/GameDataService.cs`, `Resources/mob-model-index.csv`
- HDM 公式の網羅的カタログを採用し、`BaseId`, `NameId`, `ModelCharaId`, `McType`, `Scale` を正確に保持。
- ルーインランナーは `ModelCharaId: 1281`（正解モデル）として解決。
- Lumina の `BNpcName` シートから日本語名を抽出し、同一名称・モデルの冗長な重複を排除してリスト化。

### 2. Glamourer IPC 非同期フレームポーリング (`PendingNpcJob`) の導入
- **ファイル**: `Services/GlamourerIpc.cs`, `Managers/ActorManager.cs`
- `GlamourerIpc.cs` の `ApplyNpcAppearance` 内にあった `Thread.Sleep(16)` を撤廃し、非ブロッキングな `TryApplyNpcAppearance` を実装。
- `ActorManager.cs` に `PendingNpcJob` キューを追加。人型NPCスポーン時、人間ベースラインの可視化完了後にキューへ登録し、毎フレームの `UpdateFrame` で Glamourer が認識するまでポーリング（最大 120 フレーム）。
- Glamourer 側でアクターが認識された瞬間に、NPC固有の顔・髪・肌・装備を適用し、自キャラの肌色・パラメータ汚染をクリアして Penumbra Redraw を発行。
- ゴントランやミューヌが自キャラの姿にならず、本人の姿で確実に実体化。

### 3. Demihuman NPC（レターモーグリ等）の透明化防止
- **ファイル**: `Services/GameDataService.cs`, `Managers/ActorManager.cs`
- レターモーグリ等の特殊NPCについて、`NpcEquip` シートからパーツデータを取得し、`chara->DrawData.EquipmentModelIds` に注入、さらに `chara->DrawData.IsHatHidden = false` を設定。
- 単なる ModelCharaId スワップでは透明になっていた Demihuman が、正常にモーグリ等の体・帽子付きで表示される。

---

## 検証手順
1. プラグインをリロード。
2. モンスター検索で「ルーインランナー」を選択してスポーンし、正しいモブモデルが表示されることを確認。
3. NPC検索で「ゴントラン」「ミューヌ」を選択してスポーンし、自キャラ（Ruma / testruma）ではなく本人の外見・衣装で出現することを確認。
4. NPC検索で「レターモーグリ」を選択してスポーンし、ギズモだけでなくモーグリの体が表示されることを確認。
