# 実装計画: NPC および モンスター/MOB スポーンの描画不具合修正 (HDM準拠)

## 1. 背景と課題

ユーザーが NPC または モンスター / MOB を選択してスポーンさせた際、3Dモデルが表示されずギズモのみが表示される不具合が発生している。

### 原因分析
1. **モンスター（`ModelCharaId > 0`）**:
   - ゲームエンジンは、COM で生成された初期状態（自キャラクローン）の未描画アクターに対し、いきなり `ModelCharaId` を書き込んで `EnableDraw()` すると、人間骨格描画オブジェクトと競合して描画オブジェクトが壊れ、不可視（透明）になる。
   - **HDM (https://github.com/Enceladeum/HDM) の解法**:
     - まず `ModelCharaId = 0`, `Scale = 1.0f` の「人間ベースライン」として `IsReadyToDraw()` を待ち `EnableDraw()` を行う。
     - その描画オブジェクトが完全に実体化（`DrawObject != null && DrawObject->IsVisible`）したことを確認する（Phase 2）。
     - 実体化後に初めて `native->ModelContainer.ModelCharaId` と `Scale` を書き込み、`DisableDraw()` -> （2フレーム待機後 `IsReadyToDraw()`） -> `EnableDraw()` という再描画（RedrawJob）を行うことで、ゲームエンジンにモンスターモデルを安全にロードさせる。
     - モンスターモデルに対し Penumbra Redraw や Glamourer を呼ぶと DrawObject が無効化されるため絶対に呼ばない。

2. **人型 NPC（`SourceType == Npc`）**:
   - `chara->DrawData.CustomizeData` や `EquipmentModelIds` をメモリコピーしただけでは、ゲームエンジンのスケルトンやテクスチャはリロードされず、自キャラの外見のままになるか描画不正になる。
   - **HDM (https://github.com/Enceladeum/HDM) の解法**:
     - Glamourer の `GetState` でパペットの JObject を取得。
     - NPC の 26バイト CustomizeData を `CustomizeMap` に従ってマッピングし、`Apply = true` を設定。
     - NPC の装備（Head, Body, Hands, Legs, Feet 等）を `CustomItemId` ビット演算を用いてマッピングし、`Apply = true` を設定。
     - `Parameters` および `Materials` ブロックを削除し、自キャラの肌色・シェーダーパラメータが NPC に混入するのを防止。
     - `Glamourer.ApplyState` を呼ぶことで、Glamourer にスケルトン再構築と装備適用を実行させる。

---

## 2. 変更内容

### A. `Services/GlamourerIpc.cs`
- `GetState` IPC Subscriber (`Glamourer.GetState`, `int objectIndex, uint key -> (int ec, JObject? state)`) の追加。
- `ApplyNpcAppearance(int actorIndex, byte[] customizeData, ulong[]? equipmentModelIds, bool showHeadgear)` の追加。
  - HDM の `HumanGuise.cs` と同様に `CustomizeMap` (36項目) による 26バイト CustomizeData の展開。
  - `CustomItemId(ushort model, byte variant, ulong equipType)` による装備 ID 生成。
  - `Parameters` / `Materials` の除去。
  - `ApplyState(state, actorIndex, 0, 7UL)` で適用。

### B. `Managers/ActorManager.cs`
- スポーン時は常に人間ベースライン（`ModelCharaId = 0`, `Scale = 1.0f`）として生成。
- `readyJobs` による 2フェーズ監視：
  - Phase 1: `IsReadyToDraw()` -> `EnableDraw()`。
  - Phase 2: `DrawObject != null && DrawObject->IsVisible`。
- Phase 2 確定時：
  - **モンスター（`template.ModelCharaId > 0`）の場合**:
    - `chara->ModelContainer.ModelCharaId = (int)template.ModelCharaId;`
    - `chara->GameObject.Scale = template.Scale > 0 ? template.Scale : 1.0f;`
    - `chara->GameObject.DisableDraw();`
    - `monsterRedrawJobs` に追加（2フレーム待機後、`IsReadyToDraw()` を待って `EnableDraw()`）。
  - **人型 NPC（`template.SourceType == Npc` かつ `ModelCharaId == 0`）の場合**:
    - `glamourerIpc.ApplyNpcAppearance` を実行。
    - スケルトン確定のため、`DisableDraw()` -> `IsReadyToDraw()` -> `EnableDraw()`（または Penumbra Redraw）を実行。

### C. バージョン管理・同期
- `package.json` -> `0.1.22`
- `CharacterSpawn.json`, `CharacterSpawn.csproj`, `repo.json` -> `0.1.22.0`
- `CHANGELOG.md` 追記、`docs` 同期、コミット & プッシュ、ビルド成果物を全バージョンフォルダに配置。

---

## 3. 検証計画
- ビルドが正常に通ること（GitHub Actions）。
- モンスター（例: サボテンダー、ナマズオ等）をスポーンさせた際に 3D モデルが即座に正常表示されること。
- NPC（例: ミューヌ、ヤ・シュトラ等）をスポーンさせた際に 容姿・装備が正常に反映されること。
- ギズモによる移動・回転が引き続き滑らかに動作すること。
