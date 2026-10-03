# 実装計画: 開発ロードマップ更新 ＆ ギズモ表示連動（SceneEditWindow 閉鎖時非表示）実装

## 1. 概要
ユーザーからご提案いただいた以下の拡張機能を正式に今後の開発ロードマップへ組み込み、その第1弾として **「SceneEditWindow を閉じた際に 3D ギズモが自動的に非表示になる機能（③）」** を実装する。

### 今後の開発ロードマップ
1. **Phase 1 (今回実装: v0.1.69.0)**: SceneEditWindow 閉鎖時のギズモ非表示連動
2. **Phase 2**: ミニオン・マウント（Companion / Mount）のカスタムキャラクター登録・配置対応
3. **Phase 3**: Embedded Modpack（内包Modパック）実装（モンスター個別MOD適用 ＆ 同一エモート別MOD割り当て）
4. **Phase 4**: Brio ポーズ（`.pose`）の読み込み・配置固定 (Idle) およびモーション連動

---

## 2. Phase 1（ギズモ表示連動）の課題と設計

### 課題
現在、`Plugin.cs` の描画ループ（`DrawUI`）における 3D ギズモ描画は以下の判定のみで行われている：
```csharp
if (Configuration.CurrentGizmoMode != GizmoMode.Select)
{
    var targetActor = stageTab.SelectedActor;
    if (targetActor == null || !targetActor.IsSpawned || !targetActor.IsReady)
    {
        targetActor = actorManager.CurrentPreviewActor;
    }

    if (targetActor != null && targetActor.IsSpawned && targetActor.IsReady)
    {
        gizmoRenderer.Render(targetActor, ...);
    }
}
```
- [Scene Edit] ウィンドウ内でギズモ操作を行った後、ウィンドウを閉じても `Configuration.CurrentGizmoMode` は `Translate` や `Rotate`, `Scale` の状態のまま維持される。
- そのため、ウィンドウを閉じた後も直前に選択していたアクターに対して全画面オーバーレイで 3D ギズモが描画され続けてしまい、ゲーム画面の視認性や没入感を損なっていた。

### 設計方針
ギズモが描画されるべき正当なコンテキストは以下のいずれかである：
1. **`sceneEditWindow.IsOpen` が `true` の場合**（Scene Edit ウィンドウが開いている間、選択中のアクターを編集する）
2. **`mainWindow.IsOpen` かつ `libraryTab` でプレビューアクターが表示されている場合**（Character Library でアクターを調整している場合）

したがって、上記以外の状況（特に `SceneEditWindow` が閉じられた時）にはギズモを描画しないようにガードを設ける。
また、さらに直感的な操作感を提供するため、**`SceneEditWindow` の `IsOpen` が `false` になった際、またはギズモ描画条件を満たさない際には、不要なマウスイベントのキャプチャを確実に防止**する。

---

## 3. 実装内容

### `Plugin.cs`
- `DrawUI` 内のギズモ描画条件を以下のように更新：
```csharp
// 3D Gizmo Overlay 描画 (SceneEditWindow または MainWindow プレビュー中のみアクティブ化)
bool shouldDrawGizmo = sceneEditWindow.IsOpen || (mainWindow.IsOpen && actorManager.CurrentPreviewActor != null);
if (shouldDrawGizmo && Configuration.CurrentGizmoMode != GizmoMode.Select)
{
    var targetActor = stageTab.SelectedActor;
    if (targetActor == null || !targetActor.IsSpawned || !targetActor.IsReady)
    {
        targetActor = actorManager.CurrentPreviewActor;
    }

    if (targetActor != null && targetActor.IsSpawned && targetActor.IsReady)
    {
        gizmoRenderer.Render(targetActor, (newPos, newRot, newScale) =>
        {
            actorManager.UpdateActorTransform(targetActor, newPos, newRot, newScale);
            stageTab.SyncPlacementTransformFromGizmo(newPos, newRot, newScale);
        });
    }
}
```

---

## 4. 検証項目
1. [Scene Edit] ウィンドウを開き、アクターを選択してギズモ（移動・回転・スケール）が表示されることを確認。
2. [Scene Edit] ウィンドウの右上の [X] ボタン、または Esc キーでウィンドウを閉じる。
3. **ウィンドウが閉じた瞬間に、画面上の 3D ギズモが綺麗に消えること**を確認。
4. 再度 [Scene Edit] ウィンドウを開いた際、再びギズモが表示され正常に操作できることを確認。
