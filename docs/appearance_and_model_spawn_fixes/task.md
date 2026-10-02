# タスクリスト: 外見適用およびAQR準拠MCDF/Penumbra連携

## ステータス概要
- [x] AQRのMCDF内包Mod展開ロジックの解析 (`McdfCharaFileManager.cs`)
- [x] Penumbra Temporary Collection IPC（V5/V6）のシグネチャ解明
- [x] `Services/McdfParser.cs` の改修（Modファイル実体・FileSwaps・ManipulationData抽出）
- [x] `Services/PenumbraIpc.cs` の改修（一時コレクション作成・割り当て・一時Mod登録・削除の実装）
- [x] `Models/CharacterModels.cs` に `TemporaryCollectionGuid` 追加
- [x] `Managers/ActorManager.cs` の改修（スポーン時の一時コレクション作成・割り当て・登録、デスポーン時の削除）
- [x] `Plugin.cs` の改修（`PluginInterface` の `ActorManager` への受け渡し、動的バージョンログ）
- [x] `UI/CharacterLibraryTab.cs` の改修（MCDFの手動コレクション選択削除、自動一時コレクション化の案内表示）
- [x] 古いプラグインバージョン（v0.1.9等）のXIVLauncher環境上書き配置準備
- [ ] Git Commit & Push
- [ ] GitHub Actions ビルド確認と XIVLauncher 全バージョンフォルダへの最新DLL適用
