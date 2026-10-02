# 修正内容の確認 (Walkthrough): ImGuizmo 導入・平面移動・移動/回転分離 (v0.1.18)

## 実施した変更
1. **`Dalamud.Bindings.ImGuizmo` の全面導入**:
   - `UI/GizmoRenderer.cs` を業界標準の 3D ギズモライブラリ `ImGuizmo` を用いた設計に刷新。
   - `CameraManager` から View / Projection 行列を取得し、完全な 3D レイキャストによるクリック・ドラッグ操作を実現。
2. **XY, XZ, YZ 平面移動（四角形 Quad）ハンドルの提供**:
   - 移動モード時に赤・緑・青の半透明四角形が表示され、2軸平面上での自由なドラッグ移動が可能に。
3. **移動（Translate）と回転（Rotate）のモード分離**:
   - Stagehand スタイルのモード切替（Translate / Rotate）を追加。
   - 移動時は軸矢印と平面 Quad、回転時は専用の回転リングを表示。
4. **ギズモ描画・入力透過の修正 (v0.1.19)**:
   - FFXIV のリバースZプロジェクション深度行列の補正 (`M43 = -(clip * near)`, `M33 = -((far + near) / (far - near))`, `view.M44 = 1.0f`) を導入し、ギズモが表示されない問題を根本解決。
   - 全画面オーバーレイに `ImGuiWindowFlags.NoInputs` を付与し、ゲーム画面の視点カメラ操作（右ドラッグ・左クリック等）が一切ブロックされないよう修正。
   - 重複していた Gizmo チェックボックスを廃止し、セレクトボタン（Select / Translate / Rotate）に一本化。
5. **回転挙動の改善・NPCダイアログ防止・ヘッダーボタン削除 (v0.1.20)**:
   - 回転モードを `ImGuizmoOperation.RotateY`（水平回転リング）に変更。キャラクターの向き変更に特化し、リングを回すことで即座に滑らかに 360 度回転するよう修正。
   - ギズモ操作中（ホバー・ドラッグ中）は `NoInputs` を解除してクリック透過を遮断し、足元のキャラをクリックして「New NPC: Failed to get response.」ダイアログが暴発する問題を根本解決。ギズモ外では `NoInputs` を維持し自由なカメラ操作を両立。
   - `ActorManager` でスポーンしたアクターに `TargetableStatus = 0` および `EventId = 0` を設定。
   - メインウィンドウ左上に重複表示されていたギズモボタンを削除。
6. **バージョン更新**:
   - `v0.1.20` / `0.1.20.0` に更新。
