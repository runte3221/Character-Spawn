# 実装計画: Glamourer 適用フラグ適正化と外見永続化 (v0.1.28)

## 1. 概要
- **対象バージョン**: v0.1.28 (0.1.28.0)
- **目的**: 
  1. `Chonk`（男性キャラ）などの別キャラクターをスポーンした際、自キャラ（Ruma Meow）の外見に戻ってしまう問題を根本から完全に解消する。
  2. デザインファイル内で `Race` や特定スロットの `Apply: false` が設定されている場合でも、キャラクタースポーンとして完全な外見を強制適用する。
  3. ゲームエンジンのネイティブメモリ（`chara->DrawData.CustomizeData`）にも直接 26 バイトを同期し、Redraw 時にゲームエンジンが自キャラを再構築するのを防ぐ。

## 2. 根本原因の技術的分析 (IL・設定ファイル完全解明)
1. **Glamourer デザインファイルの Apply 設定**:
   - `Chonk` のデザインファイル（`238897be-39ba-4ab6-b258-52e5292fe6dd.json`）を直接解析した結果：
     `"Race": { "Value": 1, "Apply": false }`
     `"Gender": { "Value": 0, "Apply": true }`
     `"Clan": { "Value": 2, "Apply": true }`
   - `Race`（ヒューラン）の適用が OFF になっていた。
   - `ApplyDesign(Guid)` を呼ぶと、Glamourer はデザイン内の設定通り「Race は変更しない」ため、アクターの種族は自キャラの「ミコッテ（Race: 4）」のまま、性別「男性（Gender: 0）」とクラン「ハイランダー（Clan: 2）」のみが適用された。
   - ミコッテ（Race 4）にハイランダー（Clan 2）はゲームエンジン上存在しないため、DirectX スケルトン初期化エラーとなり、自キャラのベースラインに戻っていた。
2. **ネイティブメモリ未同期**:
   - スポーン時に自キャラの完全クローンとして作成したあと、ネイティブ構造体の 26 バイトが自キャラ（ミコッテ女性）のまま残っていたため、ゲームエンジンの再描画フックがネイティブメモリからミコッテ女性を描画していた。
3. **`test.mcdf` のデータ**:
   - `test.mcdf` 内の `GlamourerData` を解凍したところ、データ自体が「ミコッテ女性（Race: 4, Clan: 7, Gender: 1）＝ Ruma Meow」であったため、Ruma の姿になるのは正常なデータ通りの挙動であった。

## 3. 変更計画 (v0.1.28)
1. **`Services/GlamourerIpc.cs`**:
   - `ForceAllApply`: デザイン JObject の全 Customize スロット（Race, Gender, Clan, Face, etc.）および Equipment スロットの `Apply` を強制的に `true` に設定。
   - `ExtractCustomizeBytes`: JObject の Customize からネイティブ用の 26 バイト `CustomizeData` を抽出。
   - `ApplyDesignToActorEx`: JObject を解決し、`ForceAllApply` を施した上で 26 バイトを抽出し、`ApplyState`（flags=6UL）で完全適用。
2. **`Managers/ActorManager.cs`**:
   - Glamourer パスおよび MCDF パスで、抽出した 26 バイトの `CustomizeData` をネイティブ `chara->DrawData.CustomizeData` に直接書き込み、`CharacterSetup.CopyFromCharacter` を実行。
3. **バージョン管理・リリース**:
   - `package.json`, `CharacterSpawn.csproj`, `CharacterSpawn.json`, `repo.json`, `CHANGELOG.md` を更新。
   - GitHub にコミット・プッシュし、CI で自動ビルド。
   - `installedPlugins` の 0.1.28.0, 0.1.27.0, 0.1.26.0 に配置。
