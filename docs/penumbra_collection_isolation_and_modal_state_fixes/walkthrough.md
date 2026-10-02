# ウォークスルー: Penumbra コレクション完全隔離・アンアサインおよびモーダル状態汚染の解消

## 変更概要
キャラクター切り替え時や複数キャラ作成時に発生していた「以前のコレクションが残る」「デザインや設定がずれる」問題を解消するため、以下の4つのレイヤーで修正を実施しました。

### 1. Penumbra コレクションの完全なアンアサイン機能 (`Services/PenumbraIpc.cs`)
- `UnassignCollectionForActor(int actorIndex)` を新設:
  - 通常コレクションの解除（`SetCollectionForObject` に `null` / `Guid.Empty` を指定）
  - 一時コレクションの解除（`AssignTemporaryCollection` に `Guid.Empty` を指定）
- `SetCollectionForActor` の戻り値として `ec == 1 (NothingChanged)` を成功と認識するように改善。

### 2. デスポーン時・適用時の Penumbra キャッシュ初期化 (`Managers/ActorManager.cs`)
- `DespawnCharacter` 実行時、一時コレクションの破棄に加え、アクターのコレクション割り当てを明示的に解除（Unassign）。
- `ApplyAppearanceDirect` の先頭で必ず `UnassignCollectionForActor` を呼び出し、同じスロット（Global#200）に以前スポーンしていたキャラのコレクション設定が残らないよう保証。
- コレクション名が空のアクターはデフォルト状態（MOD 未割り当て）に初期化され、以前の MOD が残留する問題を根絶。

### 3. モーダル状態管理の完全分離 (`UI/CharacterLibraryTab.cs`)
- MCDF からパースされた Glamourer デザインを保持する専用フィールド `modalMcdfGlamourerDesign` を新設。
- `SaveModalTemplate` をリファクタリングし、`SourceType`（Glamourer / MCDF / NPC / Monster）ごとに保存する変数を排他的に限定。
- タブを切り替えたり複数キャラを連続作成しても、MCDF のデータが通常 Glamourer キャラに混ざったり、逆に Glamourer の Guid が MCDF に残ったりしない構造に刷新。

### 4. Glamourer Guid 適用の最適化 (`Services/GlamourerIpc.cs`)
- Guid が指定されたデザインの場合、生 JSON への変換と失敗する Base64 パース（`result: 7`）をスキップし、直接 `ApplyDesign(Guid, actorIndex, 0, 6UL)` を実行。
- デザインファイルから抽出した 26 バイトの `CustomizeData` を直接ネイティブ構造体に同期。

## 動作確認手順
1. Character タブを開き、以前作成した MCDF キャラクター（例: `testruma` や `test`）を「Spawn」する。
2. その後「Despawn」する。
3. 続けて、Glamourer ＆ Penumbra で作成したキャラクター（例: `Lyle`）を「Spawn」する。
4. `Lyle` に指定された Penumbra コレクション（`[OC-Lyle]`）のみが正しく適用され、MCDF の MOD や以前のキャラの外見・コレクションが一切混ざらないことを確認する。
5. 「New Chara」で MCDF と Glamourer ＆ Penumbra を切り替えて保存しても、設定内容が正しく個別保存されることを確認する。
