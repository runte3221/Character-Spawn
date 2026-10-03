# タスクリスト: ポージング・モーション ＆ 自律移動 AI 統合 (根幹機能)

## 概要
Character Spawn プラグインの根幹機能となる、カスタムスポーンアクターに対するアニメーション制御（ActionTimeline、シームレスループ、再生速度、表情固定、視線追従）および自律移動AI（ウェイポイント巡回・プレイヤー追従・元位置復帰）の実装を行う。

---

## タスク進捗

### 【Step 2.1】アクション・モーション再生基盤 ＆ 視線追従・表情固定（`v0.1.71.0`）
- [x] **アニメーション制御サービスの新設 (`Services/AnimationService.cs`)**
  - [x] HDM (`AnimationService`) 準拠の ActionTimeline 再生 (`PlayTimeline`)
  - [x] `BaseOverride` によるシームレスループ維持機構
  - [x] `OverallSpeed` によるモーション再生速度調整 (0.1x〜3.0x)
  - [x] `ActionTimeline` 表情スロットによる表情固定
  - [x] プレイヤー接近時のネイティブ視線追従 (`SetTargetId` & 滑らかな方位角補間)
- [x] **シーンライフサイクル連動 (`Managers/SceneManager.cs`)**
  - [x] スポーン時の `placement.Motion` 自動適用
  - [x] デスポーン時・非表示時の `StopMotion` (Idle 復帰)
- [x] **Scene Edit UI アニメーションエディタ本実装 (`UI/SceneEditWindow.cs`)**
  - [x] ActionTimeline / エモートのインクリメンタル検索・選択リスト
  - [x] ループ・速度・表情・視線トグル
  - [x] 設定変更のリアルタイムプレビュー反映
- [x] **CI/CD ビルド修正 ＆ リリース**
  - [x] `UI/SceneEditWindow.cs` の namespace インポート抜け修正
  - [x] `AnimationService.cs` の `SetRotation` 型安全化
  - [x] `v0.1.71.0` GitHub Actions ビルド・自動リリース完了

### 【Step 2.1-fix】実機フィードバックに基づく機能改修・完全化（`v0.1.72.0`）
- [x] **表情選択の完全ロード ＆ 独立スロット化 (`Services/GameDataService.cs`, `Services/AnimationService.cs`)**
  - [x] `facial/` パターンによる全78種類の表情抽出と分かりやすい名称マッピング
  - [x] スロット2（ActionTimelineSlots.Facial）による待機モーションを阻害しない独立表情固定
  - [x] 表情コンボボックスの名称プレビューとインクリメンタル検索
- [x] **LookAt Player 追従の完全動作化 (`Services/AnimationService.cs`)**
  - [x] `NativeAddress` 直接参照による COM アクター追従バイパス
  - [x] `SetTargetId` と滑らかな体幹方位角補間による8m以内追従・範囲外自動復帰
- [x] **モーション再生速度の強制維持 (`Services/AnimationService.cs`)**
  - [x] `TimelineSequencer.SetSlotSpeed(0, speed)` による速度スケーリング
  - [x] 毎フレームの速度維持ループ
- [x] **モーション一覧件数の拡張 (100 -> 500件) とエモート最優先表示 (`UI/SceneEditWindow.cs`)**
- [x] **非同期モデルロード後の自動再同期 (30-tick resync)**

### 【Step 2.2】自律移動 AI ＆ パトロール・追従・復帰ルーチン（次工程）
- [ ] ウェイポイント巡回ルーチン (指定ルート巡回、各地点でのモーション再生)
- [ ] プレイヤー接近感知・追従・規定距離超過時の元の位置復帰ルーチン
- [ ] Scene Edit UI への巡回ルート・追従パラメータ設定タブ追加
