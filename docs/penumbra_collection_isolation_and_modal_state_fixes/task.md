# タスク: Penumbra コレクション完全隔離・アンアサインおよびモーダル状態汚染の解消

## 背景・課題
- ユーザー報告:
  1. `Lyle` を Glamourer ＆ Penumbra でスポーンさせると、別の Collection（前にスポーンしたキャラの Collection）を引っ張ってきている挙動になっている。
  2. 何度も新しいキャラを作っていると、どんどん前に保存したものとずれていっているような気がする。
  3. 特に「MCDF で作成 ⇒ Glamourer ＆ Penumbra で作成」といった遷移を行うと顕著に発生する。

## 根本原因
1. **モーダル状態（UI）のフィールド混濁**:
   - `CharacterLibraryTab.cs` において、`customGlamourerString` が MCDF ファイルからパースされた巨大 Base64 デザイン文字列と、通常 Glamourer デザイン手動入力の両方で共有されていた。
   - `SaveModalTemplate` で `modalSourceType == CharacterSourceType.Glamourer` の時、`customGlamourerString` が空でないと MCDF のデータが優先され、選択した Guid が上書き保存されてしまっていた。
   - 新規作成・編集モーダルを開いた際、タブ間の状態変数が排他的に分離・初期化されていなかった。
2. **Penumbra コレクションの残留・未解除**:
   - アクターをデスポーン（破棄）した際、Penumbra 側のコレクション割り当て（SetCollectionForObject）および一時コレクション割り当て（AssignTemporaryCollection）の明示的解除（Unassign）が行われていなかった。
   - 次にスポーンしたキャラで `PenumbraCollectionName` が空の場合、何の設定も行われないため、アクター（Global#200）に以前のキャラのコレクションがそのまま残留して適用されていた。
3. **Penumbra IPC の `ec=1 (NothingChanged)` 誤判定**:
   - Penumbra の `SetCollectionForObject` が `ec=1 (NothingChanged: 既にそのコレクションが適用中)` を返した際、`res.Item1 == 0` のみ成功と判定していたため失敗扱いになり、不要な古いレガシー IPC を呼びにいっていた。
4. **Glamourer Guid 適用時の Base64 パースエラー (`result: 7`)**:
   - Guid のあるデザインに対しても `ApplyState` に生 JSON を渡していたため、Glamourer 側の `DesignConverter.FromBase64` でエラーとなり、フォールバックの `ApplyDesign` で `CustomizeBytes`（26バイト）が同期されずに返されていた。

## 修正タスク
- [x] `Services/PenumbraIpc.cs`:
  - `SetCollectionForActor` で `ec=0 (Success)` に加えて `ec=1 (NothingChanged)` も成功として判定。
  - アクターに対する通常および一時コレクションの割り当てを完全解除する `UnassignCollectionForActor(int actorIndex)` メソッドを新設。
- [x] `Managers/ActorManager.cs`:
  - `DespawnCharacter`: アクターデスポーン時に必ず `penumbraIpc.UnassignCollectionForActor` を呼び出し、Penumbra 側の割り当てテーブルを完全初期化。
  - `ApplyAppearanceDirect`: 外見適用直前に `UnassignCollectionForActor` を実行し、前キャラの残存設定をクリア。未指定時はデフォルトコレクションに戻るように保証。
- [x] `Services/GlamourerIpc.cs`:
  - `ApplyDesignToActorEx`: Guid が存在する場合は直接 `ApplyDesign(Guid, actorIndex, 0, 6UL)` を呼び出し、26バイト `CustomizeData` を取得してネイティブ同期。
- [x] `UI/CharacterLibraryTab.cs`:
  - `modalMcdfGlamourerDesign` を新設し、MCDF パース結果と Glamourer デザイン入力を完全分離。
  - `OpenNewCharacterModal` / `OpenEditCharacterModal`: 各 SourceType ごとに変数を排他初期化。
  - `SaveModalTemplate`: SourceType ごとに厳格にプロパティを分離して保存（他タイプの残留・混入を物理的に排除）。
- [x] バージョン更新 (0.1.29 / 0.1.29.0) & CHANGELOG.md 追記 & Git Push。
