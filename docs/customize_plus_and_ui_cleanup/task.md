# タスクリスト: Customize+ Profile 連携および UI 整理 (v0.1.17)

## ステータス概要
- [x] ドキュメント作成 (`task.md`, `implementation_plan.md`, `walkthrough.md`)
- [x] UI から `Or Direct Design String / Code:` の入力項目を非表示化 (`CharacterLibraryTab.cs`)
- [x] `Services/CustomizePlusIpc.cs` の新規実装 (API v6+ EzIPC 連携、プロファイル一覧取得・一時プロファイル適用・解除)
- [x] `Models/CharacterModels.cs` の拡張 (`CustomizePlusProfileGuid`, `CustomizePlusProfileName`, `TemporaryCustomizePlusGuid`)
- [x] `Services/McdfParser.cs` の拡張 (`McdfBundle.CustomizePlusData` 展開)
- [x] `UI/CharacterLibraryTab.cs` に Customize+ Profile 選択セレクターを追加
- [x] `Managers/ActorManager.cs` に Customize+ プロファイルの適用・破棄ロジックを追加
- [x] `Plugin.cs` での `CustomizePlusIpc` の初期化と DI 連携
- [x] バージョン更新 (0.1.17 / 0.1.17.0)
  - `package.json`, `CharacterSpawn.json`, `CharacterSpawn.csproj`, `repo.json`
  - `CHANGELOG.md` 追記
- [ ] Git Commit & Push
- [ ] GitHub Actions ビルド待機 & XIVLauncher 全バージョンフォルダへの最新 DLL 配置
- [ ] 完了報告
