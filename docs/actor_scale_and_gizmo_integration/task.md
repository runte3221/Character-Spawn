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
  - [x] `tools/release.ps1 0.1.65.0` での自動リリース、CI/CD 成功確認
  - [x] `walkthrough.md` の確定・Git 同期

- [x] **7. リアルタイムスケール＆ギズモ隔離＆Default Scale機能拡張 (`v0.1.66.0`)**
  - [x] `Managers/ActorManager.cs`: `UpdateActorTransform` で `chara->GameObject.DrawObject->Object.Scale = new Vector3(targetScale)` を設定し、DirectX 描画ジオメトリをリアルタイム更新
  - [x] `Managers/SceneManager.cs`: `AddPlacement` 時に `template.Scale` を `placement.Scale` に引き継ぎ、モンスター固有サイズを自動継承
  - [x] `UI/GizmoRenderer.cs`: `CurrentGizmoMode == GizmoMode.Scale` の時のみ Scale を通知し、移動・回転ギズモによるスケール誤上書きを完全隔離
  - [x] `Plugin.cs` & `UI/StageSceneTab.cs`: `float? newScale` が非 null の時のみスケールを更新・保存するガードを配線
  - [x] `UI/SceneEditWindow.cs`: [Apply Own Transform] の隣に [Default Scale] ボタンを追加し、テンプレート固有のサイズにワンクリック復元
  - [x] ドキュメント更新、CHANGELOG.md 追記、`tools/release.ps1 0.1.66.0` でのリリース
