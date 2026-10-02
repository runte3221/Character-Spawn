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

### 2.2. Glamourer 外見適用アーキテクチャ (Glamourer 1.7.1.3 正式仕様準拠)
* **名前指定 IPC (`ApplyStateName` / `ApplyDesignName`) の不適合理由 (IL解析解明)**:
  * Glamourer 1.7.1.3 の `ApplyStateName` / `ApplyDesignName` は、内部で `FindExistingStates`（既存キャッシュ `stateManager.Values`）のみを検索する仕様である。
  * スポーンしたてのパペット（`Csp Lhcpetra` 等）はステート辞書に存在しないため、名前指定は必ず `ActorNotFound (2)` または内部バグによる `InvalidKey (6)` を返して 100% 失敗する。
* **インデックス指定 IPC (`ApplyState` / `ApplyDesign`) の適合理由 (IL解析解明)**:
  * 一方、インデックス指定 IPC（`ApplyState` / `ApplyDesign`）は、内部で `ObjectManager[objectIndex]`（ゲームオブジェクト配列）からアクターを直接取得し、**`stateManager.GetOrCreate` を呼び出す**。
  * これにより、未登録の新規パペットであってもステートが自動生成され、外見が 100% 確実に適用される（result: 0）。
* **自キャラ誤爆物理遮断ガードの設置**:
  * `actorIndex <= 0` の場合は、パペット向け外見適用処理を物理的に拒絶する。
  * パペットは `GlobalIndex`（COM#0 = 200〜）でスポーンし、自キャラ（`GlobalIndex: 0`）とは明確に分離されているため、`actorIndex > 0` を前提とした呼び出しにより自キャラ誤爆を 100% 防止する。
* **正規の適用手順**:
  1. テンプレートが GUID 指定の場合：
     * `GetDesign(targetGuid)` で JObject を取得し、`ForceAllApply`（全パーツ強制適用）を実行。
     * Base64 に圧縮し、`Glamourer.ApplyState(compressedBase64, actorIndex, 0, 7UL)`（Flags: 7 = Once | Equipment | Customization）を実行。
     * フォールバックとして `Glamourer.ApplyDesign(targetGuid, actorIndex, 0, 7UL)` を実行。
  2. 非GUID（MCDF内包等）の場合：
     * `ParseDesignString` -> `ForceAllApply` -> Base64 圧縮 -> `ApplyState(..., actorIndex, 0, 7UL)` を実行。
* **効果**: スポーン直後のパペットに男性キャラ等のデザインが 100% 確実に反映され、自キャラ（Index 0）への誤爆も完全に遮断される。

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
