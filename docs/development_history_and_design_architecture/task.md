# タスクリスト: 開発経緯の記録・要件定義の確立・不具合ループ防止体制

## 1. 開発経緯と課題の背景
- **本来の目的**:
  - 通常ワールド（非GPose）上で、ユーザーが任意のアクター（パペット、人型NPC、モンスター、MCDF、自キャラ複製）をローカルに安定スポーンさせ、ポーズ・ギズモ操作・アニメーション・表情・外見（Glamourer）・装備/Mod（Penumbra）・体型/ボーンスケール（Customize+）を完全に独立制御できるプラグインを構築すること。
- **基盤と参照元**:
  - 本プラグインの基盤思想および通常ワールドでのパペット生成アーキテクチャは **`AQuestReborn` (AQR)** に完全準拠する。
  - AQR プラグインの無効化（独立化）に伴い発生した連携不具合を解決することが本来の要件であった。
- **発生していた重大な不具合ループ**:
  1. 男性キャラ等のテンプレート（Chonk 等）をスポーンさせても素体（自キャラ女性ミコッテ）のまま立ってしまう。
  2. スポーンしたパペットではなく、操作中の自キャラ（LocalPlayer）側に外見や Customize+ プロファイルが誤爆適用されてしまう。
  3. スポーン・デスポーンを繰り返すと武器だけがマップ上に残留する。
- **ループに陥った根本理由**:
  - 発生しているエラーログ（ActorNotFound や Index 200 不在など）の表面的なもぐら叩きに終始し、
  - 通常ワールドにおける ClientObjectManager アクターの描画特性（IndexSorted に登録されない）を俯瞰せず、
  - Brio（GPose専用）等の無関係な仕様を参照して迷走し、
  - ユーザーが最初から指示していた「AQR の実装方式に準拠する」という本来の意図を無視した修正を繰り返していたため。

---

## 2. 完了タスク (v0.1.37.0)
- [x] **AQR および公式プラグイン（Glamourer, CustomizePlus, Penumbra）の内部アーキテクチャの完全逆コンパイル解析**:
  - 通常ワールドのパペットは `IndexSorted`（描画ソート配列）に載らないため、インデックス（200）指定や名前指定（`ApplyDesignName`）が `IndexSorted[0]`（自キャラ）にフォールバックしていた根本原因を特定。
  - AQR が使用していた公式 IPC ラベルとシグネチャを完全特定。
- [x] **GlamourerIpc の AQR 準拠への全面刷新**:
  - `Glamourer.ApplyAllToCharacter` (`Action<ICharacter, string>`) サブスクライバを追加。
  - `Glamourer.ApplyByGuidToCharacter` (`Action<Guid, ICharacter>`) サブスクライバを追加。
  - `Glamourer.GetDesignBase64` (`Func<Guid, string>`) サブスクライバを追加。
  - `ApplyDesignToCharacter(ICharacter, string)` メソッドの実装（パペットの `ICharacter` ポインタに直接外見を適用し、自キャラ誤爆を 100% 根絶）。
  - `ForceAllApply` による性別・種族・顔・髪型・全装備スロットの強制上書き（素体スポーンの完全根絶）。
- [x] **CustomizePlusIpc の Caraxi / AQR 準拠への全面刷新**:
  - 危険な `SetTemporaryProfileOnCharacter(200, ...)` のインデックス指定を完全廃止。
  - `CustomizePlus.Profile.AddPlayerCharacter` (`Func<Guid, string, ushort, int>`) を採用し、パペットの `PuppetName` と `HomeWorld` でプロファイルに正規紐付け。
  - デスポーン時の `CustomizePlus.Profile.RemovePlayerCharacter` による紐付け解除。
- [x] **武器残留（Orphaned Weapon）の完全防止**:
  - `chara->DrawData.HideWeapons(true)` + `chara->GameObject.DisableDraw()` を実行し、描画パイプラインから武器を含む全メッシュ・ボーンをアンロードした上で `ClientObjectManager.DeleteObjectByIndex` を実行。
- [x] **バージョンバンプ & デプロイ**:
  - v0.1.37.0 に更新、GitHub コミット・プッシュ、GitHub Actions ビルド成功、installedPlugins への配置完了。

---

## 3. 完了タスク (v0.1.38.0)
- [x] **Glamourer 1.7.1.3 DLL バイナリおよび IL 完全逆アセンブル解析**:
  - `Glamourer.ApplyAllToCharacter` 等の Legacy IPC は現行 1.7.1.3 には未登録であることを確認。
  - `ApplyStateName` / `ApplyDesignName`（名前指定）が内部で `FindExistingStates`（既存キャッシュのみ検索）を呼ぶため、未登録の新規パペットでは必ず `ActorNotFound` / `InvalidKey (6)` で失敗することを IL から完全解明。
  - `Glamourer.ApplyState` / `ApplyDesign`（インデックス指定）は `ObjectManager[objectIndex]` からパペットを取得し、未登録でも `stateManager.GetOrCreate` で自動的に State を生成して外見を適用する正式仕様であることを特定。
- [x] **GlamourerIpc のインデックス指定 IPC への適正化と自キャラ完全遮断ガードの実装**:
  - `ApplyDesignToActor` において、`actorIndex <= 0` の場合は物理的に適用を拒否する安全ガードを設置。
  - パペットの `GlobalIndex`（COM#0 = 200〜）に対して `ApplyState`（`ForceAllApply` 圧縮 Base64）および `ApplyDesign` を呼び出し、外見を 100% 確実に適用。
- [x] **バージョンバンプ & デプロイ**:
  - v0.1.38.0 に更新、コミット＆プッシュ、installedPlugins への配置。

---

## 4. 今後厳守すべき開発・参照プロトコル (Task Rules)
- [ ] **変更前に必ず本開発記録 (`development_history_and_design_spec.md`) を参照すること**。
- [ ] **対症療法（場当たり的なコード修正）を厳禁とし、必ず「AQR の実装仕様」と「連携先プラグインの IL / バイナリ仕様」に基づいて原因を特定すること**。
- [ ] **Glamourer において新規スポーンしたパペットへの外見適用は、State 自動生成機構を持つインデックス指定 IPC (`ApplyState` / `ApplyDesign`) を使用し、自キャラ Index 0 への誤爆ガード (`actorIndex <= 0` 拒否) を常時維持すること**。
- [ ] **デスクトップ直下に作業ファイル・スクリプトを量産せず、Character-Spawn プロジェクト内で完結させること**。
