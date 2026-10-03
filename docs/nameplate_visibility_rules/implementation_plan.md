# 実装計画: ネームプレート表示制御（Edit表示中のみ全員表示・通常時はカスタムネーム有効時のみ表示）

## 背景と問題の所在
- **現状**:
  - v0.1.89 / v0.1.90 で 3D 直接クリック選択のためアクターの `TargetableStatus` をデフォルト有効化したことにより、ゲームエンジンが全カスタムスポーンに対して頭上ネームプレートを生成・表示するようになった。
  - しかし常時全員の名前が表示されていると、鑑賞や撮影時に名前が邪魔になってしまう。
- **ユーザーの要望**:
  - Edit（SceneEditWindow）を開いている時だけ、識別用に全員の名前を表示してほしい。
  - Edit を開いていない（閉じている）時は、「カスタムネームを表示」（`[x] Custom Name`）に設定しているスポーンだけ名前を表示し、それ以外のスポーンは名前を非表示にしたい。

---

## 修正内容詳細

### 1. `Models/CharacterModels.cs` & `Models/SceneData.cs`
- `SpawnedActorData` に `[JsonIgnore] public Guid? PlacementId { get; set; }` を追加。
- `SceneActorNamePlateConfig.ShowCustomName` の新規デフォルト値を `false`（非表示）に変更。

### 2. `Managers/SceneManager.cs`
- `SpawnPlacementInternal` で `spawned.PlacementId = placement.PlacementId;` を設定。
- `GetPlacementForActor(SpawnedActorData actor)` を追加し、アクターから対応する `SceneActorPlacement` を高速に逆引き可能にする。

### 3. `UI/SceneEditWindow.cs`
- 「Custom Name」チェックボックス（`placement.NamePlate.ShowCustomName`）操作時に、`spawned.NamePlate.Show` にも即時同期して即座に反映。

### 4. `Managers/NamePlateController.cs` & `Plugin.cs`
- コンストラクタ引数に `Func<bool> isEditOpenFunc` と `Func<SpawnedActorData, SceneActorPlacement?> getPlacementFunc` を追加。
- `OnNamePlateUpdate`:
  - `bool isEditOpen = isEditOpenFunc != null && isEditOpenFunc();`
  - **`isEditOpen == true`（Edit表示中）**:
    - 全カスタムスポーンの名前を表示（`handler.Name = displayName;`）。
  - **`isEditOpen == false`（通常時・閉じた状態）**:
    - `placement.NamePlate.ShowCustomName` が有効なスポーンのみ名前を表示。
    - それ以外の全スポーンは `handler.RemoveName();` で頭上ネームプレートを完全に消去。

### 5. バージョン更新・ドキュメント・CI/CD
- `tools/bump-version.ps1 0.1.91.0`
- `CHANGELOG.md` 追記
- `docs/nameplate_visibility_rules/walkthrough.md` 作成
- コミット＆プッシュ（PowerShell `;` 結合）
- GitHub Actions CI/CD ビルド完了確認
