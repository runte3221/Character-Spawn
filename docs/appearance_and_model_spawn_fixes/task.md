# タスクリスト: 外見適用およびモデルスポーンの根本改修 (v0.1.13)

## 概要
Glamourer design、Penumbra Collection、MCDF、NPC、モンスターのスポーン時に発生していた外見未反映（自キャラ化）およびギズモのみ表示となる不具合を、A Quest Reborn (AQR) および HDM の完全解析に基づき根本解決する。

## タスク一覧

- [x] **MCDF フォーマットの完全解明と LZ4 解凍ストリーム実装 (AQR 準拠)**
  - [x] 実ファイル (`test.mcdf`, `testruma.mcdf`) および AQR の `McdfCharaFileManager.cs` を精査し、MCDF が丸ごと LZ4 圧縮ストリームであることを解明
  - [x] `lz4net` をプロジェクトに導入し、`LZ4Stream` 解凍後に `"MCDF"` 4バイトヘッダ検出と UTF-8 JSON ペイロード (`GlamourerData`) を抽出するロジックを `Services/McdfParser.cs` に実装
  - [x] スポーン時および保存時に 1000 文字以上の Base64 外見データが正常に抽出・保持され、アクターに確実に適用されるよう接続

- [x] **Penumbra V5 IPC 正確なシグネチャの解明と型不一致例外の解消 (AQR 準拠)**
  - [x] `Penumbra.Api.dll` (v1.7.2.1) の CIL 逆アセンブル解析を実施
  - [x] `Penumbra.SetCollectionForObject.V5` が `(int actorIndex, Guid? collectionId, bool allowCreate, bool allowDelete) -> (int, (Guid, string)?)` を要求していることを完全解明
  - [x] `Services/PenumbraIpc.cs` に正確なシグネチャを実装し、戻り値の型不一致例外を解消してコレクション割り当て成功（戻り値 0）を実現

- [x] **Penumbra Collection & Glamourer 二重 Redraw フローの適用 (AQR 準拠)**
  - [x] AQR と同様に、Penumbra コレクション割り当て直後に 1回目の Redraw を実行し、Glamourer 適用後に 2回目の Redraw を実行するパイプラインを `Managers/ActorManager.cs` に整備

- [x] **HDM 準拠のモンスター・非人型 NPC スポーン修正 (ギズモのみ解消)**
  - [x] HDM (`Enceladeum/HDM`) の IL 逆アセンブル解析を実施
  - [x] モンスター（`ModelCharaId > 0`）に対して Penumbra Redraw を呼ぶと DrawObject が無効化・破棄されて「ギズモのみ」になる真因を特定
  - [x] モンスターおよび非人型 NPC では Penumbra Redraw を呼ばず、ゲームエンジンのネイティブ描画サイクル (`DisableDraw` -> `IsReadyToDraw()` -> `EnableDraw()`) で安全に描画を完了させるよう改修

- [x] **バージョン更新・ドキュメント同期・リリース**
  - [x] バージョンを `0.1.13` / `0.1.13.0` に更新 (`package.json`, `CharacterSpawn.json`, `CharacterSpawn.csproj`, `repo.json`)
  - [x] `CHANGELOG.md` に詳細を追記
  - [x] `docs/appearance_and_model_spawn_fixes` のドキュメント更新
  - [x] Git コミット & プッシュ
  - [x] ローカル環境（XIVLauncher installedPlugins）への最新成果物配置
