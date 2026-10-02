# タスクリスト: 外見適用およびモデルスポーンの根本改修 (v0.1.12)

## 概要
Glamourer design、Penumbra Collection、MCDF、NPC、モンスターのスポーン時に発生していた外見未反映（自キャラ化）およびギズモのみ表示となる不具合を、A Quest Reborn (AQR) および HDM の完全解析に基づき根本解決する。

## タスク一覧

- [x] **MCDF フォーマットの完全解析とバイナリパース実装 (AQR 準拠)**
  - [x] 実ファイル (`test.mcdf`, `testruma.mcdf`) をバイナリ解析し、MCDF が ZIP ではなく独自バイナリ構造であることを解明
  - [x] `"MCDF"` 4バイトヘッダ検出と UTF-8 JSON ペイロード (`GlamourerData`) の抽出ロジックを `Services/McdfParser.cs` に実装
  - [x] MCDF 選択時およびスポーン時に Base64 外見文字列が Glamourer に正しく渡されるよう接続

- [x] **Penumbra IPC タプル戻り値の不一致解消 (AQR 準拠)**
  - [x] Dalamud ログの解析により `Penumbra.SetCollectionForObject.V5` が `ValueTuple<int, Guid>` を返しているのに `int` で購読し例外落ちしていた真因を特定
  - [x] `Services/PenumbraIpc.cs` にタプル対応 subscriber およびフォールバックチェーンを実装

- [x] **スポーン直後の即時外見適用による自キャラ露出ゼロ化**
  - [x] 初期生成時に自キャラのダブルコピー後、`readyJobs` で可視化を待っていたため自キャラが画面に露出していた原因を特定
  - [x] スポーン直後に直ちに `DisableDraw()` を呼び、描画有効化前に Penumbra Collection / Glamourer Design / MCDF / NPC 外見をアクターに直接適用するアーキテクチャに改修
  - [x] `IsReadyToDraw()` を確認してから `EnableDraw()` を呼び出すことで、最初の1フレーム目から目的の外見で描画されるよう修正

- [x] **HDM 準拠のモンスター・非人型 NPC スポーン修正 (ギズモのみ解消)**
  - [x] HDM (`Enceladeum/HDM`) の IL 逆アセンブル解析を実施
  - [x] モンスター（`ModelCharaId > 0`）に対して Penumbra Redraw を呼ぶと DrawObject が無効化・破棄されて「ギズモのみ」になる真因を特定
  - [x] モンスターおよび非人型 NPC では Penumbra Redraw を呼ばず、ゲームエンジンのネイティブ描画サイクル (`DisableDraw` -> `IsReadyToDraw()` -> `EnableDraw()`) で安全に描画を完了させるよう改修

- [x] **UI のブラッシュアップ**
  - [x] 左ペインの境界線をフラット化し、キャラ選択時の横線アーティファクトを解消
  - [x] 不要な開発用補足説明文の完全削除を確認

- [x] **バージョン更新・ドキュメント同期・リリース**
  - [x] バージョンを `0.1.12` / `0.1.12.0` に更新 (`package.json`, `CharacterSpawn.json`, `CharacterSpawn.csproj`, `repo.json`)
  - [x] `CHANGELOG.md` に詳細を追記
  - [x] `docs/appearance_and_model_spawn_fixes` のドキュメント更新
  - [x] Git コミット & プッシュ
  - [x] ローカル環境（XIVLauncher installedPlugins）への最新成果物配置
