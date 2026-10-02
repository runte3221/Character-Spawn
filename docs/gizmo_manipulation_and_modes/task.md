# タスクリスト: ImGuizmo導入・平面移動Quad・移動/回転モード分離 (v0.1.18 - v0.1.19)

## v0.1.19 修正タスク
- [x] ギズモが表示されない原因の特定と修正
  - `Camera->ViewMatrix` および `RenderCamera->ProjectionMatrix` のリバースZ補正（Stagehand / BDTH 準拠）
  - `ImGuizmo.RecomposeMatrixFromComponents` / `DecomposeMatrixToComponents` への移行
- [x] ゲーム画面のカメラ視点移動ができなくなる問題の解消
  - 全画面オーバーレイウィンドウに `ImGuiWindowFlags.NoInputs` を付与し、ゲームマウス入力を阻害しないよう修正
- [x] ギズモON/OFFチェックボックスの廃止とセレクトボタンへの一本化
  - `CharacterLibraryTab.cs` の Gizmo チェックボックスを廃止
  - `StageSceneTab.cs` の Gizmo チェックボックスを廃止
  - `MainWindow.cs` の Settings タブの Gizmo チェックボックスを廃止
  - セレクトボタン（矢印）がギズモOFF、移動・回転ボタンがギズモONとして連動
- [x] バージョン更新 (0.1.19 / 0.1.19.0)
  - `package.json`, `CharacterSpawn.json`, `CharacterSpawn.csproj`, `repo.json`
  - `CHANGELOG.md` 追記
- [ ] Git Commit & Push
- [ ] GitHub Actions ビルド待機 & XIVLauncher 全バージョンフォルダへの最新 DLL 配置
- [ ] 完了報告
