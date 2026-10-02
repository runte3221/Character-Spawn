# タスクリスト: AQR仕様解析および外見適用正常化

## 1. 調査・解析フェーズ（完了）
- [x] AQuestReborn（AQR）のバイナリ（`AQuestReborn.dll` / `Brio.dll`）および逆コンパイルコードの徹底調査
  - [x] スポーン処理のフロー解明 (`CheckForCustomNpcCreationLoad` -> `ActorSpawnService.CreateCharacter` -> `CloneCharacter`)
  - [x] Penumbra コレクション適用処理のフロー解明 (`SetCollectionForObject` 引数: Guid, 直後の `RedrawObject`)
  - [x] Glamourer デザイン適用処理のフロー解明 (`ApplyDesign` / `ApplyState`, 引数, タイミング)
  - [x] MCDF 外見データ読み込み処理のフロー解明 (`McdfCharaFileManager.ApplyMcdfCharaFile`)
- [x] 現在の Character-Spawn プラグインと AQR の仕様差分の全洗い出し
  - [x] メモリ改変（`ObjectKind`, `BattleNpcSubKind`, `OwnerId`）が引き起こす Identifier 誤認の特定
  - [x] 遅延ポーリング（`ReadyJob`）によるレースコンディションの特定
  - [x] Penumbra IPC のコレクション名（string）渡しによる適用失敗の特定
  - [x] MCDF データの不要な再加工処理の特定

## 2. 実装計画・設計フェーズ（完了）
- [x] 解析結果と検討内容をまとめたドキュメントの作成 (`task.md`, `implementation_plan.md`, `walkthrough.md`)
- [x] 修正方針の確定と設計

## 3. 実装・修正フェーズ（次のステップ）
- [ ] スポーン処理の改修 (`ActorManager.cs`)
  - [ ] 不要なメモリ改変（`ObjectKind`, `BattleNpcSubKind`, `OwnerId`, `NameId` 等）の全削除
  - [ ] キャラクター命名規則の AQR 準拠 (`template.Name.Split(' ')[0] + " Cnpc"`)
- [ ] 外見適用アーキテクチャの改修 (`ActorManager.cs`, `GlamourerIpc.cs`, `PenumbraIpc.cs`)
  - [ ] スポーン完了直後の同一フレーム・同一コンテキストでの直列適用（遅延待機ループの撤廃）
  - [ ] Penumbra コレクション設定の Guid 渡し化および直後の RedrawObject 実行
  - [ ] Glamourer デザイン適用の AQR 完全準拠呼び出し
- [ ] MCDF 適用処理の純化 (`ActorManager.cs`, `McdfParser.cs`)
  - [ ] MCDF 内包の Base64 データをそのまま `ApplyState` に渡すフローへの統一
- [ ] 自キャラ（Index 0）保護ガードの強化

## 4. 検証・リリースフェーズ
- [ ] ビルド検証（ビルドエラーのないこと）
- [ ] バージョン更新（`tools/bump-version.ps1` による 0.1.42.0 への一括同期）
- [ ] Git コミット・プッシュ
- [ ] ゲーム内動作確認（自キャラ変身の根絶、スポーンアクターへの外見・Penumbra正常反映確認）
