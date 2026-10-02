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
## v0.1.20 修正タスク
- [x] 水平回転（Yaw）専用リング `RotateY` への切り替え（キャラクター向き変更の確実化）
- [x] ギズモ操作中のクリック透過による「New NPC / Failed to get response.」ダイアログの防止（動的 NoInputs 制御 & TargetableStatus=0, EventId=0 徹底）
- [x] メインウィンドウ上部左側の余計なギズモボタンの削除
- [x] バージョン更新 (0.1.20 / 0.1.20.0)
  - `package.json`, `CharacterSpawn.json`, `CharacterSpawn.csproj`, `repo.json`
  - `CHANGELOG.md` 追記
## v0.1.21 修正タスク
- [x] 180度付近での回転詰まり・ジッター解消（Quaternion + Forward Vector Atan2 による 360 度連続回転）
- [x] バージョン更新 (0.1.21 / 0.1.21.0)
  - `package.json`, `CharacterSpawn.json`, `CharacterSpawn.csproj`, `repo.json`
  - `CHANGELOG.md` 追記
- [x] Git Commit & Push
- [x] GitHub Actions ビルド待機 & XIVLauncher 全バージョンフォルダへの最新 DLL 配置
- [x] 完了報告
