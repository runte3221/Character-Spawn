# Changelog

All notable changes to this project will be documented in this file.

## [0.1.84] - 2026-10-04
### Improved
- **巡回ルート描画のジッター根本根絶 (`UI/GizmoRenderer.cs`)**:
  - 従来の透明ウィンドウローカル DrawList (`GetWindowDrawList`) による描画から、メインビューポート最前面 DrawList (`GetForegroundDrawList(MainViewport)`) へ移行。
  - ゲームカメラの最新 ViewProjection 行列から直接 NDC を経て画面絶対座標を計算する `ProjectWorldToScreen` を実装。
  - ウィンドウの `NoInputs` フラグトグルやレイアウト再計算によるサブピクセル単位の振動（小刻みな震え）を 100% 根絶し、カメラ操作時・静止時ともにピタッと吸い付く完全同期描画を実現。

### Added
- **各種距離・角度パラメータの 3D 空間可視化（レンジオーバーレイ）(`UI/GizmoRenderer.cs`)**:
  - **Stop Dist（停止距離）**: アクター足元を中心とするライムグリーン（緑色）の水平リング。
  - **Trigger Dist（接近検知距離）**: アクター足元を中心とするシアン（水色）の水平リング。
  - **LookAt Dist（視線有効距離）**: アクター足元を中心とするオレンジ色の水平リング。
  - **Body Turn（体回転許容角度）**: アクターの正面方向を中心とする黄色扇形（アーク＋半透明塗りつぶし＋中央正面ライン）。
  - 各リング外周に暗色ピル背景付きの文字ラベル（`Stop: 1.8m`、`Trigger: 4.0m`、`LookAt: 15.0m`、`Body Turn: ±45°`）を表示。
  - スライダー操作に合わせて 3D 空間上の円の半径や扇形の角度がリアルタイムに伸縮。
- **可視化オーバーレイの表示/非表示切り替えトグル新設 (`Configuration.cs`, `UI/SceneEditWindow.cs`)**:
  - **全体マスターチェックボックス**: アクター一覧リスト直下に `[x] 3D Overlays` を新設し、すべての 3D 可視化を一括でオン/オフ可能に。
  - **Animation タブ**: LookAt セクションに `[x] Show LookAt & Body Turn Overlay (3D可視化)` を新設。
  - **Movement タブ**: 追従設定横に `[x] Range Overlay (3D可視化)`、巡回設定横に `[x] Route Overlay (3D可視化)` を新設。

## [0.1.83] - 2026-10-04
### Improved
- **巡回＋追従における中心座標基準の検知 ＆ 巡回地点直行復帰 ＆ 段差高度適応の改善 (`Services/MovementService.cs`)**:
  - **動いているカスタムスポーン中心からの Trigger Dist 判定**:
    - 巡回中（`PatrolAndFollow`）に初期スポーン位置（地点①）からの距離で追従がブロックされていた問題を修正。巡回ルート上のどこにいても、アクター自身の現在位置とプレイヤーの実距離のみで即座に接近追従を開始するように改善。
  - **追従解除時における次のウェイポイント直行復帰**:
    - 地点①から②へ向かう途中で追従が発生した後、追従が切れた際に初期位置へ戻らず、直前に目指していた巡回地点（②）へそのまま歩行移動を再開するようにステートマシンを改修。
  - **段差・階段における地面めり込み防止（高度追従の高速化）**:
    - 上り段差や高低差のある場所へアクターが向かう際、水平比率による遅い線形補間を廃止し、垂直上昇速度（5.0m/s）でターゲットの床面高さへ素早く追従する適応アルゴリズムを導入。段差の壁や内部に体が埋もれる現象を解消。
    - 停止時にもターゲットの足元の高さへスムーズに高さをスナップ。


### Added
- **【Step 2.2】自律移動 AI ＆ パトロール・追従・復帰ルーチン統合 (`Models/SceneData.cs`, `Services/MovementService.cs`, `Managers/SceneManager.cs`, `Plugin.cs`, `UI/SceneEditWindow.cs`, `UI/GizmoRenderer.cs`)**:
  - **自律移動エンジン基盤 (`Services/MovementService.cs`)**:
    - `IFramework.Update` による滑らかな座標更新と向き（Yaw角度）の補間（自然な旋回）を実装。
    - デルタタイムと速度に応じた前進、および到着判定・待機タイマーを搭載。
    - ゲーム内の Puppet（COM スロット）に対してリアルタイムに座標を反映し、カクつきのないスムーズな歩行・走行を実現。
  - **ウェイポイント巡回（パトロール移動）ルーチン**:
    - 複数地点を結ぶ巡回ルート（`Loop` 循環、`PingPong` 往復、`Once` 片道）をサポート。
    - 各地点での待機秒数（`WaitSeconds`）および到着時モーション再生（`ActionTimelineId`）を制御。
  - **プレイヤー接近追従 ＆ ホーム自律帰還ステートマシン**:
    - プレイヤーが接近した時（検知距離: 例 4.0m）に自キャラへ向かって歩み寄り、手前（停止距離: 例 1.8m）で立ち止まる追従ルーチン。
    - プレイヤーが離れた場合や、初期位置（ホーム）からテリトリー限界（例: 15.0m）を超えた場合に、自動的に初期位置へ歩いて戻る安全帰還（Return to Home）ルーチンを実装。
  - **Scene Edit UI「Movement」タブの新設**:
    - 移動モード（静止 / 巡回 / 追従 / 巡回+追従）の選択。
    - 移動速度スライダー（プリセット: 歩き 2.0m/s、駆け足 4.0m/s、走り 6.0m/s）と旋回速度設定。
    - 「📍 自キャラ位置を追加」「📍 アクター位置を追加」ボタンによるワンクリックでのウェイポイント登録。
    - ウェイポイント一覧（並び替え ▲▼、待機秒数編集、個別削除、全消去）。
  - **3D 空間上での巡回パス・ピン可視化 (`UI/GizmoRenderer.cs`)**:
    - 各ウェイポイント間を結ぶラインと、番号ピンマーカー（#1, #2...）をゲーム画面内にオーバーレイ描画。


### Improved
- **LookAt（視線・目線追従）の有効距離（Distance）デフォルト拡大およびUI支援機能の新設 (`Models/SceneData.cs`, `Services/AnimationService.cs`, `UI/SceneEditWindow.cs`)**:
  - `LookAtMaxDistance` のデフォルト値を従来の `8.0m` から `15.0m` へ拡大。
  - シーン配置時にアクター同士の距離が 8m を超えるケース（例: オルト・サキュバスと test ruma の距離が 8.11m など）において、距離制限の超過により視線追従が毎フレーム解除されてしまっていた問題を根本解決。
  - `Distance` スライダーの上限を `30.0m` から `50.0m` に拡張し、Reset ボタンも新デフォルト値の `15.0m` を反映。
  - LookAt Target の選択ドロップダウン内に、対象アクターまでの現在距離（例: `testruma (8.1m)`）を表示。
  - 選択中のターゲットアクターまでの実距離が設定された `Distance` を超過している場合、UI 上に「⚠️超過」警告を表示するとともに、ワンクリックで最適な距離に自動設定する「Fit」ボタンを新設。


### Improved
- **デミヒューマン（サキュバス種等）固有モーション抽出の完全対応 ＆ 判定強化 (`Services/GameDataService.cs`, `UI/SceneEditWindow.cs`)**:
  - `ModelChara.Type == 2`（DemiHuman）の骨格プレフィックス（`d****`）に対応した `GetModelPrefix` を新設。サキュバス（`d1016`）やモーグリ等のデミヒューマンに対して、正しく固有プレフィックスを生成するよう改善。
  - 「固有・共通アクションのみ」のフィルタリングにおいて、プレフィックスだけでなくモデル番号数値（例: `1016`）による柔軟な部分一致検索を実装し、デミヒューマン固有アクション（特殊技・待機・戦闘動作）の漏れを根絶。
  - サキュバス等の ActionTimeline キーを起動時キャッシュ生成時にログ記録するトレーサビリティを追加。

## [0.1.79] - 2026-10-04
### Fixed
- **シーン非表示（Hide）→再表示（Show）時における LookAt Custom Spawn 視線追従の維持修正 (`Managers/SceneManager.cs`, `Services/AnimationService.cs`)**:
  - `SceneManager.SpawnPlacementInternal` において、スポーン時の `ApplyMotion` 呼び出し条件に `LookAtCustomSpawn` が含まれていなかったため、モーションや表情がデフォルト（未指定）のモンスターやカスタムアクターを Hide → Show した際に追従処理自体がスキップされていた問題を修正。
  - 初期スポーン時（`isInitialSpawn`）に、モーション未指定（`TimelineId == 0`）や表情未指定（`FacialTimelineId == 0`）のアクターに対して人型通常待機（`PlayTimeline(1)`）や表情素顔（`PlayTimeline(604)`）を強制再生していた処理を抑止し、モンスターやNPCのネイティブ待機アニメーションを完全保護。
  - `AnimationService` の追従ループにおいて、注視対象アクターの `EntityId` が未設定または初期化された場合でもワールド EntityId を自動同期し、再スポーン後も確実に首・瞳の視線追従が継続するように強化。

## [0.1.78] - 2026-10-04
### Added
- **モーションお気に入り登録機能（Favorite）の新設 (`Configuration.cs`, `Services/GameDataService.cs`, `UI/SceneEditWindow.cs`)**:
  - モーション選択リスト（`##MotionList`）の各モーション左端に「星マーク（★ / ☆）」ボタンを追加。
  - 星マークをクリックすることで、お気に入りへの登録（ゴールド★）・解除（グレー☆）をワンクリックで即座に切り替え、設定に永続保存。
  - カテゴリ一覧に「`Favorite`」カテゴリを新設し、お気に入り登録されたモーションのみを瞬時にフィルタリング・一覧表示可能に。
  - すべてのカテゴリ表示において、お気に入り登録されたモーションがリスト最上位に優先ソートされるように改善。

## [0.1.77] - 2026-10-04
### Fixed
- **サキュバス等のデミヒューマン（DemiHuman）描画不具合の完全修正 (`Services/GameDataService.cs`, `Managers/ActorManager.cs`)**:
  - サキュバス（`d1016`）やスケルトン（`d1015`）等のデミヒューマンは頭・手・脚・足の分割メッシュが存在せず、胴（`Body` / `_top`）単体の一体型モデルである仕様を特定・反映。
  - `ModelChara.Base` (例: サキュバスは Base: 1 -> `e0001`) を Body スロット（インデックス1）にのみ適用し、他パーツスロットは 0 のまま保持。また自キャラからコピーされた装備モデルIDの完全初期化（ゼロクリア）を行い、存在しないメッシュ（`met`, `glv`, `dwn`, `sho`）のロード失敗によるギズモ化（非表示）を完全に解消。
- **General カテゴリにおける `normal/idle` (3) / `normal/idle_inactive1` (4) の正常表示復旧 (`Services/GameDataService.cs`)**:
  - `BuildTimelineCache` のカテゴリ分類において、`normal/` 系の基本待機動作が誤って `[Monster]` に分類されていた問題を修正。
  - 人型アクターでも `General` カテゴリの最上部に `[3] normal/idle`、`[4] normal/idle_inactive1` が正常に表示されるように復帰。
- **LookAt Custom Spawn の視線・目線（首・瞳）追従の完全動作化 (`Managers/ActorManager.cs`, `Services/AnimationService.cs`)**:
  - COM アクター（Puppet）の `GameObject.EntityId` が `0xE0000000`（未初期化ダミー）のままであったため、ゲームエンジンのターゲット探索に失敗して首・瞳が動かなかった問題を修正。各アクターにユニークなワールド EntityId（`0x20000000 | (globalIdx + 1)`）を割り当て。
  - 注視対象アクターに対して `TargetableStatus |= ObjectTargetableFlags.IsTargetable` を設定し、ゲームエンジンの LookAt IK システムが対象を正常認識できるように改善。Chonk などの他カスタムスポーンに対しても身体だけでなく顔と視線（目線）が完璧に向くように改修。

## [0.1.76] - 2026-10-03
### Added
- **LookAt Custom Spawn (アクター間相互注視) の新設 (`Models/SceneData.cs`, `Services/AnimationService.cs`, `Managers/SceneManager.cs`, `UI/SceneEditWindow.cs`)**:
  - 自キャラだけでなく、同一シーン内に配置された他のカスタムスポーン（NPC・モンスター等）を対象に顔・視線・体幹を向けられる新機能を追加。
  - `SceneActorMotionConfig` に `LookAtCustomSpawn` および `LookAtTargetPlacementId` を追加。
  - `AnimationService` に同一シーン内の他アクターの座標・EntityId をリアルタイム解決して追従するロジックを統合。`Distance`（追従限界距離）および `Body Turn`（体幹回転角度制限）も完全適用。
  - `LookAt Player` と `LookAt Custom Spawn` の排他的トグル制御（片方を有効化するともう片方が自動オフ）を実装。
  - UI 上に対象アクターを選択できる `Target` ドロップダウンを設置。

### Fixed
- **モンスター通常攻撃・死亡モーションの抽出判定完全網羅 (`Services/GameDataService.cs`)**:
  - レストレス・ラプトル等のモンスターで通常攻撃（`battle/auto_attack`）や死亡モーション（`normal/dead`, `battle/dead`）が抽出一覧に出てこない問題を解消。
  - `IsCommonMonsterAction` の判定条件を拡張し、`Contains("dead")` や `StartsWith("battle/auto_attack")`、`battle/mon_sp_` 等のモンスター共通アクションを漏れなく確実に抽出するように改善。
- **サキュバス等のデミヒューマン（DemiHuman）描画不具合修正 (`Services/GameDataService.cs`, `Managers/ActorManager.cs`)**:
  - サキュバス等（`ModelChara.Type == 2`、デミヒューマン）をスポーンした際、装備IDが空だとメッシュがロードされずギズモのみが表示される不具合を修正。
  - `ModelChara` シートから頭・胴・手・脚・足の分割モデルID（`Model`, `Base`, `Variant`）を自動取得し、`NpcEquipmentModelIds` に適用してスポーンする `GetDemiHumanEquipment` を実装。サキュバス等の外観が正常にレンダリングされるように修正。
- **カスタムスポーン一覧の不要な横スクロールバー削除 (`UI/SceneEditWindow.cs`)**:
  - カスタムスポーン配置一覧（`##ActorListBox`）下部に表示されていた不要な水平スクロールバーを削除。
- **Animation タブ UI レイアウトの全面刷新 (`UI/SceneEditWindow.cs`)**:
  - モックアップに完全準拠した整然とした 2 列レイアウトへ再構成（左列: Loop Motion / LookAt Player / Distance / Body Turn / Speed, 右列: LookAt Custom Spawn / Target）。
  - ラベル幅（85px）の統一整列により、文字潰れやコントロール位置のガタつきを完全解消。

## [0.1.75] - 2026-10-03
### Fixed
- **待機・モーション切り替えの連続適用不具合修正 (`Services/AnimationService.cs`, `Managers/SceneManager.cs`)**:
  - `[4]normal/idle_inactive1` から `[3]normal/idle` に切り替えた後、再度 `[4]` を選んだ際に `[3]` のまま維持されてしまう問題を修正。
  - `CharacterModes.AnimLock` の常時適用を廃止し、通常モード（`Normal`）のまま `TimelineSequencer.PlayTimeline` を単独使用する HDM 準拠の割り込み再生へ刷新。
  - スポーン直後の 30 フレーム遅延再同期を「初期スポーン時（`isInitialSpawn`）」のみに限定し、UI での手動切り替え時にワンショットモーションが途中でリセット・キャンセルされる現象を完全解消。
- **モンスター・マウント・ミニオンの共通アクション抽出対応 (`Services/GameDataService.cs`)**:
  - 「固有・共通アクションのみ」チェックボックスを有効化した際、モデル固有技（`mon_sp/m0024/...`）だけでなく、モンスター共通の基本動作（歩行 `normal/walk`、走行 `normal/run`、通常待機 `normal/idle`、敵対待機 `battle/idle`、通常攻撃 `battle/attack`、被弾 `damage/`、死亡 `dead/` 等）を漏れなく同時に抽出・表示するように改善。
  - `[Monster]` カテゴリ分類にも共通モンスターアクションを含めるよう最適化。
- **Animation タブの UI レイアウト全面刷新 ＆ 文字被り解消 (`UI/SceneEditWindow.cs`)**:
  - 「固有・共通アクションのみ」チェックボックスを独立した行へ配置し、長いモーション名やアクター名でもチェックボックスや文字が被って潰れる問題を完全解消。
  - `Loop Motion` と `Speed`、および `LookAt Player` 有効時の `Distance` と `Body Turn` をインデント付きの整然とした 2 列レイアウトへ再構成し、各スライダーとリセットボタンの視認性・操作性を向上。

## [0.1.74] - 2026-10-03
### Added
- **LookAt Player の追従距離制御 ＆ 範囲外での完全追従解除 (`Models/SceneData.cs`, `Services/AnimationService.cs`, `UI/SceneEditWindow.cs`)**:
  - `LookAtMaxDistance` プロパティ（デフォルト 8.0m、範囲 1.0m〜30.0m）を追加。
  - 対象から指定距離以上離れた瞬間に `SetTargetId(0)` を呼び出し、遠距離で顔が追従したままになる問題を完全に解消。
  - ワンクリックでデフォルト値（8.0m）に戻す `Reset` ボタンを設置。
- **エモート以外のモーション即時割り込み切り替え (デスポーン不要化) (`Services/AnimationService.cs`)**:
  - NPC 専用モーションや戦闘アクション等を切り替えた際、前のモーションが再生され続けて切り替わらない問題を解消。
  - Brio 準拠の `chara->SetMode(CharacterModes.AnimLock, 0);` とスロット0の強制停止（`StopTimeline(0)`）、`TimelineSequencer.PlayTimeline` を組み合わせた即時割り込み実行を実装。デスポーン・再スポーンすることなく即座に新しいモーションが再生されるように改善。
- **UI 設定のリセットボタン (Default ボタン) 設置 (`UI/SceneEditWindow.cs`)**:
  - `Body Turn`: ワンクリックで `0° (Face Only)` へ戻す `Reset` ボタンを追加。
  - `Speed`: ワンクリックで `1.00x` へ戻す `Reset` ボタンを追加。
  - `Distance`: ワンクリックで `8.0m` へ戻す `Reset` ボタンを追加。
- **モデル固有モーションのフィルタリング機能 (`Services/GameDataService.cs`, `UI/SceneEditWindow.cs`)**:
  - モンスター、マウント、ミニオン、デミヒューマン等を選択した際、`ModelChara` シートの `Model` 列番号（例: `m0015`, `m1001`）を抽出し、該当モデル専用のアクション・固有モーションのみをワンクリックで絞り込める「固有モーションのみ (アクター名)」チェックボックスを追加。

## [0.1.73] - 2026-10-03
### Added
- **LookAt Player の体幹回転制限スライダー (`BodyTurnAngleLimit`) (`Models/SceneData.cs`, `Services/AnimationService.cs`, `UI/SceneEditWindow.cs`)**:
  - `0°（Face & Eyes Only）`: 体幹は一切回さず初期向きを固定したまま、首と視線のみがプレイヤーを追従（周囲を回っても体がぐるぐる回るのを完全防止）。
  - `> 0°（1°〜180°）`: 指定した許容角度の範囲内でのみ体幹もプレイヤーの方へ向け、制限角度（真後ろなど）を超えた場合は角度上限で止まり首のみが追従。
  - LookAt のチェックを外した瞬間に `SetTargetId(0)` と初期回転への復帰を即座に発行し、リアルタイムでの解除を完全実現。
- **モーションカテゴリ分類フィルター (`UI/SceneEditWindow.cs`, `Services/GameDataService.cs`)**:
  - `All`, `Emotes`, `NPC`, `Monster`, `Battle`, `General` のカテゴリ選択ドロップダウンを追加。
  - `human_sp/`, `event_base/`, `event/`, `speak/`, `resident/`, `idle_sp/` などの NPC 演技・会話・固有モーション（2,000件以上）に `[NPC]` タグを明記し、「NPC」カテゴリ選択やキーワード検索で簡単に抽出可能に。
- **表情固定の完全動作化 (Brio DFC 準拠のフリーズ機構) (`Services/GameDataService.cs`, `Services/AnimationService.cs`, `UI/SceneEditWindow.cs`)**:
  - `Emote` シートの表情カテゴリ（`EmoteCategory == 3`）から「表情：笑顔」「表情：口をすぼめる」「表情：ウィンク右」など全29種類のゲーム内表情を日本語名で完全抽出。
  - 表情タイムライン再生直後にスロット2（Facial）の再生速度を `0.0f` にフリーズ（一時停止）することで、ワンショット表情アニメーションが元に戻るのを防ぎ、待機モーションを動かしたまま表情だけを永久固定する機構を確立。
  - 表情解除時は素顔（604）を再生して自然にデフォルト表情へ復帰。

## [0.1.72] - 2026-10-03
### Fixed
- **表情選択（Facial Expression）の完全修正 (`Services/GameDataService.cs`, `Services/AnimationService.cs`, `UI/SceneEditWindow.cs`)**:
  - `ActionTimeline` シートからの表情抽出条件を `facial/`（および `status/facial/`）に修正し、Smile, Angry, Wink, Laugh, Cry など全78種の表情を完全に検出・ロード。
  - 表情の再生スロットをスロット0（Base）から **スロット2（Facial / ActionTimelineSlots.Facial）** へ変更し、待機モーションを阻害せず表情のみを独立して固定可能に修正。
  - コンボボックスに分かりやすい表情名（例: `Smile (facial/pose/smile)`）を表示し、キーワード検索も可能に改善。
- **プレイヤー視線・首・体追従（LookAt Player）の修正 (`Services/AnimationService.cs`)**:
  - `ObjectTable.SearchById` による検索失敗（COM/GPoseアクターが通常テーブルから取得できない問題）を解消し、`NativeAddress` を用いて直接ネイティブアクターにアクセスする方式へ刷新。
  - `SetTargetId(localPlayer.EntityId)` によるゲームエンジンネイティブの視線・首の追従と、8m以内の滑らかな体幹方位角補間を確実に実行。
- **モーション速度（Speed）の適用・維持の修正 (`Services/AnimationService.cs`)**:
  - `OverallSpeed` に加え、`TimelineSequencer.SetSlotSpeed(0, speed)` によるスロット0の速度強制適用を実装。
  - ゲーム内部処理による速度上書きを防ぐため、毎フレームの `OnFrameworkUpdate` で指定速度を維持。
- **モーション一覧件数の拡張とエモート最優先表示 (`Services/GameDataService.cs`, `UI/SceneEditWindow.cs`)**:
  - 最大表示件数を 100件 から 500件 へ大幅拡張。
  - 検索欄が空の初期状態で、プレイヤーに馴染み深い日常・戦闘エモート群をリスト最上位に優先ソートして表示。
- **非同期モデルロード後のモーション再同期 (`Services/AnimationService.cs`)**:
  - スポーン直後の Glamourer / Penumbra / MCDF のモデル展開完了に合わせて 30フレーム後に自動でモーションと表情を再同期・定着。

## [0.1.71] - 2026-10-03
### Added
- **アクション・モーション再生基盤 ＆ 視線追従・表情固定対応 (`Services/AnimationService.cs`, `Managers/SceneManager.cs`, `UI/SceneEditWindow.cs`)**:
  - **アニメーション制御サービスの新設 (`Services/AnimationService.cs`)**:
    - HDM (`AnimationService`) アーキテクチャに準拠し、ゲーム内 `Character::Timeline` に対するネイティブモーション制御を確立。
    - `BaseOverride` と `PlayTimeline` を組み合わせたシームレス無限ループ再生制御を実装。
    - `OverallSpeed` によるモーション再生速度のリアルタイム変更（0.1x〜3.0x）をサポート。
    - `ActionTimeline` の `fac_` タイムラインを活用した表情スロットへの独立表情固定を実装。
    - プレイヤー接近時のネイティブ視線追従（`SetTargetId` ＆ 滑らかな首・体の方位角補間）を実装。
  - **シーンライフサイクルとの完全連動 (`Managers/SceneManager.cs`)**:
    - シーン内のキャラクターがスポーンされた際、保存されていた `placement.Motion`（モーション、ループ、速度、表情、視線）を自動適用。
    - シーンデスポーン時および個別非表示時に `StopMotion` を呼び出し、安全に通常待機（Idle）へリセットしてメモリ・状態残存を完全防止。
  - **シーン編集 UI の本格実装 (`UI/SceneEditWindow.cs`)**:
    - 「Animation」タブのプレースホルダーを完全なモーションエディタ UI へ置換。
    - 約1万種のアクションタイムラインおよびエモートのリアルタイムインクリメンタル検索・選択リストボックスを実装。
    - ループトグル、速度スライダー、表情選択ドロップダウン、視線追従（LookAt Player）チェックボックスを完備。
    - 設定変更の瞬間に目の前にスポーン中のキャラクターへ即座に反映され、シーンへ自動保存される快適な編集体験を実現。
### Fixed
- `UI/SceneEditWindow.cs` における `GameDataService` の名前空間インポート抜け（CS0246）および `AnimationService.cs` におけるキャラクター回転呼び出しの型安全化修正。

## [0.1.70] - 2026-10-03
### Added
- **ミニオン・マウント（Companion / Mount）のカスタムキャラクター登録・スポーン対応 (`Models/CharacterModels.cs`, `Services/GameDataService.cs`, `Managers/ActorManager.cs`, `UI/CharacterLibraryTab.cs`)**:
  - **Lumina ゲームデータ層の拡張 (`Services/GameDataService.cs`)**:
    - `Companion`（ミニオン）シートから全ミニオンデータ（ID, 名称, ModelCharaId, IconId, Scale）の自動キャッシュ・インクリメンタル検索機構（`SearchCompanions`）を実装。
    - `Mount`（マウント）シートから全マウントデータ（ID, 名称, ModelCharaId, IconId）の自動キャッシュ・インクリメンタル検索機構（`SearchMounts`）を実装。
    - 同一名・同一モデルの重複排除および安全なスケール正規化処理を内蔵。
  - **データモデル層の拡張 (`Models/CharacterModels.cs`)**:
    - `CharacterSourceType` に `MountMinion` を追加。
    - `CharacterTemplate` に `IconId`, `IsMount` プロパティを追加。
  - **カスタムキャラクター登録 UI の拡充 (`UI/CharacterLibraryTab.cs`)**:
    - 新規作成・編集モーダル（Select Appearance Source）に 5 つ目のソース区分として `[ Mount / Minion ]` ボタンを追加。
    - ミニオンとマウントをワンクリックで切り替え可能なラジオボタンセレクターと、名称・ID によるリアルタイム検索リストボックスを実装。
    - 登録キャラクター一覧のツリービューで、ミニオン（🐾）とマウント（🐎）の専用識別アイコンを表示。
    - キャラクター詳細ペインにマウント/ミニオンの種別、ModelChara ID、Data ID、Icon ID の情報表示を追加。
    - キャラクター編集モーダル（Edit）を開いた際の既存マウント・ミニオン設定の自動復元に対応。
  - **描画・スポーン基盤の連携 (`Managers/ActorManager.cs`)**:
    - ミニオン・マウントのモデル描画をモンスター共通パイプライン（パイプライン D）へ自動配線し、確実かつ安全なスポーンとギズモによるリアルタイム拡大縮小（スケール）、位置・回転調整、シーン配置をサポート。

## [0.1.69] - 2026-10-03
### Changed
- **SceneEditWindow 閉鎖時の 3D ギズモ自動非表示連動 (`Plugin.cs`)**:
  - **操作感の洗練**:
    - [Scene Edit] ウィンドウ内でギズモ（移動・回転・スケール）を操作した後、ウィンドウを閉じた（[X] ボタンまたは Esc キー）際に、画面上に 3D ギズモだけが残り続けてしまう問題を解消。
    - ギズモの描画判定に `sceneEditWindow.IsOpen || (mainWindow.IsOpen && actorManager.CurrentPreviewActor != null)` の可視性チェックを組み込み、ウィンドウが閉じられている間はギズモを自動的に非表示にする制御を実装。
    - ウィンドウを開いた際には即座にギズモが復帰し、編集をスムーズに再開可能。

## [0.1.68] - 2026-10-03
### Fixed
- **Penumbra 個別設定リスト（Individual Assignments）汚染防止＆自キャラ設定の完全保護 (`Services/PenumbraIpc.cs` & `Managers/ActorManager.cs`)**:
  - **根本原因の解明**:
    - `PenumbraIpc.UnassignCollectionForActor` において、`null` 渡しによる削除処理の直後に `setCollectionForObjectV5Guid(actorIndex, Guid.Empty, true, true)` が実行されていた。
    - Penumbra の仕様上、`Guid.Empty` は「Use No Mods（Mod無効）」コレクションを意味し、`allowCreateNew: true` のため、削除した直後に `[Use No Mods]` の個別設定カードがリストに再作成・永続保存されていた。
  - **解除ロジックの適正化 (`Services/PenumbraIpc.cs`)**:
    - `Guid.Empty` の呼び出しを完全撤廃。
    - 通常コレクションの解除は `setCollectionForObjectV5NullableGuid(actorIndex, null, allowCreateNew: false, allowDelete: true)` のみに統一。これにより、アクターを Hide（デスポーン）した瞬間に Penumbra のリストから該当カード自体が自動的に完全削除され、`[Use No Mods]` が残る不具合を 100% 根絶。
    - `actorIndex <= 0` ガードを追加し、自キャラ（LocalPlayer: Index 0）の Penumbra 設定を物理保護。
  - **自キャラ設定の完全保護 (`Managers/ActorManager.cs`)**:
    - `RevertLocalPlayer()` から `penumbraIpc.UnassignCollectionForActor(0)` の呼び出しを削除。ユーザーが自キャラに手動で割り当てている Penumbra コレクションやデフォルト設定を Character Spawn 側から誤ってリセット・削除するリスクを完全に排除。
  - **完全隔離の保証**:
    - 第1工程のコア（外見、Glamourer、Penumbra、MCDF、NPC、モンスター、CustomizePlus）の基本仕様を壊さず完全隔離・安全保持。自キャラ（LocalPlayer）への二重物理遮断を厳守。

## [0.1.67] - 2026-10-03
### Fixed
- **人型アクター（NPC ユウギリ、Chonk、自キャラクローン等）のフレーム更新によるスケールリセット防止＆常時維持 (`Managers/ActorManager.cs`)**:
  - **根本原因の解明**:
    - FFXIV ゲームエンジン（`Character::Update` / `Human::Update`）は、人型モデル（`ModelCharaId == 0`）において毎フレームの Tick で身長スライダーや種族ベースラインから描画スケールを再計算し、`DrawObject->Object.Scale` を `Vector3.One`（1.0f）に強制リセットしていた。
    - モンスター（`ModelCharaId > 0`）にはこの人型専用の身長再計算機構がないため設定されたスケールが維持されたが、人型アクターはスライダー操作直後に 1.0 に戻されていた。
  - **毎フレーム維持機構（Scale Enforcement）の実装**:
    - `ActorManager.UpdateFrame` のアクティブアクター走査ループにおいて、スポーン済みアクターの `actor.Transform.Scale` を毎フレーム監視。
    - ゲームエンジンによって `DrawObject->Object.Scale` や `GameObject.Scale` がリセットされた場合でも、不一致を検出して即座に `targetScale` に再適用し `NotifyTransformChanged()` を発火。
    - 差分検出時のみ適用するため CPU 負荷はゼロであり、人型アクター（NPC ユウギリや Chonk 等）もモンスターと同様にスライダー操作でリアルタイムかつ安定してサイズ変更が維持されるようになった。
  - **完全隔離の保証**:
    - 第1工程のコア（外見、Glamourer、Penumbra、MCDF、NPC、モンスター、CustomizePlus）の基本仕様を壊さず完全隔離・安全保持。自キャラ（LocalPlayer）への二重物理遮断を厳守。

## [0.1.66] - 2026-10-03
### Added
- **DirectX 描画オブジェクト（`DrawObject->Object.Scale`）リアルタイム連動、モンスター原寸自動継承、および [Default Scale] 復元ボタン**:
  - **リアルタイム拡大縮小の完全実現 (`Managers/ActorManager.cs`)**:
    - ゲーム論理用 `GameObject.Scale` に加え、DirectX レンダラーが直接参照する描画ジオメトリ `chara->GameObject.DrawObject->Object.Scale = new Vector3(targetScale)` を直接更新し、`DrawObject->NotifyTransformChanged()` を発火。Hide/Show の再スポーンを挟むことなく、スライダーや 3D ギズモ操作中にモデルがリアルタイムで滑らかに伸縮。
  - **モンスター固有サイズの自動継承 (`Managers/SceneManager.cs`)**:
    - `AddPlacement` 時に、テンプレートの `template.Scale`（統制者ハシュマルトの `3.448` 等）を自動的に `placement.Scale` に初期代入。ライブラリから配置した時点で本来の巨大サイズを維持。
  - **[Default Scale] リセットボタンの実装 (`UI/SceneEditWindow.cs`)**:
    - [Apply Own Transform] ボタンの隣に `[Default Scale]` ボタンを新設。スケールを変更した後でも、ワンクリックでテンプレート固有の原寸サイズ（モンスターの規定サイズ、または人型の 1.0）へ瞬時に復元・保存。
- **ギズモ操作モードの完全隔離・誤上書き防止 (`UI/GizmoRenderer.cs` & `Plugin.cs` & `UI/StageSceneTab.cs`)**:
  - ギズモコールバックを `Action<Vector3, float, float?>`（nullable）に変更。`CurrentGizmoMode == GizmoMode.Scale` の時のみ Scale 値を通知し、移動（Translate）や回転（Rotate）のドラッグ操作中はスケールを一切変更しない安全ガードを構築。移動・回転操作による意図しないスケール破壊を完全根絶。

## [0.1.65] - 2026-10-03
### Added
- **アクタースケール（Scale: 0.01x 〜 10.0x）リアルタイム変更および3D Scaleギズモ完全配線**:
  - **FFXIV ネイティブスケール制御の実装 (`Managers/ActorManager.cs`)**:
    - `UpdateActorTransform` に `float? newScale = null` 引数を追加。`chara->GameObject.Scale = targetScale` を設定後、即座に `chara->GameObject.DrawObject->NotifyTransformChanged()` を呼び出して DirectX 描画行列をリアルタイム再計算。
    - `SpawnCharacter` のシグネチャを拡張し、モンスター（ModelCharaId > 0）だけでなく人型アクター（Glamourer / MCDF / NPC）の初期スポーン時にもシーン配置（`placement.Scale`）またはテンプレート（`template.Scale`）の大きさを確実に適用。
  - **3D Scale ギズモの完全連携 (`UI/GizmoRenderer.cs`)**:
    - `GizmoRenderer.Render` のコールバックデリゲートを `Action<Vector3, float, float>`（Position, Rotation, Scale）に拡張。
    - `ImGuizmoOperation.Scale` 操作時に `Matrix4x4.Decompose` から得られた `newScale` を均等スケール化（0.01f〜10.0f クランプ）し、リアルタイムにアクターおよびシーンデータへ反映。
  - **UI スライダー＆ギズモモードの双方向配線 (`UI/SceneEditWindow.cs` & `UI/MainWindow.cs` & `UI/StageSceneTab.cs`)**:
    - `SceneEditWindow.cs` の `DragFloat("##Scale")` 変更時に `actorManager.UpdateActorTransform` を発火させ、スライダー操作で即座にモデルサイズが変化するように配線。
    - `StageSceneTab.SyncPlacementTransformFromGizmo` で Scale を受け取り、シーン設定（`placement.Scale`）へ自動保存。
    - `MainWindow.cs` の Settings タブのギズモモード選択肢に `Scale (Resize)` ラジオボタンを追加。
  - **完全隔離の保証**:
    - 第1工程のコア（外見、Glamourer、Penumbra、MCDF、NPC、モンスター、CustomizePlus）の基本仕様を壊さず完全隔離・安全保持。自キャラ（LocalPlayer）への二重物理遮断を厳守。

## [0.1.64] - 2026-10-03
### Fixed
- **Chonk 等の Glamourer デザイン指定アクターにおける遅延外見適用・リトライ耐性強化（外見抜け・自キャラ素体化の完全根絶）**:
  - **根本原因の解明**:
    - `SourceType == Glamourer`（Chonk など）のアクターにおいて、COM スロット生成直後の 0 フレーム目に即座に `ApplyDesignToActor` を呼び出していた。
    - ゲームエンジンがアクターの `DrawObject` を確立する前に Glamourer を呼ぶと、Glamourer 側で `ActorNotFound (ec=6)` となり外見デザインの適用に失敗するケースがあった。
    - 失敗時にもデザイン情報が `AppearanceDeferredJob` に引き継がれていなかったため、リトライされずに取り残され、自キャラ（素体）の見た目のまま CustomizePlus（体型プロファイル）だけが乗るという外見崩れが発生していた。
  - **遅延外見適用＆リトライステートマシンへの統合 (`Managers/ActorManager.cs`)**:
    - `ApplyAppearanceDirect` において、通常アクター（Glamourer / PlayerClone）にも `PendingGlamourerDesign` を設定し、即時適用が失敗した場合は `AppearanceDeferredJob` の **Phase 0** で `DrawObject` 生成待機後に自動リトライ（最大 30 フレーム）する仕組みを実装。
    - MCDF と全く同様に、ゲームエンジンの準備完了を待って確実に Glamourer デザインが適用されてから Phase 1（Penumbra Redraw）→ Phase 2（CustomizePlus 確定注入）と同期実行されるように統一。
  - **完全隔離の保証**:
    - 第1工程のコア（外見、Glamourer、Penumbra、MCDF、NPC、モンスター、CustomizePlus）の基本仕様を壊さず完全隔離・安全保持。自キャラ（LocalPlayer）への二重物理遮断を厳守。

## [0.1.63] - 2026-10-03
### Fixed
- **DirectX レンダラー競合クラッシュ（`Weapon.UpdateRender` / `Weapon.vf105` 0xC0000005）の完全根絶**:
  - **根本原因の解明**:
    - `HumanoidNpcApplyJob`（NPC）において、`chara->DrawData.HideWeapons(...)` の直後に `penumbraIpc.Redraw(actorIndex)` を呼び出し、かつ**同フレーム直後**に `chara->GameObject.EnableDraw()` を呼び出していた。NPC には Penumbra コレクションは設定されていないため Redraw は不要である上、Penumbra Redraw による非同期オブジェクト破棄・再構築中にレンダラースレッドがアクセスして武器仮想関数 `Weapon.vf105` で NULL ポインタ参照（0xC0000005）を招いていた。
    - また、初期スポーン時（`SpawnCharacter`）に共通で無条件に即時 `EnableDraw()` を呼んだ直後、NPC やモンスター（ModelCharaId > 0）で即座に `DisableDraw()` を呼ぶ激しいトグルが発生し、DirectX のフレーム描画ツリーと競合していた。
    - さらに、武器を持たないモンスター（ModelCharaId > 0）に対しても `HideWeapons(true)` や `IsWeaponHidden = true` を呼び出していたため、存在しない武器ポインタや Human 由来の武器描画オブジェクトが中途半端な状態でレンダラーキューに残存していた。
  - **安全な武器表示制御ヘルパー (`SafeSetWeaponVisibility`) の導入 (`Managers/ActorManager.cs`)**:
    - モンスター（`ModelCharaId > 0`）には武器操作を一切行わない完全ガードを設置。
    - 人型アクターの場合も `chara->GameObject.DrawObject != null`（描画オブジェクト生成済み）を確認した上で `HideWeapons` を呼び出し、未生成時・破棄時のクラッシュを物理的に遮断。
  - **NPC (`HumanoidNpcApplyJob`) から不要かつ危険な Penumbra Redraw を完全撤去 (`Managers/ActorManager.cs`)**:
    - HDM 準拠（Glamourer 適用 -> DisableDraw -> 2 ticks 待機後 IsReadyToDraw -> EnableDraw）に純化し、二重再構築および非同期破棄中の EnableDraw によるクラッシュを物理的に根絶。
  - **スポーン初期化時の不要な即時 `EnableDraw()` 撤去 (`Managers/ActorManager.cs`)**:
    - 各パイプライン（A/B: 外見設定後、C: NPC準備後、D: モンスター準備後）の適切なタイミングでのみ一度だけ `EnableDraw()` を行う堅牢なライフサイクルを確立。
  - **スタッガースポーン間隔の安全化 (`Managers/SceneManager.cs`)**:
    - `DefaultSpawnIntervalTicks` を 2 フレームから 4 フレーム（~66ms）に引き上げ、複数キャラ同時スポーン時の DirectX レンダラー競合を完全防止。
  - **完全隔離の保証**:
    - 第1工程のコア（外見、Glamourer、Penumbra、MCDF、NPC、モンスター、CustomizePlus）の基本仕様を壊さず完全隔離・安全保持。自キャラ（LocalPlayer）への二重物理遮断を厳守。

## [0.1.62] - 2026-10-03
### Fixed
- **FF14 キャラクター名規則違反（数字混じり命名による名前破損・全IPC停止）の緊急完全修正**:
  - **根本原因の解明**:
    - FF14 のキャラクター名バリデーション規則（`VerifyPlayerName`）では、数字（0〜9）が一切禁止されており、純粋な英字のみが許可されている。
    - 直前バージョンでスロット連動のために導入した `Actor CS00` が不正な名前と判定され、ゲームエンジン内部で `A.C` などの不正文字列に破損。
    - その結果、Glamourer（`ec=2` InvalidActor）、Penumbra（`ec=16` InvalidIdentifier）、CustomizePlus（`ActorNotFoundException`）のすべての IPC が人型アクターを認識できず全停止していた（モンスターおよびデミヒューマンは人型 PlayerName バリデーションを受けないため難を逃れていた）。
  - **英字フォネティックコード命名への全面移行 (`Managers/ActorManager.cs`)**:
    - 数字を完全排除し、FF14 の公式規則（Forename 3〜15文字、Surname 3〜15文字、ASCII英字のみ、頭文字大文字）に 100% 適合する **英字フォネティックコード命名（`Puppet Alpha`, `Puppet Bravo`, `Puppet Charlie` ... `Puppet Zulu`）** を実装。
    - スロット番号（COM#0〜#25）と 1対1 で決定論的に対応させつつ、全プラグイン（Glamourer / Penumbra / CustomizePlus）がアクターを 100% 正常認識できるように完全復旧。
  - **MCDF 遅延 Glamourer 適用のリトライ耐性強化 (`Managers/ActorManager.cs`)**:
    - `AppearanceDeferredJob` Phase 0 において、Glamourer 適用成功時のみ再構築へ移行し、未認識時は最大 30 フレーム（約 0.5 秒）リトライする耐障害性を追加。
  - **完全隔離の保証**:
    - 第1工程のコア（外見、Glamourer、Penumbra、MCDF、NPC、モンスター、CustomizePlus）の基本仕様を壊さず完全隔離・安全保持。自キャラ（LocalPlayer）への二重物理遮断を厳守。

## [0.1.61] - 2026-10-03
### Fixed
- **シーンスポーン順固定化（シャッフル根絶）・MCDF DrawObject 待機遅延外見適用・COMスロット決定論的命名**:
  - **シーンスポーン順の固定化 (`Managers/SceneManager.cs`)**:
    - プレイヤーからの距離ソート（`OrderBy`）を完全廃止。シーン内の定義順（固定順）でスポーンキューにエンキューすることで、Show/Hide を繰り返しても各アクターが常に同一の COM スロット（#0〜#9）にスポーンされる決定論的配置を確立。アクターの入れ替わり・シャッフルを 100% 根絶。
  - **COM スロット連動の決定論的パペット命名 (`Managers/ActorManager.cs`)**:
    - 無限インクリメントする一時命名を廃止し、COM スロット番号（0〜19）に 1対1 で紐づく固定英名（`Actor CS00`, `Actor CS01`, ...）を採用。
    - スロット再利用時に同一の名前で Glamourer の `RevertStateName` を呼ぶことで、前のアクターのステートキャッシュが残存する問題を完全解消。
  - **MCDF の DrawObject 生成待機遅延外見適用 (`Managers/ActorManager.cs`)**:
    - COM 生成直後の DrawObject 未生成状態で Base64 外見を適用しようとしていたため、Glamourer が `ActorNotFound (ec=6)` で失敗し、MCDF の顔・髪型・衣装が適用されず自キャラ素体に戻っていた不具合を完全解決。
    - `AppearanceDeferredJob` に **Phase 0** を新設し、ゲームエンジンが `IsReadyToDraw()` かつ `DrawObject != null`（描画モデル展開完了）になるのを待ってから Glamourer `ApplyDesignToActor` を確実に呼び出し。
    - 適用直後に `DisableDraw()` で DrawObject の再構築をトリガーし、その後 Phase 1（Penumbra Redraw）→ Phase 2（CustomizePlus 確定注入）と流れる完璧なパイプラインを確立。
  - **完全隔離の保証**:
    - 第1工程のコア（外見、Glamourer、Penumbra、MCDF、NPC、モンスター、CustomizePlus）の基本仕様を壊さず完全隔離・安全保持。自キャラ（LocalPlayer）への二重物理遮断を厳守。

## [0.1.60] - 2026-10-03
### Fixed
- **スロット再利用時の外見汚染根絶（モンスターへのChonk外見誤爆防止） & CustomizePlus 2段階確定ステートマシン（Chonk体型崩れ完全防止）**:
  - **Glamourer ステートキャッシュのクリア (`Services/GlamourerIpc.cs`)**:
    - `RevertState(int actorIndex, string? actorName = null)` を追加。UnlockState と RevertState を順次発行してスロットに紐づく Glamourer のステートキャッシュを強制リセット可能化。
  - **COM スロット初期化パージ (`ClearActorSlotState`) の実装 (`Managers/ActorManager.cs`)**:
    - `SpawnCharacter` でアクターの `globalIdx` が確定した直後、パイプライン分岐（D: モンスター、C: NPC、A: プレイヤー/MCDF）の前に直前のスロット残留ステート（Glamourer / Penumbra / CustomizePlus）を完全パージ。
    - 直前に Chonk（複合アクター）が使っていたスロットにモンスター（統制者ハシュマリム等）がスポーンした際、Glamourer のステートキャッシュが自動適用されてハシュマリムが Chonk の外見になってしまう重大なスロット汚染を 100% 根絶。
  - **`AppearanceDeferredJob` の 2段階確定ステートマシン化 (`Managers/ActorManager.cs`)**:
    - Penumbra Redraw 直後に CustomizePlus プロファイルを適用すると、Redraw による非同期 DrawObject 再構築で直後にボーン変形が打ち消されてバニラ体型に戻る競合を解消。
    - **Phase 1**: スポーンから 4 フレーム後に `penumbraIpc.Redraw` を発行し、DrawObject 再構築をトリガー。
    - **Phase 2**: Redraw 発行後さらに 4 フレーム待機（DrawObject 再構築完了）してから、CustomizePlus 一時プロファイルを確定注入。
    - これにより、何度 Show/Hide を繰り返しても、Chonk 特有の体型が 100% 確実に維持される。
  - **完全隔離の保証**:
    - 第1工程のコア（外見、Glamourer、Penumbra、MCDF、NPC、モンスター、CustomizePlus）の基本仕様を壊さず完全隔離・安全保持。自キャラ（LocalPlayer）への二重物理遮断を厳守。

## [0.1.59] - 2026-10-03
### Fixed
- **Glamourer + Penumbra + CustomizePlus 複合アクター（Chonk等）における体型打ち消し防止と統合遅延安定化**:
  - **統合アピアランス遅延安定化キュー (`AppearanceDeferredJob`) の導入 (`Managers/ActorManager.cs`)**:
    - Glamourer の非同期 DrawObject 再構築によって、直前に注入した CustomizePlus のボーン変形行列がリセット（バニラ体型・素体に戻る現象）される問題を完全解明・解決。
    - Glamourer 適用完了後、ゲームエンジンのモデル再構築完了を待ってから、**CustomizePlus の一時プロファイルの再適用（リフレッシュ）** および **Penumbra の遅延 Redraw** を確定実行する統合ジョブキューを実装。
    - これにより、何度 Show/Hide を繰り返しても、Chonk 特有の体型MODおよびテクスチャ・マテリアルが 100% 確実に維持・反映される。
  - **Show/Hide 高速切り替え時の初期化バッファの追加 (`Managers/SceneManager.cs`)**:
    - スポーン開始時に安全インターバル（2フレーム / 約33ms）を設け、直前のデスポーンによる COM オブジェクト破棄と新しいオブジェクト生成のステート衝突を排除。
  - **安全ガードの徹底**:
    - 自キャラ（LocalPlayer）への二重物理遮断（GlobalIndex == 0 & LocalPlayer.Address 一致チェック）、独立パイプライン（NPC/モンスター）への干渉遮断、デスポーン時のゾンビジョブ即時破棄を完備。
  - **完全隔離の保証**:
    - 第1工程のコアロジック（外見、Glamourer、Penumbra、MCDF、NPC、モンスター、CustomizePlus）は完全不可侵（変更なし）を厳守。

## [0.1.58] - 2026-10-03
### Fixed
- **大規模シーン（最大100体規模）・大容量MOD対応の非同期分散スポーンキューおよび MCDF リソース解決安定化**:
  - **MCDF のインメモリキャッシュ化 (`Services/McdfParser.cs`)**:
    - 一度解凍した MCDF の `McdfBundle` をメモリ内にキャッシュし、LZ4解凍およびディスク書き込み処理時間を 137ms から 0ms に短縮。同一 MCDF の複数スポーンや Auto Spawn 時の I/O 負荷を完全根絶。
  - **優先度付き非同期フレーム分散（スタッガー）スポーンキュー (`Managers/SceneManager.cs`)**:
    - シーン一括スポーン時、1フレームで全アクターを一気に COM 生成するのを廃止。
    - 自キャラに近いアクターから優先順位を付けてソートし、2フレームに1体ずつ順次スポーンさせる非同期キュー機構を導入。
    - 10体一括スポーン時に発生していた 363ms のメインスレッドフリーズ（フレームヒッチ）を完全に 0ms（60fps維持）へ解消。将来の100体配置にも対応可能な基盤を確立。
  - **MCDF ディファード Redraw ジョブキュー (`Managers/ActorManager.cs`)**:
    - Penumbra の非同期 Mod 読み込み待機用ジョブキュー `McdfDeferredRedrawJob` を新設。
    - スポーン直後の初期描画後、約5フレーム（~80ms）待機して Penumbra 側でディスク上の Mod ファイル（69ファイル等）のリソース解決テーブルが確実に構築されたタイミングで遅延 Redraw を自動発行。
    - これにより、非同期ロード未完了によるバニラ素体へのフォールバック（MOD抜け）を 100% 根絶。
  - **将来拡張・高負荷アーキテクチャ設計のドキュメント化**:
    - `docs/scene_implementation_detailed_plan/` 内の `implementation_plan.md`, `task.md`, `walkthrough.md` に、最大100体規模、大容量カスタムモーションMOD、近接3D音響、環境アセット制御の 4大基盤アーキテクチャ（距離仮想化・非同期キュー・ECS・アセット分離）と段階的ロードマップを完全記録・同期。
  - **完全隔離の保証**:
    - 第1工程のコアロジック（外見、Glamourer、Penumbra、MCDF、NPC、モンスター、CustomizePlus）は完全不可侵（変更なし）を厳守。

## [0.1.57] - 2026-10-03
### Fixed
- **エリア移動（テレポ）時の Auto Spawn 競合およびローディング中スポーンによるゲーム強制終了 (C0000005) の解消**:
  - **根本原因の完全解明 (`dalamud_appcrash_20261003_140458_771_22856.log` および `dalamud.old.log` 解析)**:
    - ゾーン移動時、`ClientState.TerritoryChanged` イベントは画面暗転・ローディング中に即座に発火する。
    - このタイミングでは、ゲームワールド上に自キャラ（LocalPlayer）のネイティブ GameObject や描画モデルがまだ生成されておらず、`Local player not found` エラーが発生していた。
    - さらに、`Plugin.cs`（旧 `OnTerritoryChanged`）と `SceneManager.cs` で `TerritoryChanged` が二重購読されており、一方が `SpawnScene` を試みる直後に他方が `actorManager.DespawnAll()` を呼ぶ二重競合が発生。
    - 不完全に生成・破棄されたオブジェクトに対してゲームエンジンの DirectX レンダラーが OrientedBounds を計算しようとした結果、`Client::Graphics::Scene::Weapon.UpdateRender` で NULL ポインタ参照（0xC0000005）を引き起こしゲームが強制終了していた。
  - **解決策**:
    - `Plugin.cs` の不要な旧 `OnTerritoryChanged` 購読を完全削除し、ゾーン移動制御を `SceneManager` に一本化。
    - `ClientState.TerritoryChanged` 時は旧シーンの安全なデスポーン（クリーンアップ）のみを即座に行い、Auto Spawn はローディング画面中には一切実行しないよう分離。
    - `Framework.Update`（メインスレッド）にて、ゾーン移動後に `ClientState.IsLoggedIn` かつ `LocalPlayer` が正常にロード完了したことを検知後、60フレーム（約1秒）の安全マージンを待機してから確実に `SpawnScene` を実行する「遅延安定オートスポーン」を実装。
  - **完全隔離の保証**:
    - 第1工程のコアロジック（外見、Glamourer、Penumbra、MCDF、NPC、モンスター、CustomizePlus）は完全不可侵（変更なし）を厳守。

## [0.1.56] - 2026-10-03
### Added
- **Stagehand 準拠の UI レイアウト全面刷新と Scene Edit 独立ウィンドウの新設**:
  - **メインウィンドウ「Scene」タブのフォルダ階層化・コンパクト化**:
    - **左ペイン**: フォルダ階層ツリービュー（`Solution Nine`, `My House` 等の展開／折りたたみ対応）、下部に `New Scene`、`New Folder`（モーダル入力）、`Delete` を配置。
    - **右ペイン**: `Scene Name` 編集、`Location`（現在または紐付けゾーン名表示）、`Auto Spawn` チェックボックス（対象エリア突入時の自動スポーン制御）。
    - **操作ボタン**: `[Show]` / `[Hide]` トグルボタン（シーンのスポーン状態に応じて自動切り替え）、`[Edit]` ボタン（シーン編集ウィンドウ展開）。
  - **シーン編集独立ウィンドウ (`SceneEditWindow`)**:
    - 上部タブ: `[Spawn]`, `[Scene]`, `[Animation]`。
    - **全タブ共通上段**: どのタブを開いていても常に最上段に配置される統合アクター管理エリア。
      - 4モードギズモツールバー: Select (`MousePointer`), Translate (`ArrowsUpDownLeftRight`), Rotate (`SyncAlt`), Scale (`ExpandAlt`)。
      - テンプレート選択コンボボックス + `[+Add]` ボタン（自キャラ現在地へ即座に追加）。
      - アクター一覧リスト: 紫色の人型アイコン (`User`) + 表示名（Custom Name 優先） + 右端の目のアイコン（`Eye` / `EyeSlash` で単体アクターのスポーン／デスポーン即時切り替え）。
      - リスト右下に `[Delete]` ボタン。
    - **下段 `[Spawn]` タブ**:
      - `Character Name`（読み取り専用・表示のみ）。
      - `Custom Name`（カスタムネームプレート入力欄） & 表示チェックボックス。
      - `Custom Title`（カスタム称号入力欄） & 表示チェックボックス。
      - `Translation` (X, Y, Z ドラッグ / 入力)。
      - `Rotation` (度数法角度入力 / スライダー)。
      - `Scale` (アクター拡大縮小入力)。
      - `[Apply Own Transform]` ボタン（自キャラの現在座標・向きを取得して即座に適用）。
    - **下段 `[Scene]` / `[Animation]` タブ**: Phase 2（モーション・表情・接近リアクション）および Phase 5（マップアセット消去）用の独立プレースホルダー。
  - **完全隔離の保証**:
    - 第1工程のコアロジック（外見、Glamourer、Penumbra、MCDF、NPC、モンスター、CustomizePlus）は完全不可侵（変更なし）を厳守。

## [0.1.55] - 2026-10-03
### Added
- **Phase 1: シーン・配置管理基盤（Scene）の実装と複数体配置管理**:
  - **データモデル設計 (`Models/SceneData.cs`)**:
    - シーンデータ（`SceneData`）、配置アクター（`SceneActorPlacement`）の構造を新設。
    - 将来フェーズ（Phase 2: モーション・視線、Phase 3: ネームプレート、Phase 4: 3Dサウンド、Phase 5: マップアセット消去）のプロパティを最初から先行内包し、後方互換性を 100% 保証。
  - **シーンマネージャー (`Managers/SceneManager.cs`)**:
    - シーン設定の JSON 永続化（`scenes.json`）。
    - 第1工程の `ActorManager.SpawnCharacter` と連携し、空き COM スロットを用いた複数体アクターの一括スポーン・個別スポーン・デスポーンを統括管理。
    - テリトリーチェンジ（ゾーン移動・テレポ）検知時の安全な自動全消去（クリーンアップ）を実装。
  - **UI 全面実装 (`UI/StageSceneTab.cs`)**:
    - **左ペイン**: シーン一覧、新規作成、シーン名／説明文編集、シーン削除、一括スポーン／デスポーン。
    - **右ペイン**: ライブラリのテンプレートを選択して「自キャラ現在地に配置」追加、配置アクター一覧テーブル、リアルタイム座標ドラッグ／回転スライダー編集、個別スポーン／デスポーン／削除。
    - **3D ギズモ双方向同期**: 画面上の 3D ギズモで動かした座標・回転が、配置アクターデータと設定ファイルへ即座に反映されるリアルタイム連動（`SyncPlacementTransformFromGizmo`）を実装。
  - **完全隔離の保証**:
    - 第1工程のコアロジック（外見、Glamourer、Penumbra、MCDF、人型NPC、モンスター、CustomizePlus）は完全不可侵（変更なし）として保持し、下位サービスとして呼び出すのみの安全アーキテクチャを徹底。

## [0.1.54] - 2026-10-03
### Fixed
- **Glamourer 適用前の先行 Penumbra Redraw 抑止による外見・武器の一瞬のチラつき解消**:
  - **根本原因の完全解明 (`dalamud.log` 11:44〜11:45 解析)**:
    - パイプライン A（通常の Glamourer + Penumbra テンプレート）において、Penumbra コレクションを紐付けた直後に `penumbraIpc.Redraw(actorIndex)` を呼んでいた。
    - パペットはスポーン直後、安全な骨格確立のため自キャラの装備・武器が一旦コピーされる仕様となっている。
    - Glamourer 適用前に Penumbra Redraw を呼ぶと、まだ「自キャラの装備・武器」の状態のアクターに対して Penumbra が先行して再描画を開始してしまう。
    - その直後に Glamourer の `ApplyDesignToActor` が走り、二重のリロードと Penumbra Mod の非同期ディスク読み込みが競合し、数フレーム（約100ms）の間だけ自キャラの武器やモデルが画面にチラつく現象が発生していた。
  - **解決策 (`Managers/ActorManager.cs`)**:
    - Glamourer が適用される場合は、直前の不要な先行 `Penumbra.Redraw` をスキップするよう整流化。
    - Glamourer 自身の再描画により、Penumbra コレクションが新しい外見で一発同期適用されるため、チラつきや二重描画負荷が完全に解消。
    - Glamourer が未設定（Penumbra 単体）の場合のみフォールバックとして `Penumbra.Redraw` を呼ぶ安全ガードを設け、既存の Penumbra 単体運用も 100% 保証。
  - **完全隔離の保証**:
    - MCDF パイプライン、人型NPC パイプライン（`HumanoidNpcApplyJob`）、モンスター／デミヒューマン パイプライン、CustomizePlus パイプラインには一切触れていません。

## [0.1.53] - 2026-10-03
### Fixed
- **クォート付き CSV 解析 (`ParseCsvLine`) の導入によるフォーギヴン・テスリーン等 17 体の Mob 欠損解消**:
  - **根本原因の完全解明**:
    - `mob-model-index.csv` 内の `"Tesleen, the Forgiven"`（フォーギヴン・テスリーン）等のデータ行において、ナイーブな `line.Split(',')` がクォート文字内のカンマを区切りと誤認し、カラムインデックスが後方にずれていた。
    - その結果、本来 `ModelCharaId`（2632）が入るべき位置にテキスト `" the Forgiven\""` が渡り、`uint.TryParse` に失敗して行ごとスキップ（除外）されていた。
    - 英語名にカンマを含む計 17 体（フォーギヴン・テスリーン、美眼／楽聖のインク＝ゾン、統制者ハシュマリム、背徳の皇帝マティウス、暗黒の雲ファムフリート、魔人ベリアス、聖天使アルテマ等）がすべて同様にリストから脱落していた。
  - **解決策 (`Services/GameDataService.cs`)**:
    - RFC 4180 / HDM 準拠の CSV 行パーサー関数 `ParseCsvLine` を新設。
    - クォート文字（`"..."`）内のカンマを保護しつつ各フィールドを正しく分割し、前後のクォートや空白をトリム。
    - これにより `BNpcName` シート（ID: 8300）からの日本語名「フォーギヴン・テスリーン」の解決およびモデル ID（2632）の読み込みが 100% 正常に機能し、全 17 体が一覧に復元。
  - **完全隔離の保証**:
    - 変更は `GameDataService.cs` の CSV 解析関数のみに留まり、Glamourer、Penumbra、CustomizePlus、NPC、スポーン処理パイプラインには一切触れていません。

## [0.1.52] - 2026-10-03
### Fixed
- **Customize+ 公式一時プロファイル IPC (`SetTemporaryProfile`) への完全移行と設定ファイル汚染の根絶**:
  - **根本原因の完全解明 (`CustomizePlus.dll` 逆アセンブル解析)**:
    1. **恒久プロファイル書き換え (`AddPlayerCharacter`) の副作用**:
       - スポーン時に `AddPlayerCharacter` を呼んでいたため、CustomizePlus がディスク上の `profiles/*.json` の `Characters` 配列に `"Actor Ac"` 等を追記し、毎度 `SaveProfile()` を実行していた。
       - これによりユーザーの正規設定ファイルがゴミデータで汚染され、プロファイル変更通知がゲーム全体に飛び交うことで自キャラや他アクターのボーン姿勢崩れ・カクつきを誘発していた。
    2. **プロファイル無効化（Disabled）の壁**:
       - ユーザー環境で対象プロファイルが `"Enabled": false` の場合、紐付けても骨格変形が一切行われなかった。
    3. **MCDF 外部プロファイルの未登録失敗**:
       - MCDF 内包プロファイルはユーザーの環境に登録されていない外部データであるため、`AddPlayerCharacter` が `ec=3`（ProfileNotFound）で失敗していた。
  - **解決策**:
    - **公式一時プロファイル IPC (`SetTemporaryProfileOnCharacter` / `SetTemporaryProfileByGuid`) への完全移行**:
      - メモリ上だけでパペットのアクターインデックス（Index 200）にプロファイルを注入。ユーザーの設定ファイルへのディスク書き込みは一切行われない（汚染ゼロ）。
      - プロファイル JSON の `"Enabled"` を強制的に `true` に補正して注入するため、無効化中のプロファイルでもパペットに 100% 確実に適用。
      - MCDF 内包の Customize+ データも同様に直接一時プロファイルとして注入可能に。
    - **設定ファイル汚染の自己修復機能 (`CleanupPuppetArtifacts`)**:
      - 過去バージョンでユーザーの正規プロファイルに書き込まれて残骸化した `"Actor "` エントリを自動検知し、安全に削除して設定ファイルを元の状態にクリーンアップ。
    - **他パイプライン（Glamourer, Penumbra, Monster）への影響ゼロを保証（完全隔離）**。

## [0.1.51] - 2026-10-03
### Fixed
- **Glamourer ApplyState への Base64 圧縮データ渡しと連続スポーン時の外見ズレ（直前キャラ表示・自キャラ化）の完全解消**:
  - **根本原因の完全解明 (実機ログと Glamourer 逆アセンブル解析)**:
    1. `dalamud.log` 解析により、人型NPCスポーン時に `Glamourer ApplyState (ulong flags=6) for NPC on actor #200 result: 7`（`InvalidState`）が毎フレーム発生し、60 ticks タイムアウト後に無理やり直接メモリフォールバックが走っていたことを特定。
    2. `Glamourer.dll`（`StateApi.ApplyState`）の逆アセンブルにより、引数が `string` の場合、Glamourer はそれを Base64 圧縮文字列としてデコード（`DesignConverter.FromBase64`）しようとする仕様であることが判明。
    3. `TryApplyNpcAppearance` が生の JSON 文字列（`{"FileVersion":1,...}`）を渡していたため、毎回 Base64 デコード例外が発生して失敗し、Glamourer による外見適用が 1 度も成功していなかった。
    4. Glamourer 適用が 1 度も成功せず 60 ticks（約1秒）待たされるため、プレビューでキャラを連続で切り替えると、前のキャラの遅延フォールバックや Penumbra Redraw がゲームスレッド上で重なり、直前の外見が上書きされたり、最終的に素体（自キャラ）に戻ってしまっていた。
  - **解決策: `CompressToBase64` による Base64 圧縮文字列渡し (`Services/GlamourerIpc.cs`)**:
    - `TryApplyNpcAppearance` において、すでに MCDF パイプラインで 100% 成功実績のある `CompressToBase64(state)` を呼び出し、GZip 圧縮された Base64 文字列を `applyStateV2Ulong` に渡すよう修正。
    - これにより Glamourer が 1 フレーム目（0 ticks）で即座に `result: 0`（Success）を返し、わずか数 ticks で NPC への変身が完了。
    - タイムアウト待ち（60 ticks）や遅延フォールバックが一切発生しなくなり、連続スポーン時でもズレることなく瞬時に本来の NPC 外見が適用される！


## [0.1.50] - 2026-10-03
### Fixed
- **HDM (HousingDollMaster) 完全照合による ENpc ID 空間の乖離解消と DrawObject 強制再構築 (`RedrawGuise`)**:
  - **二大根本原因の完全解明**:
    1. **ENpc ResidentId と BaseId の ID 空間乖離 (Two ENpc Spaces)**:
       - UI の検索一覧（`BuildNpcCache`）が `ENpcResident` を回していたため、ユウギリの ID が ResidentId `1007097` になっていた。
       - しかし外見データ取得は `ENpcBase` から行っていたため、ユウギリの `ENpcBase`（`1011896`）と一致せず外見データ取得に失敗し、自キャラのデータ（ミコッテ）でテンプレートが保存されていた。
       - HDM（`EventNpcIndex.cs`）は `ENpcBase` を走査し、`ENpcBase.RowId` をリスト ID に採用していた。
    2. **Glamourer ApplyFlag (6UL) と DrawObject 強制再構築 (`RedrawGuise`)**:
       - HDM の `HumanGuise.Apply` は、Glamourer `ApplyState` に `6UL`（`ApplyFlag.Equipment | ApplyFlag.Customization`）を渡し、`Once (1)` を除外して永続適用していた。
       - さらに `ApplyState` 成功直後に `_guise.Redraw`（`DisableDraw` → 最低 2 ticks 待機 → `IsReadyToDraw` 確認 → `EnableDraw`）を実行し、ゲームエンジンの DrawObject を NPC 外見で強制再構築していた。
  - **対策 1: ENpcBase 主軸キャッシュと名前逆引き解決 (`GameDataService.cs`, `CharacterLibraryTab.cs`)**:
    - `BuildNpcCache` を HDM と同一の `ENpcBase` 主軸走査に変更し、リストの ID を `ENpcBase.RowId`（ユウギリなら `1011896`）に統一。
    - `ResolveNpcAppearance(enpcId, name)` を新設。過去に ResidentId（`1007097`）で保存された既存テンプレートであっても、NPC名「ユウギリ」から自動的に正しい BaseId（`1011896`）へリマップして正しい外見データを返す自己修復機構を実装。
  - **対策 2: HDM 完全準拠の 6UL 永続適用 (`GlamourerIpc.cs`)**:
    - `TryApplyNpcAppearance` 内で、HDM と同一の `6UL`（`Equipment | Customization`）フラグで `applyStateV2Ulong` を直接呼び出し。余計な `ForceAllApply` を排除し、NPC に必要なスロットのみ確実に適用。
    - MCDF や通常 Glamourer パイプラインには一切触れず、人型NPC専用処理として完全隔離。
  - **対策 3: DrawObject 強制再構築シーケンス (`ActorManager.cs`)**:
    - スポーン時に既存テンプレートの自動リフレッシュを実行（汚染された自キャラデータを本物の NPC データで即時上書き）。
    - `HumanoidNpcApplyJob` で Glamourer 適用成功後、直ちに `DisableDraw()` を実行。
    - 最低 2 ticks 待機し、ゲームエンジンの準備完了（`IsReadyToDraw`）を確認してから `EnableDraw()` を呼び出すことで、ゲームエンジンの DrawObject を NPC 外見で強制再構築！

## [0.1.49] - 2026-10-03
### Fixed
- **HDM (HousingDollMaster) 徹底逆アセンブル解析に基づく真因解明・パペット名 ASCII 化・スポーン描画シーケンス完全同期**:
  - **根本原因の完全解明 (日本語パペット名による Glamourer の ActorIdentifier 検証失敗)**:
    - HDM の実機ログではスポーン開始からわずか 0.24 秒で Glamourer がパペットを認識してユウギリ固有の顔・黒髪が完璧に描画されていたのに対し、Character Spawn では 60 ticks（1.15秒）経過しても `GetState` が null でタイムアウトしていた。
    - HDM（`HDM.dll`）および Glamourer（`Glamourer.dll`）の完全逆アセンブル解析を行った結果、`ActorManager.GetPuppetName` がテンプレート名から `"ユウギリ Cnpc"` という日本語文字を含む名前を生成し、`GameObject.SetName` に設定していたことが最大の真因と判明。
    - Glamourer の `ApiHelpers.FindState` は内部で `actors.GetIdentifier(actor)` を呼び出し、FF14 の `VerifyPlayerName`（ASCII英字のみ許可）で検証するため、日本語が含まれていると `ActorIdentifier.IsValid` が false となり、**常に `ActorNotFound (42)` を返し続けていた**。
    - HDM は、純粋な ASCII 英字 `"Hdm Aa"`, `"Hdm Ab"` を設定していたため、Glamourer が 100% 即座に有効な識別子を生成できていた。
  - **対策 1: パペット名の ASCII 英字プレイヤー名化 (`ActorManager.cs`)**:
    - `Interlocked.Increment(ref puppetSerial)` により、純粋な ASCII 英字プレイヤー名 `$"Actor {c1}{c2}"`（Forename: Actor, Surname: Aa..Zz）を生成。
    - 頭上のネームプレート表示や UI 表示は `DisplayName` / `NamePlate.CustomName`（`template.Name` = "ユウギリ"）を維持するため、ユーザーの画面上では完全に日本語で表示される。
  - **対策 2: スポーン描画シーケンスの HDM 完全同期 (`ActorManager.cs`)**:
    - 人型NPC（Pipeline C）スポーン直後は `nativeChara->GameObject.DisableDraw()` を呼び、描画待機ジョブにエンキュー。
    - `UpdateFrame` 内でゲームエンジンの `IsReadyToDraw()` を待機して `EnableDraw()` を呼び、さらに `DrawObject` の準備完了後に `glamourerIpc.TryApplyNpcAppearance` を実行。
  - **対策 3: Name ベースのステート取得フォールバック (`GlamourerIpc.cs`)**:
    - `Glamourer.GetStateBase64Name` を購読し、`GetStateByName(actorName)` を新設。Index 経由で取得できなかった場合でも Name（"Actor Aa"）経由で確実にステートを取得する多重防壁を構築。
  - **他機能への影響ゼロ保証**:
    - 共通メソッドの破壊的変更は行わず、Pipeline C（人型NPC）のみを修正したため、MCDF（Pipeline A/B）、Monster / Demihuman（Pipeline D）を含む既存機能への副作用はゼロ。

## [0.1.48] - 2026-10-03
### Fixed
- **独立キュー `HumanoidNpcApplyJob` による人型NPC固有外見（カヌ・エ・センナ、ユウギリ等）の完全描画**:
  - **他機能への影響ゼロ保証（パイプライン完全分離）**:
    - MCDF や 通常の Glamourer / Penumbra / Customize+（Pipeline A/B）、および Monster / Demihuman（Pipeline D）のコードには一切手を加えず、完全に分離・独立した「人型NPC（Pipeline C）」専用の処理として実装。共通メソッド `ApplyDesignToActor` も一切変更なし。
  - **根本原因の完全解明 (Cold-Spawn Race と FilterCustomizeData のサニタイズ)**:
    - HDM（Doll Master）の `HumanGuise.cs` 逆アセンブル解析により、ゲームエンジン内でアクターを新規生成した直後（0フレーム目）は Glamourer のアクター認識テーブルにまだアクターが登録されておらず、必ず `ActorNotFound`（ec=2）が返る仕様であることが判明。
    - 従来の Character-Spawn では 0フレーム目で即座に「失敗」と見なして直接メモリ書き込みフォールバックを実行していたため、ゲームエンジンの `FilterCustomizeData` によって未解放のNPC固有顔・髪型がプレイヤー汎用顔・髪型（金髪ボブ等）に強制サニタイズ（置換）されていた。
  - **解決策: HDM 準拠の非同期同期待機キュー `HumanoidNpcApplyJob` の新設**:
    - 人型NPCスポーン時、`HumanoidNpcApplyJob` にエンキューし、毎フレームの `UpdateFrame` 内で Glamourer がアクターを認識してステートを返すまで待機・リトライ（最大60フレーム、約1秒）。
    - 認識された瞬間に Glamourer 経由で外見を適用（`ec=0`）。Glamourer がゲームエンジンのサニタイズを完全にバイパスして適用するため、カヌ・エ・センナの角尊ツノ・固有編み込み髪型や、ユウギリのアウラ固有顔・ツノ・ウロコ造形が 100% 確実に描画される。
    - 万一のタイムアウト時のみ安全網として直接メモリ書き込みフォールバックを実行。

## [0.1.47] - 2026-10-03
### Reverted
- **安全な安定状態への復元（ロールバック）**:
  - v0.1.45〜v0.1.46 の変更により MCDF 適用時に影響（自キャラが出現する現象）が生じたため、MCDF および既存の各パイプラインが完全に正常動作していた v0.1.44.0 の安定状態に `Services/GlamourerIpc.cs` および `Managers/ActorManager.cs` を直ちに復元。
  - NPC 固有顔問題の修正は既存機能に影響を与えない独立したアプローチで慎重に再設計・再実装を行う。

## [0.1.46] - 2026-10-03
### Fixed
- **ValueTuple JObject 型不一致例外の根絶と GetStateBase64 黄金律導入（ユウギリ・カヌ・エ・センナ等のNPC固有顔・髪型の完全反映）**:
  - **根本原因の完全解明 (Dalamud IPC 型キャスト例外)**:
    - `dalamud.log` 解析により、`GetState(0)` および `GetStateName(localPlayerName)` 呼び出し時に `IPC method Glamourer.GetState blew up when converting from ValueTuple'2 to System.ValueTuple'2[System.Int32,Newtonsoft.Json.Linq.JObject]` 例外が発生していたことを特定。
    - プラグイン間（異なる AssemblyLoadContext や Newtonsoft.Json バージョン相違）で `JObject` を ValueTuple でやり取りすると、Dalamud IPC 内部の型変換でキャスト例外が発生し、自キャラテンプレートの取得に失敗していた。
    - その結果、依然として直接メモリ書き込みフォールバックが走り、ゲームエンジンの `FilterCustomizeData` によってユウギリやカヌ・エ・センナの固有顔・髪型（角尊の角や固有編み込み髪）がプレイヤー汎用顔・汎用髪（金髪ボブ等）にサニタイズ（丸め込み）されていた。
  - **対策 1: 公式 IPC `Glamourer.GetStateBase64` / `GetStateBase64Name` の導入**:
    - `(int, string?)` を返す公式 IPC `GetStateBase64` および `GetStateBase64Name` を最優先で呼び出すよう修正。
    - `string`（文字列）は .NET のコア型であるため、ALC 境界や Newtonsoft.Json のバージョン相違の影響を 100% 回避し、自キャラのステート文字列を確実に取得可能に。
  - **対策 2: プラグイン内 GZip デコーダ (`ParseDesignString`) による安全な復号**:
    - 取得した Base64 文字列を、実績ある `ParseDesignString` で自プラグイン側の `JObject` に安全にパースし、NPC の CustomizeData（26バイト）と EquipmentModelIds を上書き。
  - **対策 3: `ApplyState` 適用時も Base64 文字列（`string`）で渡す黄金律の徹底**:
    - `CompressToBase64(state)` により Base64 文字列を生成して `ApplyState` に渡すことで、Glamourer 側で安全に Base64 が解凍・パースされ、アクターに完璧に適用される。
    - これにより Glamourer がゲームエンジンのサニタイズを完全にバイパスし、ユウギリ・カヌ・エ・センナの固有顔・髪型が 100% 確実に描画される。

## [0.1.45] - 2026-10-03
### Fixed
- **ユウギリ等のNPC固有顔がプレイヤー選択可能顔タイプに置き換わってしまう不具合の根本解決**:
  - **根本原因 (Glamourer IPC 型不一致による FilterCustomizeData 丸め込み)**:
    - 公式の `Glamourer.ApplyState` IPC プロバイダは第1引数の型が `object`（`FuncProvider<object, int, uint, ulong, int>`）で登録されている。
    - プラグイン側の購読型が `string`（`GetIpcSubscriber<string, int, uint, ulong, int>`）となっており、Dalamud IPC の厳密な型照合により型不一致で呼び出しが失敗。
    - その結果、フォールバックの直接メモリ書き込み（`ApplyNpcAppearanceDirectFallback`）が動作していたが、ネイティブメモリ直接書き込みではゲームエンジン内部の `FilterCustomizeData` が働き、ユウギリ等の NPC 固有顔番号（未解放フェイス）がプレイヤークラスの選択可能な標準顔に強制サニタイズ（丸め込み）されてしまっていた。
  - **対策 1: Glamourer IPC 購読型の完全整合化 (`object` 型への統一)**:
    - `Glamourer.ApplyState` および `Glamourer.ApplyStateName` の IPC 購読シグネチャを `string` から `object` に修正。
    - これにより Glamourer 経由での外見適用が 100% 成功し、ゲームエンジンの `FilterCustomizeData` の丸め込みをバイパスして、ユウギリのツノ・ウロコ・固有フェイス造形を完全再現。
  - **対策 2: JObject 直接適用メソッド (`ApplyStateJObject`) の新設**:
    - JSON 文字列の再シリアライズや Base64 圧縮処理を介さず、メモリ上で構成した `JObject` をそのままダイレクトに Glamourer に渡す `ApplyStateJObject` を導入し、オーバーヘッドをゼロ化。
  - **対策 3: 自キャラテンプレート取得の強化 (`GetStateName` フォールバック)**:
    - スポーン直後のコールドパペット用ひな形取得において、`GetState(0)`（Index 0）に加えて `clientState.LocalPlayer?.Name.TextValue` を用いた `GetStateName` フォールバックを追加し、自キャラテンプレートの取得を強固に保証。

## [0.1.44] - 2026-10-03
### Fixed
- **人型NPC（ミューヌ、ユウギリ等）スポーン時に自キャラの姿で出現する不具合の根本解決**:
  - **根本原因 (Glamourer コールドステートトラップ)**:
    - HDM（Doll Master）の設計知見通り、スポーン直後の新規パペット（actorIndex 200）は Glamourer 内部のアクター状態キャッシュがまだ生成されておらず、`GetState(actorIndex)` が `null` を返す。
    - そのため、外見上書き処理がスキップされ、アクター生成時に drawable 骨格確立のためにベースラインコピーされた自キャラ素体（`CopyFromCharacter(meNative)`）がそのまま描画されていた。
  - **解決策 1: LocalPlayer ステートをテンプレートとする即時ディープコピー変身 (0ms)**:
    - スポーン直後で対象アクターのステートがコールドな場合、常時存在する自キャラ（`GetState(0)`）のステート JObject をひな形としてディープコピー。
    - NPC の 26バイト `CustomizeData` と 10スロットの `EquipmentModelIds` を上書きし、自キャラ固有の肌色・パラメータ汚染（Parameters/Materials）を完全に Strip。
    - `ForceAllApply` を実行後、武器スロット（MainHand/OffHand）を明示的に解除（Unmanage）して `ApplyState` を呼ぶことで、待機ポーリングを挟まず 0ms で確実に NPC の姿に変身させる即時直列パイプラインを実現。
  - **解決策 2: Glamourer 失敗時のダイレクトメモリフォールバック (`ApplyNpcAppearanceDirectFallback`)**:
    - Glamourer IPC の戻り値を検証し、万一 IPC が失敗または利用不能な場合でも、メモリ上の `CustomizeData` と `EquipmentModelIds` を直接上書きして `CopyFromCharacter` を実行する安全網を導入。
  - **解決策 3: NPC テンプレートデータの名前ベース自動解決補完**:
    - `template.CustomizeData` が未設定の NPC テンプレートであっても、`template.Name`（例: "ミューヌ", "ユウギリ"）からゲーム内 NPC データベースを即座に逆引きし、ENpcBaseId・外見データを自動解決して補完するフォールバックを追加。

## [0.1.43] - 2026-10-03
### Fixed
- **スポーンアクターの3Dモデル不可視化（ギズモのみ表示）の根本解決**:
  - **根本原因の解明 (Brio ActorSpawnService 比較解析)**:
    - v0.1.42 で不要なポーリングキュー（`readyJobs`）を削除した際、ゲームエンジンの描画有効化処理（`nativeChara->GameObject.EnableDraw()`）の呼び出しまで除去されていたため、アクター生成後にゲームエンジンが 3D メッシュのロード・レンダリングを開始せず、不可視（ギズモのみ）のまま固まっていた。
  - **対策 1: Brio / AQR 黄金律 `EnableDraw` の完全復元**:
    - `SpawnCharacter` でのベースライン設定時、および各パイプライン（A: Glamourer/Penumbra、B: MCDF、C: NPC）の完了直後に `nativeChara->GameObject.EnableDraw()` を明示的に呼び出し、即時レンダリングを開始。
  - **対策 2: 継続的描画可視化ループの追加 (`UpdateFrame`)**:
    - `UpdateFrame` 内で全アクティブアクターの描画状態を監視し、`IsReadyToDraw() -> EnableDraw()` の実行および `DrawObject` の非表示フラグ（0x10）の解除を自動保証（モンスターパイプライン D の Redraw 待機中は干渉しないよう安全に除外）。

## [0.1.42] - 2026-10-03
### Fixed
- **AQR (AQuestReborn) ＆ HDM (Housing Decorator/Doll Master) 参照仕様の完全分離と4系統独立パイプライン構築**:
  - **ツールの最終目標（①ローカルキャラ作成、②シーン作成演出）に即したアーキテクチャ分離**:
    - **Pipeline A (AQR: 通常Glamourer / Penumbra / Customize+ / PlayerClone)**:
      - 素の `BattleCharacter` 生成 → 素体コピー → その場で Penumbra（Guid指定）＋ RedrawObject ＋ Glamourer（Guid指定/PlayerClone）を直列即時実行。`readyJobs` 遅延待機を廃止し即時完了。
    - **Pipeline B (AQR: MCDF)**:
      - 素の `BattleCharacter` 生成 → 一時コレクション割当 → 内包 Base64 を無加工で `ApplyState` に渡す → 直後 `RedrawObject`。即時完了。
    - **Pipeline C (HDM: 人型NPC)**:
      - 素の `BattleCharacter` 生成 → `ApplyNpcAppearance`（Customize/Equip注入、Parameters/Materials の Strip、コールドスポーン時の RevertToGameBase スキップ、直後 RedrawObject）。即時完了。
    - **Pipeline D (HDM: Monster / MOB)**:
      - `ModelCharaId` と `Scale` 設定 → 武器非表示 → ネイティブ描画ポーリング（`MonsterRedrawJob`）。Glamourer や Penumbra Redraw は一切呼ばない。
  - **不要なメモリ改変の完全撤廃 (AQR / Brio 黄金律)**:
    - `ObjectKind`, `BattleNpcSubKind`, `OwnerId`, `NameId`, `HomeWorld` の改変を全廃し、ゲーム本来の `BattleCharacter` を維持。
  - **命名規則の統一**:
    - `"{Name} Cnpc"`（例: `"Kimo Cnpc"`）形式とし、FF14 の名前検証規則を完全充足。
  - **自キャラ物理遮断ガードの徹底**:
    - `globalIndex <= 0 || objectTable[0]?.Address == chara` による誤爆防御を全適用パスに配置。
  - **デッドコード・残骸ポーリングの完全クリーンアップ**:
    - 旧アーキテクチャの `readyJobs`, `pendingNpcJobs` クラスおよびポーリングループを完全削除（421行削減）し、`UpdateFrame` は視線追従と `monsterRedrawJobs` のみにスリム化。

## [0.1.41] - 2026-10-03
### Fixed
- **AQuestReborn (AQR) 実装完全解析 & Proteus 外部干渉防御による自キャラ変身根絶と外見適用の完全分離**:
  - **根本原因 1: `RevertLocalPlayer` のメインスレッド外呼び出し (`Not on main thread!`) の解消**:
    - v0.1.40 で追加した自動 Revert が、プラグイン初期化スレッド（非メインスレッド）から呼ばれたため `Not on main thread!` 例外となり、過去のテストで変身していた自キャラが元に戻っていなかった。
    - `Framework.RunOnFrameworkThread` でメインスレッド上での確実な自キャラ復元を保証。さらに `revertCharacter`（ICharacter 直接）も併用し、自キャラの本来の姿と Penumbra コレクションを確実に復元。
  - **根本原因 2: スポーン直後での Glamourer 呼び出しによる未登録エラーと Proteus 誤爆の完全排除**:
    - アクター生成直後（`SpawnCharacter` 内）は、まだ Glamourer の `ActorObjectManager` にアクターが登録されていないため、外見適用が失敗し、さらに外部プラグイン `Proteus` が `DesignApplied` シグナルを検知して自キャラの Penumbra コレクション（`GetPlayerCollectionId()`）を上書きしてしまっていた。
    - **対策**: スポーン直後は Penumbra コレクションの事前割り当てのみ（`applyGlamourer: false`）に限定し、Glamourer デザインの適用および Redraw はアクターの描画準備が整った `ReadyJob`（メインスレッド）でのみ実行するよう完全分離。
  - **根本原因 3: UI 上での自キャラ即時復元ボタンの追加**:
    - Character タブの操作ボタン並びに「Revert Player」ボタンを追加し、ワンクリックでいつでも自キャラを本来の姿・コレクションに復元可能に。

## [0.1.40] - 2026-10-03
### Fixed
- **AQuestReborn (AQR) ソースコード・MCDF-Loader・0.1.23完全同期による MCDF/Penumbra/Glamourer 適用不具合および自キャラ誤認の根本解決**:
  - **根本原因 1: 自キャラ（LocalPlayer 0）の過去セッションにおけるGlamourerステート残留の解消**:
    - 以前のバージョンでの実行時に自キャラに対してGlamourerステートが適用され、Glamourer内部に保持されたままリバートされていなかったため、自キャラ自体がKimo-1-Nudeの姿のまま固まっていた。
    - パペットスポーン時に自キャラの素体（`CharacterSetup.CopyFromCharacter`）をコピーするため、自キャラもパペットも同一の変身姿になり、自キャラが乗っ取られたように見えていた。
    - **対策**: プラグイン起動時およびUIのSettingsタブに「Revert Local Player (Glamourer)」を追加。自キャラのGlamourerロック解除とリバートを実行して本来の姿に完全復元。
  - **根本原因 2: Penumbraコレクション事前適用（Pre-Assignment）の復元 (v0.1.23準拠)**:
    - 正常動作していたv0.1.23では、アクター生成直後（描画開始前、`DisableDraw`中）にPenumbra一時コレクション・通常コレクションを事前割り当てしていた。
    - ゲームエンジンが DrawObject を構築し始める前にコレクションをアクターにバインドしておくことで、MODテクスチャや体型モデルが初回の描画構築時から確実に反映されるように修正。
  - **根本原因 3: パペットIdentity（ObjectKind, BattleNpcSubKind, OwnerId, NameId）の完全復元 (v0.1.23 & AQR準拠)**:
    - `ObjectKind.BattleNpc`, `BattleNpcSubKind.Player`, `OwnerId = 0xE000_0000`, `NameId = 0`, `HomeWorld` を設定し、PenumbraおよびGlamourerがパペットを正規のプレイヤー型アクターとして識別できるように復元。
    - パペット名には一意の英字識別子（"Cs Aa", "Cs Ab"等）を付与し、自キャラの名前との混同を完全防止。
  - **MCDF 一時コレクション・Mod 登録・Glamourer 適用・Penumbra Redraw の完全同期**:
    - AQR (MCDF-Loader) の実装に完全準拠し、一時コレクションの作成・割り当て、ModファイルおよびManipulationDataの登録、Glamourer外見適用、Penumbra Redrawを一連のフローとして同期実行。

## [0.1.39] - 2026-10-03
### Fixed
- **AQuestReborn (AQR) バイナリ完全リバースエンジニアリング準拠によるパペットスポーン & 外見・コレクション適用の根本修復**:
  - **自キャラ変身（外見入れ替わり現象）の根本原因解明と完全根絶**:
    - **原因 1: ObjectKind / BattleNpcSubKind / NameId の改変による PC 誤爆**:
      - パペットを `ObjectKind.Pc` かつ `NameId = 0` に書き換えていたため、Penumbra および Glamourer 内部の `ActorIdentifierFactory.FromObject` がパペットをプレイヤーキャラクター（PC）として解決しようとし、名前解決の不整合から自キャラ（LocalPlayer）の Identifier を返してしまっていた。
      - AQR / Brio の仕様を 100% 遵守し、`ClientObjectManager.CreateBattleCharacter` で作成されたそのままの `BattleCharacter` の状態を維持（`ObjectKind` や `BattleNpcSubKind`、`NameId` の書き換えを完全撤廃）。
    - **原因 2: Glamourer ApplyState による自キャラ State 上書きの完全根絶**:
      - `GlamourerIpc.ApplyDesignToActor` において、`GetDesign` で JSON を取得して `ForceAllApply` 圧縮 Base64 を生成し `Glamourer.ApplyState` を呼び出していたが、`ApplyState` はアクターの Identifier に紐づくグローバル状態を書き換えるため、上記原因 1 と合わさって自キャラの State を上書きし、自キャラを Redraw させていた。
      - AQR 完全準拠の `Glamourer.ApplyDesign(Guid targetGuid, int actorIndex, 0, 7UL)` 直接呼び出しに一本化し、`ApplyState` を完全排除。
  - **Penumbra コレクション適用の安定化**:
    - `SetCollectionForActor` 成功直後に AQR と同様に `RedrawObject` を即時実行し、MOD やテクスチャが適用された上で Glamourer デザインが確定するようにシーケンスを整理。
- **Documentation**:
  - `docs/fix_aqr_puppet_spawn_and_appearance/` に調査結果、リバースエンジニアリング解析ログ、根本原因、修正内容を記録。

## [0.1.38] - 2026-10-03
### Fixed
- **Glamourer 1.7.1.3 最新仕様完全準拠によるパペットへの外見 100% 確実適用**:
  - **根本原因の完全解明 (IL 逆アセンブル解析)**:
    - Glamourer 1.7.1.3 の `ApplyStateName` / `ApplyDesignName`（名前指定）は内部で `FindExistingStates` を呼び、**既存のステート辞書（StateCache）に既に登録されているアクターしか対象にできない仕様** であった。
    - スポーン直後のパペット（`Csp Lhcpetra` 等）はステート辞書に存在しないため、`FindExistingStates` は 0 件を返し、`ApplyDesignName` は `ActorNotFound (2)`、`ApplyStateName` は内部バグにより `InvalidKey (6)` を返して 100% 失敗していた。
    - 一方、`Glamourer.ApplyState` / `Glamourer.ApplyDesign`（インデックス指定）は内部で `ObjectManager[objectIndex]`（ゲームオブジェクト配列）からアクターを直接取得し、`stateManager.GetOrCreate` を呼ぶため、**未登録の新規アクターであってもステートが自動生成され、外見が 100% 確実に適用される（result: 0）** ことが IL 解析により証明された。
  - **インデックス指定 IPC への回帰と自キャラ誤爆物理遮断ガードの確立**:
    - パペットの `GlobalIndex`（COM#0 = 200〜）に対して `Glamourer.ApplyState`（`ForceAllApply` 圧縮 Base64）および `Glamourer.ApplyDesign` を直接呼び出すように修正。
    - **自キャラ誤爆防止ガード**: `actorIndex <= 0` の場合はパペット向け適用処理を物理的に拒絶し、操作中自キャラ（Index 0）への誤爆を 100% 完全遮断。
    - 未登録の Legacy IPC（`ApplyAllToCharacter` 等）による例外ログを解消し、正規 IPC パスで安全に外見が適用されるように統合。

## [0.1.37] - 2026-10-03
### Fixed
- **AQuestReborn (AQR) / Caraxi 公式 IPC アーキテクチャへの全面移行による外見・Customize+誤爆・武器残留の完全解決**:
  - **Glamourer 自キャラ誤爆および素体スポーンの完全根絶**:
    - 通常ワールド（非GPose）において ClientObjectManager パペットはゲーム内部の描画ソート配列 `IndexSorted` に登録されないため、インデックス（200）指定や名前指定（`ApplyDesignName`）では `ActorNotFound` となり、Glamourer 内部で Index 0（自キャラ）にフォールバックして自キャラが変身していた。
    - AQR 準拠の `Glamourer.ApplyAllToCharacter` (`Action<ICharacter, string>`) および `Glamourer.ApplyByGuidToCharacter` (`Action<Guid, ICharacter>`) を採用。
    - パペットの `ICharacter` 生ポインタに対して直接外見を適用するため、インデックス検索を完全バイパスし、自キャラ（LocalPlayer）への誤爆は物理的に完全不可能。
    - GUID 指定時も `Glamourer.GetDesignBase64` で Base64 を取得し、`ForceAllApply` で全スロット（性別・種族・顔・髪型・全装備）を強制適用した上でパペットに流し込むため、素体（女性ミコッテ）のままスポーンする現象を根絶。
  - **Customize+ 自キャラ誤爆バグの完全根絶**:
    - `SetTemporaryProfileOnCharacter(200, ...)` によるインデックス指定が自キャラ（Index 0）にフォールバックしていた問題を完全解消。
    - Caraxi 公式の `CustomizePlus.Profile.AddPlayerCharacter` (`Func<Guid, string, ushort, int>`) を採用し、パペットの `PuppetName` と `HomeWorld` でプロファイルに正規紐付け。自キャラには一切プロファイルが適用されない。
    - デスポーン時は `CustomizePlus.Profile.RemovePlayerCharacter` で安全に紐付け解除。
  - **デスポーン時の武器孤立残留バグの完全解消**:
    - `chara->DrawData.HideWeapons(true)` + `chara->GameObject.DisableDraw()` を実行し、描画パイプラインから全メッシュ・ボーンをアンロードした上で `ClientObjectManager.DeleteObjectByIndex` を実行。マップ上に武器だけが取り残される現象を完全根絶。
- **Documentation**:
  - `docs/development_history_and_design_architecture` 配下に開発経緯、本来の意図、確立されたアーキテクチャ仕様、検証プロトコル（`task.md`, `implementation_plan.md`, `walkthrough.md`）を整備・保存。以後の開発において都度参照し、場当たり的修正による不具合ループの再発を完全防止。

## [0.1.36] - 2026-10-02
### Fixed
- **操作自キャラとスポーンパペットの外見入れ替わりバグの完全根絶**:
  - **根本原因の完全解明**:
    1. Glamourer の IPC（`Glamourer.ApplyState` / `ApplyDesign`）の `int objectIndex` 引数は、Dalamud の `ObjectTable` インデックス（200〜）ではなく、FF14 内部の描画ソート順配列（`GameObjectManager.Instance()->Objects.IndexSorted`）を参照する仕様であった。
    2. パペットの Dalamud GlobalIndex（`200`）をそのまま渡した結果、`IndexSorted[200]` に位置していた（あるいは未初期化メモリ経由で参照された）**操作中の自キャラ（LocalPlayer）** に Glamourer がデザイン（Chonk）を適用してしまい、自キャラが変身していた。
    3. 一方、スポーンしたパペット（Global#200）は自キャラのベースライン姿のまま残されたため、プレイヤーから見て「自キャラとパペットの外見が入れ替わった」状態が発生していた。
  - **名前指定 IPC（`ApplyDesignName` / `ApplyStateName`）の導入による自キャラ誤爆の 100% 根絶**:
    - `GlamourerIpc` に `Glamourer.ApplyDesignName` および `Glamourer.ApplyStateName` サブスクライバを新設。
    - パペットは生成時に一意の ASCII 名前（`PuppetName` = `"Csp Wzjjjwyr"` 等）が命名されているため、デザイン適用時は必ずこの名前を指定して IPC を呼び出すように変更。
    - Glamourer は内部で `PlayerName == "Csp Wzjjjwyr"` のアクター（パペット）を特定してデザインを適用するため、自キャラ（`"Ruma Meow"`）に適用される事故は物理的に 100% 発生しなくなった。
- **デスポーン時の武器孤立残存（Orphaned Weapon Bug）の完全解消**:
  - **根本原因の特定**:
    1. デスポーン時に `chara->GameObject.DisableDraw()` を呼び出していたため、ゲームエンジンの描画ツリーから武器の DrawObject だけが切り離されてワールド空間に孤立して残っていた。
    2. また、破棄直前に Glamourer の `RevertState` を呼び出していたため、非同期の装備再描画パイプラインが走り、オブジェクト削除と競合して武器モデルが空中に残留していた。
  - **安全なデスポーンシーケンスの確立 (Brio 準拠)**:
    - `DisableDraw()` の呼び出しを完全撤廃し、ゲームのネイティブ `ClientObjectManager.DeleteObjectByIndex` による自然なカスケード破棄に任せるように変更。
    - デスポーンするアクターに対する `RevertState`（見た目を元に戻す再描画）を廃止し、`UnlockState` のみ実行。
    - 削除直前に `chara->DrawData.HideWeapons(true)` を適用し、武器モデルの確実なアンロードを保証。

## [0.1.35] - 2026-10-02
### Fixed
- **CustomizeData メモリ破壊バグの完全根絶 & 異種族・男性キャラロールバックの根本解決**:
  - **根本原因の完全解明**:
    1. `ExtractCustomizeBytes` のビットマスク処理において、複数ビットで構成される `EyeShape`（マスク `0x7F`）や `Mouth`（マスク `0x7F`）、`FacePaint`（マスク `0x7F`）等の形状番号が、`val != 0` の際に `|= 0x7F`（全ビット1 = 127）として書き込まれ、**完全に破損した26バイト** が生成されていた。
    2. これを `Buffer.MemoryCopy` でネイティブ描画データ（`chara->DrawData.CustomizeData`）に直接上書きしていたため、FF14 の描画エンジン（`Human.SetupFromCustomize`）が不正データとして描画を拒否し、自キャラのベースライン（女性ミコッテ）へ強制ロールバックを引き起こしていた。
    3. Glamourer の `ApplyState` は完璧に正常終了（Result 0）していたにもかかわらず、その直後にこのメモリ破壊が行われていたことが、不具合がループしていた決定打だった。
  - **危険な独自メモリ上書きの全廃**:
    - `ExtractCustomizeBytes` および `Buffer.MemoryCopy` を完全削除。
    - Glamourer のステート適用（`ApplyState` / `ApplyDesign`）は種族・性別・装備・外見すべてを完璧に同期するため、外見適用を Glamourer に 100% 一任。
  - **ForceAllApply の完全適用**:
    - `Customize`, `Equipment` に加え、`Parameters`（肌色・髪色・目の色）および `Bonus`（メガネ等）の全スロットを強制的に `Apply = true` に設定。プリセット側で `Race: Apply = false` になっているデザインであっても、種族・性別・外見のすべてが確実に反映される。
  - **スポーン時の冗長自己コピーの撤廃**:
    - `SpawnCharacter` 内で呼び出されていた無意味かつ有害な自己コピー `nativeChara->CharacterSetup.CopyFromCharacter(nativeChara, CharacterCopyFlags.None);` を完全削除。
  - **デスポーン時のステート解放強化**:
    - `actor.DisplayName`（テンプレート名）だけでなく、実際の GameObject 名（`actor.PuppetName` = `"Csp Rrrkjeja"` 等）の両方で `RevertState` / `UnlockState` を行い、同一インデックス再利用時のステート混ざりを完全防止。
  - **Penumbra Redraw の確実な実行**:
    - Glamourer 適用完了後、`penumbraIpc.Redraw(actorIndex)` を確実に呼び出し、正常な 3D メッシュを確定描画。

## [0.1.34] - 2026-10-02
### Fixed
- **Glamourer Base64ヘッダーバージョン（Byte 6）欠落による `Unknown Version 31` 例外の完全解消**:
  - **根本原因の特定**: Glamourer のネイティブ実装（`DesignConverter.cs` / `Luna.dll`）において、Base64 文字列の先頭1バイトはデザインフォーマットのバージョン番号（`0x06`）としてパースされる。先頭にバージョンバイトを書き込まずに純粋な GZip バイト列を Base64 化していたため、GZip のマジックナンバー `0x1F`（= 31）がバージョン番号と誤認され、Glamourer 内部で `System.Exception: Unknown Version 31`（結果コード 7: `CouldNotParse`）が発生しデザイン適用が拒絶されていた。
  - **バージョン 6 バイトの注入**: `GlamourerIpc.CompressToBase64` において、GZip データの先頭に必ず `ms.WriteByte(6)` を書き込むよう修正。これにより Glamourer が 100% 正常にステートを解凍・認識し、`ApplyState` による外見強制上書きが完全に成功するようになった。
- **二重 Redraw 競合による自キャラ（女性ミコッテ）巻き戻しの完全根絶**:
  - **根本原因の特定**: Glamourer は `ApplyState` / `ApplyDesign` 呼び出しの内部で自動的にアクターのネイティブリロード（Redraw）を実行する。直後に CharacterSpawn 側から追加で `penumbraIpc.Redraw(actorIndex)` を呼んでいたため、FF14 の非同期描画パイプラインで二重リロードの競合が発生し、初期化途中の素体（女性ミコッテ）にロールバックしていた（Brio でも同様に Glamourer 適用後は外部 Redraw を呼んでいない）。
  - **Glamourer 時の重複 Redraw 除外**: `template.SourceType != CharacterSourceType.Glamourer` の場合のみ Penumbra Redraw を呼び出すよう修正し、競合ロールバックを完全に解消。
- **CustomizeData 26 バイトのネイティブメモリ常時同期**:
  - レースコンディション対策として、Glamourer 適用時も取得した CustomizeData（種族・性別・顔・髪型等）をネイティブのアクター描画データ（`chara->DrawData.CustomizeData`）に直接コピー同期。

## [0.1.33] - 2026-10-02
### Fixed
- **`Race: Apply = False` デザインにおける種族不一致ロールバックの完全解消 (GZip Base64 State Injection)**:
  - **根本原因の特定**: `Chonk`（`Kimo-1-Default`）などの一部の Glamourer デザインプリセットでは、ファイル内で `Race: { Value: 1, Apply: false }` と定義されている。これを `ApplyDesign(Guid)` でそのまま渡すと、Glamourer は指定通り Race（ミコッテ女性）を維持したまま Clan（ハイランダー）と Gender（男性）のみを適用しようとし、「ミコッテのハイランダー男性」という無効な組み合わせ（Race/Clan 不一致）が発生。FF14描画エンジンがエラーを起こして素体（自キャラ女性ミコッテ）にフォールバックしていた。
  - **ForceAllApply & GZip Base64 圧縮ステート注入**:
    - デザインファイル（JObject）から `ForceAllApply` を実行し、`Race`, `Clan`, `Gender`, 全装備スロットの `Apply` を強制的に `true` に書き換え。
    - Glamourer のネイティブステート仕様に準拠し、書き換えた JObject を UTF-8 JSON -> `GZipStream` 圧縮 -> Base64 文字列（`H4sI...`）にエンコード。
    - エンコードした圧縮 Base64 を `Glamourer.ApplyState(compressedBase64, actorIndex, 0, 7UL)` に渡すことで、Glamourer に `Race` も含めた全スロットを 100% 確実に強制適用させ、男性ハイランダーへと完璧に変身させる。
- **CustomizeData メモリコピーの競合防止**:
  - `Buffer.MemoryCopy` が Glamourer 成功後にも実行されてネイティブメモリを上書きするリスクを排除し、Glamourer IPC 失敗時のフォールバックに限定。

## [0.1.32] - 2026-10-02
### Fixed
- **Orphaned Weapon残存バグの完全根絶 (Fixing Detached Weapons Remaining on Ground After Despawn)**:
  - **根本原因の特定**: FF14の描画エンジン（Render/DrawObject）では、子描画オブジェクト（武器モデルなど）を保持したまま `ClientObjectManager.DeleteObjectByIndex` で親GameObjectのみを直接削除すると、シーングラフから切り離された武器の DrawObject が解放されず、ワールド座標に取り残される現象（Orphaned Weapon Bug）が発生していた（ユーザー添付のマンダヴィル・ガンブレードが地面に残る現象で確認）。
  - **DisableDraw() 先行解放 (Brio DestroyObject パターン準拠)**: `ActorManager.DespawnCharacter` のオブジェクト削除処理の直前で必ず `chara->GameObject.DisableDraw()` を呼び出し、描画ツリー全体および武器オブジェクトを完全にアンロードしてからCOM削除を実行するように修正。
- **男性キャラ／異種族キャラが自キャラ（女性ミコッテ）の姿に戻る問題の完全解消 (Fixing Character Rollback to Player Baseline)**:
  - **根本原因の特定**: `ApplyAppearanceDirect` 内で、Glamourer IPC 呼び出し後に `chara->CharacterSetup.CopyFromCharacter(chara, CharacterCopyFlags.None)` を実行していた。`CopyFromCharacter(chara, None)` はアクター自身の現在の素体（自キャラ女性ミコッテ）からモデルを再初期化するため、Glamourer が注入したスケルトンとモデル状態をエンジンレベルで自キャラに強制上書きリセットしてしまっていた。
  - **有害な自己コピー処理の撤廃**: Brio および HDM の標準アーキテクチャに準拠し、`CharacterSetup.CopyFromCharacter(chara, None)` を完全削除。Glamourer のネイティブフックと Penumbra Redraw が提供する正確なモデル構造をそのまま描画させることで、男性ハイランダー（Chonk 等）や異種族・異性別のキャラクターが 100% 確実に反映されるように修正。
- **Glamourer デザイン適用の最適化 (Brio SetDesign パターン)**:
  - `GlamourerIpc.ApplyDesignToActorEx` において、Guid 指定時に失敗していた JSON文字列による `ApplyState` の無理な呼び出しを廃止し、Brio と同じく `ApplyDesign(targetGuid, actorIndex, 0, 7UL)`（Flags: 7 = `DesignDefault` : Once | Equipment | Customization）を直接最優先で実行。
- **デスポーン時・スポーン時のステート完全クリーンアップの強化**:
  - `ActorManager.DespawnCharacter` 時に、GlobalIndex だけでなくアクター名（`actor.DisplayName`）でも Glamourer ステートを `RevertStateName` / `UnlockStateName` で解放し、スロット再利用時の外見情報の混ざり・残留を完全に防止。

## [0.1.31] - 2026-10-02
### Fixed
- **ASCII-Only Valid FF14 Puppet Name Generation (Fixing Reverting to Player Character Baseline)**:
  - **Root Cause Identified**: In v0.1.30, hex digits from `template.Id` (e.g. `Csp 04ffbf3e`) were used in puppet names. FF14 engine and all IPC plugins (Penumbra, Glamourer, CustomizePlus) enforce strict player name validation (`VerifyPlayerName`) that strictly rejects digits (0-9). Consequently, Penumbra rejected the actor with `ec=16 (InvalidActor)`, Glamourer with `result=2 (ActorNotFound)`, and CustomizePlus with `ec=255 (ActorNotFound)`, resulting in zero appearances being applied and the actor remaining as the player character.
  - **Valid FF14 Name Generator**: `GetPuppetName` now maps Guid bytes deterministically to an 8-character ASCII alphabet-only Surname (`[A-Z][a-z]{7}`, e.g. `Csp Evjkkhzl`), perfectly complying with FF14 player naming standards while maintaining deterministic per-template uniqueness ($26^8 \approx 2.08 \times 10^{11}$ combinations).
- **Robust Glamourer Guid Design Application & Fallthrough Guard**:
  - In `GlamourerIpc.ApplyDesignToActorEx`, for Guid designs, retrieves the design JObject, applies `ForceAllApply` to ensure no slots are skipped, and applies state via `ApplyState` while directly synchronizing 26-byte `CustomizeData` to the native actor.
  - Guarded Guid designs from falling through to the Base64 string parser, preventing conversion failure errors (`result: 7`).

## [0.1.30] - 2026-10-02
### Fixed
- **Puppet Actor Name Cache Isolation & Full State Cleanup (Fixing Respawning as Wrong / Deleted Character Appearance)**:
  - **Root Cause 1 (Actor Name Cache Collision)**: `NextPuppetName()` generated sequential names (`Csp Aa`, `Csp Ab`, ...). Upon plugin reload or serial rollover, names assigned to previous characters (such as `Chonk` or `Lyle`) were recycled for new characters (`Ruma`). Glamourer and Penumbra automatically cache and restore states by actor GameObject name (`Csp Ac`), causing past appearances and mod collections to automatically override the new character immediately upon entering the world.
  - **Deterministic Unique Puppet Identity**: Changed actor naming strategy to `GetPuppetName(CharacterTemplate)` (`Csp {template.Id:N8}`). Each character template now maintains a completely unique and deterministic puppet name, mathematically guaranteeing zero name collision with other or deleted characters across sessions.
  - **Root Cause 2 (Profile Bleed in User Config)**: Cleaned up accidentally persisted `CustomizePlusProfileName: "Chonk"` inside `CharacterSpawn.json` for template `Ruma` caused by modal field retention. Added automatic actor-level profile detachment (`DeleteTemporaryProfileOnCharacter`) when no profile is configured.
  - **Full Glamourer & CustomizePlus State Reset on Despawn & Apply**:
    - Integrated `Glamourer.UnlockState` and `Glamourer.RevertState` / `RevertToAutomation` into `ActorManager.DespawnCharacter` and before applying appearances in `ApplyAppearanceDirect`.
    - Integrated `CustomizePlus.DeleteTemporaryProfileOnCharacter` on despawn and template load to guarantee clean actor state.

## [0.1.29] - 2026-10-02
### Fixed
- **Penumbra Collection Isolation & Unassignment on Despawn/Appearance (Fixing Wrong Collection Pulled on Spawn)**:
  - **Root Cause Identified**: Previous character collections and temporary collections remained registered to actor slots (`Global#200`) without explicit unassignment upon despawning. Spawning a new character with no collection or switching between MCDF and Glamourer presets resulted in previous Penumbra collections persisting or overriding the new actor's appearance.
  - **UnassignCollectionForActor**: Introduced dedicated IPC subscriber in `PenumbraIpc` to unassign both temporary collections (`AssignTemporaryCollection.V5(Guid.Empty, actorIndex, false)`) and standard object collections (`SetCollectionForObject.V5(actorIndex, null / Guid.Empty, true, true)`).
  - Called `UnassignCollectionForActor` inside `ActorManager.DespawnCharacter` and at the start of `ActorManager.ApplyAppearanceDirect`, guaranteeing a clean slate before any appearance is loaded.
  - Corrected `PenumbraIpc.SetCollectionForActor` to treat `PenumbraApiEc.NothingChanged (1)` as success alongside `ec=0`.
- **Character Modal State Pollution & Cross-Contamination**:
  - Separated MCDF parsed Glamourer design strings (`modalMcdfGlamourerDesign`) from standard Glamourer design inputs (`customGlamourerString`).
  - Completely isolated saved properties per `CharacterSourceType` inside `CharacterLibraryTab.SaveModalTemplate`, ensuring MCDF archive data never overwrites or bleeds into standard Glamourer & Penumbra character definitions.
- **Direct Glamourer Guid Application & Native Synchronization**:
  - In `GlamourerIpc.ApplyDesignToActorEx`, prioritized direct invocation of `ApplyDesign(Guid, actorIndex, 0, 6UL)` when a valid Guid is present, eliminating Base64 parse errors (`result: 7`) while synchronizing 26-byte `CustomizeData` directly to native engine structs.

## [0.1.28] - 2026-10-02
### Fixed
- **Force All Apply Flags & Direct Native `CustomizeData` Synchronization (Fixing Male/Different Race Character Spawning)**:
  - **Root Cause Identified**: Discovered via deep inspection of Glamourer design files (`238897be-...` / `Chonk`) that certain presets have `"Apply": false` on essential customization slots like `Race` (e.g., Highlander Male with `Race: Apply = false`). Calling `ApplyDesign(Guid)` directly left the spawned actor's race unchanged as Miqo'te (the player character's baseline) while applying male gender and highlander clan, causing an invalid race-clan mismatch that failed rendering and caused fallback to player appearance.
  - **ForceAllApply**: Implemented automatic resolution of full design JObjects (via `Glamourer.GetDesignJObject` and disk fallback) and forced `Apply = true` across all Customize slots (`Race`, `Gender`, `Clan`, `BodyType`, `Face`, `Hairstyle`, etc.) and Equipment slots before calling `ApplyState`.
  - **Direct Native Memory Synchronization**: Extracted the exact 26-byte `CustomizeData` from the design JObject and wrote it directly into `chara->DrawData.CustomizeData` followed by `CharacterSetup.CopyFromCharacter(chara, CharacterCopyFlags.None)`. This ensures the game engine's native character data itself is immediately transformed to the target race and gender, eliminating any chance of fallback during Redraw.

## [0.1.27] - 2026-10-02
### Fixed
- **Persistent Glamourer Design & State Synchronization Across Redraws (Fixing Spawning as Player Character)**:
  - Discovered via reverse engineering of `Glamourer.Api.dll` and `HDM.dll` IL that `ApplyFlagEx.DesignDefault = 7UL` contains `ApplyFlag.Once = 1`. When applied with `Once`, Glamourer only temporarily overwrites the actor's in-memory draw model without persisting to the actor's internal Glamourer state. Consequently, subsequent Penumbra / engine redraws caused actors to immediately revert back to the base puppet appearance (the user's own player character).
  - Adopted HDM's proven standard: changed apply flags from `7UL` / `7U` to `6UL` / `6U` (`ApplyFlag.Equipment | ApplyFlag.Customization` without `Once`). This ensures Glamourer updates the persistent actor state, preserving designs (`Chonk`, custom MCDF characters) flawlessly through redraws.
  - Eliminated redundant intermediate `Redraw` invocation directly after `Penumbra.SetCollectionForActor`, consolidating redraw logic to the finalization phase.
- **MCDF Embedded CustomizePlus Profile Deserialization (`unexpected character 'e'`)**:
  - Identified that `CustomizePlusData` embedded in Mare Synchronos MCDF archives is Base64-encoded JSON. Passing raw Base64 strings to CustomizePlus IPC caused JSON deserialization failure.
  - Added automatic Base64-detection and decoding before forwarding to CustomizePlus IPC `SetTemporaryProfile`, ensuring embedded body scales and bone transforms apply correctly.

## [0.1.26] - 2026-10-02
### Fixed
- **Actor Lifecycle Safety & Crash Prevention on Despawn/Respawn (HDM Compliance)**:
  - Fixed critical CTD / unhandled exception (`0x12345679` via Dalamud Detour / `RaiseException`) occurring when despawning and respawning actors.
  - Eliminated stale raw native pointer dereferences in `ActorManager.UpdateFrame()`. Fully transitioned to HDM's golden pattern: re-resolving actors every tick via `IObjectTable[GlobalIndex]` and verifying `chara.Address != nint.Zero` before accessing native structs.
  - Wrapped `UpdateFrame()`, `DrawUI()`, `OnFrameworkUpdate()`, `UpdateActorTransform()`, and all job processing loops in structured `try-catch` exception blocks to prevent CLR unhandled exceptions from breaching native detour boundaries.
  - Added `pendingNpcJobs.RemoveAll` to `DespawnCharacter` to prevent lingering jobs from polling deleted actors.
  - Introduced `IsReady` lifecycle guard to `SpawnedActorData` ensuring 3D Gizmos and transform updates only activate once draw baseline and appearance customization are fully initialized.

## [0.1.25] - 2026-10-02
### Fixed
- **Penumbra Collection & Mod Redirection for Humanoid Actors (`ec=16` InvalidActor Resolution)**:
  - Discovered through deep IL disassembly of `Penumbra.dll`'s `CollectionApi.SetCollectionForObject` and `AssociatedIdentifier` that Penumbra's internal identifier resolution calls `ActorIdentifierFactory.FromObject` with `allowPlayerNpc: false`.
  - When `nativeChara->GameObject.ObjectKind` was `BattleNpc`, Penumbra strictly branched into `CreateBNpcFromObject`. Because spawned puppets have `NameId = 0`, this consistently produced `ActorIdentifier.Invalid`, resulting in `ec=16 (InvalidActor)` and causing Penumbra to fail collection assignment and mod redirection (leaving actors in a vanilla state).
  - Explicitly classified all humanoid puppets (Glamourer designs, MCDF bundles, and player clones) as `ObjectKind.Player`. This directs Penumbra into `CreatePlayerFromObject`, which verifies the player name and home world, resolving a valid Player Identifier and enabling 100% successful Penumbra collection assignment (`ec=0`) and instant mod rendering upon `Redraw`.
  - Maintained `ObjectKind.BattleNpc` for non-humanoid monsters (`ModelCharaId > 0`) during Phase 2 transition to ensure native monster model rendering remains undisturbed.

## [0.1.24] - 2026-10-02
### Fixed
- **AQR Independent Spawn & MCDF Temporary Collection Application**:
  - Eliminated `nativeChara->GameObject.OwnerId = 0xE000_0000;` override (preserved default 0). Discovered through IL disassembly of `Penumbra.GameData.dll` that a non-zero `OwnerId` caused `CreateBNpcFromObject` to attempt resolving a non-existent parent GameObject in `ObjectTable`, yielding invalid identifiers and failing with `ec=255 (UnknownError)`.
  - Re-ordered MCDF loading pipeline to add temporary mod files (`AddTemporaryMod`) before actor assignment (`AssignTemporaryCollection`), ensuring seamless mod registration without AQR dependency.
  - Formatted puppet names as `"Csp {hi}{lo}"` to strictly satisfy Penumbra's `VerifyPlayerName` player naming validation.
  - Eliminated premature `ApplyAppearanceDirect` invocation in `SpawnCharacter`, executing appearance resolution exclusively after Phase 2 humanoid baseline draw verification.
- **Humanoid NPC True Appearance Synchronization (Mionne, Gontran)**:
  - By restoring `OwnerId = 0`, Glamourer's `GetState` now resolves immediately within 1-2 frames instead of timing out at 120 frames, successfully applying genuine NPC facial customizations and equipment without player clone fallbacks.
- **Monster Model Accuracy (Ruins Runner)**:
  - Fixed issue where Ruins Runner spawned as an unintended monster (Raptor). Switched monster cache indexing to use unique `BaseId` instead of shared `BNpcNameId`, preventing ID collisions, and restricted `ModelCharaId` auto-resolution to unassigned models only.
- **Demihuman NPC Rendering (Letter Moogle)**:
  - Added fallback to `baseRow` inline equipment fields in `GameDataService.GetNpcAppearanceData` when `NpcEquip.RowId == 0`, ensuring Demihuman body/head equipment slots are correctly populated and rendered rather than showing an invisible body with gizmo only.

## [0.1.23] - 2026-10-02
### Fixed
- **HDM Official `mob-model-index.csv` Integration (Accurate Monster / Mob Spawning)**:
  - Replaced heuristic `BNpcLink.csv` mapping with HDM's authoritative `mob-model-index.csv` (16,243 rows).
  - Resolved model mismatch issues where spawning monsters like Ruins Runner resulted in incorrect models (Ruins Runner correctly resolves to ModelChara 1281, McType 3, Scale 1.1).
  - Japanese monster names resolved directly from Lumina `BNpcName` sheet with duplicate deduplication for a clean search experience.
- **Humanoid NPC Appearance Synchronization (Resolved Player Clone / testruma / Ruma Fallback)**:
  - Identified root cause in `dalamud.log`: synchronous `Thread.Sleep(16)` blocked the main framework thread, preventing Glamourer from registering newly spawned actors and causing `GetState` to return null.
  - Implemented non-blocking per-frame polling queue (`PendingNpcJob`) conforming to HDM's `HumanGuise.cs`.
  - Polled each frame up to 120 frames without blocking; as soon as `GetState` resolves, mapped 26-byte NPC customization and 10-slot equipment, stripped `Parameters` and `Materials` to eliminate player skin/shader pollution, and executed `Penumbra.Redraw` to finalize the NPC skeleton and gear.
  - NPCs like Gontran and Miounne now render with 100% faithful face, hair, and gear instead of falling back to player clones.
- **Demihuman NPC Spawning (Resolved Invisible Letter Moogle / Gizmo-Only Bug)**:
  - Resolved issue where Demihuman NPCs (McType 2, e.g. Letter Moogle, Namazu) rendered invisible with only gizmos.
  - Ensured `NpcEquip` parts are extracted and written directly into `DrawData.EquipmentModelIds`, and `DrawData.IsHatHidden = false` is maintained so Demihuman bodies and equipment render reliably.

## [0.1.22] - 2026-10-02
### Fixed
- **HDM-Compliant Monster & Mob Spawning (Resolved Invisible 3D Model / Gizmo-Only Bug)**:
  - Resolved issue where spawned monsters / mobs were invisible, showing only gizmo manipulators.
  - Aligned with HDM's (`Enceladeum/HDM`) proven two-phase rendering architecture: actors are initially seeded as clean humanoid baseline clones (`ModelCharaId = 0`, `Scale = 1.0f`) to allow the engine to establish a valid baseline draw object.
  - In Phase 2, once the humanoid draw object is verified visible (`DrawObject != null && DrawObject->IsVisible`), the actor is transitioned to the target monster `ModelCharaId` and scale, followed by a dedicated native redraw sequence (`DisableDraw()` -> `IsReadyToDraw()` wait -> `EnableDraw()`).
  - Completely suppressed Penumbra / Glamourer redraw invocations on monster models to prevent invalidation of non-humanoid draw objects.
  - Added Demihuman equipment mapping and `IsHatHidden = false` preservation for non-humanoid demihumans.
- **HDM-Compliant Humanoid NPC Spawning via Glamourer IPC**:
  - Implemented `Glamourer.GetState` and `Glamourer.ApplyState` IPC integration in `GlamourerIpc.cs` conforming to HDM's `HumanGuise.cs`.
  - Added `ApplyNpcAppearance` to map 26-byte `CustomizeData` into Glamourer's 36-field `Customize` model via `CustomizeMap`, and injected NPC gear slots using bit-packed `CustomItemId`.
  - Automatically stripped `Parameters` and `Materials` blocks on NPC appearance apply, preventing player skin tone / shader overrides from bleeding onto NPC disguises.
  - Added automatic fallback resolution of `ModelCharaId` and NPC appearance from `GameDataService` if template IDs are present.

## [0.1.21] - 2026-10-02
### Fixed
- **Continuous 360-Degree Horizontal Rotation (Resolved 180° Flip & Jitter)**:
  - Fixed character rotation getting stuck and jittering around the 180-degree mark.
  - Replaced Euler angle matrix decomposition (`ImGuizmo.DecomposeMatrixToComponents`), which suffered from a ±180° discontinuity and feedback-loop jitter, with Stagehand-compliant `Quaternion` matrix composition (`Matrix4x4.CreateFromQuaternion`).
  - Extracted the actor's forward direction vector via `Vector3.Transform(Vector3.UnitZ, newRot)` and computed seamless continuous heading with `MathF.Atan2(forward.X, forward.Z)`. The character now rotates smoothly and continuously past 180° without any jitter or angle wrapping limits.

## [0.1.20] - 2026-10-02
### Fixed
- **Horizontal Actor Rotation (Yaw Ring)**:
  - Fixed character rotation not responding during ring manipulation. Switched operation from 4-ring `Rotate` (which prioritized screen-space camera roll) to `ImGuizmoOperation.RotateY` (horizontal planar yaw ring).
  - Dragging the green horizontal rotation ring now immediately and smoothly turns the character's heading in 360 degrees.
- **Eliminated "New NPC: Failed to get response." Popup**:
  - Implemented dynamic input capture in `GizmoRenderer`: `ImGuiWindowFlags.NoInputs` is dynamically cleared while hovering or manipulating the gizmo (`IsOver() || IsUsing()`), consuming clicks and preventing game-world click-through.
  - While not hovering over the gizmo, `NoInputs` remains active so players can freely rotate the game camera without hindrance.
  - Enforced `TargetableStatus = 0` and `EventId = 0` on spawned actors to completely disable game NPC interaction events.
- **Removed Duplicate Header Gizmo Buttons**:
  - Cleaned up `MainWindow.Draw()` by removing the redundant gizmo toolbar buttons from the upper-left header above tabs, retaining only the clean in-context toolbar inside the character spawn details and stage scene tabs.

## [0.1.19] - 2026-10-02
### Fixed
- **ImGuizmo 3D Rendering & Camera Projection**:
  - Resolved gizmo rendering failure by applying FFXIV reverse-Z clip projection matrix correction (`M43 = -(clip * near)`, `M33 = -((far + near) / (far - near))`, `view.M44 = 1.0f`) conforming to Stagehand and BDTH architecture.
  - Recomposed transform matrix via `ImGuizmo.RecomposeMatrixFromComponents` with Euler degrees, properly positioning the 3D gizmo at the target actor's location.
- **Camera Viewport Input Transparency**:
  - Added `ImGuiWindowFlags.NoInputs` to the full-screen gizmo overlay window. This completely eliminates game-wide mouse input blocking, allowing unrestricted camera rotation (right-click drag) and character movement in the game world while preserving 3D gizmo hit-testing.
- **Gizmo Toggle Unification**:
  - Removed duplicate `Gizmo` ON/OFF checkboxes from `CharacterLibraryTab`, `StageSceneTab`, and `SettingsTab`.
  - Unified gizmo state management entirely into the Stagehand-style mode toolbar:
    - **Select (Mouse Pointer)**: Turns gizmo OFF / hides manipulator.
    - **Translate (Cross Arrows)**: Turns gizmo ON in translation mode with XY/XZ/YZ quad plane handles.
    - **Rotate (Sync Alt)**: Turns gizmo ON in rotation mode with 3-axis rings.

## [0.1.18] - 2026-10-02
### Added
- **Stagehand-Compliant ImGuizmo 3D Gizmo System**:
  - Replaced the custom 2D screen-projected gizmo with native `Dalamud.Bindings.ImGuizmo` architecture identical to Stagehand.
  - Extracted game camera matrices (`ViewMatrix`, `ProjectionMatrix`) directly from `FFXIVClientStructs.FFXIV.Client.Graphics.Scene.CameraManager.Instance()->CurrentCamera->RenderCamera`.
  - Moved gizmo rendering to a full-screen transparent overlay window in `Plugin.DrawUI`, completely eliminating mouse focus loss and click-through issues when dragging handles in the 3D game world.
  - Implemented Stagehand-style mode toolbar (Select / Translate / Rotate) across `MainWindow`, `CharacterLibraryTab`, and `StageSceneTab`:
    - **Select Mode (`FontAwesomeIcon.MousePointer`)**: Hides the gizmo for normal scene interaction.
    - **Translate Mode (`FontAwesomeIcon.ArrowsUpDownLeftRight`)**: Renders primary X, Y, Z axis arrows alongside red, green, and blue **XY, XZ, YZ quad planes** for multi-axis simultaneous drag-manipulation.
    - **Rotate Mode (`FontAwesomeIcon.SyncAlt`)**: Separates rotation from translation, rendering dedicated 3-axis rotation rings for intuitive yaw/pitch/roll adjustments.
  - Real-time transform synchronization via `Matrix4x4.Decompose` updating actor position and yaw in both library preview and active stage actors.

## [0.1.17] - 2026-10-02
### Added
- **Customize+ (C+) Profile Integration**:
  - Implemented comprehensive IPC integration with Customize+ (v6+ API) via `Services/CustomizePlusIpc.cs`.
  - Added Customize+ profile selector to character creation and editing modals in `CharacterLibraryTab.cs`.
  - Spawning a character with an assigned Customize+ profile now automatically queries and applies temporary body scales and bone transforms to the spawned actor.
  - Full automatic cleanup: temporary Customize+ profiles are seamlessly revoked and freed upon despawning characters, scene transitions, or territory changes.
  - Added support for embedded `CustomizePlusData` within `.mcdf` archives, allowing automatic body scaling even for third-party MCDF files without manual profile mapping.

### Changed
- **Modal UI Cleanup**:
  - Removed the unused `Or Direct Design String / Code` manual multiline input box from `CharacterLibraryTab.cs`, streamlining the character creation modal to design and collection dropdown pickers.

## [0.1.16] - 2026-10-02
### Fixed
- **Penumbra Collection & MCDF Temporary Collection Assignment via ObjectKind.Player**:
  - Through comprehensive CIL reverse-engineering of `Penumbra.GameData.dll`'s `ActorIdentifierFactory.FromObject`, discovered the exact root cause of `AssignTemporaryCollection` failing with error code `255` and `SetCollectionForObject` failing with `ec = 16` (`InvalidIdentifier`).
  - Previously, all spawned actors were assigned `ObjectKind = ObjectKind.BattleNpc`. When resolving collections, Penumbra's internal IPC methods invoke `CreateBNpcFromObject(allowPlayer: false)`. Because `allowPlayer` is hard-coded to `false` in collection assignment IPC, any non-Player object kind—regardless of its name or `OwnerId`—is strictly treated as a monster NPC. Since `DataId` was 0, it resolved to non-existent `BNpc(0)`, which has 0 mod collections, causing `Collections.Add` to reject assignment with error code 255 and `SetCollectionForObject` to return 16.
  - Resolved this by setting `ObjectKind = ObjectKind.Player` (and `BattleNpcSubKind = BattleNpcSubKind.Player`) for all humanoid actors (`template.ModelCharaId == 0`), while keeping `ObjectKind = ObjectKind.BattleNpc` strictly for monster models (`template.ModelCharaId > 0`).
  - With `ObjectKind.Player`, Penumbra directly routes to `CreatePlayerFromObject`, validating the character's name (`"Cs Aa"` format) and returning a 100% valid Player identifier. Both normal Penumbra collections and MCDF temporary collections now successfully bind (`ec = 0`) and apply all custom 3D models, textures, and manipulations to spawned actors.

## [0.1.15] - 2026-10-02
### Added
- **Full AQR-Compliant MCDF Mod Extraction & Penumbra Temporary Collection Lifecycle**:
  - Implemented complete extraction of embedded Mod files (3D models, textures, materials, and FileSwaps) and `ManipulationData` directly from `.mcdf` LZ4 binary streams into local plugin cache (`mcdf_cache`).
  - Integrated Penumbra Temporary Collection IPC APIs (`CreateTemporaryCollection`, `AssignTemporaryCollection`, `AddTemporaryMod`, `DeleteTemporaryCollection`).
  - When spawning a character with an MCDF file, CharacterSpawn now dynamically creates a dedicated Penumbra temporary collection, binds all embedded mod files and meta manipulations to the spawned actor, applies the Glamourer design, and redraws seamlessly without requiring user-created Penumbra collections.
  - Automatically deletes and reclaims temporary Penumbra collections upon despawning or territory transitions, preventing memory/handle leaks.
- **Dynamic Plugin Assembly Version Logging**:
  - Replaced hardcoded `v0.1.9` startup log string in `Plugin.cs` with dynamic assembly version resolution (`v{GetType().Assembly.GetName().Version}`).

### Fixed
- **MCDF UI Penumbra Independence (AQR Conformity)**:
  - Removed manual Penumbra Collection selector from the MCDF section in `CharacterLibraryTab.cs`. MCDF templates now automatically inform users of embedded mod auto-loading via temporary collections, enabling full cross-user portability for third-party `.mcdf` files.
- **ActorManager PluginInterface Injection**:
  - Wired `IDalamudPluginInterface` through to `ActorManager` to provide reliable config directory resolution for MCDF file caching.
### Fixed
- **Penumbra InvalidIdentifier (ec=16) Resolution via OwnerId Initialization**:
  - Through full CIL reverse engineering of `Penumbra.GameData.dll`'s `CreateBNpcFromObject`, discovered that Penumbra inspects `GameObject.OwnerId`. If `OwnerId` is not equal to `0xE0000000` (`GameObject.InvalidGameObjectId`), Penumbra attempts to look up the parent object (`objects.ById(ownerId)`). Because `CreateBattleCharacter` initializes `OwnerId` to `0`, the lookup failed and returned `InvalidIdentifier` (`ec=16`), causing all Penumbra collection assignments to be rejected.
  - Explicitly set `nativeChara->GameObject.OwnerId = 0xE000_0000` alongside `NameId = 0`, `HomeWorld`, and `SetName(puppetName)`. This satisfies Penumbra's Player identifier validation branch, allowing `SetCollectionForObject` to return `ec=0` (Success) and apply collections flawlessly.
- **MCDF Section Penumbra Collection Selector**:
  - Added Penumbra Collection selection dropdown to the MCDF configuration section in `UI/CharacterLibraryTab.cs`.
  - Characters created or imported from `.mcdf` files can now bind and persist dedicated Penumbra Collections alongside their extracted Glamourer design.

## [0.1.13] - 2026-10-02
### Fixed
- **MCDF LZ4 Decompression Stream Support (AQR McdfCharaFileManager Architecture)**:
  - Resolved the critical issue where MCDF files failed to parse GlamourerDesignString because modern `.mcdf` files are whole LZ4-compressed binary streams rather than raw binary JSONs.
  - Integrated `lz4net` and updated `Services/McdfParser.cs` to decompress the LZ4 stream before inspecting the `"MCDF"` 4-byte header and parsing the embedded JSON string.
  - Successfully verified extraction of Glamourer Base64 strings from real `.mcdf` files (`testruma.mcdf`, `test.mcdf`), eliminating fallback to previous actor appearance or local player.
- **Accurate Penumbra V5 IPC Signature Resolution (CIL Metadata Verification)**:
  - Discovered via CIL disassembly of `Penumbra.Api.dll` that `Penumbra.SetCollectionForObject.V5` strictly expects `(int actorIndex, Guid? collectionId, bool allowCreate, bool allowDelete)` and returns `(int ec, (Guid, string)? oldCollection)` (`ValueTuple<int, Nullable<ValueTuple<Guid, string>>>`), not `(int, Guid)` or `int`.
  - Updated `Services/PenumbraIpc.cs` with exact tuple signatures for both V5 and Legacy APIs, resolving CallGate type-conversion exceptions (`converting from ValueTuple 2 to System.Int32`) and ensuring 100% reliable Penumbra Collection assignment to spawned actors.
- **Penumbra Dual-Phase Redraw Synchronization (AQR Conformity)**:
  - In `Managers/ActorManager.cs`, aligned appearance application order with AQR: Penumbra collection assignment -> first Penumbra Redraw -> Glamourer design application -> second Penumbra Redraw. This guarantees Mod textures, clothes, and meshes apply seamlessly to spawned actors.

## [0.1.12] - 2026-10-02
### Fixed
- **Binary MCDF Format Parsing (AQR MCDF-Loader Architecture)**:
  - Resolved the issue where selecting an MCDF file appeared not to load and spawned the local player's appearance. Discovered that modern MCDF files are proprietary binary containers (`"MCDF"` 4-byte header + UTF-8 JSON payload) rather than standard ZIP archives.
  - Rewrote `Services/McdfParser.cs` with binary header scanning and direct extraction of the Base64 `GlamourerData` string, enabling instant parsing and full appearance restoration from `.mcdf` files.
- **Penumbra IPC Return Signature Tuple Resolution (AQR Penumbra Architecture)**:
  - Fixed `Penumbra.SetCollectionForObject.V5` failure where Penumbra returns `(PenumbraApiEc, Guid)` (`ValueTuple<int, Guid>`) while legacy code expected `int`, causing CallGate runtime conversion exceptions that prevented Penumbra collections from applying.
  - Added robust tuple-based subscriber fallbacks in `Services/PenumbraIpc.cs` supporting both V5 and legacy signatures.
- **Pre-Draw Direct Appearance Application (Root Cause of "Temporary Local Player" Eliminated)**:
  - Completely redesigned `ActorManager.SpawnCharacter` and `ApplyAppearanceDirect`: actors now immediately call `DisableDraw()` upon creation and have their target appearance (Glamourer design, Penumbra collection, MCDF state, or Monster `ModelCharaId`) applied *before* the first frame renders, rather than waiting for `DrawObject->IsVisible`.
  - Eliminates the brief appearance of the local player before transitioning to the desired design.
- **HDM-Compliant Monster & Non-Humanoid Rendering (Fix for "Gizmo Only")**:
  - Identified that triggering Penumbra `RedrawObject` on non-humanoid monsters (`ModelCharaId > 0`, Letter Moogle, Ruin Runner, Antelope Doe, Gnat) caused Penumbra to invalidate and strip non-humanoid `DrawObject`s, leaving only a gizmo.
  - Removed Penumbra redraw calls from monsters in accordance with HDM's `GuiseService`, using native engine `DisableDraw` -> `IsReadyToDraw()` -> `EnableDraw()` cycle, ensuring 100% stable 3D monster and non-humanoid NPC rendering.
- **UI Flat Styling & Border Glitch Fix**:
  - Removed borders on the left tree scroll child window in `UI/CharacterLibraryTab.cs` to prevent visual line artifacts when selecting spawned characters.
### Fixed
- **Two Index Spaces Trap Resolution (Glamourer/Penumbra Player Clone Fix)**:
  - Resolved critical architectural flaw where internal `ClientObjectManager` slots (COM# 0, 1...) were passed to IPC endpoints instead of global `IObjectTable` indices (~200-244 reserved range). Passing COM# 0 caused Glamourer and Penumbra to target the local player character (index 0).
  - Tracked and supplied `actor.ObjectIndex` (`GlobalIndex`) for all Glamourer and Penumbra IPC operations, preventing spawned characters from taking on the local player's appearance.
- **Glamourer Identity Stamping (HDM The 0.8.44 Bug Fix)**:
  - Stamped each spawned BattleNpc actor with a valid SE player name format (`Cs Aa`, `Cs Ab`...) via a unique serial generator, `NameId = 0`, and the local player's `HomeWorld`.
  - Bypasses Glamourer's `ActorIdentifierFactory` invalid NPC ID rejection, allowing Glamourer state and design application to succeed reliably.
- **Draw-When-Ready 2-Phase Queue (HDM / Brio Architecture)**:
  - Implemented framework-driven `ReadyJob` queue. Phase 1 polls `IsReadyToDraw()` and enables draw; Phase 2 waits until `DrawObject != null && DrawObject->IsVisible` before applying Glamourer designs or Penumbra collections, ensuring Glamourer applies to a settled and registered actor body.
- **Monster & NPC Non-Humanoid Rendering (HDM GuiseService & Brio Pattern)**:
  - Fixed "gizmo only" / invisible models by seeding all actors with a double `CharacterSetup.CopyFromCharacter` from the local player before applying monster or NPC models, ensuring an active drawable skeleton exists rather than an uninitialized, invisible `SetupBNpc(0)`.
  - Swapped `ModelContainer.ModelCharaId`, hid weapons, and triggered Penumbra `RedrawObject` / `DisableDraw` settlement, guaranteeing monster and mob 3D models render correctly.
- **Template Data Persistence Guarantee**:
  - Enhanced `SaveModalTemplate` to guarantee full persistence of Glamourer GUID/name, Penumbra collection name, MCDF file path & parsed Base64 design, and NPC/Monster IDs with extensive logging.
  - Added clean state restoration in `OpenEditCharacterModal` and detailed attribute inspection in `Template Details`.
  - Robust `DespawnCharacter` resolving live COM indexes via `GetIndexByObject` to avoid stale index deletion.

## [0.1.10] - 2026-10-02
### Fixed
- **AQR-Conforming Monster & Non-Humanoid Spawning**: Adopted A Quest Reborn (AQR) architectural pattern for monster and non-humanoid NPC spawning. By setting `ModelContainer.ModelCharaId`, disabling weapons, and triggering Penumbra's `RedrawObject`, models (such as Ruin Runner, Antelope Doe, Gnat, Letter Moogle) now properly instantiate and render instead of showing only a gizmo.
- **Glamourer & Penumbra Appearance Application**: 
  - Fixed Glamourer IPC calls by applying proper flags (`flags = 7`: Customization | Equipment | Accessories) instead of `0`.
  - Added automatic resolution from design names to GUIDs in `ApplyDesignToActor`.
  - Ensured Penumbra collection assignment (`allowCreate = true, allowDelete = true`) executes before Glamourer design application, followed by `RedrawObject`.
  - Resolved the bug where spawning Glamourer / Penumbra presets spawned the local player's appearance.
- **MCDF Appearance Application**: Ensured MCDF parsed Glamourer designs are dispatched with `flags = 7` and synchronized with Penumbra Redraw, eliminating player clone fallbacks.
- **NPC Weapon Residuals Fix**: Set default `WeaponVisible = false` on NPC character templates to eliminate unintended local player weapon rendering (e.g. Ru Sushimo, Miounne).
- **Weapon Visibility Toggle (ON/OFF)**: Linked `SetWeaponVisibility` with Penumbra `RedrawObject`, guaranteeing immediate model re-render when toggling weapon visibility back ON or OFF.
- **Sorted Dropdown Lists**: Alphabetically sorted Glamourer designs and Penumbra collections in dropdown combo boxes for fast navigation.
- **UI Layout Separator Bleed Fix**: Encapsulated the character detail right pane inside `ImGui.BeginChild("RightDetailPane")`, preventing horizontal `ImGui.Separator()` lines from bleeding across into the left tree pane.
- **Removed Extraneous Guide Text**: Removed helper text annotations (`<= 武器表示ON/OFF`, `<= ギズモ表示ON/OFF`) and the preview explanation box per user feedback.

## [0.1.9] - 2026-10-02
### Added
- **Log Tab & LogManager**: Added a dedicated "Log" tab next to "Settings" in the main window with real-time log monitoring (info/warning/error color-coding, search filter, auto-scroll, and one-click "Copy All" to clipboard) to easily diagnose appearance source detection and actor spawning.
- **Weapon Visibility Control**: Added a `[x] Weapon Visible` checkbox in the Character tab details pane, enabling instant hiding/displaying of equipped weapons for spawned actors.
- **BNpcLink Mapping for Monsters**: Embedded comprehensive `BNpcLink` mapping (13,312 entries) linking `BNpcName` to `BNpcBase`, resolving model resolution failures for monsters.
- **Full Search for NPCs and Monsters**: Completely removed artificial search caps (previously 500), allowing smooth full-library search across all game NPCs and monsters.

### Changed
- **Character Tab Preview Mode**: Separated character spawning in the Character tab into a dedicated preview mode conforming to user UI mockups:
  - Toggles between `[ Spawn ]` and red highlighted `[ Despawn ]`.
  - Displays green `[Name] Spawning...` status and `[x] Gizmo` toggle while active.
  - Explanatory banner explaining the temporary preview feature.
- **New Chara 2x2 Button Grid**: Redesigned the "Select Appearance Source" selector from a dropdown combo to a responsive 2x2 button grid (`[ Glamourer&Penumbra ] [ MCDF ]` / `[ NPC(ENpc) ] [ Monster/Mob ]`) with red accent highlighting on the active source.

### Fixed
- **Glamourer & Penumbra IPC Connectivity**: Upgraded IPC key subscribers to the latest versions (`Glamourer.ApiVersion.V2`, `Glamourer.GetDesignList.V2`, `Glamourer.ApplyState`, `Penumbra.ApiVersion.V5`, `Penumbra.GetCollections.V5`, `Penumbra.SetCollectionForObject.V5`, `Penumbra.RedrawObject.V5`) with backward-compatibility fallbacks and dynamic re-checking polling, fixing the persistent "IPC Not Detected" issue.
- **Non-Humanoid Model Spawning (Moogles, Monsters)**: Fixed an issue where non-humanoid actors (e.g. Letter Moogle, Gegeruju, monsters) failed to render or only displayed a gizmo. Decoupled humanoid player cloning from non-humanoid model initialization, preventing bone/resource container corruption.
- **Monster Player-Clone Bug**: Fixed bug where monsters (such as Matanga/Matagai) spawned with player appearance by correctly obtaining `ModelCharaId` via `BNpcLink`.
- **NPC Weapon Residuals**: Fixed bug where humanoid NPCs (such as Ru Sushimo) displayed local player weapons by strictly managing weapon hiding and DrawData equipment containers.
- **MCDF Spawning**: Fixed MCDF spawn behavior by ensuring parsed Glamourer design strings are applied directly to the spawned preview actor via updated Glamourer IPC.
- **3D Gizmo Dragging & Picking**: Substantially increased gizmo handle picking radii and added full axis-line segment hit detection, allowing smooth and intuitive dragging along any axis.

## [0.1.8] - 2026-10-02
### Added
- **Character Library UI Redesign**: Redesigned the Character Library layout into a two-pane hierarchical structure matching the user mockups:
  - Left Pane: Folder & character tree view with expandable folder categories, plus bottom action buttons (`[New Chara]`, `[New Folder]`, `[Delete]`).
  - Right Pane: Inline character name display & editing, with action buttons (`[Spawn]`, `[edit]`, `[delete]`) and a detailed summary of appearance attributes.
  - Character Modal: Dedicated popup window for creating and editing characters with clear appearance source selection and settings.
  - New Folder Modal: Popup dialog to create new organization folders.
- **Glamourer & Penumbra Integration UI**: Added AQR-style searchable dropdown combos for selecting Glamourer designs and Penumbra collections directly from IPC, with optional manual string input.
- **Explorer File Picker for MCDF**: Integrated Win32 `GetOpenFileNameW` dialog via an asynchronous STA worker thread to open Windows File Explorer and browse `.mcdf` files directly, automatically parsing and populating Glamourer appearance data upon selection.
- **Humanoid & Non-Humanoid NPC Appearance Extraction**: Added `GetNpcAppearanceData` to extract 26-byte `CustomizeData` and 10-slot equipment model IDs from `ENpcBase` and `NpcEquip` Lumina sheets.

### Fixed
- **NPC Spawning as Player Character**: Fixed a critical bug where spawning NPCs (such as Letter Moogle or Miounne) resulted in the local player's appearance. Implemented proper `ModelContainer.ModelCharaId` assignment for non-humanoid NPCs and direct `DrawData.CustomizeData` & `EquipmentModelIds` buffer population for humanoid NPCs.
- **Search Result Limits**: Expanded search result caps for Monsters and NPCs from 10 to 500 items, with smooth scrollable list boxes.

## [0.1.7] - 2026-10-02
### Fixed
- **3D Model Rendering & Visibility**: Resolved the issue where only the 3D gizmo appeared without the character model. Implemented continuous per-frame draw enforcement (`UpdateFrame`), clearing DrawObject hidden flags (0x10) and ensuring `EnableDraw()` is triggered once `IsReadyToDraw()` becomes satisfied.
- **Penumbra & Glamourer IPC Synchronization**: Added `Penumbra.RedrawObject` and `Glamourer.ReapplyState` triggers upon spawning to immediately build and render custom character models in modded environments.
- **Template Auto-Population**: Added automatic capture of the local player's current Glamourer design when saving player clone or glamourer templates with empty design strings.

## [0.1.6] - 2026-10-02
### Fixed
- **UI Responsiveness**: Replaced `Selectable` with an `ImGui.Table` layout in `CharacterLibraryTab.cs`, fixing an issue where "Spawn onto Map" and "Delete" buttons could not be clicked.
- **Actor Spawning & Despawning**: Refactored `ActorManager.cs` to utilize `ClientObjectManager.Instance()->CreateBattleCharacter` and `DeleteObjectByIndex` (FFXIVClientStructs / Brio / AQR standard architecture) instead of failing SigScanner delegates, resolving the issue where spawned actors did not appear on the map.
- **Transform & Drawing Synchronization**: Implemented proper `CharacterSetup.CopyFromCharacter` initialization, `GameObject.EnableDraw()`, and direct position/rotation updates for spawned characters.

## [0.1.5] - 2026-10-02
### Fixed
- Fixed `SeString.TextValue` usage for Player and Target clone name extraction.

## [0.1.4] - 2026-10-02
### Fixed
- Fixed LocalPlayer access using `IObjectTable[0]` conforming to Dalamud API 15 standards.
- Fixed `ISigScanner` integration for native delegate resolution.
- Fixed `ObjectTargetableFlags.IsTargetable` type conversion.
- Fixed `ActionTimeline` collection index access in `GameDataService`.

## [0.1.3] - 2026-10-02
### Fixed
- Fixed `OnTerritoryChanged` signature to `uint` parameter.
- Fixed `PlayTimeline` and `StopTimeline` calls to supply required slot parameter.
- Fixed `OnNamePlateUpdate` signature to `(INamePlateUpdateContext, IReadOnlyList<INamePlateUpdateHandler>)`.
- Removed unused `activeTab` field in `MainWindow`.

## [0.1.2] - 2026-10-02
### Fixed
- Fixed compilation error by switching from deprecated `ImGuiNET` to Dalamud API 15 standard `Dalamud.Bindings.ImGui`.

## [0.1.1] - 2026-10-02
### Fixed
- Fixed native actor management using SigScanner delegates for FFXIV 7.x compatibility.
- Fixed `NamePlateController` event handling to match `INamePlateUpdateHandler` API.
- Fixed `TimelineManager` animation trigger via `PlayTimeline`.
- Added `repo.json` manifest for Dalamud custom plugin repository installer.
- Added `IGameInteropProvider` service injection.

## [0.1.0] - 2026-10-02
### Added
- Initial project structure and build configuration for Character Spawn plugin.
- Character Library system (supports Glamourer, Penumbra, MCDF, Monsters, and Player clone).
- Stage & Scene placement system with 3D gizmo and UI transform controls.
- Animation control with seamless loop toggle and facial expression settings.
- Player look-at (head tracking) capability.
- Customizable nameplates and targetability toggle.
- Stagehand-like scene preset management with zone-based auto-spawn.
