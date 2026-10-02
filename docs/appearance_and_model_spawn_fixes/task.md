# タスクリスト: 外見適用・モデルスポーン・UI修正 (v0.1.10)

## 完了したタスク
- [x] **Glamourer / Penumbra プルダウンのソート**
  - Glamourer デザインおよび Penumbra コレクションのコンボボックスをアルファベット／五十音順（`StringComparer.OrdinalIgnoreCase`）でソート表示
- [x] **Glamourer / Penumbra 選択時の自キャラ化解消**
  - AQuestReborn (AQR) の実装解析に基づき、Glamourer `ApplyDesign` / `ApplyState` の適用フラグを `0` から `flags = 7` (0x7: Customization | Equipment | Accessories 全適用) に修正
  - デザイン名から GUID への自動解決処理を追加
  - Penumbra コレクション適用 (`allowCreate = true, allowDelete = true`) -> Glamourer デザイン適用 -> Penumbra RedrawObject の適用順序を確立
- [x] **MCDF 選択時の自キャラ化解消**
  - MCDF の Base64 外見データを `flags = 7` で Glamourer に渡し、Penumbra RedrawObject を連動させて確実に適用
- [x] **NPC (ENpc) レターモーグリ等の非人型モデル描画および武器問題の解消**
  - レターモーグリ等の非人型モデル（`ModelCharaId > 0`）を AQR 方式（`ModelContainer.ModelCharaId` 設定 ＋ 武器非表示 ＋ Penumbra RedrawObject）で正常描画
  - 人型NPC（ミューヌ、ル・スーシモ等）のテンプレートデフォルトで武器を非表示 (`WeaponVisible = false`) にし、自キャラ武器が表示される問題を解消
- [x] **武器表示 ON/OFF 機能の修正**
  - `SetWeaponVisibility` メソッドを新設し、ON / OFF 切り替え時に `CopyFromCharacter` および Penumbra `RedrawObject` を即時トリガー
- [x] **UI の不要な説明文の削除**
  - プレビュー機能説明枠 (`PreviewExplanationBox`) を削除
  - `<= 武器表示ON/OFF`、`<= ギズモ表示ON/OFF` などの開発用補足テキストを削除
- [x] **Monster/Mob 描画復旧（ルーインランナー、アンテロープ・ドゥ、ナット等）**
  - 自前での人型 `CopyFromCharacter` を廃止し、AQR 同様の `ModelContainer.ModelCharaId` ＋ 武器非表示 ＋ Penumbra RedrawObject による初期化・描画復旧
- [x] **左メニュー側に横線が入る UI バグの解消**
  - 右ペイン全体を `ImGui.BeginChild("RightDetailPane")` で囲み、`ImGui.Separator()` が左カラムに突き抜ける問題を解決
- [x] **バージョン更新 (0.1.10.0) & CHANGELOG 追記 & Git Push**
  - `package.json`, `CharacterSpawn.json`, `CharacterSpawn.csproj`, `repo.json`, `CHANGELOG.md` を更新
