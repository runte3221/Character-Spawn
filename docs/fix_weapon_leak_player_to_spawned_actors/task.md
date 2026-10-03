# タスク: 自キャラ抜刀武器のスポーンアクター（サキュバス・人型NPC）への漏洩解消および正規武器サポート

## 概要
自キャラが武器を抜刀した状態で Show を行った際、サキュバス（デミヒューマン）およびアリゼー（人型NPC）が自キャラと同じ武器を装備して表示されてしまう問題を根本的に解消する。
また、元から武器を所持しているモブ・モンスターが存在することを考慮し、一律に武器を消去するのではなく、ゲームデータ（BNpcBase / ENpcBase / NpcEquip）に定義された正規の武器モデルを正確に反映・表示する仕組みを構築する。

## タスクリスト
- [x] 1. ゲームデータ層の武器モデルID取得・保持拡張 (`Services/GameDataService.cs`, `Models/CharacterModels.cs`) <!-- id: 1 -->
  - [x] `CharacterTemplate` に `NpcMainHandModelId` / `NpcOffHandModelId` を追加 <!-- id: 1.1 -->
  - [x] `NpcAppearanceData` に `MainHandModelId` / `OffHandModelId` を追加 <!-- id: 1.2 -->
  - [x] `GetNpcAppearanceData` で `ENpcBase` / `NpcEquip` の武器モデルIDを取得・格納 <!-- id: 1.3 -->
  - [x] モンスター用 `GetMonsterEquipment(uint bNpcBaseId, uint modelCharaId)` を新設（`BNpcBase.NpcEquip` から武器・装備を自動解決） <!-- id: 1.4 -->
- [x] 2. Glamourer NPC武器適用ロジックの改修 (`Services/GlamourerIpc.cs`) <!-- id: 2 -->
  - [x] `UnmanageWeaponSlot` による放置（`Apply = false`）を完全撤廃 <!-- id: 2.1 -->
  - [x] `WriteWeaponSlot` を新設し、NPCの武器（または ItemId=0）を CustomItemId として Glamourer state に明示適用 <!-- id: 2.2 -->
  - [x] `TryApplyNpcAppearance` に武器ID引数を追加し、Glamourer 経由で自キャラ武器を完全パージ <!-- id: 2.3 -->
- [x] 3. アクター管理・パイプラインのメモリ武器初期化改修 (`Managers/ActorManager.cs`) <!-- id: 3 -->
  - [x] パイプライン D (Monster): 自キャラ武器モデルIDを安全にゼロクリアし、モブ固有武器がある場合のみ反映 <!-- id: 3.1 -->
  - [x] パイプライン C (NPC): 自キャラ武器モデルIDをリセットし、NPC固有武器をセット、Glamourerと同期 <!-- id: 3.2 -->
  - [x] `SafeSetWeaponVisibility` の判定を更新し、武器持ちモンスターに対応 <!-- id: 3.3 -->
- [x] 4. 検証・ビルド・リリース <!-- id: 4 -->
  - [x] バージョン更新 (`tools/bump-version.ps1 0.1.87.0`) <!-- id: 4.1 -->
  - [x] `CHANGELOG.md` 更新 <!-- id: 4.2 -->
  - [x] `docs/fix_weapon_leak_player_to_spawned_actors/` にドキュメント同期 <!-- id: 4.3 -->
  - [x] コミット & プッシュ & GitHub Actions CI/CD ビルド完了確認 <!-- id: 4.4 -->
