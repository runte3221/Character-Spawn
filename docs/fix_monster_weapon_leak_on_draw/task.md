# Task: 自キャラ抜刀時のモンスター・デミヒューマンへの武器波及バグ修正

## 目的
自キャラが武器を抜刀した状態で「Show」を行った際、サキュバス（デミヒューマン）等のモンスターが自キャラと同じ武器を手に持ってしまう不具合を根本原因から解析・修正する。

---

## タスクリスト

- [x] **1. ログ解析および根本原因の特定**
  - [x] Dalamud ログ（`2026-10-04 02:52:40`）の解析: サキュバス（ModelChara: 18）のスポーンフローと武器データの流れを確認
  - [x] 原因特定: `SpawnCharacter` での `CopyFromCharacter(meNative)` 実行時、自キャラの抜刀武器データ（`WeaponData`）および抜刀表示フラグ（`IsWeaponHidden = false`）がパペットにコピーされる一方、パイプライン D（モンスター・デミヒューマン）において防具（`EquipmentModelIds`）しかゼロクリアされておらず、武器データ（`WeaponData`）がそのまま残留していたことを解明。

- [x] **2. 武器データの完全ゼロクリア ＆ 武器非表示の強制 (`Managers/ActorManager.cs`)**
  - [x] パイプライン D（`ModelCharaId > 0`）において、`nativeChara->DrawData.IsWeaponHidden = true;` を強制設定
  - [x] `nativeChara->DrawData.WeaponData` の全スロット（MainHand, OffHand 等）を `default` でゼロクリア
  - [x] `SpawnCharacter` 冒頭のベースライン設定において、自キャラの抜刀状態の波及を防ぐため `nativeChara->DrawData.IsWeaponHidden = true;` をデフォルト設定（人型かつ `template.WeaponVisible` の時のみ表示）
  - [x] `SafeSetWeaponVisibility` および `SetWeaponVisibility` において、`ModelCharaId > 0` のモンスターに対する安全ガードを強化（`IsWeaponHidden = true` をセットして即リターン）

- [x] **3. リリース ＆ CI/CD 検証**
  - [x] `tools/bump-version.ps1 0.1.85.0` 実行
  - [x] `CHANGELOG.md` 追記
  - [x] ドキュメント同期（`task.md`, `implementation_plan.md`, `walkthrough.md`）
  - [x] Git コミット ＆ プッシュ
  - [x] GitHub Actions CI/CD ビルド完了確認
