# タスク: ASCII英字パペット名生成とGlamourer Guid適用の堅牢化 (v0.1.31)

## 課題
- 新しく `Chonk` を作成してスポーンしたが、自キャラ（ベースライン）のままスポーンされてしまう。

## 根本原因
1. **パペット名への数字混入による FF14 名前検証エラー**:
   - v0.1.30 でパペット名を `Csp {template.Id[..8]}`（例: `Csp 04ffbf3e`）に変更した。
   - FF14 のエンジンおよび Penumbra / Glamourer / CustomizePlus の仕様上、プレイヤーキャラクター名に数字は一切使用できない（`VerifyPlayerName` で InvalidActor 判定）。
   - その結果、Penumbra (`ec=16`), Glamourer (`result=2`), CustomizePlus (`ec=255`) の全プラグインがアクターを認識できず、外見適用がすべてスキップされて自キャラのままになっていた。
2. **Glamourer Guid 適用時のフォールスルー**:
   - Guid 指定時、`ApplyDesign(Guid)` が失敗した際に下の Base64 デコード処理に落ち、Guid 文字列を Base64 としてパースしようとしてエラー（`result: 7`）を出していた。

## 実装計画と対応
- [x] **ASCII英字のみのパペット名生成**: `template.Id` から完全な英字のみ（A-Z, a-z）の Surname（例: `Evjkkhzl`）を決定論的に生成し、FF14 の名前検証を 100% パスさせつつ一意性を維持。
- [x] **Glamourer Guid 適用の堅牢化**: Guid 指定時、JObject を取得して `ForceAllApply` を施した上で `ApplyState` を優先実行し、26バイト `CustomizeData` を直接同期。Base64 パースへのフォールスルーを排除。
- [x] **バージョン 0.1.31 へのバンプとデプロイ**: `package.json`, `CharacterSpawn.csproj`, `CharacterSpawn.json`, `repo.json`, `CHANGELOG.md` を更新しプッシュ。
