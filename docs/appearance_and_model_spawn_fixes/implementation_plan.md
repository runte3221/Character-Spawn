# 実装計画: PenumbraコレクションおよびMCDF一時コレクションの正常適用 (v0.1.16)

## 1. 課題と原因分析
### 課題
- Penumbra Collectionを指定してスポーンさせても、コレクションが反映されない。
- MCDFファイルを指定してスポーンさせても、内包されているMod（モデル・テクスチャ・マテリアル・FileSwaps・MetaManipulations）がアクターに反映されない。

### 原因 (Penumbra内部解析結果)
- `Penumbra.GameData.dll` の `ActorIdentifierFactory.FromObject` において、`nativeChara->GameObject.ObjectKind` が評価される。
- これまで全アクターに対して `ObjectKind = ObjectKind.BattleNpc` を設定していた。
- コレクション割り当てIPC（`AssignTemporaryCollection` / `SetCollectionForObject`）は、内部で `CreateBNpcFromObject(allowPlayer: false)` を呼び出す。
- `allowPlayer = false` であるため、アクター名や `OwnerId` が設定されていてもプレイヤーとして認識されず、存在しないモンスター `BNpc(DataId = 0)` として解決される。
- これにより、コレクション登録時に Mod グループが 0件となり、`AssignTemporaryCollection` は `ec = 255`、`SetCollectionForObject` は `ec = 16` (`InvalidIdentifier`) で拒否されていた。

## 2. 修正方針
1. **`Managers/ActorManager.cs` の修正**:
   - 人型アクター（`template.ModelCharaId == 0`）の場合は `nativeChara->GameObject.ObjectKind = ObjectKind.Player` に設定。
   - モンスター（`template.ModelCharaId > 0`）の場合のみ `nativeChara->GameObject.ObjectKind = ObjectKind.BattleNpc` とする。
   - `puppetName`（例: `"Cs Aa"`）は Penumbra の `VerifyPlayerName`（長さ5〜31文字、スペース1つ、各パート2〜15文字の英字）を満たす。
   - これにより、Penumbra は `CreatePlayerFromObject` を実行し、正当な Player Identifier として認識され、`ec = 0` (Success) でコレクションが適用される。
2. **バージョン更新 & 配布**:
   - v0.1.16 / 0.1.16.0 に Bump。
   - GitHub Actions でビルド後、XIVLauncher の `installedPlugins/CharacterSpawn` 配下の全バージョンフォルダに最新 DLL を配備。
