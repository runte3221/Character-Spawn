# タスクリスト: ImGuizmo導入・平面移動Quad・移動/回転モード分離 (v0.1.18)

## ステータス概要
- [x] ドキュメント作成 (`task.md`, `implementation_plan.md`, `walkthrough.md`)
- [x] Stagehand 準拠の `ImGuizmo` アーキテクチャ調査（`FFXIVClientStructs` カメラ行列 + `Dalamud.Bindings.ImGuizmo`）
- [x] `Configuration.cs` にギズモモード設定（`GizmoMode`: Translate / Rotate）を追加
- [x] `UI/GizmoRenderer.cs` の完全刷新
  - `Dalamud.Bindings.ImGuizmo` の採用
  - ゲーム内カメラ（`CameraManager.Instance()->CurrentCamera`）の View/Projection 行列連携
  - フルスクリーン透明オーバーレイウィンドウによる確実なマウス入力キャプチャ
  - 移動モード（各軸矢印 + XY/XZ/YZ 平面四角形ハンドル）
  - 回転モード（3軸およびスクリーン空間回転リング）
  - マトリクス分解（`Matrix4x4.Decompose`）によるリアルタイム位置・Yaw回転の更新
- [x] UI に Stagehand スタイルのモード切り替えメニュー/ボタンを追加 (`MainWindow.cs` / `StageSceneTab.cs` / `CharacterLibraryTab.cs`)
- [x] バージョン更新 (0.1.18 / 0.1.18.0)
  - `package.json`, `CharacterSpawn.json`, `CharacterSpawn.csproj`, `repo.json`
  - `CHANGELOG.md` 追記
- [x] Git Commit & Push
- [x] GitHub Actions ビルド待機 & XIVLauncher 全バージョンフォルダへの最新 DLL 配置
- [x] 完了報告
