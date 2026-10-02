# 修正内容の確認 (Walkthrough): v0.1.15 MCDF完全自動Mod適用

## 主な変更点

### 1. MCDF内包Mod自動展開とPenumbra一時コレクションの連携 (`McdfParser.cs`, `PenumbraIpc.cs`, `ActorManager.cs`)
- **A Quest Reborn完全準拠のアーキテクチャ**:
  - MCDF内に含まれる3Dモデル、テクスチャ、マテリアルファイル群を `mcdf_cache` フォルダへ書き出し。
  - Penumbra IPC `CreateTemporaryCollection` でスポーンアクター専用の一時コレクションを動的作成。
  - `AssignTemporaryCollection` でスポーンアクターに強制割り当て。
  - `AddTemporaryMod` で展開された全Modファイルとメタマニピュレーション（ManipulationData）を登録。
  - Glamourerで外見デザインを適用し、Penumbra Redrawを実行。
  - これにより、ユーザーのPenumbraに事前にModをインストールしていなくても、他ユーザーから受け取ったMCDFファイルを100%忠実にスポーン・表示可能になりました。

### 2. リソース管理の安全化
- アクターのデスポーン時（`DespawnCharacter`）やエリア移動時に、割り当てられていた一時コレクションを `DeleteTemporaryCollection` でPenumbraから自動削除。メモリやIPCリソースの解放を保証。

### 3. UIの改善 (`CharacterLibraryTab.cs`)
- MCDF選択時にPenumbraコレクションの選択項目を非表示化。
- 代わりに「MCDF内包のModファイルを自動展開し、Penumbra一時コレクションとしてアクターに自動割り当てする（AQR / Mare準拠）」旨の案内テキストを表示。

### 4. バージョン更新および起動ログの動的解決 (`Plugin.cs`, `CharacterSpawn.csproj` 等)
- ハードコードされていた起動ログの `v0.1.9` を動的なアセンブリバージョン取得に変更。
- プラグインバージョンを `0.1.15` / `0.1.15.0` に更新。
