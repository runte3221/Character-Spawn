# 実装計画: AQuestReborn（AQR）完全リバースエンジニアリング準拠によるパペットスポーンおよび外見・コレクション適用の根本修復

## 1. 目的
`AQuestReborn.dll`、`Brio.dll`、`Glamourer.dll`、`Penumbra.dll` の逆アセンブル（IL解析）によって判明した AQR の実装方式を 100% 正確に CharacterSpawn に移植し、自キャラが変身する不具合およびコレクションが反映されない不具合を根絶する。

## 2. 変更箇所の詳細

### A. `ActorManager.cs`
- **問題箇所**:
  ```csharp
  nativeChara->GameObject.ObjectKind = template.ModelCharaId > 0 ? ObjectKind.BattleNpc : ObjectKind.Pc;
  nativeChara->GameObject.BattleNpcSubKind = BattleNpcSubKind.Player;
  nativeChara->NameId = 0;
  nativeChara->HomeWorld = meNative->HomeWorld;
  ```
- **修正内容**:
  - `ObjectKind.Pc` や `BattleNpcSubKind.Player`、`NameId = 0` の書き換えを完全削除。
  - AQR / Brio の仕様通り、`CreateBattleCharacter()` で生成されたそのままの `BattleCharacter` の状態を維持する。
  - 名前設定を AQR 準拠の `template.Name + " Cnpc"`（または最大15文字の英字名）にする。
  - `ApplyAppearanceDirect` での `ObjectKind = ObjectKind.Pc` の再設定も完全削除。

### B. `GlamourerIpc.cs`
- **問題箇所**:
  - `ApplyDesignToActor` の中で、デザイン JSON を読み込み、`ForceAllApply` して Base64 圧縮した上で `ApplyState` (`applyStateV2Ulong`) を呼び出していた。
  - `ApplyState` はアクターの Identifier を検索し、結果として自キャラ（LocalPlayer）の State を書き換えて Redraw させてしまっていた。
- **修正内容**:
  - AQR と 100% 同一の方式に一本化：
    ```csharp
    applyDesignV2Ulong.InvokeFunc(targetGuid, actorIndex, 0, 7UL);
    ```
  - Guid が取得できている場合、余計な `ApplyState` は一切呼ばず、`Glamourer.ApplyDesign` IPC のみを直接実行する。

### C. `PenumbraIpc.cs`
- **修正内容**:
  - AQR 準拠の `SetCollectionForObject.V5` を優先適用：
    ```csharp
    setCollectionForObjectV5NullableGuid.InvokeFunc(actorIndex, collGuid, true, true);
    ```
  - 適用後に AQR 同様 `RedrawObject` を実行。

## 3. バージョン更新と Git ルール
- バージョン: `0.1.39.0`
- `package.json`, `CharacterSpawn.csproj`, `CharacterSpawn.json`
- `CHANGELOG.md` 追記
- MSBuild によるビルドと XIVLauncher プラグインフォルダへの配置
- Git commit & push
