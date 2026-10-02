# 実装計画: AQR完全解析・外見適用の2フェーズ完全分離・Proteus外部干渉防御

## 1. 根本原因の分析結果

### 原因 A: `RevertLocalPlayer` のメインスレッド外呼び出し失敗
- **現象**: `Plugin.cs` の初期化時に自キャラを復元しようとしたが、`Not on main thread!` 例外が発生し、初期化スレッドから Glamourer IPC が拒絶されていた。
- **影響**: 前バージョンまでのテストで自キャラが変身していた場合、自キャラが元に戻らないままゲームが継続し、スポーン時にパペットの素体としてその変身姿がコピーされていた。

### 原因 B: アクター生成直後の未登録 Glamourer 呼び出しと Proteus プラグインの誤爆
- **現象**:
  1. `SpawnCharacter` 内（アクター作成直後）で `ApplyAppearanceDirect` を呼んだ際、Glamourer 内部の `ActorObjectManager` にまだ新アクターが登録されていないため、外見適用が正常に行われない。
  2. Glamourer の `ApplyDesign` から発火した `StateFinalized`（DesignApplied）シグナルを検知した外部プラグイン **`Proteus`** が、自キャラの Penumbra コレクション（`GetPlayerCollectionId()`）に対して Lyle Nude 用の MOD を自動適用・Recomposite してしまっていた。

## 2. 修正方針

### 1) メインスレッド上での確実な自キャラ保護・復元 (`RevertLocalPlayer`)
- `ActorManager` に `IFramework` をインジェクト。
- `Framework.RunOnFrameworkThread` で安全に自キャラ復元（Glamourer ロック解除 + `revertCharacter` + `revertState` + `revertToAutomation` + Penumbra Index 0 割り当て解除）を実行。

### 2) 外見適用の2フェーズ完全分離 (Pre-assign vs ReadyJob)
- `ApplyAppearanceDirect` に `bool applyGlamourer = true` フラグを導入。
- **Phase 1 (アクター作成直後)**: `applyGlamourer: false` で実行。ゲームエンジンが DrawObject を構築する前に Penumbra コレクションのみを事前割り当て。
- **Phase 2 (描画準備完了待機 ReadyJob)**: メインスレッド上で `applyGlamourer: true` で実行。アクターが確実にマウントされた状態で Glamourer デザインを適用し、Redraw を実行。

### 3) UI 上での即時復元
- Character タブのアクションボタン並びに「Revert Player」ボタンを追加。
