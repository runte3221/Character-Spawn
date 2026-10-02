# 変更内容の確認 (Walkthrough): NPC および モンスター/MOB スポーンの描画不具合修正 (HDM準拠)

## 実施した変更の概要

### 1. `Services/GlamourerIpc.cs`
- **`Glamourer.GetState` IPC の追加**:
  - `(int ec, JObject? state)` のタプルを受け取る V2 IPC Subscriber を購読。
- **`ApplyNpcAppearance` の実装 (HDM `HumanGuise.cs` 準拠)**:
  - `CustomizeMap`（36項目）を用いた 26バイト `CustomizeData` の JObject への安全なマッピング。
  - `CustomItemId` ビット演算（`model | (variant << 32) | (equipType << 40) | CustomFlag`）による NPC 装備 ID の構築。
  - 武器スロットの `Apply = false` 化（自キャラ武器の上書き防止）。
  - `Parameters` および `Materials` ブロックの完全除去（プレイヤーの肌色やシェーダーパラメータが NPC に混入するのを防止）。
  - `ApplyState` による一括容姿適用。

### 2. `Managers/ActorManager.cs`
- **HDM 黄金パターンのスポーンフロー**:
  - スポーン時は常に人間ベースライン（`ModelCharaId = 0`, `Scale = 1.0f`）のクローンとして生成し、即座に `DisableDraw()`。
  - `readyJobs`（Phase 1）で `IsReadyToDraw()` を待って `EnableDraw()`。
  - `readyJobs`（Phase 2）で `DrawObject != null && DrawObject->IsVisible`（人間ベースラインの完全な実体化）を確認。
- **モンスターモデル（`ModelCharaId > 0`）の段階的再描画**:
  - 実体化確認後に `native->ModelContainer.ModelCharaId` と `Scale` を設定。
  - Demihuman 装備があればスロットに書き込み、`IsHatHidden = false` を設定。
  - `DisableDraw()` -> `monsterRedrawJobs`（2フレーム待機後、`IsReadyToDraw()` を待って `EnableDraw()`）でモンスターモデルをロード。
  - モンスター描画時には Penumbra Redraw や Glamourer の呼び出しを完全に抑止（描画オブジェクトの消滅・破損を防止）。
- **人型 NPC（`SourceType == Npc`）の適用**:
  - 実体化確認後に `glamourerIpc.ApplyNpcAppearance` を実行。
  - 骨格再構築を確定させるための安全な Redraw シーケンスを実行。
- **テンプレート ID からの自動補完**:
  - モンスターの `ModelCharaId == 0` や NPC の `CustomizeData` 未ロード時、`GameDataService` から自動補完するセーフティネットを追加。

### 3. `Services/GameDataService.cs`
- `GetMonsterModelCharaId(uint bnpcNameId)` の追加。
- `BuildMonsterCache` においてモデルが有効に解決できるモンスターのみをリスト化。

### 4. バージョン更新
- `package.json`: `0.1.22`
- `CharacterSpawn.json`: `0.1.22.0`
- `CharacterSpawn.csproj`: `0.1.22.0`
- `repo.json`: `0.1.22.0`
- `CHANGELOG.md`: リリースノート追記
