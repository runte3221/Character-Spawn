# 実装計画: 外見適用・モデルスポーン・UI修正 (v0.1.10)

## 概要
ユーザーからの最新フィードバックに基づき、スポーン時の自キャラ化（Glamourer / Penumbra / MCDF）、モンスターおよび非人型NPC（レターモーグリ等）のギズモのみ表示バグ、武器表示ON/OFF、プルダウンの未ソート、不要テキストおよび横線UIバグを解消する。
特にモンスター、MCDF、Penumbraのスポーンに関しては、ユーザーの指定通り動作実績のあるプラグイン「A Quest Reborn (AQR)」の実装を解析し、そのアーキテクチャに準拠した改修を行う。

## 主要な問題点と原因
1. **Glamourer / MCDF 選択時に自キャラの外見でスポーンする**:
   - `Glamourer.ApplyDesign` / `ApplyState` の IPC 呼び出しにおいて、第4引数フラグに `0`（フラグなし）を指定していたため、Glamourer 側で外見・装備・アクセサリの適用がすべてスキップされていた。
   - AQR の解析により、第4引数に `flags = 7` (0x7 = `Customization | Equipment | Accessories`) を指定する必要があることが判明。
2. **モンスター・非人型NPC（レターモーグリ、ルーインランナー、ナット等）がギズモのみになる**:
   - `CreateBattleCharacter` 後に自前で `CopyFromCharacter` を呼んでいたことで、非人型モデルのスケルトンやリソースコンテナが破損していた。
   - AQR では `ModelContainer.ModelCharaId` を代入し武器を隠蔽した後、**Penumbra の `RedrawObject`** を呼ぶだけでゲームエンジン側がモンスターモデルを正しく生成・描画していることが判明。
3. **NPC スポーン時に自キャラの武器が表示される**:
   - NPC テンプレート作成時に `WeaponVisible` の初期値が `true` のままになっており、ベースの人型コピーから自キャラ武器が残っていた。
4. **武器表示 ON/OFF**:
   - OFF は機能するが ON 時にモデルの再読み込みが行われていなかった。Penumbra `RedrawObject` を連動させることで即座に反映可能。
5. **UI の横線突き抜けバグ**:
   - `ImGui.Columns(2)` の右ペインで `ImGui.Separator()` を呼ぶと左カラムを横断して描画される。右ペインを `ImGui.BeginChild` でカプセル化することで解決。

## 実装手順
1. **`Services/GlamourerIpc.cs`**:
   - `ApplyDesign` / `ApplyState` / `ReapplyState` の呼び出しシグネチャに `flags = 7` を追加。
   - デザイン名から GUID への自動解決フォールバックを実装。
2. **`Services/PenumbraIpc.cs`**:
   - `SetCollectionForObject` の引数を AQR に倣い `allowCreate = true, allowDelete = true` に設定。
3. **`Managers/ActorManager.cs`**:
   - `SpawnCharacter` で `SlotIndex` を `SpawnedActorData` に記録。
   - 非人型（モンスター・非人型NPC）アクターは `ModelCharaId` 設定＋武器非表示＋Penumbra `RedrawObject` で初期化（自前 `CopyFromCharacter` を廃止）。
   - `ApplyExternalAppearance` の適用順序を確立（Penumbra コレクション設定 -> Glamourer デザイン適用 -> Penumbra RedrawObject）。
   - `SetWeaponVisibility` メソッドを新設し、ON / OFF 両方で Penumbra Redraw を呼び出す。
4. **`UI/CharacterLibraryTab.cs`**:
   - 右ペイン全体を `ImGui.BeginChild("RightDetailPane")` で囲む。
   - 不要な説明文（`PreviewExplanationBox`, `<= 武器表示ON/OFF`, `<= ギズモ表示ON/OFF`）を削除。
   - Glamourer デザインおよび Penumbra コレクションのコンボボックスを五十音／アルファベット順にソート。
   - NPC テンプレートの `WeaponVisible` デフォルトを `false` に設定。
5. **バージョン更新 & Git Push**:
   - `0.1.10.0` へのバンプと CHANGELOG 追記、GitHub へのプッシュ。
