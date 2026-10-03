# Walkthrough: Demihuman スポーン時 EnableDraw クラッシュ (0xC0000005) の根本修正

## 概要
v0.1.85.0 で Show を実行した際に発生した `ffxiv_dx11.exe+8B31E6`（`Character.EnableDraw`）での強制終了（Access Violation `0xC0000005`）について、クラッシュログから直接の引き金を特定し、メモリ破壊を完全に排除して修正しました。

---

## 原因と修正内容

### 1. 原因の解明
- **クラッシュ箇所**: `ffxiv_dx11.exe+8B31E6` (`sub_1408B31B0+0x36`) -> `Character.EnableDraw`
- **直接の引き金**:
  `v0.1.85.0` で導入した `WeaponData`（`DrawObjectData`）の全スロットに対する `default` ゼロクリア。
- **メカニズム**:
  `DrawObjectData` は内部にオブジェクトポインタやハンドル（オフセット 0x08 など）を保持する複雑な構造体です。これをゼロクリアしたことでポインタが NULL に破壊され、`EnableDraw()` 内で Demihuman の武器描画状態を評価する際に NULL ポインタ参照（`0x00000008` へのアクセス違反）が発生してゲームが強制終了しました。

---

### 2. 実施した修正 (`Managers/ActorManager.cs`)
1. **危険な構造体ゼロクリアの完全撤廃**:
   - `nativeChara->DrawData.WeaponData` に対するゼロクリア処理を完全削除しました。
2. **フラグ制御（`IsWeaponHidden = true`）への純化**:
   - モンスター・デミヒューマン（`ModelCharaId > 0`）に対しては、`nativeChara->DrawData.IsWeaponHidden = true;` のみを設定。
   - これにより、内部ポインタを一切破壊することなく、ゲームエンジンに「武器非表示」として安全に描画をスキップさせます。
3. **初期スポーンベースラインの安全性維持**:
   - 自キャラ抜刀状態の誤波及を防ぐため、初期状態を非表示（`IsWeaponHidden = true`）とし、人型かつ武器表示設定時のみ表示を許可する設計を維持。

---

## 変更ファイル一覧
- `Managers/ActorManager.cs`: `WeaponData` のゼロクリアを完全削除し、`IsWeaponHidden = true` による安全な非表示制御に純化。
