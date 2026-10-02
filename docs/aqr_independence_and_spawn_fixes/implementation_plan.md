# 実装計画: AQR 依存脱却 & スポーン不具合修正

## 1. 概要
AQuestReborn (AQR) プラグインを無効化した状態で発生する、MCDF Temporary Collection の未反映（`ec=255`）、人型 NPC が自キャラの外見になる現象、モンスターモデルの不一致（ルーインランナーなど）、および Demihuman NPC（レターモーグリなど）が透明化する不具合の根本原因を特定し、HDM (Housing-Data-Manager) の完全なアーキテクチャに準拠して修正・自立化させる。

## 2. 根本原因の解析と対策方針

### 1) AQR 無効化時の MCDF Collection 反映失敗 (`ec=255`)
- **原因**: `nativeChara->GameObject.OwnerId = 0xE000_0000;` を設定していたため、Penumbra 内部の `ActorIdentifierFactory.FromObject` が ObjectTable から親 GameObject `0xE000_0000` を検索しようとして存在せず失敗し、無効なアクター識別子（`Invalid`）となっていた。そのため `AssignTemporaryCollection` が `ec=255 (UnknownError)` を返していた。
- **対策**: `OwnerId` の上書きを完全に削除し、デフォルトの `0` のまま維持する（HDM の `SpawnService.cs` に完全準拠）。
- **対策**: スポーン直後（まだ DrawObject が生成されていないタイミング）のフライング `ApplyAppearanceDirect` を削除し、Phase 2（DrawObject が可視化した瞬間）の完了時のみ適用する。
- **対策**: パペット名を Penumbra の検証規則に合致する `Csp {hi}{lo}` に設定する。

### 2) 人型 NPC（ミューヌ、ゴントラン等）が自キャラになる現象
- **原因**: 上記の `OwnerId` 汚染により、Glamourer の `GetState` も null を返し続け、120フレーム待ってもアクターが認識できずタイムアウトしていた。その結果フォールバックで直接 Customize を書いてもエンジンの `FilterCustomizeData` で自キャラに上書きされてしまっていた。
- **対策**: `OwnerId = 0` により Glamourer が 1〜2 フレームで即座にアクターを認識可能になり、`ApplyNpcAppearance` が正常に適用される。

### 3) ルーインランナーが別のモブ（ラプター）になる現象
- **原因**: `mob-model-index.csv` でルーインランナーの `baseId=16642, nameId=2`。`template.DataId` に `2` が保存されていた。しかし `GetMonsterModelCharaId(2)` は `cachedMonsters.FirstOrDefault(m => m.Id == 2 || m.BaseId == 2)` を呼ぶため、BaseId が 2 である「レストレスラプター（ModelCharaId 96）」にヒットし、正しい ModelCharaId（1281）がラプターの 96 に上書きされてしまっていた。
- **対策**: モンスターカタログの一意キー `Id` には常に一意な `BaseId` を採用する。また、既存の `template.ModelCharaId > 0` が設定済みの場合は不要な上書きを行わない。

### 4) レターモーグリが透明（ギズモのみ）になる現象
- **原因**: `GameDataService.GetNpcAppearanceData` において、`mcType != 1`（非人型/Demihuman）の時、`baseRow.NpcEquip.RowId != 0` の場合しか装備を読んでおらず、`NpcEquip.RowId == 0` の場合はインライン装備（`baseRow.ModelHead` 等）を読まずに `null` を返していた。Demihuman は装備モデルIDが書き込まれないと体も頭も描画されず透明になる。
- **対策**: `baseRow.NpcEquip.RowId == 0` の場合も、`baseRow.ModelHead` などのインライン装備フィールドを配列化して `NpcAppearanceData` に渡す。また、`IsHatHidden = false` を維持する。

## 3. 変更対象ファイル
- `CharacterSpawn/Services/GameDataService.cs`
- `CharacterSpawn/Managers/ActorManager.cs`
- `CharacterSpawn/package.json`
- `CharacterSpawn/CharacterSpawn.csproj`
- `CharacterSpawn/CharacterSpawn.json`
- `CharacterSpawn/repo.json`
- `CharacterSpawn/CHANGELOG.md`
