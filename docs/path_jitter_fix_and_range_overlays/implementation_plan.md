# Implementation Plan: ルート描画ジッター解消 ＆ 距離・角度範囲 3D 可視化オーバーレイ実装

## 1. 背景と課題

1. **ルート描画の小刻みな揺れ（ジッター）**:
   - ウェイポイントの番号ピン（①など）や地点間を結ぶラインが静止時やカメラ操作時に小刻みに震える現象が発生していた。
   - **原因**: 従来の描画処理は透明オーバーレイウィンドウ（`##CharacterSpawnGizmoOverlay`）内のローカル DrawList（`ImGui.GetWindowDrawList()`）上で行われていた。ImGuizmo の操作状態に応じてウィンドウフラグ（`ImGuiWindowFlags.NoInputs`）が毎フレームトグルしたり、ウィンドウ内部のレイアウト再計算や座標丸めによってサブピクセル単位の微細なブレが発生していた。
   - **解決策**: メインビューポート直結の `ImGui.GetForegroundDrawList(ImGuiHelpers.MainViewport)` を描画先に指定し、ゲームカメラの最新 `ViewMatrix * ProjectionMatrix` から直接 NDC を経てスクリーン絶対座標を計算する高精度射影ルーチン `ProjectWorldToScreen` を実装する。

2. **各種距離・角度パラメータの 3D 空間可視化**:
   - `Trigger Dist`（接近検知距離）: アクター足元を中心とする水色リング。
   - `Stop Dist`（停止距離）: アクター足元を中心とする緑色リング。
   - `Distance`（LookAt有効距離）: アクター足元を中心とするオレンジ色リング。
   - `Body Turn`（体回転許容角度）: 正面方向を中心とした左右 ±AngleLimit 度の黄色扇形（アーク＋半透明塗りつぶし＋中央正面ライン）。
   - これらによって、どの距離まで近づけば追従するのか、どの範囲まで視線を向けるのかが一目瞭然になる。

3. **表示切り替えトグル（チェックボックス）の配置**:
   - 画面が乱雑にならないよう、全体マスターチェックボックス（`3D Overlays`）および機能別チェックボックス（`Route Overlay`、`Range Overlay`、`LookAt & Turn Overlay`）を UI の最適な場所に配置する。

---

## 2. 変更ファイルと実装詳細

### 2.1 `Configuration.cs`
- `ShowVisualOverlays`: 全体マスタートグル（デフォルト: `true`）
- `ShowWaypointPath`: 巡回ルートパス表示（デフォルト: `true`）
- `ShowMovementRanges`: 追従範囲（Trigger/Stop Dist）表示（デフォルト: `true`）
- `ShowAnimationRanges`: 視線・体回転範囲（LookAt Dist/Body Turn）表示（デフォルト: `true`）

### 2.2 `UI/GizmoRenderer.cs`
- **描画分離**: 3D オーバーレイの描画を ImGuizmo 操作ウィンドウから完全分離し、`ImGui.GetForegroundDrawList(viewport)` へ直接レンダリング。
- **`ProjectWorldToScreen`**: `viewMatrix * projMatrix` による数学的に完全同期したスクリーン座標算出。背後クリッピング（`clip.W <= 0.01f`）によりアーティファクトを防止。
- **`DrawHorizontalCircle`**: 水平面上の 32〜64 分割円を描画。画面内にある連続点のみをラインで結ぶことで、カメラ接近時のクリップ欠損を美しく処理。
- **`DrawBodyTurnArc`**: アクターの正面方向（`Rotation`）を中心とする扇形アーク。`0°` の時は正面矢印線、`180°` の時は全周円、中間角度の時は弧＋境界線＋半透明三角形ファンを描画。
- **`DrawRangeLabel` / `DrawLabel`**: 暗色半透明ピル背景付きの文字ラベルをリング上に描画。

### 2.3 `Plugin.cs`
- `gizmoRenderer.Render` に現在選択されているアクターの配置データ `curPlacement` を引き渡し。
- `canDrawGizmo || canDrawOverlays` の判定を導入し、ギズモが Select モード（非表示）であっても 3D オーバーレイが単独で安定表示されるよう最適化。

### 2.4 `UI/SceneEditWindow.cs`
- **共通エリア（リスト下）**: `[x] 3D Overlays`（マスター一括トグル）
- **Animation タブ**: `[x] Show LookAt & Body Turn Overlay (3D可視化)`
- **Movement タブ**: `[x] Range Overlay (3D可視化)`（追従範囲）、`[x] Route Overlay (3D可視化)`（巡回ルート）

---

## 3. 検証項目
1. 巡回ルートのラインとピン番号が静止時およびカメラ旋回時に全く震えず安定して表示されること。
2. アクター足元に Trigger Dist（水色）、Stop Dist（緑色）、LookAt Dist（オレンジ色）、Body Turn（黄色扇形）が綺麗に描画されること。
3. スライダーで各数値を変更した際、リアルタイムに円の半径や扇形の角度が伸縮すること。
4. 各チェックボックス（マスターおよび個別）をオフにすると、対応する 3D 表示が消去されること。
