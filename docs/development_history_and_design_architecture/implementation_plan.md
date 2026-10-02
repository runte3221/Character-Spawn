# 実装計画・アーキテクチャ設計仕様書: Character-Spawn

## 1. 本来の要求と基本設計思想
本プラグインの核心要件は、**「通常ワールド（非GPose）において、ゲームや自キャラ（操作キャラクター）の動作を一切妨害せず、完全に独立したローカルパペットを安定して制御すること」** である。

### 参照基盤: AQuestReborn (AQR)
* AQR は通常ワールドにおいて独自 NPC・アクターを動的生成し、Glamourer・Penumbra・MCDF を完璧に適用して制御していた実績を持つ。
* したがって、パペットのスポーン方式、IPC 呼び出し方式、クリーンアップ手順は **AQR の設計に 100% 準拠する**。
* **注意**: Brio はグループポーズ（GPose）専用のツールであり、通常ワールドのキャラクター切り替えや通常時のパペット制御には対応していないため、通常ワールドのキャラクター制御の仕様として Brio を参照してはならない。

---

## 2. 確立された実装アーキテクチャ

### 2.1. アクター生成 (COM: ClientObjectManager)
* `ClientObjectManager.CreateObjectByIndex` を使用してローカルアクターをスポーン。
* 生成時にパペット固有の一意な ASCII ダミー名（例: `"Csp Wzjjjwyr"`）を命名。
* 人間ベースライン（Human Baseline）として生成し、描画準備完了（Draw-when-ready）を 2 フェーズポーリングで待機。

### 2.2. Glamourer 外見適用アーキテクチャ (AQR 準拠)
* **絶対ルール**: `IndexSorted` を参照するインデックス指定 IPC（`ApplyState(int index)`）や名前指定 IPC（`ApplyDesignName(string name)`）は **通常ワールドのパペットに対して絶対に使用してはならない**。
  * 理由: 通常ワールドの ClientObjectManager パペットは `IndexSorted` 配列に登録されないため、名前検索は `ActorNotFound` になり、インデックス指定は配列先頭の `IndexSorted[0]`（＝自キャラ）にフォールバックして自キャラが誤変身する。
* **正規の適用手順**:
  1. `objectTable[globalIndex]` からパペットの生参照（`ICharacter`）を直接解決。
  2. テンプレートが GUID 指定の場合：
     * `Glamourer.GetDesignBase64(Guid)` でデザインの Base64 文字列を取得。
     * Base64 を JSON にデコードし、`ForceAllApply`（性別・種族・顔・髪型・全装備スロットの `Apply: true`）を強制上書き。
     * Base64 に再圧縮。
  3. `Glamourer.ApplyAllToCharacter(ICharacter character, string base64)` を呼び出し、パペットのアドレスに対して直接外見を適用する。
  4. フォールバックとして `Glamourer.ApplyByGuidToCharacter(Guid guid, ICharacter character)` を利用。
* **効果**: パペットのメモリアドレスへ直接書き込まれるため、自キャラ（Index 0）への誤爆は物理的に完全不可能。全スロット強制適用により素体のまま残る問題も根絶。

### 2.3. CustomizePlus 体型・ボーン適用アーキテクチャ (Caraxi / AQR 準拠)
* **絶対ルール**: `SetTemporaryProfileOnCharacter(ushort gameObjectIndex, ...)` は **通常ワールドのパペットに対して絶対に使用してはならない**。
  * 理由: インデックス 200 が `IndexSorted` に存在しないため、Index 0（自キャラ）に一時プロファイルが適用されてしまう。
* **正規の適用手順**:
  1. `CustomizePlus.Profile.AddPlayerCharacter(Guid profileGuid, string characterName, ushort worldId)` を使用。
  2. 引数にパペット固有の `PuppetName`（例: `"Csp Wzjjjwyr"`）と `HomeWorld`（`chara->HomeWorld`）を指定して正規プロファイルに紐付ける。
  3. CustomizePlus はキャラクター名とワールドIDで一致するアクターのみにプロファイルを適用するため、自キャラ（`"Ruma Meow"`）には一切適用されない。
  4. デスポーン時は `CustomizePlus.Profile.RemovePlayerCharacter(profileGuid, characterName, worldId)` で紐付けを確実に解除する。

### 2.4. Penumbra Mod / コレクション適用アーキテクチャ
* MCDF または通常 Mod コレクションの適用:
  * 一時コレクション（Temporary Collection）を作成し、MCDF 内包の Mod ファイル群・メタ操作を展開して登録。
  * `AssignTemporaryCollection(tempGuid, actorIndex)` または `SetCollectionForActor(collectionName, actorIndex)` を実行。
  * 外見確定後に `Penumbra.Redraw(actorIndex)` を 1 度だけ呼び出してテクスチャ・マテリアルを同期。

### 2.5. デスポーン・武器残留防止シーケンス
* **絶対ルール**: 単純に COM からオブジェクトを削除したり、削除直前に非同期の再描画（RevertState）を走らせてはならない。
* **正規の破棄手順**:
  1. `CustomizePlus.Profile.RemovePlayerCharacter` でプロファイル紐付けを解除。
  2. `Penumbra.UnassignCollectionForActor` および `DeleteTemporaryCollection` で Mod 割り当てを解除。
  3. `Glamourer.UnlockState` でロックを解除。
  4. **武器・描画パイプラインのアンロード**:
     * `chara->DrawData.HideWeapons(true)`
     * `chara->DrawData.IsWeaponHidden = true`
     * `chara->GameObject.DisableDraw()`
  5. `ClientObjectManager.Instance()->DeleteObjectByIndex(comIdx, 0)` を呼び出し、ゲームネイティブの破棄を実行。
* **効果**: 武器 DrawObject がシーングラフから安全にアンロードされた状態で親オブジェクトが解放されるため、マップ上に武器だけが取り残される現象が永久に発生しない。

---

## 3. 今後の修正における絶対順守ルール
1. **ユーザー要望・仕様の最優先**:
   * 目の前の例外やエラーコードを消すためだけに、本来の設計（AQR 準拠、パペットと自キャラの完全分離）から逸脱した場当たりコード（インデックスの決め打ち、無根拠なフォールバック、無関係なプラグインのコード流用）を絶対に書かない。
2. **全体構造の把握**:
   * 「なぜそのエラーが起きているのか」をゲームエンジン（FF14 ClientStructs）とプラグイン（Glamourer / Penumbra / CustomizePlus）のアーキテクチャ全体から解明し、論理的に破綻のない修正を行う。
3. **作業環境の規律**:
   * デスクトップ上に調査用ファイルやスクリプトを一切作成しない。
   * すべてのドキュメントおよび調査コードは `Character-Spawn` フォルダ内で完結させる。
