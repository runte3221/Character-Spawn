# 実装計画: AQR準拠 MCDF/Penumbra/Glamourer 適用不具合および自キャラ誤認の根本修正

## 1. 課題と根本原因の分析

### 現象
1. **「自キャラがスポーンしたいキャラ（Kimo-1-Nude）に置き換わり、スポーンしたほうが自キャラになる」**
2. **「その後デスポーンし、スポーンしなおすと Glamourer design だけ反映され Penumbra Collection が反映していない状態で表示される」**

### 根本原因
1. **自キャラのステート残留**:
   - 以前のバージョンでの実行時、自キャラ（Index 0）に対して Glamourer のステートが適用され、Glamourer 側でそれがリバート（Revert）されずに保持されていた。
   - その結果、自キャラ自体が Kimo-1-Nude の姿のままになっていた。
   - スポーン時に `nativeChara->CharacterSetup.CopyFromCharacter(meNative)` を行うため、変身したままの自キャラがパペットにコピーされ、自キャラもパペットも同一の変身状態となり、自キャラが乗っ取られたように見えていた。
2. **Penumbra コレクション事前適用の喪失 (0.1.23 との決定的な乖離)**:
   - 正常に動作していた `v0.1.23` では、`SpawnCharacter` 内（アクター生成直後、ゲームエンジンが描画構築を始める前）に `ApplyAppearanceDirect` を同期呼び出しして Penumbra コレクションを事前適用（Pre-Assignment）していた。
   - 最近の変更で「描画準備（IsReadyToDraw）を待ってから適用する」ように変更したため、ゲームエンジンがデフォルトのモデル・テクスチャを読み込んでキャッシュしてしまい、その後の Penumbra 割り当てが反映されなくなっていた。
3. **パペットの Identity 不整合**:
   - `BattleNpcSubKind.Player`, `OwnerId = 0xE000_0000`, `NameId = 0` を削除したことで、Penumbra 側でアクターが正しく識別されず、コレクションの割り当てが失敗していた。

---

## 2. 修正方針

### 1. 自キャラ（LocalPlayer 0）のリバート処理の実装
- プラグイン起動時、および ActorManager 初期化時に `glamourerIpc.RevertState(0)` を実行し、自キャラが過去のセッションで変身させられたままになっている状態を強制解除。
- UI 上に「Revert Player」ボタンを設置し、ユーザーがいつでもワンクリックで自キャラのステートを本来の姿に戻せるようにする。

### 2. パペットの Identity（0.1.23 準拠）の復元
```csharp
nativeChara->GameObject.ObjectKind = ObjectKind.BattleNpc;
nativeChara->GameObject.BattleNpcSubKind = BattleNpcSubKind.Player;
nativeChara->GameObject.OwnerId = 0xE000_0000;
nativeChara->NameId = 0;
nativeChara->HomeWorld = meNative->HomeWorld;
string puppetName = NextPuppetName();
nativeChara->GameObject.SetName(puppetName);
```

### 3. スポーン直後（描画前）の Penumbra コレクション事前適用（Pre-Assignment）の復活
`SpawnCharacter` 内で：
```csharp
if (template.ModelCharaId == 0 && template.SourceType != CharacterSourceType.Npc)
{
    ApplyAppearanceDirect(nativeChara, globalIdx, template, spawned);
}
```

### 4. MCDF 一時コレクション・Glamourer 適用・Penumbra Redraw の同期徹底
AQR (MCDF-Loader) の完全準拠:
1. `CreateTemporaryCollection`
2. `AssignTemporaryCollection`
3. `AddTemporaryMod` (Modファイル + ManipulationData)
4. `Glamourer.ApplyDesign` / `ApplyState`
5. `Penumbra.Redraw`

---

## 3. 変更対象ファイル
- `Managers/ActorManager.cs`: Identity設定、事前適用、Revert処理の復元
- `Services/GlamourerIpc.cs`: 自キャラRevertメソッドの確実な動作
- `UI/CharacterSpawnWindow.cs`: 「Revert Player」ボタンの追加
- `package.json`, `repo.json`, `CharacterSpawn.json`, `CharacterSpawn.csproj`: バージョン更新 (v0.1.40.0)
- `CHANGELOG.md`: 変更記録の追記
