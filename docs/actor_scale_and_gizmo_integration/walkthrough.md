# ウォークスルー: アクタースケール（Scale）拡大縮小＆3Dギズモ連携実装

## 1. 変更概要
スポーンさせたキャラクター、NPC、モンスター、MCDF 等の全モデルに対し、**サイズ（Scale: 0.01x 〜 10.0x）のリアルタイム変更機能** を実装しました。
UI の数値スライダー（`DragFloat`）および 3D ギズモ（`GizmoMode.Scale`）の両方からシームレスに操作可能で、シーン設定への自動保存および再スポーン時の再現に対応しています。

---

## 2. 変更内容一覧

| ファイル | 変更内容 |
|---|---|
| `Managers/ActorManager.cs` | `UpdateActorTransform` に `float? newScale = null` を追加し、`GameObject.Scale` および `DrawObject->NotifyTransformChanged()` によるリアルタイム描画更新を実装。`SpawnCharacter` にも初期スケール引数を追加。 |
| `Managers/SceneManager.cs` | `SpawnPlacementInternal` で配置設定の `placement.Scale` を `SpawnCharacter` に渡すように接続。 |
| `UI/GizmoRenderer.cs` | コールバックを `Action<Vector3, float, float>` に拡張し、ImGuizmo の `Scale` マニピュレート結果を均等スケール値として通知。 |
| `Plugin.cs` | ギズモ描画ループから受け取った `newScale` を `ActorManager` および `StageSceneTab` に伝達。 |
| `UI/StageSceneTab.cs` | `SyncPlacementTransformFromGizmo` で Scale を受け取り、配置データに反映してシーン保存。 |
| `UI/SceneEditWindow.cs` | `DragFloat("##Scale")` 変更時に `UpdateActorTransform` を呼び出し、スライダー操作で即座にモデルサイズが変化するように配線。 |
| `UI/MainWindow.cs` | ギズモモード選択ラジオボタンに `Scale (Resize)` を追加。 |

---

## 3. 実機検証手順

### 検証 1: スライダーによるサイズ変更
1. [Scene Edit] ウィンドウを開き、スポーン済みアクターを選択。
2. 「Scale」のドラッグスライダー（`1.000`）を左右にドラッグ。
3. ゲーム内のキャラクターがリアルタイムに拡大（例: `2.000` で巨人化）または縮小（例: `0.500` でミニ化）することを確認。

### 検証 2: 3D ギズモ（Scale モード）によるサイズ変更
1. ツールバーの四角い拡大アイコン（`FontAwesomeIcon.ExpandAlt`）をクリックして Scale モードに切り替え。
2. キャラクターの中心に 3D スケールギズモ（軸およびキューブ）が表示されることを確認。
3. ギズモの軸または中央のキューブをドラッグし、キャラクターのサイズが直感的に拡大・縮小することを確認。
4. [Scene Edit] ウィンドウ内の「Scale」数値がリアルタイムに連動して更新されることを確認。

### 検証 3: シーン保存と再スポーン再現
1. キャラクターのスケールを `1.500` に設定した状態で [Hide] をクリック（デスポーン）。
2. 再度 [Show] をクリック（スポーン）。
3. キャラクターが最初から `1.500` の拡大サイズで出現することを確認。

### 検証 4: 他パイプライン（Chonk / MCDF / モンスター）との共存
1. Chonk（CustomizePlus 体型変形アクター）のスケールを変更し、太身ボーン変形が崩れずに全体が拡大縮小されることを確認。
2. モンスター（レストレス・ラプトルやハシュマリム）のスケールを変更し、正常にサイズが変わることを確認。
