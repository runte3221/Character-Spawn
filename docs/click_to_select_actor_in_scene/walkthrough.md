# 修正内容の確認 (Walkthrough): 3Dモデル直接クリックによるスポーンアクター選択機能

## 実装の概要
スポーン数が増加した際に、カスタムスポーン一覧から対象を探してクリックする作業負担を解消するため、3D 空間上のモデルを直接クリックすることで、スポーン一覧（および編集対象）が即座に選択状態になる機能を実装しました。
ImGui スクリーン空間での高精度レイ／ボックス判定と、ゲームエンジンネイティブのターゲット（`ITargetManager`）連動の 2 つを併用し、直感的でシームレスな選択 UX を提供します。

---

## 変更内容詳細

### 1. `Managers/ActorManager.cs`
- **ターゲット可否のデフォルト有効化**:
  - `SpawnCharacter` において、`nativeChara->GameObject.TargetableStatus = ObjectTargetableFlags.IsTargetable;` および `spawned.IsTargetable = true;` を設定。
  - ゲーム公式の左クリックや Tab キーによってアクターがネイティブにターゲット可能になりました。

### 2. `UI/GizmoRenderer.cs`
- **`CheckActorClickSelection` の新設**:
  - ゲームカメラの `ViewMatrix` および `ProjectionMatrix` を用いて、スポーン中の全アクターの足元座標および頭上座標（スケールに応じた身長 $1.85\text{m} \times \text{Scale}$）を高精度にスクリーン座標へ投影。
  - 各アクターのスクリーン空間バウンディングボックス（足元〜頭上、および体幅 $W = \max(H \times 0.45, 24\text{px})$）を生成。
  - マウスカーソル位置との内外判定を実施し、複数アクターが重なる場合はカメラ座標（$invView.Translation$）から最も近い（手前にある）アクターを自動優先選択。
  - **ホバーフィードバック**: アクター上にカーソルがある時はマウスカーソルを Hand アイコンにし、足元にシアン色のハイライト円（`DrawHorizontalCircle`）を描画。
  - **操作中ガード**: ImGui ウィンドウの操作中（`io.WantCaptureMouse`）や ImGuizmo の操作中（`ImGuizmo.IsUsing() || ImGuizmo.IsOver()`）はクリック判定を完全に除外。

### 3. `Managers/SceneManager.cs`
- **`GetActiveSpawnedPlacements`**: 現在スポーン中の全 `(SceneActorPlacement, SpawnedActorData)` ペアを取得するメソッドを追加。
- **`FindPlacementByGameObject`**: ゲームオブジェクト（`IGameObject`）のアドレスまたは `EntityId` から、対応する `SceneActorPlacement` を高速かつ安全に逆引きするメソッドを追加。

### 4. `Plugin.cs`
- **`DrawUI`**: MainWindow または SceneEditWindow が開いている際、`gizmoRenderer.CheckActorClickSelection` を呼び出し、モデル直接クリック時に `sceneManager.SelectedPlacement` を即座に更新。
- **`OnFrameworkUpdate`**: ゲーム画面上で通常通りターゲットを切り替えた際（クリックまたは Tab キー）、`TargetManager.Target` から該当する `SceneActorPlacement` を検知し、UI 側の選択アクターを自動同期。

---

## ユーザー操作手順・検証方法
1. プラグインを v0.1.89.0 に更新。
2. 複数のカスタムスポーン（NPC、モンスター等）を配置・スポーンする。
3. MainWindow（Scene タブ）または SceneEditWindow を開いた状態で、3D 画面上のキャラクターモデルにマウスを合わせる。
   - マウスカーソルが手のひらアイコンに変わり、足元にシアン色の円が表示されることを確認。
4. モデルを直接左クリックする。
   - スポーン一覧でそのアクターが即座に選択され、ギズモや編集タブ（Spawn/Scene/Animation/Movement）が該当アクターの内容に切り替わることを確認。
5. ゲーム内で通常通りアクターをクリック（または Tab 選択）する。
   - ゲーム内ターゲットが付くと同時に、スポーン一覧の選択も自動的にそのアクターに同期することを確認。
