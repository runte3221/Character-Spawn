# タスクリスト: ウェイポイント演出連動・追従動的復帰・Brioポーズ統合準備

## 状態凡例
- [ ] 未着手 (Not Started)
- [/] 進行中 (In Progress)
- [x] 完了 (Completed)

---

## タスク一覧

### フェーズ 1: ドキュメント作成と設計合意
- [x] マスター開発計画および既存コードの確認
- [x] 実装計画書 (`implementation_plan.md`) の作成
- [x] タスクリスト (`task.md`) の初期化
- [x] ユーザーへの実装計画提示と合意形成

### フェーズ 2: データモデル拡張 (`SceneData.cs`) [x]
- [x] `SceneActorWaypoint` への演出プロパティ追加
  - `FacialTimelineId` (到着時表情ID)
  - `FacialKey` (表情名/キー)
  - `DialogueText` (到着時セリフテキスト)
- [x] `SceneActorMovementConfig` への接近リアクション・動的復帰・リーシュ制御プロパティ追加
  - `ProximityReactionType` (Follow / StopAndLook / GreetAndResume)
  - `GreetTimelineId` / `GreetFacialId` (挨拶モーション・表情)
  - `GreetDurationSeconds` / `ReactionCooldownSeconds` (挨拶待機秒数・クールダウン)
  - `ResumeNearestWaypoint` (直近ウェイポイントへの動的復帰フラグ)
  - `LeashRange` (巡回ルートからの最大許容離脱距離)
- [x] Brioポーズ連携用データモデル (`BrioPoseData`) の先行定義

### フェーズ 3: 演出連動エンジン実装 (`AnimationService.cs` & `MovementService.cs`) [x]
- [x] `AnimationService` の拡張
  - 単発表情適用・スロット2フリーズメソッドの整備 (`ApplyFacialDirect`)
  - モーション/表情の復元（通常待機状態へのシームレス復帰: `RestoreDefaultMotion`）
  - アニメーション一時停止/フレームフリーズ基盤の整備 (`FreezeCurrentFrame`, `UnfreezeFrame`)
- [x] `MovementService` の状態遷移マシン拡張
  - `WaitingAtWaypoint` での到着時モーション・表情・セリフのトリガー
  - 待機タイマー満了時の基本モーション復帰処理
  - 移動中（WalkTimeline）と待機中のモーション切り替え制御
- [x] 接近時リアクション処理の実装
  - `StopAndLook`: その場停止＆プレイヤー視線追従
  - `GreetAndResume`: 立ち止まり＆挨拶エモート＋表情再生、プレイヤーが範囲内に留まっていてもモーション完了後に即座に巡回を再開し、一度範囲外へ離脱（または巡回1周）して戻ってきたら再トリガーする離脱リセット機構
- [x] 追従離脱時の動的復帰アルゴリズム
  - プレイヤー離脱時にルート上の最近傍ウェイポイントを自動選定
  - 巡回ルートからの離脱限界（Leash）による安全復帰ルーチン

### フェーズ 4: UI 実装 (`SceneEditWindow.cs`) [x]
- [x] ウェイポイント編集行の演出設定UI拡張
  - 各ウェイポイントの展開パネル (折りたたみ / 設定モーダル)
  - 到着時待機時間スライダー
  - 到着時モーション/エモートピッカー連動
  - 到着時表情ピッカー連動
  - セリフ入力テキストボックス
- [x] 接近時リアクション設定UI追加
  - リアクションタイプ選択 (追従 / 立ち止まり見つめる / 挨拶して巡回再開)
  - 挨拶モーション・表情ピッカー、挨拶秒数、クールダウン秒数スライダー
- [x] 追従連携・復帰オプションのUI追加
  - 動的復帰タイプ設定（直近WP復帰 / 中断WP復帰）
  - リーシュ範囲（Leash Range）スライダー

### フェーズ 5: ビルド・検証・ドキュメント完了 [x]
- [x] `CHANGELOG.md`, `package.json` の更新
- [x] `walkthrough.md` の作成
- [x] Git コミット & プッシュ
