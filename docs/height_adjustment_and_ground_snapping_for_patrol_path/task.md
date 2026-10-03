# タスク: 巡回・移動時の階段・段差自動追従および地面スナップ機能の実装

## 概要
巡回（Patrol）および追従（Follow）移動時、階段や段差がある場所でアクターが地面にめり込んだり浮遊したりせず、階段の各ステップ面を一歩一歩確実に上り下りできるようにする。
ゲームエンジンの地形衝突判定（`BGCollisionModule.RaycastMaterialFilter`）を活用した毎フレームのリアルタイム地面レイキャストを導入し、階段の始点と終点の地点を登録するだけで自動的に階段の段差に追従する安全かつ高精度な移動システムを構築する。

## タスクリスト
- [x] 1. ゲームエンジン地形衝突レイキャスト（BGCollision）による地面高検出の実装 (`Services/MovementService.cs`) <!-- id: 1 -->
  - [x] `TryGetGroundHeight(Vector3 pos, out float groundY)` の新設 <!-- id: 1.1 -->
  - [x] 上空オフセット（+2.5m）から真下へのレイキャストによる階段・段差・床面の上面Y座標検出 <!-- id: 1.2 -->
- [x] 2. 移動補間ロジック（`StepTowardTarget`）の階段・段差追従改修 (`Services/MovementService.cs`) <!-- id: 2 -->
  - [x] 階段ステップ（0.45m以内）に対する即時サーフェススナップ（めり込み・浮遊の完全防止） <!-- id: 2.1 -->
  - [x] 大きな落差・昇降に対する滑らかな垂直速度補間（最大 10.0m/s） <!-- id: 2.2 -->
  - [x] 停止時・ウェイポイント待機時の地面接地安定化 <!-- id: 2.3 -->
  - [x] プレイヤー追従時（FollowingPlayer）の階段・段差昇降への適用 <!-- id: 2.4 -->
- [x] 3. ウェイポイント追加・配置時の地面スナップ適用 (`UI/SceneEditWindow.cs`) <!-- id: 3 -->
  - [x] 自キャラ・アクター位置追加時の地面高さ自動吸着 <!-- id: 3.1 -->
- [x] 4. 検証・ビルド・リリース <!-- id: 4 -->
  - [x] バージョン更新 (`tools/bump-version.ps1 0.1.88.0`) <!-- id: 4.1 -->
  - [x] `CHANGELOG.md` 更新 <!-- id: 4.2 -->
  - [x] `docs/height_adjustment_and_ground_snapping_for_patrol_path/` にドキュメント同期 <!-- id: 4.3 -->
  - [x] コミット & プッシュ & GitHub Actions CI/CD ビルド完了確認 <!-- id: 4.4 -->
