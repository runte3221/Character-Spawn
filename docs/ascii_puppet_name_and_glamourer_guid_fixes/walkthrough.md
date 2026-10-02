# ウォークスルー: ASCII英字パペット名生成とGlamourer Guid適用の堅牢化 (v0.1.31)

## 修正の確認

### 1. アクター名の英字規則適合
- [x] パペット名が `Csp {template.Id}` の数字混入から、英字のみで構成された名前（例: `Csp Evjkkhzl`）に変更されました。
- [x] FF14 の `VerifyPlayerName` を 100% 通過するため、Penumbra, Glamourer, CustomizePlus が正常にアクターを認識できるようになります。

### 2. Glamourer Guid デザインの完全適用
- [x] Guid 指定のデザインも `ForceAllApply` を施した上で `ApplyState` を実行し、全スロットが確実に適用されます。
- [x] ネイティブの `CustomizeData`（26バイト）が直接同期されます。

## ユーザー動作確認手順
1. Character Spawn を最新版（**v0.1.31**）に更新するか、プラグインを再読み込みしてください。
2. 作成した `Chonk` を選択し、**Spawn** をクリックします。
   - 自キャラの見た目にならず、男性キャラクター（Chonk）の見た目・体型・コレクションが正常に適用されることを確認します。
3. `Ruma` などの他のキャラクターも同様に正常にスポーン・デスポーンできることを確認します。
