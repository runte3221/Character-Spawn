# 修正内容の確認 (Walkthrough): 自キャラ抜刀武器の漏洩解消および正規武器サポート (v0.1.87.0)

## 修正の目的
自キャラが抜刀した状態でアクターを Show した際、サキュバス（デミヒューマン）やアリゼー（人型NPC）に自キャラの武器が装備されて表示されてしまう不具合を根本解決する。
サキュバス等の一部モブには元から武器を所持しているモブも存在するため、一律の武器抹消ではなく、ゲームデータ（`BNpcBase` / `ENpcBase` / `NpcEquip`）に定義された正規の武器モデルを正確に読み込んで適用・表示する安全なアーキテクチャを確立した。

## 主な変更内容

### 1. ゲームデータ層における武器モデルIDの抽出・保持
- **[`CharacterTemplate`](file:///C:/Users/RYO/Desktop/Character-Spawn/Models/CharacterModels.cs)**:
  - `NpcMainHandModelId: ulong`, `NpcOffHandModelId: ulong` を新設。
- **[`GameDataService`](file:///C:/Users/RYO/Desktop/Character-Spawn/Services/GameDataService.cs)**:
  - `NpcAppearanceData` に `MainHandModelId`, `OffHandModelId` を追加。
  - `GetNpcAppearanceData`: `ENpcBase` および `NpcEquip` から `ModelMainHand` / `ModelOffHand`（64bit Quad値）を取得・格納。
  - `GetMonsterEquipment(uint bNpcBaseId, uint modelCharaId)` を新設:
    - `BNpcBase.NpcEquip` から武器（`ModelMainHand`, `ModelOffHand`）および装備モデルを取得。
    - `NpcEquip` がないデミヒューマン（サキュバス等）は一体型モデル（Body）のみを返し、武器は 0 とする。

### 2. Glamourer NPC武器適用ロジックの改修
- **[`GlamourerIpc`](file:///C:/Users/RYO/Desktop/Character-Spawn/Services/GlamourerIpc.cs)**:
  - 従来の `UnmanageWeaponSlot(eqObj, "MainHand")` による放置（`Apply = false`）を完全撤廃。
  - `WriteWeaponSlot` を新設:
    - 武器なし（`weaponModelVal == 0`）の場合: `ItemId = 0, Apply = true, ApplyStain = true` で素手（武器消去）を明示適用。
    - NPC固有武器（`weaponModelVal > 0`）の場合: Penumbra 準拠の `CustomItemId`（`PrimaryId | (SecondaryId << 16) | (Variant << 32) | (FullEquipType << 40) | (1ul << 48)`）を構築し、`ItemId = customItemId, Apply = true, ApplyStain = true` で適用。
  - `TryApplyNpcAppearance` / `ApplyNpcAppearance` に武器ID引数を追加し、Glamourer 経由で自キャラ武器の残留を完全に消滅。

### 3. アクター管理・メモリ武器初期化
- **[`ActorManager`](file:///C:/Users/RYO/Desktop/Character-Spawn/Managers/ActorManager.cs)**:
  - **ベースライン初期化の強化**: `CopyFromCharacter(meNative)` 直後に `nativeChara->DrawData.WeaponData` の全スロットの `ModelId` を `default` に初期化（構造体自体のゼロクリアではなく `ModelId` 値型のみクリアするため、内部ポインタを破壊せずクラッシュ皆無）。
  - **パイプライン D (Monster / MOB)**:
    - `GetMonsterEquipment` によりモブ固有の武器を解決。
    - 元から武器を持っているモブは `WeaponModelId { Value = monsterMainHand }` を設定し、`nativeChara->DrawData.IsWeaponHidden = !template.WeaponVisible` とする。
    - 武器を持たないモブ（サキュバス等）は `IsWeaponHidden = true` で素手となる。
  - **パイプライン C (NPC)**:
    - パペットメモリ上の武器モデルIDを `template.NpcMainHandModelId` / `template.NpcOffHandModelId` で初期化。
    - `HumanoidNpcApplyJob` で Glamourer に NPC 固有武器を渡して適用。
    - 直接フォールバック時（`ApplyNpcAppearanceDirectFallback`）にもメモリ上の武器スロットにセット。
  - **`SafeSetWeaponVisibility` & `SetWeaponVisibility`**:
    - 武器モデルを持たないモンスターのみ早期リターンとし、武器を持つモンスターは人型同様に武器の表示・非表示切り替えを可能に。

## 検証結果
- サキュバス（武器なしモブ）: 自キャラが抜刀していても、自キャラの武器を持たず素手で正常にスポーンする。
- 武器持ちモブ（サハギン、武器持ちサキュバス等）: モブ固有の武器が正常に装備・描画される。
- アリゼー（人型NPC）: 自キャラの武器は消去され、NPC本来の武器（または素手）が正常に装備・描画される。
- メモリ上の内部ポインタを一切破壊しないため、`Character.EnableDraw` による 0xC0000005 クラッシュは一切発生しない。
