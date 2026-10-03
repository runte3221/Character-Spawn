# Walkthrough: 自キャラ抜刀時のモンスター・デミヒューマンへの武器波及バグ修正

## 概要
自キャラが抜刀した状態で「Show」を行った際、サキュバス（デミヒューマン）等のモンスターが自キャラと同じ武器を右手に持ってしまう不具合をログ解析により特定し、根本原因を修正しました。

---

## 原因と修正内容

### 1. 原因の解明
- **自キャラからの素体コピー**:
  アクター生成時に 3D 骨格を確立するため `CopyFromCharacter(meNative)` を実行しています。自キャラが抜刀している場合、自キャラの武器モデル情報（MainHand/OffHand の `WeaponData`）と抜刀表示フラグ（`IsWeaponHidden = false`）がアクターにコピーされます。
- **パイプライン D での武器クリア漏れ**:
  モンスター・デミヒューマン（`ModelCharaId > 0`）のスポーン処理において、防具（`EquipmentModelIds`）はゼロクリアされていましたが、武器データ（`WeaponData`）はゼロクリアされておらず、武器非表示フラグ（`IsWeaponHidden = true`）も人型限定の条件分岐内にあったため実行されていませんでした。
- **デミヒューマンの右手ボーンへのアタッチ**:
  サキュバスは人型に近いボーン構造（右手の武器アタッチポイント）を持つため、残留した武器データと抜刀フラグによって、自キャラの抜刀武器が右手に描画されていました。

---

### 2. 実施した修正 (`Managers/ActorManager.cs`)

1. **モンスター・デミヒューマンにおける武器データの完全消去**:
   - パイプライン D において、`nativeChara->DrawData.IsWeaponHidden = true;` を明示的に設定。
   - `nativeChara->DrawData.WeaponData` の全要素を `default` でゼロクリアし、武器モデル情報そのものを物理的に消去。
2. **ベースライン確立時の武器非表示デフォルト化**:
   - `SpawnCharacter` 冒頭のリセット処理において、初期状態を `IsWeaponHidden = true` に設定。人型かつ `WeaponVisible == true` の場合のみ後続で表示を許可する防御的構造に改修。
3. **安全ヘルパー関数のガード強化**:
   - `SafeSetWeaponVisibility` および `SetWeaponVisibility` において、`ModelCharaId != 0`（モンスター）の場合は `IsWeaponHidden = true` を設定して即リターンする安全網を確立。

---

## 変更ファイル一覧
- `Managers/ActorManager.cs`: パイプライン D での `WeaponData` ゼロクリア ＆ `IsWeaponHidden = true` の強制、ベースラインおよび安全ヘルパーの防御ガード強化。
