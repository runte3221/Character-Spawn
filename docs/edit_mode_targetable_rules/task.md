# タスク: Edit非表示時のターゲット可否制御（カスタムネーム表示中のみターゲット可能化）

## 概要
Edit ウィンドウ（SceneEditWindow）を開いていない通常プレイ・鑑賞時において、カスタムネームを表示している（`[x] Custom Name`）アクター以外はゲーム内でターゲットできない（`TargetableStatus = 0`）ようにする。
Edit ウィンドウを開いている間は、編集作業を円滑に行うため全アクターをターゲット可能とする。

## タスクリスト
- [x] 1. ActorManager にターゲット可否ポリシー設定機構を追加 (`Managers/ActorManager.cs`) <!-- id: 1 -->
  - [x] `SetTargetablePolicy(Func<bool> isEditOpen, Func<SpawnedActorData, bool> isCustomNameShown)` を追加 <!-- id: 1.1 -->
  - [x] `EnforceActorDrawState`（`UpdateFrame`）で Edit 開閉状態およびカスタムネーム表示有無に応じた `TargetableStatus` の動的切り替え・維持を実装 <!-- id: 1.2 -->
- [x] 2. Plugin でのターゲット可否ポリシーの注入 (`Plugin.cs`) <!-- id: 2 -->
  - [x] `sceneEditWindow.IsOpen` と `sceneManager.GetPlacementForActor` を用いてポリシーを設定 <!-- id: 2.1 -->
- [x] 3. 検証・ビルド・リリース <!-- id: 3 -->
  - [x] バージョン更新 (`tools/bump-version.ps1 0.1.92.0`) <!-- id: 3.1 -->
  - [x] `CHANGELOG.md` 更新 <!-- id: 3.2 -->
  - [x] `docs/edit_mode_targetable_rules/walkthrough.md` 作成 <!-- id: 3.3 -->
  - [x] コミット & プッシュ & GitHub Actions CI/CD ビルド完了確認 <!-- id: 3.4 -->
