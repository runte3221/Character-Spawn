# 修正内容の確認 (Walkthrough): ウェイポイント演出連動・接近時挨拶＆自動巡回再開・動的復帰・Brioポーズ基盤 (v0.1.93.0)

## 実装の概要
マスター開発計画【Step 2 完了フェーズ】に基づき、以下の全機能を実装・統合いたしました。

1. **接近時インタラクション（立ち止まり ＆ 挨拶エモート後の自動巡回再開 ＆ 離脱リセット）**:
   - 巡回中にプレイヤーが Trigger Distance 内に入った際、その場で足を止めてプレイヤーに向き直り、指定の挨拶エモート＋表情を再生。
   - **プレイヤーが範囲内に留まっていても、指定秒数後に元の巡回ルートへ自動で歩き出す**。
   - 巡回ルートを進んでプレイヤーの範囲外へ一度離脱するとフラグが自動リセットされ、**「巡回から戻ってきたときに再度プレイヤーが範囲内にいれば、再び立ち止まって挨拶エモートを行う」** 自然なNPCの生態系サイクルを確立。
2. **ウェイポイント（Waypoints）演出連動（待機・モーション・表情・セリフ）**:
   - 各通過地点（ウェイポイント）に到着時の待機秒数、到着時モーション、到着時表情、セリフを設定可能に。
   - 地点到着時に指定秒数待機しながらモーション・表情を再生し、タイマー満了時に通常待機モーションへ安全復帰して次の地点へ移動。
   - `SceneEditWindow` の各WP行に展開ボタン（⚙）を追加し、インライン設定パネルから簡単に編集可能。
3. **巡回＋追従の連携高度化（動的復帰 & リーシュ管理）**:
   - プレイヤー追従で離れた後、初期位置や直前地点だけでなく「現在地から最も近いウェイポイント」を即座に自動選定してスムーズに巡回へ復帰（`ResumeNearestWaypoint`）。
   - 巡回ルートからの最大許容離脱距離（`LeashRange`）を設けて過剰な追従を防止。
4. **Brio ポーズ固定（Idle / Freeze）の統合準備**:
   - `AnimationService` にフレームフリーズ機構（`FreezeCurrentFrame`, `UnfreezeFrame`）を新設。
   - 外部ポーズ（Brio / Anamnesis等）のボーン姿勢適用を見据えたデータモデル `BrioPoseData` を先行定義。

---

## 修正内容詳細

### 1. データモデル (`Models/SceneData.cs`)
- **`ProximityReactionType` の新設**:
  - `Follow` (従来の接近追従)
  - `StopAndLook` (その場で立ち止まり見つめる)
  - `GreetAndResume` (立ち止まって挨拶エモート再生後、自動巡回再開)
- **`SceneActorWaypoint` の演出プロパティ拡張**:
  - `WaitSeconds`: 到着時待機秒数 (0 = ノンストップ通過)
  - `ActionTimelineId`, `ActionTimelineKey`: 到着時再生モーション
  - `FacialTimelineId`, `FacialKey`: 到着時再生表情
  - `DialogueText`: 到着時セリフ
- **`SceneActorMovementConfig` の拡張**:
  - `ProximityReaction`, `GreetTimelineId`, `GreetFacialId`, `GreetDurationSeconds`, `ReactionCooldownSeconds`
  - `ResumeNearestWaypoint`, `LeashRange`
- **`BrioPoseData` の先行定義**:
  - ボーン名、回転、位置オフセット、スケールを保持するシリアライズモデル

### 2. アニメーション制御基盤 (`Services/AnimationService.cs`)
- **`ApplyTemporaryAction(spawned, actionTimelineId, facialTimelineId, speed)`**:
  - ウェイポイント到着時アクションや接近時挨拶エモートを一時的にスロット0・スロット2で再生。
- **`ApplyFacialDirect(spawned, facialTimelineId)`**:
  - スロット2に表情タイムラインを流し込み、速度を0にして表情をフリーズ固定。
- **`RestoreDefaultMotion(spawned, defaultMotion, currentRotation)`**:
  - アクション終了時に通常待機モーション・表情・視線設定へ安全に復帰。
- **`FreezeCurrentFrame(spawned)` / `UnfreezeFrame(spawned, resumeSpeed)`**:
  - スロット0の再生速度を0.0fにしてそのフレームで完全静止させるBrioポーズ固定基盤。

### 3. 自律移動エンジン (`Services/MovementService.cs`)
- **新ステートの追加**:
  - `PausingOnProximity`: その場停止・プレイヤー注視ステート
  - `GreetingPlayer`: 挨拶エモート再生ステート
- **範囲離脱リセット（Edge-Trigger with Exit Reset）**:
  - 挨拶完了後は `HasGreetedOnThisPass = true` となり、目の前にプレイヤーが立っていても迷わず巡回を再開して歩き去る。
  - 巡回してプレイヤーの Trigger Distance 範囲外へ一度離脱すると `HasGreetedOnThisPass = false` に自動リセットされ、次回戻ってきたときに再度挨拶が発火。
- **ウェイポイント到着待機と復元**:
  - `WaitingAtWaypoint` ステートで到着時アクションを再生。
  - タイマー満了時に `RestoreDefaultMotion` で通常待機へ戻し、次のウェイポイントへ向けて前進。
- **最近傍ウェイポイント探索・復帰**:
  - 追従離脱時、現在地から全ウェイポイントまでのユークリッド距離を比較し、最も近いインデックスを即座に選定して移動再開。

### 4. UI実装 (`UI/SceneEditWindow.cs`)
- **接近時リアクション設定**:
  - 「On Proximity」ドロップダウン（Follow / Stop & Look / Greet & Resume）。
  - Greet & Resume 選択時: Greet Motion ID, Facial ID, Greet Duration (秒), Cooldown (秒) スライダーを表示。
  - Follow 選択時: Stop Dist, Leash Range, Resume to Nearest WP チェックボックスを表示。
- **ウェイポイント一覧とインライン演出パネル**:
  - 各WP行に「⚙」展開ボタンを配置。
  - クリック時にインラインパネルが展開し、Motion ID, Facial ID, Dialogue テキストボックスを直接編集可能。

---

## ユーザー操作手順・検証方法

### 1. 接近時挨拶 ＆ 自動巡回再開の検証
1. プラグインを **v0.1.93.0** に更新。
2. 巡回ルートを持つキャラクターを選択し、Movement タブを開く。
3. **On Proximity** を `Greet & Resume (挨拶エモート後に巡回再開)` に設定。
4. **Greet Motion ID** に `50` (手を振る) または `52` (お辞儀)、**Facial ID** に `501` (笑顔) を入力。
5. キャラクターが巡回している進路の前方にプレイヤーで立ちふさがる。
6. **確認点**:
   - キャラクターが Trigger Dist 範囲内に入ると足を止め、プレイヤーに向き直って笑顔で手を振る/お辞儀をする。
   - プレイヤーが目の前に立ったままでも、指定秒数（デフォルト3秒）経過後に元の歩行を再開して巡回ルートへ歩き去る。
   - キャラクターが巡回して1周回って戻ってきたら、再びプレイヤーの前で足を止めて挨拶エモートを行う。

### 2. ウェイポイント演出連動の検証
1. 巡回ルートの任意のウェイポイント（例: #2）の行にある「**⚙**」ボタンを押して演出パネルを展開。
2. **Wait Sec** を `4.0s` に設定。
3. **Motion ID** に `60`（考える・周囲を見回すなど）、**Facial ID** に任意の表情ID、**Dialogue** にセリフを入力。
4. **確認点**:
   - キャラクターが #2 の地点に到着すると立ち止まり、4秒間指定モーションと表情を再生する。
   - 4秒経過後、元の通常モーションに戻って #3 の地点へ向かって歩き出す。

### 3. 追従からの動的復帰（Nearest WP）の検証
1. **On Proximity** を `Follow` に設定し、`[x] Resume to Nearest WP` を有効にする。
2. 巡回中のキャラクターに近づいて自分についてこさせ、巡回ルートの途中まで誘導する。
3. スプリント等でキャラクターから一気に離脱する。
4. **確認点**:
   - キャラクターが迷わず「現在地から一番近いウェイポイント」へ向かって歩き出し、そこから自然に巡回を再開する。
