# 修正内容の確認 (Walkthrough): アクション・モーション再生基盤 ＆ 視線追従・表情固定 (`v0.1.71.0`)

## 実装・修正のハイライト

### 1. アニメーション制御基盤の実装 (`Services/AnimationService.cs`)
- HDM 準拠のネイティブ制御パイプラインを構築。
- `PlayTimeline` と `BaseOverride` による安定したシームレスループ機構。
- `OverallSpeed` によるスロー再生・高速再生（0.1x〜3.0x）のサポート。
- `SetTargetId` と方位角補間によるプレイヤー接近時のスムーズな視線追従。
- `SetRotation` を用いた型安全な回転処理。

### 2. シーン連動 (`Managers/SceneManager.cs`, `Plugin.cs`)
- シーンスポーン時に各アクターの設定済みモーション（`placement.Motion`）を自動適用。
- シーン破棄時・個別非表示時に安全にデフォルト待機状態へ初期化。

### 3. Scene Edit UI のモーションエディタ実装 (`UI/SceneEditWindow.cs`)
- 「Animation」タブにて ActionTimeline / エモートの検索、ループ、速度、表情、視線追従のGUI操作を提供。
- パラメータ変更のリアルタイム実機反映とシーンデータ自動保存。

### 4. ビルド・リリースパイプライン検証
- `SceneEditWindow.cs` の namespace 欠落（CS0246）および `AnimationService.cs` の回転メソッド呼び出しを修正。
- GitHub Actions CI/CD ビルド成功、および `v0.1.71.0` リリースを確認。
