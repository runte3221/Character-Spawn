# ウォークスルー: AQR仕様解析および外見適用正常化

## 1. 解析の経緯と目的
キャラクター生成時に発生していた以下の2つの不具合：
1. **自キャラとスポーンキャラの入れ替わり**:
   自キャラ（LocalPlayer, Index 0）がスポーン対象（例: おじさん）に変身し、スポーンした側（Index 200）が自キャラの姿（ネコミミ）のまま残る。
2. **Penumbra コレクションが反映されない**:
   Glamourer の外見だけが反映され、MOD テクスチャ等の Penumbra コレクションが適用されない。

これらを根本解決するため、過去バージョン（0.1.23）および AQuestReborn（AQR）のバイナリ（`AQuestReborn.dll` / `Brio.dll`）をリバースエンジニアリング（ILコード・シンボル解析）し、完全な仕様を解明しました。

---

## 2. AQR のリバースエンジニアリングで判明した事実

### (1) スポーンとメモリ状態
- AQR は内部で `Brio.Game.Actor.ActorSpawnService.CreateCharacter` を使用。
- ゲーム内部の `ClientObjectManager.CreateBattleCharacter` でスロットを確保後、自キャラから素体をコピー。
- **重要**: `ObjectKind`, `BattleNpcSubKind`, `OwnerId`, `HomeWorld` などのメモリフィールドは一切改変せず、素のゲームエンジンの状態を維持。

### (2) Penumbra コレクション適用
- `PenumbraAndGlamourerIpcWrapper.Instance.SetCollectionForObject` を呼び出す。
- 引数にはコレクション名文字列ではなく、**`Guid collectionId`** を渡す。
- 呼び出し直後に **`RedrawObject(character.ObjectIndex, RedrawType.Redraw)`** を実行。

### (3) Glamourer デザイン適用
- 通常デザイン（Guid）:
  `PenumbraAndGlamourerIpcWrapper.Instance.ApplyDesign.Invoke(designGuid, character.ObjectIndex, 0, 7UL)`
- MCDF デザイン（Base64）:
  `_glamourerApplyAll.Invoke(glamourerData, character.ObjectIndex, 0, ApplyFlag.Customization | ApplyFlag.Equipment)`
- **重要**: スポーン直後に同一フレーム・同一スレッドで直列実行されており、遅延キューによる待ち時間は存在しない。

---

## 3. なぜ当プラグインで自キャラが変身していたのか？（メカニズム解明）

```
[当プラグインの旧処理]
1. スポーン時に ObjectKind = BattleNpc, BattleNpcSubKind = Player, OwnerId = 0xE000_0000 を強制代入
2. ランダム名 "Csp Rdtbsarx" を代入
3. ReadyJob に入れて何十フレームも描画準備を待機（非同期遅延）
4. 数フレーム後、ApplyAppearanceDirect で ApplyDesign(designGuid, 200, 0, 7UL) を呼び出し
   ↓
[Glamourer 内部の動作]
1. helpers.FindState(200) が呼ばれる
2. actors.GetIdentifier(objects.Objects[200]) を実行
3. メモリの改変や遅延により、パペットの ActorIdentifier が正常なプレイヤー型として解決されない
4. または OwnerId の判定で LocalPlayer（Index 0）側のアクター情報に引き寄せられる
5. 結果として LocalPlayer（Ruma Meow）の State が返され、LocalPlayer にデザインが適用されて変身！
   パペット（Index 200）は自キャラの素体をコピーされたまま変化なし。
```

---

## 4. 今後の修正方針と検証手順

1. **修正の適用**:
   - `ActorManager.cs`: メモリ改変の全撤廃、名前ルールの統一、スポーン直後の即時直列適用
   - `PenumbraIpc.cs`: `SetCollectionForObject` の Guid 渡し化、直後 Redraw
   - `GlamourerIpc.cs` / `McdfParser.cs`: MCDF Base64 データの無加工直接適用
2. **ビルドとバージョン同期**:
   - `tools/bump-version.ps1 0.1.42.0` を実行し、全 JSON・プロジェクトのバージョンを完全同期
3. **ゲーム内検証**:
   - スポーン実行時、奥の自キャラ（LocalPlayer）の見た目・ネームプレートが一切変わらないこと
   - 手前のパペットに指定した Glamourer デザインと Penumbra コレクションが初回から完全に反映されること
   - デスポーン・再スポーンを行っても外見・コレクションが正しく維持されること
