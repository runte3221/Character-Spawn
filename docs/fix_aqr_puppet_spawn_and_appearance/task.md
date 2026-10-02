# タスク: AQuestReborn（AQR）完全リバースエンジニアリング準拠によるパペットスポーンおよび外見・コレクション適用の根本修復

## 現状の課題と原因分析
1. **現象**:
   - スポーンしたパペットに自キャラの見た目が残り、操作可能な自キャラ（LocalPlayer）がスポーン対象キャラ（Kimo-1-Nude）に変身してしまう（入れ替わり現象）。
   - デスポーン・スポーンを繰り返すと、自キャラに Glamourer デザインだけが反映され、Penumbra コレクションが反映されない。
2. **原因の完全特定（AQuestReborn.dll, Brio.dll, Glamourer.dll, Penumbra.dll のバイナリ IL 解析結果）**:
   - **Glamourer の ApplyState 誤爆**:
     CharacterSpawn では `ApplyState` (State オブジェクトを直接上書きする IPC) を呼び出していた。`ApplyState` は渡された `objectIndex` (200) から `FindState` を呼び出すが、`ObjectKind` を `Pc` に変更したことで `CreatePlayerFromObject` が走り、名前解決で自キャラの Identifier を取得して自キャラの State を上書きし、結果として自キャラを Redraw してしまっていた。
   - **ObjectKind / BattleNpcSubKind / NameId の改変**:
     AQR (AQuestReborn) は `ObjectKind` や `BattleNpcSubKind`、`NameId` を一切弄らず、Brio が生成したそのままの `BattleCharacter` として扱っている。CharacterSpawn 側で勝手に `ObjectKind.Pc` に書き換えていたことが、Penumbra / Glamourer 内部での PC 識別子誤爆の元凶であった。
   - **Glamourer / Penumbra の呼び出しプロトコル**:
     AQR は `Glamourer.ApplyDesign(Guid, objectIndex, 0, 7)` のみを呼び出し、`ApplyState` は呼んでいない。
     Penumbra についても `Penumbra.SetCollectionForObject.V5(objectIndex, (Guid?)collectionId, true, true)` と `Penumbra.RedrawObject(objectIndex, RedrawType.Default)` を呼び出している。

## 実装タスク
- [x] 1. `ActorManager.cs`:
  - `ObjectKind.Pc`, `BattleNpcSubKind.Player`, `NameId = 0` の強制変更コードを完全撤廃（AQR / Brio 準拠の BattleCharacter を維持）。
  - パペットの名前付けを AQR 準拠 (`template.Name + " Cnpc"` または安全な英数字文字列) に変更。
- [x] 2. `GlamourerIpc.cs`:
  - `ApplyDesignToActor` において、`ApplyState` による Base64 上書き処理を撤廃し、AQR と 100% 同一の `Glamourer.ApplyDesign(targetGuid, actorIndex, 0, 7UL)` を直接呼び出す。
- [x] 3. `PenumbraIpc.cs`:
  - `SetCollectionForActor` において、AQR 準拠の `SetCollectionForObject` (Guid? 引数) を確実に適用し、余計なフォールバックの混乱を防止。
- [x] 4. ビルド・バージョン更新 (0.1.39.0)・デプロイ・Git 同期:
  - `package.json`, `CharacterSpawn.csproj`, `CharacterSpawn.json` を 0.1.39.0 に更新。
  - `CHANGELOG.md` 追記。
  - MSBuild でビルドし XIVLauncher installedPlugins へのコピー。
  - `git commit` & `git push`。
- [x] 5. 検証手順の確認とユーザーへの報告 (Walkthrough 作成)。
