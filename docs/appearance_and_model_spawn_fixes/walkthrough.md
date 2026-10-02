# 改修内容の確認 (Walkthrough) - v0.1.12

## 概要
本バージョンでは、A Quest Reborn (AQR) および HDM のリバースエンジニアリングと Dalamud ログの詳細解析に基づき、Glamourer・Penumbra・MCDF・モンスター・NPC のスポーンおよび外見適用における根本原因を特定し、完全修正を行いました。

---

## 修正内容のハイライト

### 1. MCDF の独自バイナリパース対応 (`Services/McdfParser.cs`)
- **問題**: MCDF ファイルを選択してスポーンしても自キャラのままになり、読み込みに時間がかかる気配がなかった。
- **原因**: 従来のコードが ZIP アーカイブとして解凍しようとして即座に例外落ちしていた。実際の MCDF は独自バイナリファイル。
- **対策**:
  - 先頭 `"MCDF"` マジックバイトを検出するバイナリスキャナを実装。
  - バージョンとペイロード長を読み取り、平文 UTF-8 JSON から `GlamourerData`（Base64 外見文字列）を正確に抽出。
  - MCDF ファイルから確実に外見が読み込まれ、Glamourer に正しく渡されるようになりました。

### 2. Penumbra IPC のタプル戻り値対応 (`Services/PenumbraIpc.cs`)
- **問題**: Penumbra Collection を選択しても反映されず、自キャラのテクスチャ/Mod のままになっていた。
- **原因**: Dalamud ログの記録により、`Penumbra.SetCollectionForObject.V5` が `(PenumbraApiEc, Guid)` (`ValueTuple<int, Guid>`) を返しているのに対し、プラグイン側が `int` で受け取ろうとして型変換例外で失敗していた。
- **対策**:
  - 戻り値型を `(int, Guid)` として購読する Subscriber を最優先に配置。
  - レガシーな `int` や string 引数への多段フォールバックチェーンを実装し、Penumbra Collection の設定が 100% 成功するようになりました。

### 3. 初期フレームからの自キャラ露出ゼロ化 (`Managers/ActorManager.cs`)
- **問題**: スポーン直後に自キャラが表示され、数秒後に Ruma に切り替わっていた（途中から反映）。
- **原因**: アクター生成直後に自キャラからコピーし、描画を有効にしたまま `DrawObject->IsVisible` を待ってから外見を適用していたため。
- **対策**:
  - スポーン直後に直ちに `DisableDraw()` を実行し、描画を遮断。
  - 描画が開始される前に、直ちに Penumbra Collection、Glamourer Design、MCDF、または Monster ModelCharaId を適用。
  - `IsReadyToDraw()` を確認してから `EnableDraw()` を呼び出すことで、**描画される最初の1フレーム目から目的の外見で出現**するようになり、自キャラが立ってしまう現象を根絶しました。

### 4. HDM 準拠のモンスター・非人型 NPC スポーン (`Managers/ActorManager.cs`)
- **問題**: レターモーグリ、ルーインランナー、アンテロープ、ナット等をスポーンさせるとギズモのみが表示され、モデルが消えてしまっていた。
- **原因**: モンスターモデル（`ModelCharaId > 0`）に対して Penumbra の `RedrawObject` を呼んでいたため、Penumbra が非人型 DrawObject を無効化・破棄していた。
- **対策**:
  - HDM の `GuiseService` と同様に、モンスターモデルには Penumbra Redraw を絶対に呼ばず、ネイティブ描画サイクル (`DisableDraw` -> `IsReadyToDraw()` -> `EnableDraw()`) で完結させました。
  - レターモーグリや全モンスターが確実に 3D モデルとして出現するようになりました。

### 5. UI のフラット化と横線アーティファクトの解消 (`UI/CharacterLibraryTab.cs`)
- 左ペインのスクロール枠（`LibraryTreeScroll`）の境界線を非表示にし、キャラ選択時に不要な横線が入る視覚的な不具合を解消しました。

---

## 変更されたファイル一覧
- `package.json` (0.1.12 に更新)
- `CharacterSpawn.json` (0.1.12.0 に更新)
- `CharacterSpawn.csproj` (0.1.12.0 に更新)
- `repo.json` (0.1.12.0 に更新)
- `CHANGELOG.md` (0.1.12 リリースノート追加)
- `Services/McdfParser.cs` (バイナリ MCDF 解析対応)
- `Services/PenumbraIpc.cs` (タプル対応 Subscriber 追加)
- `Managers/ActorManager.cs` (事前外見適用・自キャラ露出防止・HDM準拠モンスター描画)
- `UI/CharacterLibraryTab.cs` (左ペイン境界線フラット化)
- `docs/appearance_and_model_spawn_fixes/` (task.md, implementation_plan.md, walkthrough.md 同期)
