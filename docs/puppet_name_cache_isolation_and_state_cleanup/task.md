# タスク: パペット名キャッシュ分離と完全ステートリセットによる外見混入・過去キャラ残留の解消 (v0.1.30)

## 課題
- 新しく保存した `Ruma` をスポーン⇒デスポーン⇒スポーンと繰り返すと、毎回違う見た目になる（`Lyle` だったり `Chonk` だったり）。
- スポーンし直すたびに別のキャラを読み込んでしまう。
- すでに一覧から削除（デリート）したはずのキャラ（Chonk等）が表示される。

## 根本原因
1. **アクター名キャッシュ衝突 (The Puppet Name Cache Bug)**:
   - `NextPuppetName()` が `Csp Aa`, `Csp Ab`... と連番で生成していたため、プラグインリロードや周回で以前のキャラ（Chonk/Lyle）と同じ名前が再利用されていた。
   - Glamourer と Penumbra はアクターの GameObject 名（`Csp Ac` 等）でステートやコレクションを自動キャッシュ・自動復元するため、アクターが配置された瞬間に過去のキャラの外見が勝手に適用されていた。
2. **テンプレート設定への CustomizePlus プロファイル混入**:
   - モーダル作成時のフィールド引きずりにより、ユーザーの `CharacterSpawn.json` 内の `Ruma` テンプレートに `"CustomizePlusProfileName": "Chonk"` が誤って保存されていた。
3. **デスポーン・外見適用時のステートリセット不足**:
   - デスポーン時や新しい外見適用時に、Glamourer のステートロック解除（`UnlockState`）およびリバート（`RevertState`）、CustomizePlus のアクター紐づけ解除（`DeleteTemporaryProfileOnCharacter`）が行われていなかった。

## 実装計画と対応
- [x] **アクター名の決定論的一意化**: `GetPuppetName(CharacterTemplate)` により `Csp {template.Id:N8}` をアクター名に採用し、他キャラや過去キャラとの名前衝突を数学的に 100% 防止。
- [x] **Glamourer ステートのリセット & 解除**: `Services/GlamourerIpc.cs` に `RevertState` と `UnlockState` を実装し、`DespawnCharacter` および `ApplyAppearanceDirect` で呼び出し。
- [x] **CustomizePlus アクター紐づけの完全解除**: `customizePlusIpc.DeleteTemporaryProfileOnCharacter` をデスポーン時・外見適用時・未指定時に確実に呼び出す。
- [x] **ユーザー設定ファイルの修正**: `CharacterSpawn.json` 内の `Ruma` から Chonk の CustomizePlus プロファイル設定を削除。
- [x] **バージョン 0.1.30 へのバンプとデプロイ**: `package.json`, `CharacterSpawn.csproj`, `CharacterSpawn.json`, `repo.json`, `CHANGELOG.md` の更新とプッシュ。
