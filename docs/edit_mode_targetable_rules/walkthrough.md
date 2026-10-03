# 修正内容の確認 (Walkthrough): Edit非表示時のターゲット可否制御（カスタムネーム表示中のみターゲット可能化）

## 実装の概要
Edit ウィンドウ（SceneEditWindow）を開いていない通常プレイ・鑑賞時において、**カスタムネームを表示している（`[x] Custom Name`）アクター以外はゲーム画面上でターゲットできない（`TargetableStatus = 0`）** ように改善しました。
これにより、鑑賞や撮影時に名前非表示のモブやモンスターを誤ってクリックしたり Tab ターゲットで拾ってしまうのを完全に防ぎます。

---

## 修正内容詳細

### 1. `Managers/ActorManager.cs`
- **ターゲット可否ポリシー設定機構 (`SetTargetablePolicy`) の追加**:
  - `isEditOpenFunc`（Edit 開閉状態判定）および `isCustomNameShownFunc`（カスタムネーム表示判定）を受け取るメソッドを追加。
- **`EnforceActorDrawState`（`UpdateFrame` 毎フレーム実行）での動的制御**:
  - `bool isEditOpen = isEditOpenFunc != null && isEditOpenFunc();`
  - `bool isCustomNameShown = isCustomNameShownFunc != null ? isCustomNameShownFunc(actor) : actor.NamePlate.Show;`
  - `bool shouldBeTargetable = actor.IsTargetable && (isEditOpen || isCustomNameShown);`
  - **Edit ウィンドウ表示中**: 全カスタムスポーンに `TargetableStatus |= IsTargetable` を付与し、編集作業やモデル直接クリック選択を円滑化。
  - **Edit ウィンドウ非表示時**:
    - `ShowCustomName == true` のアクター: `TargetableStatus |= IsTargetable` を維持（ターゲット可能）。
    - `ShowCustomName == false` のアクター: `TargetableStatus &= ~IsTargetable` でターゲット不可に切り替え。

### 2. `Plugin.cs`
- 初期化時に `actorManager.SetTargetablePolicy` を呼び出し：
  - `isEditOpen`: `() => sceneEditWindow.IsOpen`
  - `isCustomNameShown`: `actor => sceneManager.GetPlacementForActor(actor)?.NamePlate.ShowCustomName ?? actor.NamePlate.Show`

---

## ユーザー操作手順・検証方法
1. プラグインを v0.1.92.0 に更新。
2. 複数のカスタムスポーン（名前表示アクターと名前非表示のモブ）を配置。
3. **Edit（SceneEditWindow）を開いている状態**:
   - 名前が表示されているキャラも名前非表示のモブも、画面上で左クリックして通常通りターゲットできることを確認。
4. **Edit（SceneEditWindow）を閉じる**:
   - 名前が表示されているアクター（`[x] Custom Name` の看板キャラや主要NPC等）をクリックすると、通常通りターゲットできることを確認。
   - 名前が表示されていないモブ（サキュバスや背景キャラ等）をクリック（または Tab ターゲット）しても、ターゲットサークルが出ずターゲットされないことを確認。
