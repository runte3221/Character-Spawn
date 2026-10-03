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

### 【Step 2.1-fix2】表情フリーズ固定 ＆ LookAt体幹角度制限 ＆ モーションカテゴリ（`v0.1.73.0`）
- [x] **表情固定の完全動作化 (Brio DFC 準拠のフリーズ機構) (`Services/GameDataService.cs`, `Services/AnimationService.cs`)**
  - [x] `Emote` シート（`EmoteCategory == 3`）から全29種類のゲーム内表情（表情：笑顔、表情：口をすぼめる等）を日本語抽出
  - [x] 再生直後に表情スロット（Facial = 2）の速度を `0.0f` にフリーズしてワンショット終了・素顔戻りを防止
  - [x] 表情解除時に素顔（604）を再生して自然にリセット
- [x] **LookAt Player の体幹回転制限スライダー (`BodyTurnAngleLimit`) (`Models/SceneData.cs`, `Services/AnimationService.cs`, `UI/SceneEditWindow.cs`)**
  - [x] 0° 設定時：体は動かさず【顔と視線のみ】追従（周囲を回っても体がぐるぐる回るのを完全解消）
  - [x] >0° 設定時：左右指定角度の範囲内でのみ体幹もプレイヤーの方へ向け、上限超過時は首のみ追従
  - [x] チェック解除時の即時 `SetTargetId(0)` 発行と初期回転復帰によるリアルタイム解除
- [x] **モーションカテゴリ分類フィルター (`UI/SceneEditWindow.cs`, `Services/GameDataService.cs`)**
  - [x] `All`, `Emotes`, `NPC`, `Monster`, `Battle`, `General` のドロップダウン
  - [x] 人型NPC演技・会話・固有モーション（2,000件以上）への `[NPC]` タグ付与とワンクリック絞り込み

### 【Step 2.1-fix3】LookAt距離制限・範囲外完全解除 ＆ モーション即時割り込み ＆ 固有モーション絞り込み（`v0.1.74.0`）
- [x] **LookAt Player の距離制御 ＆ 範囲外完全解除 (`Models/SceneData.cs`, `Services/AnimationService.cs`, `UI/SceneEditWindow.cs`)**
  - [x] `LookAtMaxDistance` 設定（デフォルト 8.0m、1.0m〜30.0m）
  - [x] 範囲外に出た瞬間に `SetTargetId(0)` を発行し、遠距離での顔・視線固定を完全解消
  - [x] 8.0m へ戻すワンクリック `Reset` ボタンの設置
- [x] **エモート以外のモーション即時割り込み切り替え (デスポーン不要化) (`Services/AnimationService.cs`)**
  - [x] NPC専用モーションや戦闘アクション等を切り替えた際、前のモーションが保持される問題を解消
  - [x] Brio 準拠の `chara->SetMode(CharacterModes.AnimLock, 0);` ＋ `StopTimeline(0)` ＋ `TimelineSequencer.PlayTimeline` による即時割り込み実行
- [x] **UI パラメータの Default リセットボタン設置 (`UI/SceneEditWindow.cs`)**
  - [x] `Body Turn`（0°へ）、`Speed`（1.00xへ）、`Distance`（8.0mへ）のリセットボタン
- [x] **モデル固有モーションのフィルタリング機能 (`Services/GameDataService.cs`, `UI/SceneEditWindow.cs`)**
  - [x] モンスター、マウント、ミニオン、デミヒューマンの `ModelChara.Model` 番号（`m0015`等）を抽出
  - [x] 「固有モーションのみ (アクター名)」チェックボックスによる対象専用アクションの絞り込み

### 【Step 2.1-fix4】モーション連続切り替え完全化 ＆ モンスター共通アクション抽出 ＆ UIレイアウト刷新（`v0.1.75.0`）
- [x] **モーション連続切り替えの不具合解消 (`Services/AnimationService.cs`, `Managers/SceneManager.cs`)**
  - [x] `AnimLock` 常時適用を解除し、`Normal` モードのまま `TimelineSequencer.PlayTimeline` で切り替える HDM 準拠方式へ改修
  - [x] 手動切り替え時の 30 フレーム遅延再同期を防止（初期スポーン時のみ実行）し、待機・短尺モーションの即時定着を実現
- [x] **モンスター共通アクションの抽出対応 (`Services/GameDataService.cs`)**
  - [x] `IsCommonMonsterAction` による歩行・走行・通常待機・敵対待機・通常攻撃・被弾・死亡モーションの判別
  - [x] 「固有・共通アクションのみ」チェックボックス有効時に、モデル固有技と共通基本アクションの両方を一覧表示
- [x] **Animation タブ UI レイアウトの全面刷新 (`UI/SceneEditWindow.cs`)**
  - [x] Distance / Body Turn / Speed の整然とした 2 列インデント配置
  - [x] 「固有・共通アクションのみ」チェックボックスを独立行に配置し、テキスト被り・潰れを完全解消

### 【Step 2.1-fix5】LookAt Custom Spawn ＆ デミヒューマン描画修正 ＆ モンスター攻撃・死亡抽出完全化（`v0.1.76.0`）
- [x] **LookAt Custom Spawn (同一シーン内カスタムスポーン注視) (`Models/SceneData.cs`, `Services/AnimationService.cs`, `Managers/SceneManager.cs`, `UI/SceneEditWindow.cs`)**
  - [x] `SceneActorMotionConfig` に `LookAtCustomSpawn` (bool) と `LookAtTargetPlacementId` (Guid) を追加
  - [x] 同一シーン内の他アクターの座標・EntityId をリアルタイム解決して視線・体幹追従を行う機構の実装
  - [x] `LookAt Player` と `LookAt Custom Spawn` の排他的トグル制御（どちらか一方のみ有効）
  - [x] UI 上で注視対象アクターを選択できる `Target` ドロップダウンの追加
- [x] **サキュバス等のデミヒューマン描画不具合修正 (`Services/GameDataService.cs`, `Managers/ActorManager.cs`)**
  - [x] `ModelChara.Type == 2`（DemiHuman）のメッシュ分割仕様（頭・胴・手・脚・足）に対応
  - [x] `GetDemiHumanEquipment` で `Model`, `Base`, `Variant` から分割装備IDを自動生成して `NpcEquipmentModelIds` に適用
  - [x] ギズモしか表示されなかったサキュバス等の外見が完全描画されるように修正
- [x] **モンスター通常攻撃・死亡モーションの抽出条件完全網羅 (`Services/GameDataService.cs`)**
  - [x] `battle/auto_attack`, `battle/mon_sp_`, `normal/dead`, `battle/dead`, `damage` 等の網羅的抽出
  - [x] レストレス・ラプトル等での通常攻撃・死亡モーションの完全表示
- [x] **Animation タブ UI レイアウトの全面刷新 ＆ 横スクロールバー削除 (`UI/SceneEditWindow.cs`)**
  - [x] ユーザー指定モックアップ（画像3）に完全準拠した 2 列レイアウトへ刷新（左列: Loop / Player / Distance / Body Turn / Speed, 右列: Custom Spawn / Target）
  - [x] ラベル幅（85px）の統一整列によるガタつき・テキスト潰れの解消
  - [x] カスタムスポーン配置一覧下部の不要な水平スクロールバーを削除

### 【Step 2.1-fix6】デミヒューマン完全描画 ＆ General通常待機復旧 ＆ LookAt視線追従完全化（`v0.1.77.0`）
- [x] **サキュバス等のデミヒューマン完全描画 (`Services/GameDataService.cs`, `Managers/ActorManager.cs`)**
  - [x] サキュバス（1016）やスケルトン（1015）の Body (Top) 単体メッシュ仕様に適合
  - [x] `ModelChara.Base` を Body スロット（インデックス1）にのみ適用し、他スロットは 0 のまま保持
  - [x] 自キャラからコピーされた装備モデルIDの完全初期化（ゼロクリア）によるギズモ化根絶
- [x] **General カテゴリにおける `normal/idle` (3) / `normal/idle_inactive1` (4) の復旧 (`Services/GameDataService.cs`)**
  - [x] `BuildTimelineCache` のカテゴリ分類で `normal/` 系の基本待機動作を `[General]` に保持
  - [x] モンスター絞り込み時にも `IsCommonMonsterAction` で漏れなく両立抽出
- [x] **LookAt Custom Spawn の視線・目線（首・瞳）追従完全化 (`Managers/ActorManager.cs`, `Services/AnimationService.cs`)**
  - [x] COM アクターにユニークなワールド EntityId（`0x20000000 | (globalIdx + 1)`）を割り当て
  - [x] 注視対象アクターに `TargetableStatus |= ObjectTargetableFlags.IsTargetable` を設定し、LookAtIK が正常認識・追従するよう改修

### 【Step 2.1-fix7】モーションお気に入り登録 ＆ Favorite カテゴリ新設（`v0.1.78.0`）
- [x] **モーションお気に入り登録機能 (`Configuration.cs`, `Services/GameDataService.cs`, `UI/SceneEditWindow.cs`)**
  - [x] `Configuration.cs` に `FavoriteTimelineIds` (`HashSet<ushort>`) を追加し、お気に入り状態の永続保存を実現
  - [x] モーション一覧の各アイテム行左端に星マークボタン（★ / ☆）を配置し、ワンクリックでのお気に入り登録・解除トグル
  - [x] モーションカテゴリに「`Favorite`」を追加し、お気に入り登録されたモーションのみを瞬時にフィルタリング
  - [x] すべてのカテゴリ表示において、お気に入り登録されたモーションを最上位に優先ソート

### 【Step 2.1-fix8】Hide/Show 時の LookAt Custom Spawn 視線維持 ＆ ネイティブ待機保護（`v0.1.79.0`）
- [x] **シーン Hide → Show 時における LookAt Custom Spawn 視線追従の維持 (`Managers/SceneManager.cs`, `Services/AnimationService.cs`)**
  - [x] `SpawnPlacementInternal` の `ApplyMotion` 呼び出し条件に `LookAtCustomSpawn` および速度変更を追加
  - [x] 初期スポーン時（`isInitialSpawn`）に、モーション・表情未指定時の強制通常待機（1）や素顔（604）の再生を抑止し、モンスターやNPCのネイティブ待機アニメーションを保護
  - [x] 追従ループにおいて注視対象アクターの `EntityId` 再同期と `TargetableStatus` 保証を徹底

### 【Step 2.1-fix9】デミヒューマン（サキュバス種）固有モーション抽出の完全対応（`v0.1.80.0`）
- [x] **デミヒューマン固有プレフィックス（`d****`）の自動判定 ＆ フィルタリング強化 (`Services/GameDataService.cs`, `UI/SceneEditWindow.cs`)**
  - [x] `ModelChara.Type == 2`（DemiHuman）に対応する `GetModelPrefix` を新設（`d1016` 等）
  - [x] 「固有・共通アクションのみ」の絞り込みで、`d1016` だけでなくモデル番号数値（`1016`）による柔軟な部分一致を導入
  - [x] ActionTimeline 読み込み時にサキュバス関連キーをログ記録するトレーサビリティの追加

### 【Step 2.2】自律移動 AI ＆ パトロール・追従・復帰ルーチン（次工程）
- [ ] ウェイポイント巡回ルーチン (指定ルート巡回、各地点でのモーション再生)
- [ ] プレイヤー接近感知・追従・規定距離超過時の元の位置復帰ルーチン
- [ ] Scene Edit UI への巡回ルート・追従パラメータ設定タブ追加


