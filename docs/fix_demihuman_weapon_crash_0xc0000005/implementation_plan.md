# Implementation Plan: Demihuman スポーン時 EnableDraw クラッシュ (0xC0000005) の根本修正

## 1. クラッシュログの詳細解析

### 1.1 クラッシュダンプ解析 (`dalamud_appcrash_20261004_030436_285_29884.log`)
```
Unhandled native exception occurred at ffxiv_dx11.exe+8B31E6 (sub_1408B31B0+0x36)
Code: C0000005 (Access Violation)
Address: 7FF67FC631E6
Parameters: 0, 8 (Attempted to read address 0x00000008)

Call Stack:
  [0] ffxiv_dx11.exe+8B31E6 (sub_1408B31B0+0x36)
  [1] ffxiv_dx11.exe+90718F (Client::Game::Character::Character.EnableDraw+0x22F)
  [2] Penumbra.Interop.Hooks.Objects.EnableDraw.Detour
  [6] Glamourer.Interop.EnableDrawEvent.Detour

Registers:
  RAX: 0
  RDX: 2E800885110 (vtbl_Client::Graphics::Scene::Demihuman)
  RBP: 2E418972F78 (vtbl_Client::Game::Character::DrawDataContainer)
```

### 1.2 根本原因の特定
- `v0.1.85.0` において、抜刀武器の消去を目的として `nativeChara->DrawData.WeaponData` の全要素を `default`（全て 0）で塗りつぶす処理を導入した。
- `DrawObjectData`（約112バイト）は単純なモデルID値の構造体ではなく、ゲームエンジン内部のリソースポインタや内部オブジェクトハンドル（オフセット 0x08 など）を保持する。
- これを `default` でゼロクリアしたため、内部ポインタが NULL（0）に破壊された。
- その結果、`EnableDraw()` が Demihuman の描画オブジェクトを更新する際、`sub_1408B31B0+0x36` で `[RAX+8]`（`RAX=0`）を参照して **Access Violation 例外（0xC0000005）が発生し、ゲームが強制終了**した。

---

## 2. 修正方針

### 2.1 危険な構造体ゼロクリアの完全撤廃 (`Managers/ActorManager.cs`)
- `weaponSpan[i] = default;` によるメモリゼロクリアを完全削除。
- 武器データの内部ポインタには一切手を触れず、ゲームエンジン本来のメモリ状態を維持する。

### 2.2 安全なフラグ制御（`IsWeaponHidden = true`）への純化
- モンスター・デミヒューマン（`ModelCharaId > 0`）に対しては、`nativeChara->DrawData.IsWeaponHidden = true;` のみを行う。
- ゲームエンジンは `IsWeaponHidden == true` を検知すると武器の描画を行わないため、ポインタを破壊することなく安全に武器を非表示にできる。

### 2.3 スポーン初期ベースラインの防御構造維持
- `SpawnCharacter` 冒頭での `nativeChara->DrawData.IsWeaponHidden = true;`（デフォルト非表示）を維持し、自キャラ抜刀状態の誤波及を防止。
- 人型アクター（`ModelCharaId == 0`）かつ `template.WeaponVisible == true` の場合のみ後続で表示を許可。

---

## 3. 検証項目
1. Show 実行時にゲームがクラッシュ（0xC0000005）せず、正常に全アクターがスポーンすること。
2. 自キャラが抜刀している状態でも、オルト・サキュバス（デミヒューマン）の右手に自キャラの武器が表示されないこと。
3. 人型アクターで武器表示 ON のアクターには通常通り武器が表示されること。
