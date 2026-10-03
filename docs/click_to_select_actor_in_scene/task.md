# タスク: 3Dモデル直接クリックによるスポーンアクター選択機能の実装

## 概要
スポーン数が増加した際に、カスタムスポーン一覧から対象を探して選択する負担を解消するため、3D 空間上のモデルを直接クリックすることで、スポーン一覧（および編集対象）が即座に選択状態になる機能を実現する。
ImGui スクリーン空間での高精度レイ／ボックス判定と、ゲームエンジンネイティブのターゲット（`ITargetManager`）連動の 2 つを併用し、直感的でシームレスな選択 UX を提供する。

## タスクリスト
- [x] 1. アクターのターゲット可否デフォルト有効化 (`Managers/ActorManager.cs`) <!-- id: 1 -->
  - [x] `SpawnCharacter` で `TargetableStatus = ObjectTargetableFlags.IsTargetable` および `IsTargetable = true` を設定 <!-- id: 1.1 -->
- [x] 2. 3Dモデル直接クリック判定の実装 (`UI/GizmoRenderer.cs`) <!-- id: 2 -->
  - [x] `CheckActorClickSelection` を新設 <!-- id: 2.1 -->
  - [x] スクリーン座標変換、カメラ距離判定（最前面優先）、ImGui/ImGuizmo 操作除外ガード <!-- id: 2.2 -->
  - [x] ホバー時のインジケーター（視覚的フィードバック）描画 <!-- id: 2.3 -->
- [x] 3. ゲーム内ターゲット連動およびクリック呼び出しの統合 (`Plugin.cs`, `Managers/SceneManager.cs`) <!-- id: 3 -->
  - [x] `DrawUI` で `CheckActorClickSelection` を呼び出し、クリック時に `sceneManager.SelectedPlacement` を更新 <!-- id: 3.1 -->
  - [x] `OnFrameworkUpdate` で `TargetManager.Target` と `sceneManager.SelectedPlacement` を自動同期 <!-- id: 3.2 -->
- [x] 4. 検証・ビルド・リリース <!-- id: 4 -->
  - [x] バージョン更新 (`tools/bump-version.ps1 0.1.89.0`) <!-- id: 4.1 -->
  - [x] `CHANGELOG.md` 更新 <!-- id: 4.2 -->
  - [x] `docs/click_to_select_actor_in_scene/` にドキュメント同期 <!-- id: 4.3 -->
  - [x] コミット & プッシュ & GitHub Actions CI/CD ビルド完了確認 <!-- id: 4.4 -->
