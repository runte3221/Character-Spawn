# 修正内容の確認 (Walkthrough): 外見適用・モデルスポーン・UI修正 (v0.1.10)

## 実施した変更内容

### 1. Glamourer & Penumbra 選択時の外見適用（自キャラ化解消）
- **原因の特定**:
  - A Quest Reborn (AQR) のバイナリおよび IL コードを解析した結果、Glamourer の `ApplyDesign` / `ApplyState` の第4引数フラグに `7`（`Customization | Equipment | Accessories` = 0x7）を渡していることが判明。これまでのコードでは `0` を渡していたため、外見適用処理がすべて無効化され自キャラのままになっていました。
- **改修内容**:
  - `GlamourerIpc.cs`: `ApplyDesign` / `ApplyState` / `ReapplyState` の呼び出しにおいて、`flags = 7` (0x7UL / 0x7U) を渡すように修正。
  - 名前から GUID への自動検索・解決機能を追加し、UIで名前選択・GUID選択のどちらでも確実に適用できるように対応。
  - `PenumbraIpc.cs`: コレクション設定の引数を `allowCreate = true, allowDelete = true` に更新。
  - `ActorManager.cs`: アクターの生成スロット（`SlotIndex`）を確実に IPC に渡し、Penumbra コレクション設定 -> Glamourer デザイン適用 -> Penumbra `RedrawObject` の順序で実行。

### 2. MCDF ファイル選択時の外見適用
- MCDF ファイルから抽出した Base64 文字列を `flags = 7` を指定して Glamourer に渡し、Penumbra Redraw を連動させることで、自キャラ化することなく MCDF 内の外見がアクターに確実に反映されます。

### 3. モンスター・非人型NPCの描画復旧（ギズモのみ表示の解消）
- **原因の特定**:
  - AQR の実装では、人型からのコピーは行わず、`chara->ModelContainer.ModelCharaId = template.ModelCharaId;` を設定して武器を隠蔽した後、**Penumbra の `RedrawObject`** を呼ぶことでゲームエンジン側がモンスターモデルを自動生成・ロードしていました。自前で `CopyFromCharacter` を呼んでいたことがモデル描画破損の原因でした。
- **改修内容**:
  - モンスター（ルーインランナー、アンテロープ・ドゥ、ナット等）および非人型NPC（レターモーグリ等）において、自前での再構築を廃止し、AQR 同様に `ModelCharaId` 設定 ＋ 武器隠蔽 ＋ Penumbra `RedrawObject` による確実な描画復旧を実装。

### 4. NPC (ENpc) 人型モデルの武器問題解消
- 人型NPC（ミューヌ、ル・スーシモ等）のテンプレート作成時、デフォルトで `WeaponVisible = false` に設定。自キャラからベースコピーした武器が表示されてしまう現象を防止。

### 5. 武器表示 ON/OFF 機能の確実化
- `ActorManager.SetWeaponVisibility` メソッドを新設。チェックボックス切り替え時に `HideWeapons` フラグを更新し、Penumbra `RedrawObject` を実行することで、OFF だけでなく ON の再描画も即座に反映されるようにしました。

### 6. プルダウンのソート
- `UI/CharacterLibraryTab.cs`: Glamourer Design および Penumbra Collection のドロップダウンコンボボックスで、五十音順・アルファベット順（`OrderBy(..., StringComparer.OrdinalIgnoreCase)`）にソートして表示。

### 7. UI 横線突き抜けバグの解消 & 説明文の削除
- 右ペイン全体を `ImGui.BeginChild("RightDetailPane", new Vector2(-1, -1), false)` で囲み、`ImGui.Separator()` が左カラムに突き抜ける問題を解決。
- 不要な説明文枠 (`PreviewExplanationBox`) および開発用補足テキスト（`<= 武器表示ON/OFF`、`<= ギズモ表示ON/OFF`）を削除。

---

## 変更ファイル一覧
- `Services/GlamourerIpc.cs`: `flags = 7` 指定、GUID/名前解決、オーバーロード対応
- `Services/PenumbraIpc.cs`: `allowDelete = true`、RedrawObject 連携
- `Models/CharacterModels.cs`: `SpawnedActorData.SlotIndex` プロパティの追加
- `Managers/ActorManager.cs`: AQR準拠の非人型モデル初期化、外見適用フロー、`SetWeaponVisibility`
- `UI/CharacterLibraryTab.cs`: 右ペインの `BeginChild` 化、不要説明文削除、プルダウンソート、NPC武器デフォルト非表示
- `package.json`: バージョン更新 (0.1.10)
- `CharacterSpawn.json`: バージョン更新 (0.1.10.0)
- `CharacterSpawn.csproj`: バージョン更新 (0.1.10.0)
- `repo.json`: バージョン更新 (0.1.10.0)
- `CHANGELOG.md`: リリースノート追記
