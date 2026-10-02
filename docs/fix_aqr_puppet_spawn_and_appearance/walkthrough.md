# 修正内容の確認 (Walkthrough): v0.1.39.0

## 変更内容の要約
AQuestReborn (AQR) のバイナリ（`AQuestReborn.dll` / `Brio.dll`）および連携先プラグイン（`Glamourer.dll` / `Penumbra.dll`）の IL 逆アセンブル解析を実施し、通常ワールドにおけるパペット生成および外見・コレクション適用のアーキテクチャを 100% 忠実に再現・修正しました。

### 1. 根本原因の特定と解決

#### 原因 ①: `ObjectKind.Pc` と `NameId = 0` の強制設定による自キャラ誤認
- **詳細**:
  前回の修正でパペットに対して `nativeChara->GameObject.ObjectKind = ObjectKind.Pc` および `NameId = 0` を設定していました。しかし、ゲームエンジンにおいて `ObjectKind.Pc` かつ `NameId == 0` はプレイヤーキャラクター（PC）を意味します。
  COM で作られたパペットはクライアントの正規プレイヤースロットには存在しないため、Glamourer および Penumbra の `ActorIdentifierFactory.FromObject` が名前とワールドから識別子を解決する際に自キャラ（LocalPlayer）の識別子を返してしまい、自キャラが変身する原因となっていました。
- **解決策**:
  AQR / Brio の仕様を 100% 遵守し、`CreateBattleCharacter()` で作成された `BattleCharacter` の `ObjectKind`, `BattleNpcSubKind`, `NameId` には一切触らないように修正しました。

#### 原因 ②: `Glamourer.ApplyState` による自キャラ State 上書き
- **詳細**:
  `ApplyState` はアクターの Identifier に紐づくグローバル状態テーブルを書き換える IPC です。パペットの Identifier が自キャラと同一視されていたため、自キャラのステートにデザインが上書きされ、自キャラが Redraw されて「自キャラがスポーンさせたいキャラに変身する」入れ替わり現象が発生していました。
- **解決策**:
  AQR と完全に同一の `Glamourer.ApplyDesign(targetGuid, actorIndex, 0, 7UL)` 直接呼び出しに一本化し、`ApplyState` を完全排除しました。

#### 原因 ③: Penumbra コレクション適用の安定化
- **解決策**:
  `SetCollectionForActor` 成功直後に AQR と同様に `RedrawObject` を実行し、コレクションがゲームエンジンに反映された上で Glamourer デザインが確定するようにシーケンスを整理しました。

---

## 変更ファイル一覧
- [ActorManager.cs](file:///C:/Users/RYO/Desktop/Character-Spawn/Managers/ActorManager.cs)
  - `ObjectKind.Pc` / `BattleNpcSubKind.Player` / `NameId = 0` の書き換えを完全削除。
  - Penumbra コレクション適用直後の RedrawObject 実行を追加。
- [GlamourerIpc.cs](file:///C:/Users/RYO/Desktop/Character-Spawn/Services/GlamourerIpc.cs)
  - `ApplyDesignToActor` での `ApplyState` を撤廃し、AQR 完全準拠の `Glamourer.ApplyDesign` IPC を直接呼び出し。
- [package.json](file:///C:/Users/RYO/Desktop/Character-Spawn/package.json) (v0.1.39)
- [CharacterSpawn.csproj](file:///C:/Users/RYO/Desktop/Character-Spawn/CharacterSpawn.csproj) (v0.1.39.0)
- [CharacterSpawn.json](file:///C:/Users/RYO/Desktop/Character-Spawn/CharacterSpawn.json) (v0.1.39.0)
- [CHANGELOG.md](file:///C:/Users/RYO/Desktop/Character-Spawn/CHANGELOG.md) (v0.1.39 追記)

---

## 検証手順 (User Verification)
1. **GitHub Actions ビルド完了後**:
   - ゲーム内 Dalamud で Character Spawn プラグインを再読み込み（または一度無効化して有効化 / 更新）して v0.1.39.0 になっていることを確認してください。
2. **新規スポーンテスト**:
   - キャラクターを作成し（または既存のキャラを選択）、スポーンを実行します。
   - **確認項目**:
     - 操作中の自キャラの見た目が一切変わらないこと（自キャラが変身しないこと）。
     - スポーンしたパペットに Glamourer デザインおよび Penumbra コレクションが正常に反映されていること。
3. **デスポーン & 再スポーンテスト**:
   - デスポーンし、再度スポーンを実行します。
   - **確認項目**:
     - 自キャラに影響がないこと。
     - パペットに外見とコレクションが正常に維持されていること。
