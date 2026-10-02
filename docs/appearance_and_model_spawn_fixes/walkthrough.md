# 修正内容の確認 (Walkthrough): Penumbra & MCDF コレクション割り当て修正 (v0.1.16)

## 実施した変更
1. **`Managers/ActorManager.cs`**:
   - スポーン初期化時（`SpawnCharacterInternal`）において、人型アクター（`template.ModelCharaId == 0`）の場合は `nativeChara->GameObject.ObjectKind = ObjectKind.Player`（および `BattleNpcSubKind.Player`）を設定するように変更。
   - モンスター（`template.ModelCharaId > 0`）の場合のみ `ObjectKind = ObjectKind.BattleNpc` を設定。
2. **Penumbra Identifier解決の動作**:
   - `ObjectKind.Player` となったことで、Penumbra IPC の `FromObject` は `CreatePlayerFromObject` を呼び出す。
   - `VerifyPlayerName` を通過し、正当な Player Identifier が生成される。
   - `AssignTemporaryCollection`（MCDF用）および `SetCollectionForObject`（通常コレクション用）がエラー（ec=255, ec=16）なく `ec = 0` (Success) で完了し、Mod ファイル・テクスチャ・3Dモデルがアクターに正常反映される。
3. **バージョン更新**:
   - `package.json`, `CharacterSpawn.json`, `CharacterSpawn.csproj`, `repo.json` を `0.1.16` / `0.1.16.0` に更新。
   - `CHANGELOG.md` に変更点を記載。
