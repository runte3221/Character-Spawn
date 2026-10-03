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

### 【Step 2.2】自律移動 AI ＆ パトロール・追従・復帰ルーチン（次工程）
- [ ] ウェイポイント巡回ルーチン (指定ルート巡回、各地点でのモーション再生)
- [ ] プレイヤー接近感知・追従・規定距離超過時の元の位置復帰ルーチン
- [ ] Scene Edit UI への巡回ルート・追従パラメータ設定タブ追加
