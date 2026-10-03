# 実装計画: 自キャラ抜刀武器の漏洩解消および正規NPC・モブ武器サポート

## 背景と問題の所在
1. **問題現象**:
   - 自キャラが武器を抜刀した状態でアクターを Show（スポーン）すると、サキュバス（デミヒューマン）やアリゼー（人型NPC）が自キャラの抜刀武器を持った状態で出現する。
2. **原因の解明**:
   - `ActorManager.cs`: アクター生成時に drawable 骨格を確立するため、全アクターが無条件で `nativeChara->CharacterSetup.CopyFromCharacter(meNative)` されている。自キャラが抜刀していると、自キャラの武器モデルIDや抜刀ステートがパペットのメモリにコピーされる。
   - `GlamourerIpc.cs`: 人型NPCの適用時、`UnmanageWeaponSlot` により武器スロットを `Apply = false` にしていた。このため、初期メモリに残った自キャラの武器がそのまま表示されていた。
   - `ActorManager.cs` (Pipeline D): モンスター生成時、防具スロット（`EquipmentModelIds`）はゼロクリアしていたが、`WeaponData` のモデルIDはクリアされていなかった（前回は構造体まるごとゼロクリアしたため内部ポインタ破壊による 0xC0000005 クラッシュを招いた）。
   - サキュバス等の一部モブには「元から武器を持っているモブ」が存在するため、一律消去ではなく、`BNpcBase` / `NpcEquip` に定義された正規の武器モデルを正確に読み込んでセットする必要がある。

## 変更計画

### 1. ゲームデータ層の武器モデルID取得・保持
- `Models/CharacterModels.cs`:
  - `CharacterTemplate` に `NpcMainHandModelId: ulong`, `NpcOffHandModelId: ulong` を追加。
- `Services/GameDataService.cs`:
  - `NpcAppearanceData` に `MainHandModelId: ulong`, `OffHandModelId: ulong` を追加。
  - `GetNpcAppearanceData`: `ENpcBase` または `NpcEquip` から `ModelMainHand` / `ModelOffHand` を取得。
  - `GetMonsterEquipment(uint bNpcBaseId, uint modelCharaId)` を新設:
    - `BNpcBase.NpcEquip` から `ModelMainHand`, `ModelOffHand`, および装備スロットを取得。
    - `NpcEquip` がない場合は `GetDemiHumanEquipment(modelCharaId)` でフォールバックし、武器は 0 とする。

### 2. Glamourer NPC武器適用ロジックの改修
- `Services/GlamourerIpc.cs`:
  - `UnmanageWeaponSlot` による `Apply = false` を廃止。
  - `WriteWeaponSlot(JObject equip, string key, ulong weaponModelVal, bool isOffhand)` を追加:
    - `weaponModelVal == 0`: `ItemId = 0, Apply = true, ApplyStain = true` で素手（武器なし）を強制適用。
    - `weaponModelVal > 0`: `CustomItemId` を計算して `ItemId = customItemId, Apply = true, ApplyStain = true` でNPC固有武器を適用。
  - `TryApplyNpcAppearance` の引数に武器モデルIDを受け取り、Glamourer 経由で自キャラ武器を確実にパージ。

### 3. アクター管理・パイプラインのメモリ武器初期化
- `Managers/ActorManager.cs`:
  - Pipeline D (Monster):
    - `nativeChara->DrawData.WeaponData` の各要素の `ModelId` を `default` に初期化（ポインタは破壊しない）。
    - モンスター固有の武器がある場合（`monsterMainHand > 0`）は `new WeaponModelId { Value = monsterMainHand }` を設定し、`nativeChara->DrawData.IsWeaponHidden = !template.WeaponVisible` とする。
    - 武器がない場合は `nativeChara->DrawData.IsWeaponHidden = true`。
  - Pipeline C (NPC):
    - パペットメモリ内の `WeaponData` の `ModelId` をリセットし、NPC固有武器があればセット。
    - `HumanoidNpcApplyJob` で `template.NpcMainHandModelId` / `template.NpcOffHandModelId` を Glamourer に渡して適用。
    - フォールバック時にもメモリ上の `WeaponData[0].ModelId` をセット。
  - `SafeSetWeaponVisibility`:
    - モンスターであっても武器モデル（`WeaponData[0].ModelId.Value > 0`）を持つ場合は表示・非表示の切り替えを許可。

### 4. バージョン更新・ドキュメント同期・CI/CD
- `tools/bump-version.ps1 0.1.87.0`
- `CHANGELOG.md` 追記
- `docs/fix_weapon_leak_player_to_spawned_actors/walkthrough.md` 作成
- コミット＆プッシュ、GitHub Actions CI/CD ビルド完了確認
