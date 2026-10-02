# 実装計画: Penumbra コレクション完全隔離・アンアサインおよびモーダル状態汚染の解消

## 1. 概要
キャラクター作成モーダルでの状態汚染（MCDF と通常 Glamourer の混濁）を解消し、アクターのデスポーン時および外見適用前に Penumbra のコレクション割り当て（通常・一時コレクション）を完全にアンアサイン（リセット）することで、キャラクター切り替え時に以前のコレクションやデザインが混ざる不具合を根本解決する。

## 2. 変更内容

### A. PenumbraIpc.cs
- `SetCollectionForActor` の戻り値判定において、`ec == 0 (Success)` だけでなく `ec == 1 (NothingChanged: 既に指定コレクションが割り当て済み)` も成功として扱う。
- `UnassignCollectionForActor(int actorIndex)` メソッドを追加:
  - `SetCollectionForObject.V5(actorIndex, null, true, true)` / `(actorIndex, Guid.Empty, true, true)` により通常コレクション割り当てを解除。
  - `AssignTemporaryCollection.V5(Guid.Empty, actorIndex, false)` により一時コレクション割り当てを解除。

### B. ActorManager.cs
- `DespawnCharacter`:
  - 一時コレクションの削除（`DeleteTemporaryCollection`）に加え、`penumbraIpc.UnassignCollectionForActor(actor.GlobalIndex)` を実行。
- `ApplyAppearanceDirect`:
  - 外見適用プロセスの冒頭で `penumbraIpc.UnassignCollectionForActor(actorIndex)` を実行し、アクタースロット（Global#200）に残る以前の割り当て設定を完全に一掃。
  - `template.PenumbraCollectionName` が空の場合はアンアサインされた状態（Default）が維持され、以前のキャラの MOD 設定が引き継がれるのを防止。

### C. GlamourerIpc.cs
- `ApplyDesignToActorEx`:
  - デザインが Guid 形式の場合は `ApplyState` に生 JSON を渡すのではなく、直接 `ApplyDesign(Guid, actorIndex, 0, 6UL)` を実行。
  - デザインファイルから抽出した 26 バイトの `CustomizeData` を返し、ネイティブ同期を確実に実行。

### D. CharacterLibraryTab.cs
- フィールド `modalMcdfGlamourerDesign` を追加し、MCDF の抽出データと Glamourer 入力データを完全に分離。
- `OpenNewCharacterModal` / `OpenEditCharacterModal`:
  - モーダル初期化時に各 SourceType の変数を排他的に初期化。
- `SaveModalTemplate`:
  - `modalSourceType`（Glamourer / MCDF / NPC / Monster）ごとに保存するプロパティを排他的に限定。他タイプのデータ（MCDF パスや Glamourer デザイン）が混ざらないよう厳格化。

## 3. 検証項目
1. MCDF キャラクターをスポーン後、デスポーン。
2. その後、Glamourer ＆ Penumbra のキャラクター（`Lyle` 等）をスポーンした際、MCDF の MOD や以前のコレクションが引き継がれず、指定したコレクションのみが正確に適用されること。
3. 新規キャラクター作成モーダルで、MCDF と Glamourer を切り替えてもデザインやコレクション名が汚染されないこと。
