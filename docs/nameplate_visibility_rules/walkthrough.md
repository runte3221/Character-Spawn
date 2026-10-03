# 修正内容の確認 (Walkthrough): ネームプレート表示制御（Edit表示中のみ全員表示・通常時はカスタムネーム有効時のみ表示）

## 実装の概要
カスタムスポーンの頭上ネームプレートについて、以下のルールを実装しました：
1. **Edit ウィンドウ（SceneEditWindow）を開いているとき**:
   - 配置やポーズ、移動ルートの編集・位置合わせ時にどのアクターか一目で識別できるよう、全カスタムスポーンの名前を頭上に表示。
2. **Edit ウィンドウを開いていないとき（通常プレイ・鑑賞時）**:
   - 各配置アクターの設定で「カスタムネームを表示」（`[x] Custom Name`）にしているスポーンだけ名前を表示。
   - それ以外のスポーン（チェックがオフのアクター）は、頭上ネームプレートを自動的に完全非表示（`RemoveName()`）にして鑑賞・撮影時の没入感を向上。
3. **新規配置時のデフォルト値変更**:
   - `SceneActorNamePlateConfig.ShowCustomName` の新規デフォルト値を `false`（非表示）に変更し、新しく配置したモブやキャラクターの頭上に勝手に名前が表示されないよう改善。

---

## 修正内容詳細

### 1. `Models/CharacterModels.cs` & `Models/SceneData.cs`
- `SpawnedActorData` に `[JsonIgnore] public Guid? PlacementId { get; set; }` を追加し、配置データとの紐付けを確立。
- `SceneActorNamePlateConfig.ShowCustomName` の初期値を `false`（非表示）に変更。

### 2. `Managers/SceneManager.cs`
- `SpawnPlacementInternal` で `spawned.PlacementId = placement.PlacementId;` を設定。
- `GetPlacementForActor(SpawnedActorData actor)` を追加し、アクターから対応する `SceneActorPlacement` を高速かつ確実に逆引き可能にしました。

### 3. `UI/SceneEditWindow.cs`
- UI の「Custom Name」チェックボックス（`placement.NamePlate.ShowCustomName`）操作時に、`spawned.NamePlate.Show` にも即時同期して変更をリアルタイム反映。

### 4. `Managers/NamePlateController.cs` & `Plugin.cs`
- `isEditOpenFunc`（`() => sceneEditWindow.IsOpen`）および `getPlacementFunc`（`actor => sceneManager.GetPlacementForActor(actor)`）を導入。
- `OnNamePlateUpdate` において：
  - `isEditOpen == true`（Edit表示中）：全カスタムスポーンの名前を表示（`handler.Name = displayName;`）。
  - `isEditOpen == false`（Edit非表示時）：`placement.NamePlate.ShowCustomName` が有効なスポーンのみ名前を表示し、それ以外のスポーンは `handler.RemoveName();` で頭上ネームプレートを完全消去。

---

## ユーザー操作手順・検証方法
1. プラグインを v0.1.91.0 に更新。
2. 複数のカスタムスポーン（サキュバス、NPC、モンスター等）が存在するシーンをスポーン。
3. **Edit（SceneEditWindow）を開いている状態**:
   - 全てのアクターの頭上に名前が表示され、どのアクターか一目で判別できることを確認。
4. **アクターのネームプレート設定**:
   - 名前を表示させたいアクター（看板キャラやNPC等）の「Spawn」タブで、`[x] Custom Name` にチェックを入れる。
   - 名前を非表示にしたいアクター（モブ等）の `[ ] Custom Name` のチェックを外す。
5. **Edit（SceneEditWindow）を閉じる**:
   - チェックを外したアクターの頭上から名前が消え、チェックを入れたアクターの名前だけが表示されることを確認。
