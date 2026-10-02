# 実装計画: AQR仕様解析および外見適用正常化

## 1. 概要と背景
キャラクター生成時に「自キャラがスポーン対象の姿になり、スポーン側が自キャラの姿になる（入れ替わる）」、および「Penumbra コレクションが反映されず Glamourer デザインのみ反映される」という不具合が連続して再発していた。
AQuestReborn（AQR）のバイナリおよび逆コンパイルコードを徹底的にリバースエンジニアリングし、AQR の正常動作フローを完全解明した上で、現在の仕様との乖離点をすべて解消する。

---

## 2. AQuestReborn（AQR）の処理仕様（解析結果）

### (1) スポーン生成フロー
- **呼出箇所**: `AQuestReborn.CheckForCustomNpcCreationLoad`
- **内部呼出**: `Brio.Game.Actor.ActorSpawnService.CreateCharacter(out ICharacter? character, 4, 1, position, 0.0f, name)`
- **動作詳細**:
  1. `CharacterUtility.Instance.CreateBattleCharacter(2 + count, false)` により空きスロット（ObjectIndex 200番台）を確保。
  2. `nativeChara->SetName(name)` によりアクター名（例: `"Kimo Cnpc"`）を設定。
  3. `nativeChara->CharacterSetup.CopyFromCharacter(localPlayerNative, flags)` で自キャラの素体をコピー。
  4. 座標・回転を設定し、描画可能状態になったら描画を有効化（`DrawWhenReady`）。
  5. **決定的事実**: `ObjectKind`、`BattleNpcSubKind`、`OwnerId`、`NameId`、`HomeWorld` などのメモリフィールドは**一切書き換えていない**。

### (2) Penumbra コレクション適用フロー
- **呼出箇所**: `CheckForCustomNpcCreationLoad`
- **動作詳細**:
  1. Penumbra IPC から全コレクション名を取得し、名前が一致するコレクションの `Guid` を検索。
  2. `SetCollectionForObject(character.ObjectIndex, collection.Key, allowConflict: true, force: true)` を呼ぶ。
     - **重要**: 第2引数は「文字列」ではなく **`Guid`（Nullable<Guid>）**。
  3. 直後に `RedrawObject(character.ObjectIndex, RedrawType.Redraw)` を呼ぶ。

### (3) Glamourer デザイン適用フロー
- **呼出箇所**: `CheckForCustomNpcCreationLoad` / `ReapplyCustomNpcAppearance`
- **通常デザイン（Guid指定）**:
  - `ApplyDesign(designGuid, character.ObjectIndex, 0, 7UL)` を呼ぶ。
  - 第3引数 lock key: `0`、第4引数 ApplyFlag: `7UL`（Customization | Equipment | Crest）。
- **MCDF デザイン（Base64指定）**:
  - `ApplyState(glamourerData, character.ObjectIndex, 0, ApplyFlag.Customization | ApplyFlag.Equipment)` を呼ぶ。
  - **重要**: MCDF 内の Base64 文字列は**何ら加工・変換せずそのまま渡す**。
  - 直後に `Penumbra.RedrawObject(character.ObjectIndex, RedrawType.Redraw)` を実行。

### (4) 実行タイミング
- AQR では、スポーン関数 `CreateCharacter` が完了して `ICharacter` を取得した**その直後の行（同一フレーム・同一コンテキスト）で、Penumbra コレクションと Glamourer デザインを一連の直列処理として即時適用**している。
- 遅延ポーリング（数十フレーム待機など）は挟まない。

---

## 3. 現在の実装との差分および問題点

| 項目 | 現在の実装 (Character-Spawn) | AQR の実装 | 不具合への影響 |
| :--- | :--- | :--- | :--- |
| **メモリ書き換え** | `ObjectKind = BattleNpc`, `BattleNpcSubKind = Player`, `OwnerId = 0xE000_0000` を強制設定 | メモリ書き換えは一切行わない（素の BattleCharacter） | Glamourer/Penumbra の `ActorIdentifierFactory` がアクター追跡を見失い、LocalPlayer（Index 0）に誤認・誤爆する根本原因 |
| **適用タイミング** | `applyGlamourer: false` で事前登録後、`readyJobs` で十数フレーム待機・ポーリング後に適用 | スポーン直後に同一フレーム・直列で即時適用 | フレーム待機の間にアクター状態やインデックスのズレが生じるレースコンディション |
| **Penumbra IPC 引数** | `SetCollectionForObject` にコレクション名（string）を渡している | コレクション名から Guid を特定し、Guid を渡している | Penumbra 側で引数型不一致（または名前解決失敗）によりコレクションが無視される（反映されない原因） |
| **MCDF データ** | Base64 文字列を解凍・ヘッダ付与・再圧縮する独自加工を行っている | MCDF 内包の Base64 文字列を無加工のまま `ApplyState` に渡す | 変換処理による破損リスク、余分なオーバーヘッド |
| **キャラクター名** | ランダムな英字列（例: `"Csp Rdtbsarx"`） | テンプレート名由来（例: `"Kimo Cnpc"`） | デバッグ・識別が困難で、Glamourer 側の名前一致判定に失敗しやすい |

---

## 4. 修正計画

### Phase 1: スポーン処理の AQR 完全準拠
1. `ActorManager.cs` の `SpawnCharacter` 内の以下のメモリ書き換えを全削除：
   - `nativeChara->GameObject.ObjectKind = ObjectKind.BattleNpc;`
   - `nativeChara->GameObject.BattleNpcSubKind = BattleNpcSubKind.Player;`
   - `nativeChara->GameObject.OwnerId = 0xE000_0000;`
   - `nativeChara->NameId = 0;`
   - `nativeChara->HomeWorld = meNative->HomeWorld;`
2. キャラクター命名規則を `template.Name.Split(' ')[0] + " Cnpc"`（最大20文字）に変更。

### Phase 2: 外見適用アーキテクチャの即時直列化
1. スポーン直後の同一実行フロー内で、AQR と同一の手順で外見を適用：
   - **Step 1**: Penumbra コレクションの適用（Guid 指定）＆ `RedrawObject`
   - **Step 2**: Glamourer デザインの適用（Guid または MCDF Base64）
2. `readyJobs` による Glamourer 適用遅延を撤廃。

### Phase 3: Penumbra IPC の Guid 化
1. `PenumbraIpc.cs` の `SetCollectionForActor` を改修：
   - コレクション一覧（`GetCollections`）から対象名の `Guid` を取得。
   - `SetCollectionForObject` に `Guid` を渡して呼び出し。
   - 直後に `RedrawObject` を呼び出す。

### Phase 4: MCDF 処理の簡素化・純化
1. `McdfCharaFileData.GlamourerData`（Base64）を加工せず、そのまま Glamourer `ApplyState` IPC へ渡す。
2. 直後に Penumbra の `RedrawObject` を呼び出す。

### Phase 5: 自キャラ保護ガードの強化
1. `ObjectIndex <= 0` の場合は例外なく Glamourer / Penumbra 適用を遮断。
2. スポーン対象アクターの `ICharacter` と `LocalPlayer` のアドレスが異なることを厳密に検証。
