# 実装計画: 巡回・移動時の階段・段差自動追従および地面スナップ機能の実装

## 背景と問題の所在
1. **現状の問題**:
   - 階段や段差があるルートを移動させる際、従来の直線補間（線形補間＋固定速度上昇）では、階段の途中や斜面でアクターの足元が地面に埋もれたり、階段を下りる際に宙に浮いた状態から急落下したりしていた。
   - ユーザーが階段の段に合わせて数十個の地点（ウェイポイント）を細かく登録してもめり込みが発生し、作成や編集作業が極めて煩雑になっていた。
2. **解決のアプローチ**:
   - FFXIV クライアント内部のネイティブ地形衝突エンジンである `FFXIVClientStructs.FFXIV.Common.Component.BGCollision.BGCollisionModule` の静的メソッド `RaycastMaterialFilter` を活用。
   - アクターの進行方向座標（X, Z）の直上から真下へ向けてレイキャストを照射し、階段の踏み面や床、段差の正確な上面Y座標（ミリ単位）をリアルタイムに取得する。
   - 階段の1段（約 0.15m〜0.3m）を踏み越える瞬間、アクターの足を即座にその段の上面に接地（スナップ）させることで、階段に足が埋まる現象を100%根絶する。
   - ユーザーは「階段の下」と「階段の上」の2地点を指定するだけで、アクターが階段のステップに沿って自然に上り下りできるようになる。

## 変更計画

### 1. `Services/MovementService.cs`
- `FFXIVClientStructs.FFXIV.Common.Component.BGCollision` をインポート。
- `TryGetGroundHeight(Vector3 pos, out float groundY, float upOffset = 2.5f, float maxDistance = 6.0f)` を実装:
  - `Framework.Instance()->BGCollisionModule` 経由で真下方向の地形コリジョンを検出。
  - ヒットした地点の `hit.Point.Y` を返す。
- `StepTowardTarget`:
  - 目的地への水平移動ステップ `newPos = curPos + moveDir * moveStep;` を算出。
  - 新しい水平座標 `newPos` に対して `TryGetGroundHeight` を実行。
  - 検出された `groundY` と現在高の差分 `heightDiff` に応じて:
    - 階段の昇降（`|heightDiff| <= 0.45f`）: 即座に `newPos.Y = groundY` として接地。
    - 大きな高低差: 毎秒最大 10.0m の垂直速度で追従。
  - 地面レイキャスト非ヒット時のフォールバックも維持。
  - 停止時（`horizDist <= stopDistance`）にも地面スナップを行い、階段途中で立ち止まった場合の浮遊・埋没を防止。

### 2. `UI/SceneEditWindow.cs`
- ウェイポイント追加ボタン（「自キャラ位置を追加」「アクター位置を追加」）において、追加する座標の足元高さを `MovementService.TryGetGroundHeight` で地面に正確に合わせて登録。

### 3. バージョン更新・ドキュメント同期・CI/CD
- `tools/bump-version.ps1 0.1.88.0`
- `CHANGELOG.md` 追記
- `docs/height_adjustment_and_ground_snapping_for_patrol_path/walkthrough.md` 作成
- コミット＆プッシュ、GitHub Actions CI/CD ビルド完了確認
