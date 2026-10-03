# 実装計画書: アクタースケール（Scale）拡大縮小＆3Dギズモ連携実装

## 1. 目的とスコープ
本計画は、Character Spawn においてスポーンさせたキャラクター・NPC・モンスター・MCDF 等の全モデルに対し、**任意の大きさ（Scale: 0.01x 〜 10.0x）をリアルタイムに適用可能にする機能** を実装する。
また、UI 上の数値スライダー（`DragFloat`）だけでなく、3D ギズモ（`GizmoMode.Scale`）をドラッグして直感的にサイズ変更できるように完全配線を行う。

---

## 2. アーキテクチャと安全設計

### 2.1 ゲームエンジン（FFXIV）への反映パイプライン
1. **`GameObject.Scale` フィールドへの適用**:
   - `FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject` のネイティブ `float Scale` に目標スケール値を代入。
   - ゲームエンジン自身の毎フレームの描画更新（`CharacterBase.Update`）によって、キャラクター本体のベーススケールとして機能する。
2. **`DrawObject->NotifyTransformChanged()` の呼び出し**:
   - スライダーのドラッグ中やギズモ操作中、毎フレーム即座に見た目を更新するため、`chara->GameObject.DrawObject != null` の場合に `NotifyTransformChanged()` を呼び出して DirectX の描画行列を再計算させる（Stagehand / Brio 準拠）。
3. **安全ガード**:
   - `actor.NativeAddress == 0` または `!actor.IsReady` 時の呼び出しを遮断。
   - `chara->GameObject.DrawObject == null` 時は `NotifyTransformChanged()` をスキップし、例外を防止。

### 2.2 第1工程のコア（外見、Glamourer、Penumbra、MCDF、CustomizePlus）との完全共存
- **CustomizePlus との共存**:
  - CustomizePlus は各ボーン（胸・腰・脚など）のローカルスケールを制御するが、`GameObject.Scale` はモデル全体のワールドスケールを制御する。階層が異なるため、Chonk などの巨躯・太身のボーン変形が崩れることなく、モデル全体が等比で拡大・縮小される。
- **Glamourer / Penumbra との共存**:
  - スケール変更はメッシュやマテリアル、テクスチャ、外見デザインには一切干渉しないため、外見抜けや打ち消しは発生しない。

---

## 3. 実装詳細

### 3.1 `Managers/ActorManager.cs`
- `UpdateActorTransform(SpawnedActorData actor, Vector3 newPosition, float newRotation, float? newScale = null)`
  - `newScale` が指定された場合、`actor.Transform.Scale` を更新。
  - `chara->GameObject.Scale = targetScale;` を設定。
  - `if (chara->GameObject.DrawObject != null) chara->GameObject.DrawObject->NotifyTransformChanged();`
- `SpawnCharacter(CharacterTemplate template, Vector3 pos, float rot, float? initialScale = null)`
  - 初期スポーン時、モンスターだけでなく全アクターに対して `initialScale` または `template.Scale` を `chara->GameObject.Scale` に初期設定。

### 3.2 `Managers/SceneManager.cs`
- `SpawnPlacementInternal`:
  - `actorManager.SpawnCharacter(template, placement.Position, placement.Rotation, placement.Scale)` を呼び出し、初期スケールを確実に渡す。

### 3.3 `UI/GizmoRenderer.cs`
- `Render(SpawnedActorData? selectedActor, Action<Vector3, float, float> onTransformChanged)`
- `Matrix4x4.Decompose` から取得した `newScale`（Vector3）から均等スケール値 `float uniformScale = (newScale.X + newScale.Y + newScale.Z) / 3.0f;` を導出。
- `onTransformChanged(newPos, newYawRad, uniformScale)` を発火。

### 3.4 `Plugin.cs`
- ギズモ描画ループで `(newPos, newRot, newScale)` を受け取り：
  - `actorManager.UpdateActorTransform(targetActor, newPos, newRot, newScale);`
  - `stageTab.SyncPlacementTransformFromGizmo(newPos, newRot, newScale);`

### 3.5 `UI/StageSceneTab.cs`
- `SyncPlacementTransformFromGizmo(Vector3 newPos, float newRot, float newScale)`
  - `placement.Scale = newScale;` を設定し、`sceneManager.SaveScenes();`

### 3.6 `UI/SceneEditWindow.cs`
- `ImGui.DragFloat("##Scale", ref scale, ...)` 変更時：
  - `actorManager.UpdateActorTransform(spawned, placement.Position, rotRad, scale);` を呼び出し。

### 3.7 `UI/MainWindow.cs`
- ギズモモード選択ラジオボタンに `Scale` を追加。

---

## 4. 検証項目
1. **UI スライダー検証**: SceneEditWindow の Scale スライダーをドラッグして、モデルがリアルタイムに伸縮するか。
2. **3D ギズモ検証**: ギズモを Scale モード（赤アイコン）に切り替え、3D ギズモをドラッグしてモデルが直感的に拡大縮小するか。
3. **シーン保存・復元検証**: スケールを変更した状態で [Hide] -> [Show] を行い、指定したスケールで再スポーンされるか。
4. **他パイプライン検証**: Chonk、MCDF、NPC、モンスターそれぞれでスケール変更が正常に機能し、外見や体型が崩れないか。

---

## 5. リアルタイム化・モンスター初期サイズ継承・ギズモ隔離・Default Scaleボタン追加 (v0.1.66)

### 5.1 課題とアーキテクチャ改訂
1. **リアルタイム拡大縮小（Hide/Show不要化）**:
   - `GameObject.Scale` はスポーン初期化時にのみゲームエンジンが読み込むため、稼働中アクターには DirectX 描画オブジェクトの内部 `chara->GameObject.DrawObject->Object.Scale = new Vector3(targetScale)` を直接更新し、`NotifyTransformChanged()` を呼び出してリアルタイムに反映。
2. **モンスター固有サイズの自動継承**:
   - `SceneManager.AddPlacement` で `Scale = template.Scale > 0.001f ? template.Scale : 1.0f` を初期代入し、統制者ハシュマルト等の巨大モンスターをシーン配置した際にデフォルトサイズが維持されるように修正。
3. **ギズモ操作モードの完全隔離**:
   - `GizmoRenderer` からの通知を `float? newScale` に変更。`CurrentGizmoMode == GizmoMode.Scale` の時のみスケール値を渡し、移動（Translate）や回転（Rotate）の操作中は `null` を渡して既存スケール値を一切変更しない安全ガードを構築。
4. **[Default Scale] リセットボタン**:
   - `SceneEditWindow` の [Apply Own Transform] の隣に `[Default Scale]` ボタンを配置。ワンクリックでテンプレート固有のサイズ（モンスター原寸や人型 1.0）へ瞬時に復元・保存。

---

## 6. 人型アクターのフレーム更新によるスケールリセット防止＆常時維持 (v0.1.67)

### 6.1 課題と根本原因
- **現象**: モンスターやデミヒューマンはサイズ変更がリアルタイムに維持されるが、人型アクター（NPC ユウギリや Chonk、プレイヤーキャラクローン等）はスライダーを動かしても変化しない、あるいは一瞬変化して即座に 1.0 に戻る。
- **根本原因**: FFXIV ゲームエンジンの `Character::Update` / `Human::Update`（`ModelCharaId == 0` 専用の毎フレーム Tick）が、種族基準値および身長スライダーからボーン座標と描画スケールを毎フレーム再計算し、`DrawObject->Object.Scale` を `Vector3.One`（1.0f）に強制リセットしている。モンスター（`ModelCharaId > 0`）にはこの機構がないため維持されていた。

### 6.2 実装方針 (Scale Enforcement)
- `ActorManager.UpdateFrame` 内のアクティブアクター走査ループで、全スポーン済みアクターの `actor.Transform.Scale` を監視。
- `DrawObject->Object.Scale` または `chara->GameObject.Scale` が `targetScale` と不一致（エンジンに 1.0f に書き戻された場合）であることを検知した場合にのみ、`chara->GameObject.Scale = targetScale` および `chara->GameObject.DrawObject->Object.Scale = new Vector3(targetScale)` を即座に再適用し、`NotifyTransformChanged()` を発火。
- 不一致時のみ処理が走るため CPU 負荷はゼロ。人型モデルに対してもゲームエンジンの毎フレームリセットに打ち勝ち、安定したサイズ維持を実現。


