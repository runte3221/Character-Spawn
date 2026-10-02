# 実装計画: Glamourer 適用フラグ適正化と外見永続化 (v0.1.27)

## 1. 概要
- **対象バージョン**: v0.1.27 (0.1.27.0)
- **目的**: 
  1. `Chonk`（男性キャラ）や別キャラクターの MCDF（`test.mcdf`）をスポーンした際、Redraw によって自キャラ（Ruma Meow）の外見に巻き戻ってしまう問題を完全に解消する。
  2. MCDF に内包された CustomizePlus データ（Base64 形式）がデシリアライズエラーになるのを防止し、体型・ボーンスケールを正確に反映する。

## 2. 根本原因の技術的分析
1. **Glamourer の ApplyFlag 仕様**:
   - `ApplyFlag` 定義:
     - `Once = 1`: 一時適用。ゲーム内の一時モデルを変更するが、アクターの Glamourer State は更新しない。
     - `Equipment = 2`: 装備品データ
     - `Customization = 4`: キャラメイク（種族・髪型・顔等）データ
     - `Lock = 8`: 変更ロック
   - これまでのコードでは `flags = 7UL`（`DesignDefault` = `Once | Equipment | Customization`）を使用していた。
   - `Once (1)` が指定されているため、直後に Redraw が発生すると、Glamourer の State（スポーン時に自キャラからクローンしたベースライン）に即座に巻き戻っていた。
2. **HDM の実装比較**:
   - `HDM.dll` の `HumanGuise.TryApplyOnce` を IL 解析した結果、`ldc.i4.6 -> conv.u8 (6UL)` を渡していることが判明。
   - `ApplyFlag.Equipment (2) | ApplyFlag.Customization (4) = 6` を指定することで、アクターの State そのものを更新させ、その後の Redraw やゾーンチェンジでも外見が維持される。
3. **MCDF の CustomizePlus データ**:
   - Mare Synchronos 由来の MCDF に含まれる `CustomizePlusData` は、JSON を UTF-8 でエンコードした上で Base64 化された文字列（`eyJCb25lcyI6...`）。
   - CustomizePlus IPC `SetTemporaryProfileOnCharacter` は生の JSON 文字列を期待しているため、Base64 文字列の先頭文字 `'e'` を構文エラーとして弾いていた。

## 3. 変更計画
1. **`Services/GlamourerIpc.cs`**:
   - `ApplyDesignToActor`: flags を `6UL` / `6U` に変更。
   - `ApplyState`: flags を `6UL` / `6U` に変更。
   - `ReapplyState`: flags を `6UL` / `6U` に変更。
2. **`Managers/ActorManager.cs`**:
   - `ApplyAppearanceDirect`: `Penumbra.SetCollectionForActor` 直後の重複 Redraw を削除（Glamourer 適用後の最終 Redraw に集約）。
   - `ApplyCustomizePlusProfile`: `fallbackMcdfCPlusData` の先頭が `{` や `[` でない場合に Base64 デコードを行う防御処理を追加。
3. **バージョン管理・リリース**:
   - `package.json`, `CharacterSpawn.csproj`, `CharacterSpawn.json`, `repo.json`, `CHANGELOG.md` を更新。
   - GitHub にコミット・プッシュし、CI でバイナリを自動ビルド。
   - `installedPlugins` の 0.1.27.0 および 0.1.26.0 に配置。
