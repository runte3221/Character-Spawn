# 実装計画: Stagehand準拠 ImGuizmo 導入・平面移動・移動/回転分離 (v0.1.18)

## 1. ユーザー要望
1. **ギズモが光るがドラッグしても動かない問題の解消**:
   - 左クリックしながら移動させようとしても反応しない現象を根本解決。
2. **Stagehand のような XY, YZ, XZ 平面移動（四角形選択部分）の追加**:
   - 画像1にある赤・青・緑の四角形（XY平面、XZ平面、YZ平面）で 2軸を同時に操作可能にする。
3. **移動と回転のモード分離**:
   - 画像2にある Stagehand のメニューのような形で、移動（Translate）と回転（Rotate）を切り替えられるようにする。

## 2. 原因分析
- 従来の `GizmoRenderer.cs` は 2D スクリーン投影線分とマウスベクトルの内積による自作エミュレーションであり、かつ `MainWindow.Draw` の内部で処理されていた。
- ウィンドウ外のクリックでフォーカスが外れ、マウスイベントが正常に伝達されていなかった。
- 遠近法（パースペクティブ）を考慮した 3D 空間での正しい移動・レイキャストが行われていなかった。

## 3. 解決策 (Stagehand 準拠 ImGuizmo アーキテクチャ)
- Stagehand (`Stagehand.dll`) のリバースエンジニアリングにより、Stagehand は Dalamud 同梱の **`Dalamud.Bindings.ImGuizmo`** を使用していることが判明。
- ゲーム内カメラ (`FFXIVClientStructs.FFXIV.Client.Graphics.Scene.CameraManager.Instance()->CurrentCamera->RenderCamera`) から View 行列と Projection 行列を直接取得。
- フルスクリーンの透明オーバーレイウィンドウを作成し、`ImGuizmo.BeginFrame()` および `ImGuizmo.Manipulate()` を実行。
- `ImGuizmoOperation.Translate`: 各軸矢印に加え、XY・XZ・YZ 平面移動 Quad（四角形ハンドル）が標準で完璧に動作。
- `ImGuizmoOperation.Rotate`: 各軸の回転リングを独立して操作。
- UI に Stagehand スタイルのモード切替ボタン（Translate / Rotate）を追加。
