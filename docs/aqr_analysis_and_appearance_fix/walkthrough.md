# ウォークスルー: AQR/HDM完全分離と独立パイプライン構築

## 1. 検討の経緯とツールの最終目標

本ツールは単なるキャラクター表示ツールにとどまらず、**「通常ワールド（非GPose）において、自キャラの動作を一切妨害せず、完全に独立したローカルキャラクター（PC/NPC/モンスター）を配置し、アニメーション・視線・表情・サウンドを伴うステージ演出（シーン）を構築する」** ことを最終目標としています。

### 参照基盤の役割分担
* **AQR (AQuestReborn)**:
  - Glamourer ＆ Penumbra ＆ Customize+ によるオリジナルPC/パペット
  - MCDF（ModPack外見データ）の読み込みと適用
* **HDM (Housing Decorator / Doll Master)**:
  - NPC (人型 ENpc) の外見適用 (HumanGuise)
  - Monster / MOB (非人型モデル) のネイティブ描画切り替え (GuiseService)
* **AQR ＆ HDM**:
  - 配置記録、アニメーション、視線追従、ネームプレート、サウンドによるシーン構築

---

## 2. 徹底分析：AQR 修正案と HDM 側の競合リスク

これまでの実装では、AQR の処理（MCDF/Glamourer）と HDM の処理（NPC/モンスター）が単一のメソッドに混ざり合っていたため、以下の致命的な競合が発生していました：

1. **描画停止 (`DisableDraw`) の衝突**:
   HDM ではモンスター切り替えに必須だが、AQR系では DrawObject の構築完了を阻害し、Glamourer が ObjectIndex 200 の認識に失敗して LocalPlayer（Index 0）に変身を誤爆させる原因になっていた。
2. **待機ポーリングの衝突**:
   HDM ではネイティブ描画完了待ちが必要だが、AQR系では即時適用すべきところを遅延させたためレースコンディションが発生した。
3. **Penumbra Redraw の衝突**:
   AQR系には必須だが、モンスターアクターに対して呼ぶと DrawObject が破棄されて非人型モデルが消滅してしまう。
4. **メモリ改変の衝突**:
   `ObjectKind = BattleNpc` や `OwnerId = 0xE000_0000` を強制設定したため、全パイプラインでアクター識別エンジンが混乱した。

---

## 3. 採用する独立アーキテクチャ（4系統完全分離）

混同を永久に避けるため、キャラクターの `SourceType` に応じた **4つの完全独立パイプライン** を構築します：

1. **Pipeline A (AQR - Glamourer/Penumbra/Customize+)**:
   - 素の BattleCharacter 生成
   - スポーン直後にその場で Penumbra（Guid指定）＋ RedrawObject ＋ Glamourer（Guid指定）を直列即時実行。
   - `readyJobs` による遅延待機は一切スキップ。
2. **Pipeline B (AQR - MCDF)**:
   - 素の BattleCharacter 生成
   - MCDF を展開し、一時コレクションを割り当て、内包 Base64 データを無加工で `ApplyState` に渡す。直後に `RedrawObject`。
   - 遅延待機なしで即座に完了。
3. **Pipeline C (HDM - 人型 NPC)**:
   - 素の BattleCharacter 生成
   - HDM HumanGuise 方式により、26バイト CustomizeData と 10スロット EquipmentModelIds を JObject に注入。
   - 自キャラの肌色・パラメータ（Parameters/Materials）は Strip し、コールドスポーン時の RevertToGameBase はスキップして適用。
4. **Pipeline D (HDM - Monster / MOB)**:
   - 素の BattleCharacter 生成
   - `ModelCharaId` と `Scale` を設定し、武器非表示、ネイティブ 2フェーズ描画（DisableDraw -> IsReadyToDraw -> EnableDraw）。
   - **Glamourer や Penumbra Redraw は一切呼ばない**。

---

## 4. 今回の実装完了内容 (`0.1.42.0`)
- **ActorManager.cs の純化**:
  - 不要なメモリ書き換え (`ObjectKind`, `BattleNpcSubKind`, `OwnerId`, `NameId`, `HomeWorld`) を全廃し、AQR/Brio 準拠の素の `BattleCharacter` のまま保持。
  - キャラクター命名規則を AQR 黄金律 `"{Name} Cnpc"` (例: `"Kimo Cnpc"`) に統一し、FF14 の名前検証規則を完全クリア。
  - `ApplyNpcAppearance` を HDM HumanGuise 準拠の独立メソッドとして新設。
  - `ApplyAppearanceDirect` を AQR 専用（Pipeline A/B）に純化し、自キャラ物理遮断ガード (`globalIndex <= 0 || objectTable[0]?.Address == chara`) を配置。
  - 旧アーキテクチャの残骸ポーリング (`readyJobs`, `pendingNpcJobs`) を完全削除（421行削減）し、`UpdateFrame` は視線追従と `monsterRedrawJobs` のみにスリム化。

## 5. 今後の検証・確認手順

1. **ローカルキャラクター単体検証**:
   - AQR系（Kimo 等）: スポーン時、自キャラが変身せず、パペットのみに外見と Penumbra コレクションが初回から適用されること。
   - MCDF系: 内包 Mod と Glamourer が正常に反映されること。
   - NPC系: エレゼン、ララフェル等の人型 NPC が肌色汚染なく表示されること。
   - モンスター系: ナマズオや大型モンスターが正常に実体化し、消滅しないこと。
2. **シーン作成（複数配置）検証**:
   - 異なるパイプライン（例: AQR系キャラ ＋ NPC ＋ モンスター）を同時にステージ上にスポーンさせ、互いに干渉しないこと。
   - ギズモによる座標移動、アニメーション変更、視線追従、ネームプレートが全パイプラインで等しく動作すること。

---

## 6. プラグインアップデート手順の確立と再発防止策

### 発生した問題
Dalamud プラグインインストーラで「ダウンロードが失敗しました。(https://raw.githubusercontent.com/runte3221/Character-Spawn/main/repo.json)」という赤字エラーが発生。

### 原因
`bump-version.ps1` 内で PowerShell の `ConvertTo-Json` を実行した際、要素が1件の配列（`[ { ... } ]`）が単一オブジェクト（`{ ... }`）に自動アンラップされて出力されたため、Dalamud が期待するプラグイン一覧の配列デシリアライズに失敗していた。

### 恒久対策
1. **`repo.json` の配列構造の完全保護**:
   `bump-version.ps1` において、JSON を壊す `ConvertTo-Json` ではなく、正規表現による `"AssemblyVersion"` のピンポイント置換を採用。
2. **自動バリデーションの組み込み**:
   更新後に `repo.json` が配列 `[` で始まり `]` で終わること、および各マニフェスト（.csproj, CharacterSpawn.json, repo.json）のバージョンが一致していることをスクリプト内で自動検査し、不正時は即座に中断。
3. **ワンコマンド・リリース自動化スクリプトの導入 (`tools/release.ps1`)**:
   `powershell -File tools/release.ps1 <Version> "Commit message"` を実行するだけで、
   - バージョン更新
   - マニフェスト・CHANGELOG整合性検証
   - Git コミット ＆ プッシュ
   - GitHub Actions CI/CD ビルドの完了待機（成功確認）
   - 公開された raw.githubusercontent.com 上の `repo.json` の配列パース検証
   を一貫して自動実行し、リリースの不整合を完全に根絶。


