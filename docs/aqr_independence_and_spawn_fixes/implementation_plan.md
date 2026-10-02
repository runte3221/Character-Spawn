# 実装計画: AQR 依存脱却 & スポーン不具合修正

## 1. 概要
AQuestReborn (AQR) プラグインを無効化した状態で発生する、Penumbra 通常コレクションおよび MCDF Temporary Collection の未反映（`ec=16` / `ec=255`）、人型 NPC が自キャラの外見になる現象、モンスターモデルの不一致（ルーインランナーなど）、および Demihuman NPC（レターモーグリなど）が透明化する不具合の根本原因を特定し、自立的かつ確実に MOD や外見が反映されるように修正する。

## 2. 根本原因の解析と対策方針

### 1) Penumbra コレクションが反映されずバニラになる現象 (`ec=16: InvalidActor`)
- **原因**: 
  - Penumbra 1.7.2.1 の `CollectionApi.SetCollectionForObject` および `TemporaryApi.AssignTemporaryCollection` は、内部の `AssociatedIdentifier` において `ActorIdentifierFactory.FromObject` を **`allowPlayerNpc: false`** で呼び出している。
  - アクターの `ObjectKind` が `BattleNpc` の場合、`allowPlayerNpc` が false だと「NameId 0 + 有効な名前」の救済ブランチ（Player Identifier 化）が発動せず、必ず `CreateBNpcFromObject` に分岐する。
  - パペットは `NameId == 0` であるため、`VerifyNpcData(0)` に失敗して必ず **`ActorIdentifier.Invalid`** と判定され、`ec=16 (InvalidActor)` を返してコレクションの割り当てを拒否していた。
  - その結果、Penumbra のコレクションがアクターに紐付けられず、Mod が一切適用されないバニラ状態になっていた。
- **対策**:
  - 人型アクター（Glamourer / MCDF / PlayerClone / 人型NPC）の `ObjectKind` を **`ObjectKind.Player`** に設定する。
  - これにより Penumbra の `FromObject` は `CreatePlayerFromObject` に分岐し、名前（`Csp Aa`）とワールドから正当な Player Identifier として解決され、`ec=0` でコレクションが正常に紐付けられ、Redraw 時に Mod が確実に反映される。
  - モンスター（`ModelCharaId > 0`）は Phase 2 のモデル適用時に `ObjectKind.BattleNpc` に切り替える。

### 2) AQR 無効化時の MCDF Collection 反映失敗 (`ec=255`)
- **原因**: `nativeChara->GameObject.OwnerId = 0xE000_0000;` を設定していたため、Penumbra が存在しない親 GameObject を検索しようとして失敗していた。また、描画オブジェクト生成前にフライングで適用していた。
- **対策**: `OwnerId = 0` を維持し、Phase 2（DrawObject が可視化した瞬間）の完了時のみ適用する。パペット名を `Csp {hi}{lo}` に設定する。

### 3) 人型 NPC（ミューヌ、ゴントラン等）が自キャラになる現象
- **原因**: `OwnerId` 汚染により Glamourer の `GetState` が 120 フレームでタイムアウトし、フォールバックのメモリ直接書き込みがゲームエンジンに上書きされていた。
- **対策**: `OwnerId = 0` および `ObjectKind.Player` により、Glamourer が 1〜2 フレームで認識し外見が正常に適用される。

### 4) ルーインランナーが別のモブ（ラプター）になる現象
- **原因**: `mob-model-index.csv` でルーインランナーの `NameId` が `2` であり、BaseId 2 のレストレスラプターと衝突して ModelCharaId が上書きされていた。
- **対策**: モンスターカタログの一意キーを `BaseId` に固定し、既存の `ModelCharaId > 0` が設定済みの場合は不要な上書きを行わない。

### 5) レターモーグリが透明（ギズモのみ）になる現象
- **原因**: Demihuman は装備モデルIDが書き込まれないと透明になるが、`NpcEquip.RowId == 0` の場合にインライン装備を読んでいなかった。
- **対策**: `baseRow` のインライン装備フィールドを配列化して渡すフォールバックを追加。
