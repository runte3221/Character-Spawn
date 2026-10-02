# 実装計画: 外見適用およびモデルスポーンの根本改修 (v0.1.12)

## 1. 課題と根本原因の特定

### A. MCDF が読み込めず自キャラ化する
- **原因**: 従来のコードは MCDF を標準の ZIP アーカイブとして解凍しようとしていた。しかし、Mare Content Delivery File (MCDF) は先頭に `"MCDF"` マジックバイト、バージョン、データ長を持つ独自バイナリファイルであり、内部に平文 UTF-8 JSON が格納されている。ZIP としてパースした結果例外で即座に失敗し、デザイン文字列が空となり自キャラのままになっていた。

### B. Penumbra Collection が未反映となる
- **原因**: Dalamud ログの解析により、`Penumbra.SetCollectionForObject.V5` の戻り値型が `(PenumbraApiEc, Guid)`（C# の `ValueTuple<int, Guid>`）であるのに対し、購読側が `int` を期待していたため、CallGate 呼び出し時に型変換例外が発生し、失敗していた。

### C. なぜ Ruma だけ途中から反映され、他のキャラは自キャラのままだったのか
- **原因 1**: `ActorManager.SpawnCharacter` でアクター作成直後、自キャラからダブルコピーを行い、描画を有効にしたまま `readyJobs` で `DrawObject->IsVisible` を待っていた。そのため、可視化されるまでの数秒間は画面に自キャラが表示されていた。
- **原因 2**: Ruma は Glamourer のデザインのみを使用していたため、数秒後の可視化完了時に `ApplyDesign` が成功して「途中から反映」された。しかし Penumbra Collection は例外で落ち、MCDF はパース例外で空になっていたため、他は自キャラのまま残っていた。

### D. モンスター・非人型 NPC（モーグリ等）が「ギズモのみ」になる
- **原因**: HDM の逆アセンブル解析の結果、モンスターモデル（`ModelCharaId > 0`）に対して Penumbra の `RedrawObject` を呼ぶと、Penumbra が非人型アクターの DrawObject を無効化・破棄してしまうことが判明した。HDM では Penumbra Redraw を呼ばず、ネイティブの描画切り替えのみを行っている。

---

## 2. アーキテクチャ改修方針

### 1. MCDF バイナリパーサーの全面刷新 (`Services/McdfParser.cs`)
- ファイル先頭をスキャンして `"MCDF"` シグネチャを検出。
- 続くバージョン (1 byte) およびデータ長 (int32) を取得し、UTF-8 JSON を直接デコード。
- JSON 内の `"GlamourerData"` プロパティから Base64 外見文字列を確実に抽出。

### 2. Penumbra IPC のタプル対応 (`Services/PenumbraIpc.cs`)
- `ICallGateSubscriber<int, Guid, bool, bool, (int, Guid)>` を最優先で購読。
- 旧バージョンや string 引数への多段フォールバックチェーンを実装し、どのような環境でも確実にコレクションを適用。

### 3. 初期非表示 & 即時外見適用 (`Managers/ActorManager.cs`)
- スポーン直後に直ちに `nativeChara->GameObject.DisableDraw()` を呼び出す。
- 自キャラからの骨格構築後、描画が有効化される前に直ちに `ApplyAppearanceDirect` で外見（Penumbra Collection, Glamourer Design, MCDF, または Monster ModelCharaId）を設定。
- `readyJobs` では `IsReadyToDraw()` を待って `EnableDraw()` を呼ぶ。描画される最初の1フレーム目から目的の見た目で表示され、自キャラの露出が完全にゼロになる。

### 4. HDM 準拠のモンスター描画 (`Managers/ActorManager.cs`)
- `ModelCharaId > 0` の場合は Penumbra Redraw を絶対に呼ばない。
- `ModelCharaId` の代入と武器非表示設定後、ネイティブ描画サイクル (`DisableDraw` -> `IsReadyToDraw()` -> `EnableDraw()`) だけで描画を完結させる。

### 5. UI 境界線のフラット化 (`UI/CharacterLibraryTab.cs`)
- 左ペインのスクロール領域で `border: false` を指定し、キャラ選択時の横線アーティファクトを根絶。

---

## 3. 検証・品質保証
- バイナリ MCDF ファイルのパース検証
- Penumbra IPC のタプル通信確認
- スポーン初期フレームでの自キャラ露出ゼロ化
- レターモーグリ、ルーインランナー、アンテロープ、Glamourer キャラクターの描画確認
