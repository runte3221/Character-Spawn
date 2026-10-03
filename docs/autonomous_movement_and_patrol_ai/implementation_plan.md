# 実装計画: 自律移動 AI ＆ パトロール・追従・復帰ルーチン統合

## 1. 概要
Character Spawn の自律行動システム（Step 2.2）として、配置したカスタムキャラクター（Puppet）が指定されたルートを自律的に巡回（パトロール）し、プレイヤー接近時には自然に歩み寄り、一定範囲を超えると初期位置へ戻る「生きた自律 NPC の振る舞い」を実現する。

---

## 2. アーキテクチャ設計

### 2.1 データモデル設計 (`Models/SceneData.cs`)
既存のシーンデータ構造との 100% 後方互換性を維持しながら、移動制御用のデータモデルを追加する。

```csharp
public enum MovementMode
{
    None = 0,               // 移動なし (静止・ポーズ固定)
    Patrol = 1,             // ウェイポイント巡回
    FollowPlayer = 2,       // プレイヤー接近時追従のみ
    PatrolAndFollow = 3     // 通常は巡回し、プレイヤー接近時に一時中断して追従
}

public enum PatrolLoopType
{
    Loop = 0,               // 循環 (A -> B -> C -> A ...)
    PingPong = 1,           // 往復 (A -> B -> C -> B -> A ...)
    Once = 2                // 片道 (A -> B -> C で停止)
}

public class SceneActorWaypoint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Vector3 Position { get; set; } = Vector3.Zero;
    public float WaitSeconds { get; set; } = 0.0f; // 到着時の待機秒数
    public ushort ActionTimelineId { get; set; } = 0; // 到着時のモーション (0=直前のモーション維持)
    public string ActionTimelineKey { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class SceneActorMovementConfig
{
    public MovementMode Mode { get; set; } = MovementMode.None;
    public PatrolLoopType LoopType { get; set; } = PatrolLoopType.Loop;
    public float Speed { get; set; } = 2.5f; // 移動速度 (m/s) [歩き=2.0, 駆け足=4.0, 走り=6.0]
    public float TurnSpeed { get; set; } = 360.0f; // 旋回速度 (度/秒)
    
    // ウェイポイントリスト
    public List<SceneActorWaypoint> Waypoints { get; set; } = new();

    // 移動中モーション設定
    public ushort WalkTimelineId { get; set; } = 0; // 0=自動判定または通常移動
    public string WalkTimelineKey { get; set; } = string.Empty;

    // プレイヤー接近追従 & テリトリー帰還設定
    public float FollowTriggerDistance { get; set; } = 4.0f; // プレイヤーがこの距離内に入ったら歩み寄り開始
    public float FollowStopDistance { get; set; } = 1.8f;    // プレイヤーの手前この距離で停止
    public float MaxTerritoryDistance { get; set; } = 12.0f; // ホーム位置からこの距離以上離れたら追従中断
    public bool ReturnToHome { get; set; } = true;          // 追従解除時にホーム位置/直前地点へ歩いて戻る
}
```

### 2.2 自律移動エンジン基盤 (`Services/MovementService.cs`)
毎フレームの `Framework.Update` イベントで、アクティブなアクターの自律移動状態を更新する。

1. **移動補間と滑らかなターン (Lerp / Slerp)**:
   - 目的地（次のウェイポイントまたはプレイヤー）への方向ベクトルを算出。
   - アクターの向き（Yaw角）を急激に変えるのではなく、`TurnSpeed`（度/秒）に基づく補間で自然に目的地へ振り向かせる。
   - 目的地に向かって `Speed * deltaTime` 分だけ位置を進め、`ActorManager.UpdateActorTransform` でゲーム内の Puppet に即時反映。
2. **到着判定と地点アクション**:
   - 目的地との平面距離が閾値（例: 0.2m）以内になったら「到着」と判定。
   - `WaitSeconds > 0` の場合はタイマーを開始して静止。
   - `ActionTimelineId > 0` の場合は指定モーション（見渡す、座る、作業するなど）を `AnimationService` 経由で再生。
   - 待機完了後、巡回方式（`Loop`, `PingPong`, `Once`）に従って次のウェイポイントをターゲットに設定。
3. **プレイヤー接近追従 ＆ ホーム帰還ステートマシン**:
   - `State: PATROLLING`: 通常のウェイポイント巡回。
   - `State: FOLLOWING`: プレイヤーが `FollowTriggerDistance` 以内に接近かつホームから `MaxTerritoryDistance` 未満の場合に遷移。プレイヤーの手前 `FollowStopDistance` まで歩み寄る。
   - `State: RETURNING`: プレイヤーが離れた、またはテリトリー境界を超えた場合に遷移。元の位置（ホームまたは直前のウェイポイント）へ自律歩行で戻り、復帰後に巡回を再開。

### 2.3 UI 統合 (`UI/SceneEditWindow.cs`)
Scene Edit ウィンドウに「Movement」タブ（またはモーション・配置と連動した設定セクション）を追加。

1. **基本設定**:
   - 移動モードの選択（静止 / 巡回 / 追従 / 巡回+追従）
   - 巡回タイプ（循環 / 往復 / 片道）
   - 移動速度スライダー（1.0m/s 〜 10.0m/s、プリセット: 歩き / 駆け足 / 走り）
2. **ウェイポイント管理**:
   - 「📍 自キャラの位置をウェイポイントに追加」ボタン
   - ウェイポイント一覧（並び替え、削除、待機秒数・モーション指定）
3. **プレイヤー追従・帰還設定**:
   - 感知距離、停止距離、テリトリー限界距離スライダー
   - 「ホームへ戻る」チェックボックス
4. **3D 空間パス描画 (Gizmo / Path Overlay)**:
   - ウェイポイント間を結ぶ線（パスライン）と各ウェイポイントのピン番号を 3D 空間上にプレビュー表示。

---

## 3. 実装ステップとマイルストーン

### Step 1: データモデル拡張と基盤新設
- `Models/SceneData.cs` に `MovementMode`, `PatrolLoopType`, `SceneActorWaypoint`, `SceneActorMovementConfig` を追加。
- `SceneActorPlacement` に `Movement` プロパティを追加。

### Step 2: MovementService の作成と基本移動・旋回ロジックの実装
- `Services/MovementService.cs` を新設。
- フレーム更新ループ（`OnFrameworkUpdate`）での座標・向き補間ルーチンを構築。
- `ActorManager.UpdateActorTransform` を用いた Puppet 座標更新の検証。

### Step 3: ウェイポイント巡回（パトロール移動）ルーチンの実装
- ウェイポイント順送り（Loop / PingPong / Once）処理の実装。
- 地点到着時の待機タイマーおよびアクション再生機能の実装。

### Step 4: プレイヤー接近追従 ＆ ホーム復帰ステートマシンの実装
- プレイヤー距離監視と歩み寄りルーチンの実装。
- テリトリー境界制限と自動帰還（Return to Home）処理の実装。

### Step 5: SceneEditWindow への Movement UI ＆ パス可視化の実装
- UI タブの新設、ウェイポイント追加・編集・削除 UI の構築。
- 3D 空間への移動ルートライン描画。

### Step 6: 統合検証・ビルド・リリース
- 多様なアクター（MCDF, 人型NPC, モンスター, デミヒューマン）での移動挙動テスト。
- バージョン更新（`tools/bump-version.ps1`）、ドキュメント同期、GitHub Push、CI/CD ビルド確認。
