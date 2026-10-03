# 実装計画: 3Dモデル直接クリックによるスポーンアクター選択機能の実装

## 背景と問題の所在
1. **現状の課題**:
   - スポーンするアクターの数が増えてくると、編集対象を変更するたびに UI の「カスタムスポーン一覧」から対象の名前を探してクリックする必要があり、作業効率が著しく低下していた。
   - 画面上のアクターを見て直感的に「このキャラを動かしたい」と思った際に、モデルをクリックして直接選択できる機能が求められていた。
2. **解決のアプローチ**:
   - **アプローチ 1 (3D 空間モデル直接クリック)**:
     - `GizmoRenderer.cs` において、毎フレームの描画時にゲームカメラの ViewProjection 行列を用いて全スポーン中アクターのワールド座標をスクリーン空間に投影。
     - マウスクリック位置（`ImGui.GetMousePos()`）とアクターの投影矩形（足元から頭上、体幅）の内外判定を実施。
     - 複数のアクターが重なっている場合はカメラから最も近い（手前にある）アクターを選択。
     - 操作中ガード: ImGui ウィンドウのボタン・スライダー等の操作中や、ImGuizmo 矢印ドラッグ中はクリック判定を除外。
   - **アプローチ 2 (ゲーム内ターゲット連動)**:
     - アクターの `TargetableStatus` をデフォルトで有効化。
     - ゲーム内で通常通り左クリック（または Tab キー）でアクターをターゲットした際、`ITargetManager.Target` から該当する `SceneActorPlacement` を検出し、UI 側の選択状態を自動同期。

## 変更計画

### 1. `Managers/ActorManager.cs`
- `SpawnCharacter` において、`nativeChara->GameObject.TargetableStatus |= ObjectTargetableFlags.IsTargetable` および `spawned.IsTargetable = true` を設定。

### 2. `UI/GizmoRenderer.cs`
- `CheckActorClickSelection` を追加:
  - カメラのワールド座標 `invView.Translation` を算出。
  - 各アクターのスクリーン投影矩形とマウス位置のヒットテスト。
  - 最前面のアクターを検出してコールバック `onSelect(placement)` を呼出。
  - ホバー中のアクター足元にハイライトサークルを描画。

### 3. `Plugin.cs`
- `DrawUI`: 全スポーン中アクターの一覧を `gizmoRenderer.CheckActorClickSelection` に渡し、クリック時に `sceneManager.SelectedPlacement` を更新。
- `OnFrameworkUpdate`: `TargetManager.Target` がスポーンされたアクターの場合、`sceneManager.SelectedPlacement` を自動同期。

### 4. バージョン更新・ドキュメント同期・CI/CD
- `tools/bump-version.ps1 0.1.89.0`
- `CHANGELOG.md` 追記
- `docs/click_to_select_actor_in_scene/walkthrough.md` 作成
- コミット＆プッシュ、GitHub Actions CI/CD ビルド完了確認
