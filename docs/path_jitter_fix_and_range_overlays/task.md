# Task: ルート描画ジッター解消 ＆ 距離・角度範囲 3D 可視化オーバーレイ実装

## 目的
1. 巡回ルートのパスラインおよび地点ピン番号（①など）が小刻みに震える（ジッター）現象を根本解決する。
2. `Trigger Dist`（接近検知距離）、`Stop Dist`（停止距離）、Animation タブの `Distance`（LookAt有効距離）、`Body Turn`（体回転許容角度）をゲーム内の 3D 空間上に直感的に可視化する。
3. ユーザーが可視化を非表示・表示切り替えできるチェックボックス（マスター一括切替および機能別個別切替）を提供する。

---

## タスクリスト

- [x] **1. 設定プロパティの拡張 (`Configuration.cs`)**
  - [x] マスター一括表示トグル: `ShowVisualOverlays` (bool, デフォルト: true)
  - [x] 巡回ルート表示トグル: `ShowWaypointPath` (bool, デフォルト: true)
  - [x] 移動追従範囲表示トグル: `ShowMovementRanges` (bool, デフォルト: true)
  - [x] 視線・体回転範囲表示トグル: `ShowAnimationRanges` (bool, デフォルト: true)

- [x] **2. ジッター根本解消 ＆ 3D 範囲オーバーレイ描画刷新 (`UI/GizmoRenderer.cs`)**
  - [x] ウィンドウローカル DrawList (`GetWindowDrawList`) からメインビューポート直結の最前面 DrawList (`GetForegroundDrawList(MainViewport)`) に描画先を移行し、ウィンドウの `NoInputs` トグルやレイアウト再計算に伴うピクセルジッターを根絶
  - [x] ゲームカメラの最新 ViewProjection 行列（`ViewMatrix * ProjectionMatrix`）から直接スクリーン投影を行う `ProjectWorldToScreen` を実装
  - [x] アクター足元を中心とする水平リング描画ルーチン `DrawHorizontalCircle` を新設
  - [x] 正面を中心とする扇形（アーク＋半透明塗りつぶし＋中央正面ライン）描画ルーチン `DrawBodyTurnArc` を新設
  - [x] 距離・角度ラベル描画ルーチン `DrawRangeLabel`, `DrawLabel` を新設
  - [x] `Stop Dist`（ライムグリーン円）、`Trigger Dist`（水色円）、`LookAt Dist`（オレンジ円）、`Body Turn`（黄色扇形）の統合レンダリング `RenderRangeOverlays` を実装
  - [x] ImGuizmo 操作ウィンドウと 3D オーバーレイの分離設計（ギズモが必要な時のみウィンドウを開き、描画バッファ干渉を完全排除）

- [x] **3. ギズモ・オーバーレイ連携の拡張 (`Plugin.cs`)**
  - [x] `gizmoRenderer.Render` に選択中アクターの配置データ `curPlacement` を渡す
  - [x] ギズモが Select モード（非表示）時であっても、オーバーレイ有効時にパスや範囲円を描画できるよう条件分岐を最適化

- [x] **4. UI 表示切り替えチェックボックスの実装 (`UI/SceneEditWindow.cs`)**
  - [x] アクター一覧リスト直下に `[x] 3D Overlays`（マスター一括切り替え）を配置
  - [x] Movement タブの巡回設定横に `[x] Route Overlay (3D可視化)` を配置
  - [x] Movement タブの追従設定横に `[x] Range Overlay (3D可視化)` を配置
  - [x] Animation タブの LookAt パラメータ直前に `[x] Show LookAt & Body Turn Overlay (3D可視化)` を配置

- [x] **5. バージョン更新・ドキュメント同期・リリース・CI/CD 完了確認**
  - [x] `tools/bump-version.ps1 0.1.84.0` 実行
  - [x] `CHANGELOG.md` 追記
  - [x] `docs/path_jitter_fix_and_range_overlays` の `task.md`, `implementation_plan.md`, `walkthrough.md` 同期
  - [x] Git コミット ＆ プッシュ
  - [x] GitHub Actions CI/CD ビルド完了確認
