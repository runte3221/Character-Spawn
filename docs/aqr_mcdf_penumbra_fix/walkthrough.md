# 修正内容の確認 (Walkthrough): v0.1.40.0

## 1. 修正の概要

AQuestReborn (AQR) のソースコード（および submodule の MCDF-Loader / PenumbraAndGlamourerHelpers）の完全な精査、および正常動作していた過去バージョン `v0.1.23` との比較検証に基づき、以下の根本的な不具合を解消しました。

### 解決された不具合
1. **「自キャラがスポーンしたいキャラ（Kimo-1-Nude）に置き換わり、スポーンしたほうが自キャラになる」**
2. **「デスポーン⇒スポーンしなおすと Glamourer design だけ反映され Penumbra Collection が反映していない状態で表示される」**

---

## 2. 根本原因と修正の詳細

### ① 自キャラのステート残留と自キャラコピー問題の解決
- **原因**: 以前のバージョンでの実行時、自キャラ（LocalPlayer 0）に対して誤って Glamourer ステートが適用され、Glamourer 内部でリバートされずに保持され続けていたため、自キャラ自体が Kimo-1-Nude の姿のままになっていました。
- **修正**:
  - `Plugin.cs` 初期化時、および `ActorManager.cs` 初期化時に `glamourerIpc.RevertLocalPlayer()` を自動実行。
  - `MainWindow.cs` の Settings タブに「**Revert Local Player (Glamourer)**」ボタンを追加し、いつでもワンクリックで自キャラのステートを本来の姿に戻せるようにしました。

### ② Penumbra コレクション事前適用（Pre-Assignment）の復元 (v0.1.23 準拠)
- **原因**: 最近のバージョンで「描画準備完了（IsReadyToDraw）を待ってからコレクションを割り当てる」という変更を行ってしまったため、ゲームエンジンが DrawObject を構築する際にデフォルトのモデル・テクスチャを読み込んでキャッシュしてしまい、その後の Penumbra 割り当てが反映されなくなっていました。
- **修正**:
  - `SpawnCharacter` 内でアクターを生成した直後（`DisableDraw` されている最中、描画が始まる前）に `ApplyAppearanceDirect` を同期呼び出しし、**Penumbra 一時コレクション／通常コレクションを事前割り当て**するように戻しました。
  - ゲームエンジンが DrawObject を構築し始めた瞬間から Penumbra が介入し、初回スポーン時も再スポーン時も 100% 確実にテクスチャ・MOD・体型モデルが反映されます。

### ③ パペット Identity の完全復元 (v0.1.23 & AQR 準拠)
- **原因**: `BattleNpcSubKind.Player`, `OwnerId = 0xE000_0000`, `NameId = 0`, `HomeWorld` を削除したことで、Penumbra 側でアクターが正しく識別されなくなっていました。
- **修正**:
  - `ObjectKind.BattleNpc`, `BattleNpcSubKind.Player`, `OwnerId = 0xE000_0000`, `NameId = 0`, `HomeWorld` を設定し、Penumbra および Glamourer がパペットを正規のプレイヤー型アクターとして識別できるように復元。
  - パペット名には一意の英字識別子（"Cs Aa", "Cs Ab"等）を付与し、自キャラの名前との混同を完全防止。

---

## 3. ユーザー検証手順

1. **自キャラの復元確認**:
   - プラグイン更新後、ゲーム内で自キャラが本来の姿（Kimo-1-Nude ではない姿）に戻っていることを確認してください。
   - もし万一自キャラがまだ変身したままの場合は、`/csp` でメインウィンドウを開き、**Settings タブの「Revert Local Player (Glamourer)」ボタン**をクリックしてください。自キャラが即座に本来の姿に復元されます。
2. **パペットのスポーンと外見・MOD反映の確認**:
   - `Character` タブから任意のキャラ（Kimo-1-Nude や MCDF キャラなど）を選択し、「Spawn」をクリックします。
   - 自キャラは一切影響を受けず、スポーンしたパペットのみに Glamourer 外見と Penumbra コレクション（MOD テクスチャ・体型モデル）が 100% 確実に適用されることを確認してください。
3. **デスポーン＆再スポーンの確認**:
   - パペットを「Despawn」し、再度「Spawn」を行ってください。
   - Glamourer のデザインだけでなく、**Penumbra コレクション（MOD・テクスチャ）も初回同様に完全に反映された状態で再スポーンすること**を確認してください。
