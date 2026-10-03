# タスク: ネームプレート表示制御（Edit表示中のみ全員表示・通常時はカスタムネーム有効時のみ表示）

## 概要
カスタムスポーンの頭上ネームプレートについて、以下の表示ルールを実装する：
1. **Edit ウィンドウ（SceneEditWindow）を開いているとき**:
   - どのアクターか識別できるように、全カスタムスポーンの名前を表示する。
2. **Edit ウィンドウを開いていないとき（通常プレイ・鑑賞時）**:
   - 各アクターの設定で「カスタムネームを表示」（`[x] Custom Name`）にしているスポーンだけ名前を表示し、それ以外のスポーンは名前を非表示にする。

## タスクリスト
- [x] 1. モデル定義およびデフォルト値の更新 (`Models/CharacterModels.cs`, `Models/SceneData.cs`) <!-- id: 1 -->
  - [x] `SpawnedActorData` に `PlacementId` を追加 <!-- id: 1.1 -->
  - [x] `SceneActorNamePlateConfig.ShowCustomName` の新規デフォルト値を `false`（非表示）に変更 <!-- id: 1.2 -->
- [x] 2. SceneManager による配置データ逆引き・初期同期 (`Managers/SceneManager.cs`) <!-- id: 2 -->
  - [x] `SpawnPlacementInternal` で `spawned.PlacementId` を紐付け <!-- id: 2.1 -->
  - [x] `GetPlacementForActor(SpawnedActorData actor)` を新設 <!-- id: 2.2 -->
- [x] 3. SceneEditWindow UI 操作時の即時同期 (`UI/SceneEditWindow.cs`) <!-- id: 3 -->
  - [x] `ShowCustomName` チェック変更時に `spawned.NamePlate.Show` へ即時反映 <!-- id: 3.1 -->
- [x] 4. NamePlateController の表示ルール実装 (`Managers/NamePlateController.cs`, `Plugin.cs`) <!-- id: 4 -->
  - [x] `isEditOpen` に応じた条件分岐（Edit中は全アクター表示、通常時は `ShowCustomName` 有効時のみ表示、それ以外は `RemoveName()`） <!-- id: 4.1 -->
  - [x] `Plugin.cs` で `sceneEditWindow.IsOpen` と `GetPlacementForActor` を注入 <!-- id: 4.2 -->
- [x] 5. 検証・ビルド・リリース <!-- id: 5 -->
  - [x] バージョン更新 (`tools/bump-version.ps1 0.1.91.0`) <!-- id: 5.1 -->
  - [x] `CHANGELOG.md` 更新 <!-- id: 5.2 -->
  - [x] `docs/nameplate_visibility_rules/walkthrough.md` 作成 <!-- id: 5.3 -->
  - [x] コミット & プッシュ & GitHub Actions CI/CD ビルド完了確認 <!-- id: 5.4 -->
