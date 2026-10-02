# タスクリスト: 外見適用（Glamourer / Penumbra / MCDF）およびモデルスポーン（モンスター / NPC）の不具合修正

## 完了したタスク

- [x] **HDM および AQR リポジトリの構造調査・完全解析**
  - [x] HDM (`Enceladeum/HDM`) から `SpawnService.cs` および `GuiseService.cs` を取得し完全解析
  - [x] Two Index Spaces Trap（COM index と Global ObjectTable index の取り違えによる自キャラ化）の特定
  - [x] Glamourer Identity の罠（The 0.8.44 Bug: NameId=0 かつ無効名による Glamourer の Invalid 判定）の特定
  - [x] Draw-When-Ready（2フェーズ待機キュー: IsReadyToDraw -> EnableDraw -> DrawObject->IsVisible）の特定
  - [x] モンスター・NPC のダブルコピー（素の SetupBNpc は不可視となりギズモのみになる問題）の特定
- [x] **`Models/CharacterModels.cs` の改修**
  - [x] `SpawnedActorData` に `GlobalIndex`（ushort）および `ComIndex`（ushort）を追加
- [x] **`Managers/ActorManager.cs` の HDM & AQR アーキテクチャへの全面改修**
  - [x] `ReadyJob` クラスと `readyJobs` リストの導入
  - [x] ユニーク名生成 `NextPuppetName()` による Glamourer Identity スタンプの実装
  - [x] `SpawnCharacter` での自キャラからのダブルコピー・初期化・グローバルインデックス解決
  - [x] `UpdateFrame` での 2フェーズ（IsReadyToDraw -> EnableDraw -> DrawObject->IsVisible）ポーリング処理
  - [x] `ApplyExternalAppearance` でのグローバルインデックス適用（Penumbra Collection, Glamourer flags=7, Monster ModelCharaId & Redraw, NPC Customize/Equip）
  - [x] `DespawnCharacter` での `GetIndexByObject` による動的 COM インデックス解決とクリーンな破棄
  - [x] `SetWeaponVisibility` のグローバルインデックス対応
- [x] **`UI/CharacterLibraryTab.cs` の保存・表示・復元保証**
  - [x] `SaveModalTemplate` での全ソースタイプ（Glamourer, MCDF, NPC, Monster）の完全保存と詳細ログ
  - [x] `DrawModalGlamourerSection` での GUID 保存保証
  - [x] `OpenEditCharacterModal` での Glamourer デザイン／GUID 復元
  - [x] `DrawRightPane` の `Template Details` での Glamourer / MCDF / NPC / Monster 詳細表示強化
- [x] **バージョン管理とリリース同期 (v0.1.11 / v0.1.11.0)**
  - [x] `package.json` (`0.1.11`), `CharacterSpawn.json` (`0.1.11.0`), `CharacterSpawn.csproj` (`0.1.11.0`), `repo.json` (`0.1.11.0`) を更新
  - [x] `CHANGELOG.md` に 0.1.11 の詳細内容を追記
  - [x] `docs/appearance_and_model_spawn_fixes` のドキュメント（`task.md`, `implementation_plan.md`, `walkthrough.md`）を同期
  - [x] `git add . && git commit && git push` の実行
