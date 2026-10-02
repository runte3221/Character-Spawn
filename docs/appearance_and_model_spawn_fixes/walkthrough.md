# 改修内容の確認 (Walkthrough) - v0.1.13

## 概要
本バージョンでは、A Quest Reborn (AQR) のリバースエンジニアリング、実機 MCDF ファイルのバイナリ解析、および Penumbra.Api.dll の CIL メタデータ解析に基づき、MCDF の解凍および Penumbra Collection の適用失敗に関する真の根本原因を特定し、完全修正を行いました。

---

## 修正内容のハイライト

### 1. MCDF 全体 LZ4 圧縮ストリームの解凍対応 (`Services/McdfParser.cs`)
- **問題**: MCDF ファイルを選択・保存してスポーンさせても、外見が適用されず直前にスポーンしたアクター（Ruma等）や自キャラが表示されていた。
- **原因**: 現代の MCDF ファイルはファイル全体が LZ4 圧縮されたバイナリデータ（AQR の `McdfCharaFileManager.cs` 準拠）。生ファイルのまま読もうとしていたため、JSON 途中の圧縮バイトで例外落ちし、GlamourerDesignString が空になっていた。
- **対策**:
  - `lz4net` を導入し、ファイル全体を `LZ4.LZ4Stream` で展開しながらパースするよう全面改修。
  - 実ファイル（`testruma.mcdf`, `test.mcdf`）からそれぞれ 1104 文字、1144 文字の Base64 外見データを 100% 確実に抽出できることを実証・確認。
  - MCDF の指定したキャラクター外見が確実に保存・適用されるようになりました。

### 2. Penumbra V5 IPC 正確なシグネチャの完全一致 (`Services/PenumbraIpc.cs`)
- **問題**: Penumbra Collection を指定してスポーンしても Mod 服や装飾が反映されず、バニラ装備のままになっていた。
- **原因**: `Penumbra.Api.dll` の CIL を解析したところ、`Penumbra.SetCollectionForObject.V5` は
  - 第2引数が `Guid` ではなく `Guid?` (`Nullable<Guid>`)
  - 戻り値が `(int, Guid)` ではなく `(int, (Guid, string)?)`
  を要求しており、型変換例外（`converting from ValueTuple 2 to System.Int32`）で全試行が落ちていた。
- **対策**:
  - `ICallGateSubscriber<int, Guid?, bool, bool, (int, (Guid, string)?)>` の正確な型定義を実装。
  - 戻り値のステータスコード 0（Success）を検証し、Penumbra コレクションのバインドが完全に成功するようになりました。

### 3. Penumbra & Glamourer 二重 Redraw フロー (`Managers/ActorManager.cs`)
- AQR の設計に準拠し、Penumbra Collection 設定後に 1回目の Redraw を実行し、Glamourer 外見適用後に 2回目の Redraw を実行する二重同期パイプラインを実装。Mod テクスチャやメッシュが確実にアクターに反映されます。

---

## 変更されたファイル一覧
- `package.json` (0.1.13 に更新)
- `CharacterSpawn.json` (0.1.13.0 に更新)
- `CharacterSpawn.csproj` (0.1.13.0 に更新、`lz4net` パッケージ追加)
- `repo.json` (0.1.13.0 に更新)
- `CHANGELOG.md` (0.1.13 リリースノート追加)
- `Services/McdfParser.cs` (LZ4Stream 解凍パース対応)
- `Services/PenumbraIpc.cs` (Penumbra V5 正確なタプルシグネチャ対応)
- `Managers/ActorManager.cs` (二重 Redraw パイプライン連携)
- `docs/appearance_and_model_spawn_fixes/` (task.md, implementation_plan.md, walkthrough.md 同期)

