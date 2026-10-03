# Implementation Plan: 自キャラ抜刀時のモンスター・デミヒューマンへの武器波及バグ修正

## 1. 現象とログ解析

### 1.1 報告された現象
- 自キャラが武器を抜刀（Draw Weapon）した状態で「Show」を行うと、オルト・サキュバス（デミヒューマン）が自キャラと同じ武器（剣や杖など）を右手に持った状態で描画されてしまう。

### 1.2 ログ解析（`dalamud.log` 02:52:40）
```
[INF] [CharacterSpawn] Spawning 'オルト・サキュバス' (Source: Monster, ModelChara: 18, Scale: 1, Weapon: False) at COM#12...
[INF] [CharacterSpawn] Populated DemiHuman equipment for 'オルト・サキュバス' (ModelChara: 18).
[INF] [CharacterSpawn] [Pipeline D: Monster] Spawned 'オルト・サキュバス' on Global#212. Enqueued to MonsterRedrawJob.
```
- ログ上では `Weapon: False` として認識されており、エラー自体は発生していない。

### 1.3 根本原因の特定
1. **初期スケルトン確立時のディープコピー**:
   - `SpawnCharacter` では、ゲームエンジン内で安全に 3D モデルを描画可能にするため、アクター生成直後に `meNative`（自キャラ）からスケルトン・骨格情報を `CopyFromCharacter(meNative, CharacterCopyFlags.WeaponHiding)` でコピーしている。
   - 自キャラが抜刀している場合、`meNative->DrawData` には自キャラの武器モデル情報（MainHand/OffHand の `WeaponData`）が格納されており、かつ `IsWeaponHidden` は `false`（抜刀・表示中）となっている。
2. **モンスター処理（パイプライン D）での武器クリア漏れ**:
   - `ActorManager.cs` のパイプライン D（`ModelCharaId > 0`）では、防具装備モデル（`EquipmentModelIds`）はゼロクリアされていたが、**武器データ（`DrawData.WeaponData`）はゼロクリアされていなかった**。
   - さらに、`nativeChara->DrawData.IsWeaponHidden = !template.WeaponVisible;` は人型アクター（`ModelCharaId == 0`）の条件分岐内にしか存在しなかったため、**モンスターでは `IsWeaponHidden` の変更すら行われず、自キャラの抜刀状態（`false`）がそのまま維持されていた**。
3. **デミヒューマン（McType == 2）の骨格特性**:
   - サキュバスなどのデミヒューマンは、通常のモンスター（獣など）と異なり人型に近いボーン構造（右手・左手の武器アタッチポイント）を持つ。
   - そのため、残留した武器データと抜刀表示フラグにより、ゲームエンジンが「サキュバスの右手に武器モデルを描画する」と判断して武器がアタッチされてしまっていた。

---

## 2. 修正方針

### 2.1 パイプライン D における武器データの完全ゼロクリア ＆ 武器非表示の強制 (`Managers/ActorManager.cs`)
```csharp
if (template.ModelCharaId > 0)
{
    nativeChara->GameObject.DisableDraw();
    nativeChara->ModelContainer.ModelCharaId = (int)template.ModelCharaId;
    nativeChara->GameObject.Scale = targetScale;

    // モンスター・デミヒューマンは武器を持たないため、武器描画フラグを非表示に強制し、
    // 自キャラからコピーされた武器データ（抜刀中の武器モデル等）を完全にゼロクリア
    nativeChara->DrawData.IsWeaponHidden = true;
    var weaponSpan = nativeChara->DrawData.WeaponData;
    for (int i = 0; i < weaponSpan.Length; i++)
    {
        weaponSpan[i] = default;
    }
...
```

### 2.2 ベースライン初期化時の防御的デフォルト設定
- `SpawnCharacter` 冒頭において、自キャラの抜刀状態が後続のアクターに波及しないよう、初期状態を `nativeChara->DrawData.IsWeaponHidden = true;` に設定。
- 人型アクター（`ModelCharaId == 0`）かつ `template.WeaponVisible == true` の場合のみ表示を許可。

### 2.3 `SafeSetWeaponVisibility` および `SetWeaponVisibility` の安全網強化
- `ModelCharaId != 0`（モンスター）の場合は、武器操作関数が呼ばれた場合でも `IsWeaponHidden = true` を設定して安全に即時リターン。

---

## 3. 検証項目
1. 自キャラが抜刀した状態で Show を実行しても、オルト・サキュバスなどのデミヒューマンやモンスターに武器が表示されないこと。
2. 自キャラが納刀した状態でも、通常の人型アクター（武器表示 ON の設定時）には意図通り武器が表示されること。
3. 武器の切り替えやスポーン/デスポーン時に DirectX エラーやクラッシュが発生しないこと。
