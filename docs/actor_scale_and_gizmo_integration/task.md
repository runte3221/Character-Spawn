# タスクリスト: アクタースケール（Scale）拡大縮小＆3Dギズモ連携実装

## 作業タスク

- [x] **1. ドキュメント整備とアーキテクチャ設計**
  - [x] `docs/actor_scale_and_gizmo_integration/task.md` 作成
  - [x] `docs/actor_scale_and_gizmo_integration/implementation_plan.md` 作成
  - [x] `docs/actor_scale_and_gizmo_integration/walkthrough.md` 作成

- [x] **2. コア・ランタイム制御拡張 (`Managers/ActorManager.cs`)**
  - [x] `UpdateActorTransform` のシグネチャを `(SpawnedActorData actor, Vector3 newPosition, float newRotation, float? newScale = null)` に拡張
  - [x] `chara->GameObject.Scale = targetScale;` を設定し、`chara->GameObject.DrawObject != null` の場合に `DrawObject->NotifyTransformChanged()` を呼び出して即時描画更新
  - [x] `SpawnCharacter` の引数に `float? initialScale = null` を受け取れるように拡張し、人型アクター（Glamourer / MCDF / NPC）の初期スポーン時にも `placement.Scale` または `template.Scale` を確実に反映

- [x] **3. 3Dギズモ Scale コールバック連携 (`UI/GizmoRenderer.cs`)**
  - [x] コールバックデリゲートを `Action<Vector3, float, float>`（Position, Rotation, Scale）に拡張
  - [x] `Matrix4x4.Decompose` から取得した `newScale` から均等スケール値を導出し、コールバックを発火

- [x] **4. メインループ＆配置同期 (`Plugin.cs` & `UI/StageSceneTab.cs`)**
  - [x] `Plugin.cs` のギズモ描画ループで `(newPos, newRot, newScale)` を受け取り、`actorManager.UpdateActorTransform` と `stageTab.SyncPlacementTransformFromGizmo` に渡す
  - [x] `StageSceneTab.SyncPlacementTransformFromGizmo(Vector3 newPos, float newRot, float newScale)` に拡張し、Scale の変更をシーン設定ファイルへ自動保存

- [x] **5. UI スライダー連携 (`UI/SceneEditWindow.cs` & `UI/MainWindow.cs`)**
  - [x] `SceneEditWindow.cs` の `DragFloat("##Scale", ...)` で値が変更された際、`actorManager.UpdateActorTransform(spawned, placement.Position, rotRad, scale)` を呼び出し、スライダー操作で即座にモデルサイズが変化するように配線
  - [x] `MainWindow.cs` のギズモモード RadioButton に `Scale (Resize)` を追加

- [x] **6. 検証とリリース (`v0.1.65.0`)**
  - [x] 各パイプライン（MCDF、NPC、モンスター、Chonk）への非干渉確認
  - [ ] `tools/release.ps1 0.1.65.0` での自動リリース、CI/CD 成功確認
  - [x] `walkthrough.md` の確定・Git 同期
