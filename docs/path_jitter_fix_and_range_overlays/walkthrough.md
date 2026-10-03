# Walkthrough: ルート描画ジッター解消 ＆ 距離・角度範囲 3D 可視化オーバーレイ実装

## 概要
本改修では、巡回ルートラインやピン番号が小刻みに震えるジッター問題を根本解決するとともに、`Trigger Dist`、`Stop Dist`、`Distance (LookAt)`、`Body Turn` の 3D 空間可視化（レンジサークルおよび扇形アーク）と、それらを自由に表示/非表示できるチェックボックスUIを新設しました。

---

## 修正・実装内容

### 1. 巡回ルート描画のジッター根本根絶 (`UI/GizmoRenderer.cs`)
- **原因とメカニズム**:
  - これまでは ImGuizmo 用の透明ウィンドウ内のローカル描画バッファ（`ImGui.GetWindowDrawList()`）に描画していました。
  - マウス操作状態によって `NoInputs` フラグがフレーム単位で切り替わったり、ウィンドウレイアウトの微小な再計算が発生することで、サブピクセル単位の振動（ジッター）が発生していました。
- **改善点**:
  - メインビューポート最前面の描画リスト `ImGui.GetForegroundDrawList(ImGuiHelpers.MainViewport)` に直接描画するアーキテクチャに刷新しました。
  - ゲームカメラの `ViewMatrix` と `ProjectionMatrix` から数学的に直接スクリーン座標を導出する自前射影関数 `ProjectWorldToScreen` を実装しました。
  - これにより、カメラ操作時も静止時も 1 ピクセルの狂いもなく完全に静止・追従する極めて滑らかな描画を実現しました。

---

### 2. 各種パラメータの 3D 空間可視化 (`UI/GizmoRenderer.cs`)
選択中アクターの足元を中心として、以下の範囲が 3D 空間上にリアルタイム描画されます：

| 範囲パラメータ | 形状・色 | 概要 |
| :--- | :--- | :--- |
| **Stop Dist** | **ライムグリーン円** (緑色) | 接近したプレイヤーまたはターゲットの手前で停止する安全距離 |
| **Trigger Dist** | **シアン円** (水色) | プレイヤーがこの円内に入ると追従を開始する検知距離 |
| **LookAt Dist** | **オレンジ円** (橙色) | 視線追従（LookAt）が有効に反応する最大限界距離 |
| **Body Turn** | **黄色扇形** (アーク＋半透明ファン) | 体を向ける回転許容角度（±AngleLimit°）。正面ライン＆角度ラベル付き |

※スライダーで数値を動かすと、3D 空間上の円の大きさや扇形の角度がリアルタイムに伸縮します。

---

### 3. 表示切り替えチェックボックスの実装 (`UI/SceneEditWindow.cs`)
ゲーム画面が煩雑にならないよう、以下の場所にトグルチェックボックスを配置しました：

1. **全体マスタートグル (`3D Overlays`)**:
   - アクター一覧リストの直下、Delete ボタンの左側に配置。
   - チェックを外すと、すべての 3D オーバーレイ（パスライン、ピン、円、扇形）を一括で非表示にできます。
2. **Animation タブ (`Show LookAt & Body Turn Overlay`)**:
   - LookAt Player / Custom Spawn 設定の直下に配置。
   - オレンジの視線距離円と黄色の体回転扇形を表示/非表示できます。
3. **Movement タブ (`Range Overlay` & `Route Overlay`)**:
   - 接近追従設定の横に `Range Overlay`（水色 Trigger Dist ＆ 緑色 Stop Dist の表示切替）。
   - 巡回ルート設定の横に `Route Overlay`（黄色パスライン ＆ 番号ピンの表示切替）。

---

## 変更ファイル一覧
- `Configuration.cs`: 3D 表示設定フラグ（`ShowVisualOverlays`, `ShowWaypointPath`, `ShowMovementRanges`, `ShowAnimationRanges`）を追加。
- `UI/GizmoRenderer.cs`: ジッター根本解消（`ForegroundDrawList` ＋ `ProjectWorldToScreen`）、水平円描画、扇形描画、距離ラベル描画。
- `Plugin.cs`: 選択中アクターの配置データを `GizmoRenderer` に渡し、オーバーレイ単独表示をサポート。
- `UI/SceneEditWindow.cs`: マスタートグルおよび各タブ内の個別表示トグルチェックボックスを追加。
