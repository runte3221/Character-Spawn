# 実装計画: ポージング・モーション ＆ 自律移動 AI 統合

## 1. 目的
Character Spawn の第2工程（根幹機能）として、配置したカスタムキャラクターに対してゲーム内ネイティブのアクションモーション、表情、視線追従を適用し、生きたNPCのように振る舞わせる基盤を確立する。

## 2. アーキテクチャ設計

### 2.1 アニメーション制御基盤 (`Services/AnimationService.cs`)
- **ActionTimeline の再生**:
  - `FFXIVClientStructs.FFXIV.Client.Game.Character.Character` ポインタを通じて `PlayTimeline(ushort timelineId, ushort slot)` を実行。
  - ループ維持のため、`Timeline.BaseOverride` に `timelineId` を指定。これにより待機モーションとしてエンジン内部で定着。
  - `Timeline.OverallSpeed` による再生速度の動的スケーリング。
  - 表情固定は表情用 ActionTimeline（`fac_` プレフィックス）をタイムラインスロットに流し込むことで実現。
- **視線・頭部追従 (LookAt Player)**:
  - プレイヤー接近時、`SetTargetId(localPlayer.EntityId)` を呼び出し。
  - アクターからプレイヤーへのベクトルを計算し、`SetRotation` で滑らかに Yaw 角を補間。
  - プレイヤーが追従可能範囲（デフォルト8m）を外れた場合、元の初期回転角度へ自動復帰。
- **シーン連動**:
  - `SceneManager.SpawnSceneAsync` 時に各配置アクターの `placement.Motion` を `ApplyMotion`。
  - シーンデスポーン時には `StopMotion` を呼び出し、`BaseOverride = 0`, `OverallSpeed = 1.0f`, `StopTimeline(0)`, `PlayTimeline(1, 0)` でデフォルト待機状態へ初期化。

### 2.2 UI 設計 (`UI/SceneEditWindow.cs`)
- Scene Edit ウィンドウの第3タブ「Animation」を本格実装。
- `GameDataService.SearchTimelines` を用いた約1万件の ActionTimeline インクリメンタル検索。
- スライダー（再生速度: 0.1x〜3.0x）、チェックボックス（ループ、視線追従）、コンボボックス（表情選択）。
- 設定値変更時は即座に `ApplyMotion` を呼び出して実機プレビューを更新し、シーンデータへ自動永続化。

## 3. 検証・品質計画
- GitHub Actions による .NET 10 x64 ビルド検証
- コンパイルエラー解消確認
- `latest.zip` リリースおよび CDN 配布検証
