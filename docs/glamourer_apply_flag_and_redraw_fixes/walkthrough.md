# 修正確認 (Walkthrough): Glamourer 適用フラグ適正化と外見巻き戻り防止 (v0.1.27)

## 実施した変更

### 1. `Services/GlamourerIpc.cs`
- `ApplyDesignToActor`、`ApplyState`、`ReapplyState` において、Glamourer IPC に渡す flags を従来の `7UL` / `7U`（`Once | Equipment | Customization`）から **`6UL` / `6U`（`Equipment | Customization`、Onceなし）** に変更しました。
- これにより、Glamourer のアクター内部 State が確実に目的のデザインへ書き換わるようになり、Penumbra の再描画（Redraw）が走っても自キャラ（Ruma Meow）の外見に巻き戻ることがなくなりました。

### 2. `Managers/ActorManager.cs`
- `Penumbra.SetCollectionForActor` を実行した直後に呼んでいた余計な `penumbraIpc.Redraw` を削除し、Glamourer 適用後の最終 Redraw のみに整理しました。
- MCDF に内包される `CustomizePlusData` が Base64 エンコード文字列（`eyJCb25lcyI6...`）である場合を自動検知し、UTF-8 JSON 文字列に復号してから CustomizePlus IPC に渡す処理を追加しました。

---

## ユーザー側の確認手順

ゲーム内で以下の操作を行い、外見が意図通りに反映されるかご確認ください。

1. **プラグインの再読み込み**:
   - ゲーム内チャットで `/xlplugins` を開き、Character Spawn をリロード（または無効化→有効化）してください。
2. **Glamourer & Penumbra プリセットの検証**:
   - 男性キャラなどの別キャラクター（`Chonk`）を選択してスポーンさせます。
   - **確認項目**: 自キャラ（Ruma Meow）に戻ることなく、`Chonk` の外見・装備・MOD が維持されて描画されること。
3. **MCDF プリセットの検証**:
   - `test.mcdf` をロードしてスポーンさせます。
   - **確認項目**: 自キャラに戻ることなく、`test.mcdf` の外見およびボーン調整（CustomizePlus）が反映されること。
