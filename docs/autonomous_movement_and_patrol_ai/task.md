# タスクリスト: 自律移動 AI ＆ パトロール・追従・復帰ルーチン統合

## 【Step 2.2】自律移動 AI ＆ パトロール・追従・復帰ルーチン

### 1. データモデル設計 ＆ 基盤定義 (`Models/SceneData.cs`)
- [x] `MovementMode` 列挙型（None, Patrol, FollowPlayer, PatrolAndFollow）の定義
- [x] `PatrolLoopType` 列挙型（Loop, PingPong, Once）の定義
- [x] `SceneActorWaypoint` クラス（Id, Position, WaitSeconds, ActionTimelineId, Description）の実装
- [x] `SceneActorMovementConfig` クラスの実装
- [x] `SceneActorPlacement` への `Movement` プロパティ追加（既存データの互換性保持）

### 2. 自律移動エンジン基盤 (`Services/MovementService.cs`)
- [x] `MovementService` クラスの新設
- [x] `IFramework.Update` によるフレーム更新ループの実装
- [x] アクターの現在地から目標地点への滑らかな旋回（Yaw角補間）と前進移動計算
- [x] `ActorManager.UpdateActorTransform` による Puppet 座標のリアルタイム反映
- [x] デルタタイムクリップによるラグ時のワープ防止

### 3. ウェイポイント巡回ルーチン（パトロール AI）
- [x] ウェイポイント到達判定（平面距離閾値）の実装
- [x] 地点到着時アクション（指定秒数の待機、指定モーションの再生）の制御
- [x] 巡回タイプ（循環 Loop、往復 PingPong、片道 Once）ごとのインデックス進行処理
- [x] シーン Hide/Show 時の移動状態の適切なリセット・再開処理

### 4. プレイヤー接近追従 ＆ ホーム復帰ステートマシン
- [x] プレイヤー（LocalPlayer）との距離監視ルーチン
- [x] 接近時の歩み寄り（Follow）への状態遷移と停止距離（FollowStopDistance）での停止
- [x] テリトリー境界（MaxTerritoryDistance）超過時の追従中断判定
- [x] ホーム位置（初期スポーン位置）への自律歩行帰還（Return to Home）処理

### 5. SceneEditWindow への UI 統合 ＆ 3D パス可視化
- [x] Scene Edit ウィンドウへの「Movement」タブ／セクション追加
- [x] 移動モード、巡回タイプ、移動速度スライダー等の設定 UI
- [x] 「📍 自キャラ位置を追加」「📍 アクター位置を追加」ボタンとリスト編集（待機秒数・並び替え・個別削除・全消去）
- [x] プレイヤー追従・テリトリー境界設定 UI
- [x] 3D 空間上での巡回ルートライン（パス描画）およびウェイポイント番号ピンの可視化 (`UI/GizmoRenderer.cs`)

### 6. 実機動作検証 ＆ リリース
- [x] バージョン更新（`tools/bump-version.ps1 0.1.82.0`）
- [x] CHANGELOG.md および docs の同期
- [ ] Git commit & push、GitHub Actions ビルド完了確認

