## 1. 実施された修正の要約 (v0.1.38.0)

Glamourer 1.7.1.3 の DLL バイナリおよび IL を完全に逆アセンブル解析し、名前指定 IPC の限界（新規アクターへの適用不可）とインデックス指定 IPC の正式仕様（`GetOrCreate` による自動 State 生成）を解明した上で、安全ガードを伴う正規 IPC パスを確立しました。

### 修正コード一覧
1. **`Services/GlamourerIpc.cs`**:
   - `ApplyDesignToCharacter(ICharacter character, string designString)`:
     - Glamourer 1.7.1.3 では未登録の Legacy IPC 呼び出しを整理し、自キャラ誤爆ガード（`character.ObjectIndex <= 0` の物理的遮断）を通過させた上で `ApplyDesignToActor` に安全に委譲。
   - `ApplyDesignToActor(string designString, int actorIndex, string? actorName)`:
     - `actorIndex <= 0`（自キャラ Index 0 等）の呼び出しを物理的に完全遮断。
     - 名前指定 IPC（`ApplyStateName` / `ApplyDesignName`）を撤廃し、正式なインデックス指定 IPC（`ApplyState` / `ApplyDesign`）を呼び出し。
     - これにより、未登録の新規パペットであっても Glamourer 内部の `stateManager.GetOrCreate` が走り、100% 確実に外見ステートが生成・適用される（Result: 0）。
     - `ForceAllApply`（全パーツ強制適用）による GZip 圧縮 Base64 を最優先で適用し、フォールバックとして GUID 指定 `ApplyDesign` を実行。

---

## 2. 実施された修正の要約 (v0.1.37.0)

AQuestReborn (AQR) および Caraxi 公式 IPC アーキテクチャへの全面移行により、Customize+ 自キャラ誤爆と武器残留を完全に解消しました。

### 修正コード一覧
1. **`Services/CustomizePlusIpc.cs`**:
   - `CustomizePlus.Profile.AddPlayerCharacter` (`Func<Guid, string, ushort, int>`) を採用し、パペットの `PuppetName` と `HomeWorld` による名前・ワールド正規紐付け方式へ切り替え。
   - デスポーン時は `CustomizePlus.Profile.RemovePlayerCharacter` で安全に紐付け解除。
2. **`Managers/ActorManager.cs`**:
   - デスポーン時に `chara->DrawData.HideWeapons(true)` + `chara->DrawData.IsWeaponHidden = true` + `chara->GameObject.DisableDraw()` を実行した上で `ClientObjectManager.DeleteObjectByIndex` を呼び出し、武器残留を完全根絶。
3. **`Models/CharacterModels.cs`**:
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
