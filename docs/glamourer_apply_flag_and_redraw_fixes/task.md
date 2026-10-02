# タスクリスト: Glamourer 外見巻き戻り解消および Redraw / CustomizePlus 最適化 (v0.1.28)

## 課題
- [x] Glamourer & Penumbra で Ruma のスポーンは成功するが、Chonk や別キャラをスポーンすると自キャラ（女性キャラ Ruma Meow）に戻ってしまう問題の徹底調査
- [x] MCDF において `testruma.mcdf` は正常に表示されるが、別キャラ `test.mcdf` をロードすると自キャラに戻ってしまう問題の徹底調査
- [x] MCDF 適用時にログに出力されていた `[CustomizePlus] IPCCharacterProfile deserialization issue ... unexpected character 'e'` エラーの調査

## 根本原因の徹底解明 (v0.1.28)
- [x] **Glamourer デザインファイルの Apply フラグ抜け**:
  - `Chonk`（`Kimo-1-Nude`）のデザインファイル `238897be-...json` を解析したところ、`"Race": { "Value": 1, "Apply": false }` となっており、**種族（ヒューラン）の適用チェックが OFF** だった。
  - Glamourer の `ApplyDesign(Guid)` は各スロットの Apply フラグに従うため、種族が「ミコッテ（Race: 4）」のまま、性別「男性（Gender: 0）」とクラン「ハイランダー（Clan: 2）」のみが適用され、ミコッテにハイランダーという存在しないキメラ状態が発生し、DirectX/Havok 描画構築が破綻して自キャラのベースラインに戻っていた。
- [x] **ネイティブ `chara->DrawData.CustomizeData` の未同期**:
  - スポーン時に自キャラの完全クローンとして作成したあと、ネイティブ構造体の 26 バイトが自キャラ（ミコッテ女性）のまま残っていた。
  - Redraw が走った際、ゲームエンジン自身がネイティブメモリからミコッテ女性のモデルを再構築してしまっていた。
- [x] **`test.mcdf` のデータ自体の正体**:
  - `test.mcdf` 内の `GlamourerData` を解凍して解析した結果、そもそも「ミコッテ女性（Race: 4, Clan: 7, Gender: 1）＝ Ruma Meow」のデザインデータそのものが格納されていたため、Ruma の姿になるのは正常なファイル内容通りの動作であった。

## 実装と修正 (v0.1.28)
- [x] `Services/GlamourerIpc.cs`:
  - `ForceAllApply`: デザイン JObject の全 Customize スロット（Race, Gender, Clan, Face, etc.）および Equipment スロットの `Apply` を強制的に `true` に設定する処理を追加。
  - `ExtractCustomizeBytes`: JObject の Customize からネイティブ用の 26 バイト `CustomizeData` を抽出するヘルパーを追加。
  - `ApplyDesignToActorEx`: Guid または Base64/JSON から JObject を読み出し、`ForceAllApply` を施した上で 26 バイトを抽出し、`ApplyState`（flags=6UL）で完全適用するよう改修。
- [x] `Managers/ActorManager.cs`:
  - Glamourer パスおよび MCDF パスの両方で、`ApplyDesignToActorEx` から抽出した 26 バイトの `CustomizeData` をネイティブ `chara->DrawData.CustomizeData` に直接書き込み、`CharacterSetup.CopyFromCharacter` を実行するよう改修。
- [x] バージョンアップ & デプロイ:
  - `package.json` -> 0.1.28
  - `CharacterSpawn.csproj`, `CharacterSpawn.json`, `repo.json` -> 0.1.28.0
  - `CHANGELOG.md` 更新
  - GitHub push & CI 自動ビルド完了
  - `installedPlugins` への最新バイナリ配置完了
