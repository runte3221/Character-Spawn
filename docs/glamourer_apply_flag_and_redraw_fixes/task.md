# タスクリスト: Glamourer 外見巻き戻り解消および Redraw / CustomizePlus 最適化 (v0.1.27)

## 課題
- [x] Glamourer & Penumbra で Ruma のスポーンは成功するが、Chonk や別キャラをスポーンすると自キャラ（女性キャラ Ruma Meow）に戻ってしまう問題の調査
- [x] MCDF において `testruma.mcdf` は正常に表示されるが、別キャラ `test.mcdf` をロードすると自キャラに戻ってしまう問題の調査
- [x] MCDF 適用時にログに出力されていた `[CustomizePlus] IPCCharacterProfile deserialization issue ... unexpected character 'e'` エラーの調査

## 根本原因の特定
- [x] `Glamourer.Api.dll` および `HDM.dll` の IL レベル逆アセンブル調査
  - `ApplyFlagEx.DesignDefault = 7UL` には `ApplyFlag.Once = 1` が含まれており、State を更新せず一時適用扱いになる
  - 適用直後に Penumbra やエンジンの `Redraw` が走ると、Glamourer の State（自キャラベースライン）に即座に巻き戻っていた
  - ユーザーの自キャラが Ruma だったため、Ruma や testruma だけは巻き戻っても同一の外見だったため正常に見えていた
  - HDM は `ApplyFlag.Equipment (2) | ApplyFlag.Customization (4) = 6UL`（Once なし）で永続 State を更新していることを確認
- [x] MCDF 内包の `CustomizePlusData` の調査
  - データが Base64 エンコードされた JSON 文字列（`eyJCb25lcyI6...`）であり、そのまま IPC に渡したため JSON デシリアライズに失敗していた

## 実装と修正
- [x] `Services/GlamourerIpc.cs`:
  - `ApplyDesignToActor` の flags を `7UL` / `7U` から `6UL` / `6U` に変更
  - `ApplyState` の flags を `6UL` / `6U` に変更
  - `ReapplyState` の flags を `6UL` / `6U` に変更
- [x] `Managers/ActorManager.cs`:
  - `Penumbra.SetCollectionForActor` 直後の重複 Redraw を削除
  - `ApplyCustomizePlusProfile` に Base64 自動判定＆デコード処理を追加
- [x] バージョンアップ & デプロイ:
  - `package.json` -> 0.1.27
  - `CharacterSpawn.csproj`, `CharacterSpawn.json`, `repo.json` -> 0.1.27.0
  - `CHANGELOG.md` 更新
  - GitHub push & CI 自動ビルド
  - `installedPlugins` への最新バイナリ配置
