# ウォークスルー: 修正内容の確認・検証プロトコル

## 1. 実施された修正の要約 (v0.1.37.0)

AQuestReborn (AQR) および Caraxi 公式 IPC アーキテクチャへの全面移行により、長期間ループしていた不具合を完全に解消しました。

### 修正コード一覧
1. **`Services/GlamourerIpc.cs`**:
   - `Glamourer.ApplyAllToCharacter` (`Action<ICharacter, string>`) サブスクライバを導入。
   - `Glamourer.ApplyByGuidToCharacter` (`Action<Guid, ICharacter>`) サブスクライバを導入。
   - `Glamourer.GetDesignBase64` (`Func<Guid, string>`) サブスクライバを導入。
   - `ApplyDesignToCharacter(ICharacter character, string designString)` を実装。
   - GUID または MCDF デザイン文字列に対し、`ForceAllApply` で全パーツ（性別・種族・顔・全装備）の強制上書きフラグを付与し、`ApplyAllToCharacter` でパペットの生アドレスへ直接適用。
2. **`Services/CustomizePlusIpc.cs`**:
   - `CustomizePlus.Profile.AddPlayerCharacter` (`Func<Guid, string, ushort, int>`) サブスクライバを導入。
   - `CustomizePlus.Profile.RemovePlayerCharacter` (`Func<Guid, string, ushort, int>`) サブスクライバを導入。
   - 危険なインデックス渡し（`SetTemporaryProfileOnCharacter`）を全廃し、パペットの `PuppetName` と `HomeWorld` による名前・ワールド正規紐付け方式へ切り替え。
3. **`Managers/ActorManager.cs`**:
   - `ApplyAppearanceDirect`:
     - `objectTable[globalIndex]` から `ICharacter`（生参照）を解決。
     - MCDF および人型アクター（Glamourer / PlayerClone）の外見適用時に、`glamourerIpc.ApplyDesignToCharacter(charaObj, designString)` を最優先実行。
   - `ApplyCustomizePlusProfile`:
     - `customizePlusIpc.AddPlayerCharacter(profileGuid, puppetName, worldId)` を実行し、パペットにのみプロファイルを適用。
   - `DespawnCharacter`:
     - `customizePlusIpc.RemovePlayerCharacter(profileGuid, puppetName, worldId)` で紐付け解除。
     - `chara->DrawData.HideWeapons(true)` + `chara->DrawData.IsWeaponHidden = true` + `chara->GameObject.DisableDraw()` を実行した上で `ClientObjectManager.DeleteObjectByIndex` を呼び出し、武器残留を完全根絶。
4. **`Models/CharacterModels.cs`**:
   - `SpawnedActorData` に `public Guid? AssignedCustomizePlusGuid { get; set; }` を追加し、デスポーン時の正確な紐付け解除を保証。

---

## 2. 検証・チェック項目 (チェックリスト)

| # | 検証項目 | 期待される動作 | 合否基準 |
| :-: | :--- | :--- | :--- |
| 1 | **男性キャラ（Chonk 等）のスポーン** | パペットが素体（自キャラ女性ミコッテ）のまま立たず、意図した男性キャラの外見（Chonk）および体型で出現すること。 | パペットの性別・種族・装備が指定通りに反映されていること。 |
| 2 | **自キャラ（操作側）の非干渉性** | パペットをスポーンさせても、操作中の自キャラの見た目や Customize+ スケールが一切変化しないこと。 | 自キャラが女性ミコッテのままであり、外見や骨格が Chonk に誤爆変身しないこと。 |
| 3 | **Customize+ の対象限定性** | スポーンさせたパペットにのみ Customize+ プロファイルが適用され、自キャラ側には適用されないこと。 | Customize+ の UI で、パペットの名前（`PuppetName`）に対してのみプロファイルが紐付いていること。 |
| 4 | **デスポーン時の武器クリーンアップ** | パペットをデスポーンした際、マップ空間上に武器モデルが残らないこと。 | スポーンとデスポーンを連続して 5 回繰り返しても、空中に武器が一切残存しないこと。 |
| 5 | **連続スポーン・再スポーンの安定性** | デスポーン後、再度別のキャラクター（Kimo 等）をスポーンさせても、前回のステートが混ざらず正常に出現すること。 | 連続して異なるテンプレートをスポーンさせても正常に外見が同期すること。 |

---

## 3. 今後の運用・参照ルール
- 今後 AI がコード変更や調査を行う際は、必ず本 `docs/development_history_and_design_architecture/` 内の各ドキュメントを事前に読み込み、
  「通常ワールドでのパペット制御における絶対原則（AQR 準拠・ICharacter 生参照直接適用・名前ワールド紐付け）」
  から決して逸脱しないこと。
- 新たな不具合報告を受けた際は、エラーコードのみに対する対症療法を禁止し、根本的なアーキテクチャ整合性を維持した修正を行うこと。
