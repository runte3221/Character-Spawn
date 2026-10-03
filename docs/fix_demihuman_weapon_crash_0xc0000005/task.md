# Task: Demihuman スポーン時 EnableDraw クラッシュ (0xC0000005) の根本修正

## 目的
v0.1.85.0 で Show を実行した際に発生した `ffxiv_dx11.exe+8B31E6`（`Character.EnableDraw`）での Access Violation 例外（`0xC0000005`）の根本原因を特定し、完全修正する。

---

## タスクリスト

- [x] **1. クラッシュログおよびダンプの解析**
  - [x] `dalamud_appcrash_20261004_030436_285_29884.log` の解析:
    - 例外: `C0000005` at `ffxiv_dx11.exe+8B31E6` (`sub_1408B31B0+0x36`)
    - コールスタック: `Character.EnableDraw` -> `sub_1408B31B0+0x36`
    - レジスタ: `RAX = 0`, `Parameters: 0, 8`（アドレス 0x8 へのアクセス違反）
    - オブジェクト: `RDX = Demihuman DrawObject`, `RBP = DrawDataContainer`
  - [x] 原因特定: `v0.1.85.0` で追加した `nativeChara->DrawData.WeaponData` のゼロクリア（`weaponSpan[i] = default`）により、`DrawObjectData` が保持していた内部リソースポインタが NULL に破壊され、`EnableDraw` が Demihuman の武器描画状態を評価する際に `NULL + 8` を参照して即時クラッシュしていたことを突き止めた。

- [x] **2. 危険な構造体ゼロクリアの完全撤廃 ＆ フラグ制御への純化 (`Managers/ActorManager.cs`)**
  - [x] `nativeChara->DrawData.WeaponData` のゼロクリア処理を完全削除
  - [x] 武器の非表示は `nativeChara->DrawData.IsWeaponHidden = true;` の安全なフラグ制御のみで行うよう修正（ポインタ破壊を完全防止）
  - [x] モンスターに対する `SafeSetWeaponVisibility` / `SetWeaponVisibility` の安全ガードを維持

- [x] **3. リリース ＆ CI/CD 検証**
  - [x] `tools/bump-version.ps1 0.1.86.0` 実行
  - [x] `CHANGELOG.md` 追記
  - [x] ドキュメント同期（`task.md`, `implementation_plan.md`, `walkthrough.md`）
  - [x] Git コミット ＆ プッシュ
  - [x] GitHub Actions CI/CD ビルド完了確認
