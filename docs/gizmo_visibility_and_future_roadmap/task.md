# タスクリスト: 開発ロードマップ更新 ＆ ギズモ表示連動（SceneEditWindow 閉鎖時非表示）実装

## 開発ロードマップ

- [x] **Phase 0: コア機能・スケール・Penumbra 安全化 (完了)**
  - [x] v0.1.65.0: スケール（Scale）基本操作＆3D ギズモ配線
  - [x] v0.1.66.0: リアルタイム描画連動、モンスター初期サイズ継承、Default Scale ボタン、ギズモ操作分離
  - [x] v0.1.67.0: 人型モデルの毎フレームスケール強制リセット防止（Scale Enforcement）
  - [x] v0.1.68.0: Penumbra 個別設定リスト（Individual Assignments）汚染防止＆自キャラ保護

- [x] **Phase 1: ギズモ表示制御の洗練 (即時実装対象: v0.1.69.0)**
  - [x] `Plugin.cs`: `SceneEditWindow` が閉じている場合は 3D ギズモの全画面描画を自動停止（非表示）にする判定ロジックの実装
  - [x] 実機検証およびリリース

- [ ] **Phase 2: ミニオン・マウント（Companion / Mount）のカスタムキャラクター登録対応**
  - [ ] `GameDataService`: Lumina の `Companion` シートおよび `Mount` シートから名称、アイコン、`ModelCharaId` の検索・取得ロジック実装
  - [ ] `CharacterModels.cs`: `CharacterSourceType` に `Companion` / `Mount`（または `MountMinion`）を追加
  - [ ] `CharacterLibraryTab.cs`: カスタムキャラクター登録モーダルに「Mount / Minion」タブ（5つ目）を新設
  - [ ] スポーン・配置・スケール・アニメーションの既存パイプライン（モンスター共通）連携

- [ ] **Phase 3: Embedded Modpack（内包Modパック）実装**
  - [ ] シーン定義・テンプレートに「埋め込みModパック（ファイル置換定義）」を内包するデータ構造の追加
  - [ ] `PenumbraIpc`: MCDF パイプラインをベースに、モンスターや特定キャラ向けに独立した一時コレクション（Temporary Collection）を作成・注入
  - [ ] 同一エモート（例: もみ手）に対するキャラクター別の異なるアニメーション MOD（ダンス A / ダンス B）の個別割り当て検証

- [ ] **Phase 4: Brio ポーズ（`.pose`）の読み込み・配置固定 (Idle) およびモーション連動**
  - [ ] Brio の `.pose` ファイル（MessagePack / JSON）パーサーの実装
  - [ ] 通常フィールド上での Havok ボーン姿勢固定（Freeze / Idle）機能の実装
  - [ ] ボーンフィルター（特定部位のみポーズ固定 ＋ 他部位通常モーション再生）の部分ブレンド機能の検討・実装
