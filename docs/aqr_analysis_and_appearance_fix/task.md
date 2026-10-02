# タスクリスト: AQR/HDM完全分離と独立パイプライン構築

## 1. 調査・解析・設計フェーズ（完了）
- [x] 本ツールの最終目標（①ローカルキャラ作成、②ステージシーン作成）の再定義と確認
- [x] AQR 仕様（Glamourer, Penumbra, Customize+, MCDF）の完全解析
- [x] HDM 仕様（NPC: HumanGuise, Monster: GuiseService）の完全解析
- [x] AQR と HDM の競合ポイント（DisableDraw, 待機ポーリング, Penumbra Redraw, メモリ書き換え）の徹底洗い出し
- [x] 4つの完全独立パイプラインのアーキテクチャ設計
- [x] 公式ドキュメントの作成・更新 (`task.md`, `implementation_plan.md`, `walkthrough.md`)

## 2. 実装フェーズ（完了）
- [x] **共通基盤の純化 (`ActorManager.cs`)**
  - [x] 不要なメモリ改変（`ObjectKind`, `BattleNpcSubKind`, `OwnerId`, `NameId` 等）の全削除
  - [x] キャラクター命名規則の統一 (`"{Name} Cnpc"`, 最大20文字)
  - [x] 自キャラ（Index 0 / LocalPlayer）への誤爆防止物理ガードの徹底
- [x] **パイプライン A & B: AQR 系統の実装 (`ActorManager.cs`, `PenumbraIpc.cs`, `GlamourerIpc.cs`)**
  - [x] スポーン直後の同一コンテキスト・即時直列実行化（`readyJobs` 不要化・即時完了）
  - [x] `PenumbraIpc`: コレクション名から Guid を特定し、Guid 渡しで `SetCollectionForObject` を実行 ＆ 直後 `RedrawObject`
  - [x] 通常 Glamourer: 公式 `ApplyDesign(Guid, objectIndex, 0, 7UL)` の即時呼び出し
  - [x] MCDF: 内包 Base64 データを無加工で `ApplyState` に渡し、直後 `RedrawObject` を実行
- [x] **パイプライン C & D: HDM 系統の分離実装 (`ActorManager.cs`, `GlamourerIpc.cs`)**
  - [x] パイプライン C (人型NPC): HDM HumanGuise 方式（Customize/Equip 注入、Parameters/Materials の Strip、コールドスポーン時の RevertToGameBase スキップ、即時適用）
  - [x] パイプライン D (Monster/MOB): HDM GuiseService 方式（ModelCharaId/Scale 設定、ネイティブ 2フェーズ描画待機、★Glamourer/Penumbra Redraw は一切呼ばない）
- [x] **デッドコード・残骸ポーリングの完全クリーンアップ**
  - [x] 未使用の `readyJobs`, `pendingNpcJobs` クラスおよびポーリングループを完全削除
  - [x] `UpdateFrame` を視線追従と `monsterRedrawJobs` のみにスリム化
- [x] **② シーン作成・演出機能の透過的連動確認**
  - [x] 全パイプライン（A/B/C/D）のアクターに対するギズモ移動・配置記録
  - [x] アニメーション（エモート、表情、ループ）、視線追従の適用確認
  - [x] ネームプレートの表示・非表示・カスタム名の連動確認

## 3. 検証・リリースフェーズ
- [x] 構文・コード整合性検証
- [x] バージョン更新（`tools/bump-version.ps1 0.1.42.0` による一括同期）
- [x] Git コミット・プッシュ (10d45db)
- [ ] ゲーム内実機検証:
  - [ ] 自キャラ（奥の Ruma Meow）が一切変身しないことの確認
  - [ ] AQR系（Kimo 等）が初回スポーンから正常外見＆Penumbra で表示されること
  - [ ] MCDFアクターが正常に展開・表示されること
  - [ ] NPC（ENpc）が正常に表示されること
  - [ ] モンスターが正常に表示され、消滅や自キャラ化が起きないこと
  - [ ] シーン保存・複数スポーンで各アクターが正しく配置されること
