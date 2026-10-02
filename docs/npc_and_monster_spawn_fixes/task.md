# タスクリスト: NPC および モンスター/MOB スポーンの描画不具合修正 (HDM準拠)

## 概要
NPC および モンスター / MOB を選択してスポーンさせた際に、3Dモデルが表示されずギズモしか表示されない問題を解消する。
HDM (https://github.com/Enceladeum/HDM) の実装を徹底調査した結果判明した、人間ベースライン描画オブジェクトの確立、段階的再描画（Draw-when-ready / RedrawJob）、および Glamourer 経由の人型NPC容姿注入を実装する。

## タスク項目

- [x] 1. 設計ドキュメントの作成 (`task.md`, `implementation_plan.md`, `walkthrough.md`) <!-- id: 0 -->
- [x] 2. `GlamourerIpc.cs` の拡張 <!-- id: 1 -->
  - `GetState` (`Glamourer.GetState`, `(int ec, JObject? state)`) の追加
  - `ApplyNpcAppearance(int actorIndex, byte[] customizeData, ulong[]? equipmentModelIds)` の実装 (HDM の `HumanGuise` 準拠)
  - `CustomItemId` ビット計算と `CustomizeMap` による 26バイト CustomizeData の JObject マッピング
  - `Parameters` / `Materials` 除去によるプレイヤー肌色オーバーライド防止
- [x] 3. `ActorManager.cs` のモンスタースポーン処理を HDM 準拠に改修 <!-- id: 2 -->
  - 初期スポーン時は常に人間クローン（`ModelCharaId = 0`, `Scale = 1.0f`）として生成
  - Phase 1: `IsReadyToDraw()` -> `EnableDraw()`
  - Phase 2: `DrawObject != null && DrawObject->IsVisible`（人間ベースラインの実体化）を確認
  - 実体化後にモンスターモデルへ切り替え：`ModelCharaId` と `Scale` を書き込み
  - `DisableDraw()` -> `MonsterRedrawJob`（待機 + `IsReadyToDraw()` -> `EnableDraw()`）でモンスターを確実にロード
  - モンスター時は Penumbra Redraw や Glamourer の呼び出しを完全抑止（DrawObject の消滅防止）
- [x] 4. `ActorManager.cs` の人型 NPC スポーン処理を HDM 準拠に改修 <!-- id: 3 -->
  - 人型 NPC 実体化時に `GlamourerIpc.ApplyNpcAppearance` を適用
  - 骨格再構築のための安全な Redraw フローを適用
- [x] 5. テンプレート保存・復元処理の確認・強化 (`CharacterLibraryTab.cs` & `GameDataService.cs`) <!-- id: 4 -->
  - モンスター・NPC の ID, ModelCharaId, CustomizeData, NpcEquipmentModelIds の保存と読み込み確認
  - `GameDataService.GetMonsterModelCharaId` によるモデル未紐付けモンスターの補完
- [ ] 6. バージョン更新 & ビルド & 全バージョンフォルダ配置 <!-- id: 5 -->
  - `package.json` を `0.1.22` に更新
  - `CharacterSpawn.json`, `CharacterSpawn.csproj`, `repo.json` を `0.1.22.0` に更新
  - `CHANGELOG.md` 追記
  - `docs` フォルダ同期
  - GitHub Actions ビルド & 最新 DLL を全バージョンフォルダ（`0.1.22.0` を含む）へ配置
- [ ] 7. ユーザーへの完了報告と確認依頼 <!-- id: 6 -->
