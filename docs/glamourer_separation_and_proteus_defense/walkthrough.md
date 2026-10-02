# 変更内容の確認 (Walkthrough): v0.1.41.0

## 変更されたファイル一覧
1. `Services/GlamourerIpc.cs`:
   - `RevertLocalPlayer` に `ICharacter? playerCharacter = null` を追加し、直接リバート (`revertCharacter`)、名前リバート、Index 0 リバート、Automation 復帰を網羅。
2. `Managers/ActorManager.cs`:
   - `IFramework` を導入し、`RevertLocalPlayer` を `Framework.RunOnFrameworkThread` で安全にメインスレッドディスパッチ。
   - `ApplyAppearanceDirect` に `bool applyGlamourer = true` を追加。
   - `SpawnCharacter` 内では `applyGlamourer: false`（Penumbra コレクション事前割り当てのみ）とし、`ReadyJob`（メインスレッド・描画準備完了後）で `applyGlamourer: true`（Glamourer 適用と確定 Redraw）を実行。
3. `Plugin.cs`:
   - `ActorManager` の初期化に `Framework` を渡し、初期化時の自キャラ自動復元を `Framework.RunOnFrameworkThread` でラップ。
4. `UI/CharacterLibraryTab.cs`:
   - Character タブの操作ボタン（Spawn, Edit, Delete）の並びに「Revert Player」ボタンを追加。
5. `CharacterSpawn.json` & `CharacterSpawn.csproj`:
   - バージョンを `0.1.41.0` にインクリメント。
6. `CHANGELOG.md`:
   - v0.1.41.0 の詳細な修正内容を追記。
