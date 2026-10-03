# タスク: 追従停止時の上下ジッター解消および全モデル種別のクリック選択対応

## 概要
1. 階段や段差の境界で追従アクターが激しく上下に振動・ジッターする不具合を解消する。
2. モンスター、デミヒューマン、マウント、ミニオンなどの非人型モデルが 3D 空間クリックおよびゲーム内ターゲットで選択できない問題を根本修正する。

## タスクリスト
- [x] 1. 追従停止時の上下ジッター解消 (`Services/MovementService.cs`) <!-- id: 1 -->
  - [x] 停止処理における競合コード（`yDiffAtStop` によるプレイヤーY座標への引っ張り）を削除し、BGCollision 地面レイキャスト（`TryGetGroundHeight`）に一元化 <!-- id: 1.1 -->
- [x] 2. モンスター・デミヒューマン・マウント・ミニオンのターゲット可否常時維持 (`Managers/ActorManager.cs`) <!-- id: 2 -->
  - [x] `MonsterRedrawJob` 完了時に `TargetableStatus |= IsTargetable` を再設定 <!-- id: 2.1 -->
  - [x] `EnforceActorDrawState`（毎フレーム実行）で全アクティブアクターの `TargetableStatus` を常時維持 <!-- id: 2.2 -->
- [x] 3. 3Dモデル直接クリック判定の全モデル種別対応強化 (`UI/GizmoRenderer.cs`, `Plugin.cs`) <!-- id: 3 -->
  - [x] `IObjectTable` を活用し、各アクターの現在実座標と `HitboxRadius`（当たり判定半径）を取得 <!-- id: 3.1 -->
  - [x] カメラの右方向ベクトルを用いた 3D モデル幅の正確なスクリーン空間投影 <!-- id: 3.2 -->
  - [x] ミニオン、マウント、モンスター、人型に応じた適切な高さ算出と最小クリック領域保証 <!-- id: 3.3 -->
- [x] 4. 検証・ビルド・リリース <!-- id: 4 -->
  - [x] バージョン更新 (`tools/bump-version.ps1 0.1.90.0`) <!-- id: 4.1 -->
  - [x] `CHANGELOG.md` 更新 <!-- id: 4.2 -->
  - [x] `docs/fix_jitter_and_non_human_selection/walkthrough.md` 作成 <!-- id: 4.3 -->
  - [x] コミット & プッシュ & GitHub Actions CI/CD ビルド完了確認 <!-- id: 4.4 -->
