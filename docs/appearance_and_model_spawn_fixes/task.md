# タスクリスト: 外見適用およびAQR準拠MCDF/Penumbra連携

## ステータス概要
- [x] Penumbra内部コード（`Penumbra.GameData.dll`）のCIL逆アセンブルによる根本原因特定
  - `ObjectKind.BattleNpc` 時の `CreateBNpcFromObject(allowPlayer: false)` ルーティングを解明
  - `AssignTemporaryCollection` での `ec = 255` および `SetCollectionForObject` での `ec = 16` の原因を完全特定
- [x] AQR / Brio のスポーン仕様調査（Brioは人型キャラを `ObjectKind.Player` で生成）
- [x] `Managers/ActorManager.cs` の改修
  - 人型アクター（`template.ModelCharaId == 0`）の `ObjectKind` を `ObjectKind.Player` に変更
  - モンスター（`template.ModelCharaId > 0`）のみ `ObjectKind.BattleNpc` に設定
  - `puppetName` の形式（`"Cs Aa"` 等）が Penumbra の `VerifyPlayerName` を満たしていることを担保
- [x] バージョン更新 (v0.1.16 / 0.1.16.0)
  - `package.json`, `CharacterSpawn.json`, `CharacterSpawn.csproj`, `repo.json`
  - `CHANGELOG.md` 追記
  - `docs/appearance_and_model_spawn_fixes` ドキュメント同期
- [x] Git Commit & Push
- [x] GitHub Actions ビルド完了待機 & XIVLauncher 全バージョンフォルダへの最新DLL適用
