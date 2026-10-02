# 恒久保存記録: キャラクター外見適用・描画パイプライン及びリリース手順の完全解明と解決記録

本書は、`Character-Spawn` プラグインにおいて発生していた「自キャラとスポーンアクターの外見入れ替わり・変身ループ不具合」「マニフェスト配信エラー」「3Dモデル不可視化（ギズモのみ表示）」の経緯、根本原因、および確立された解決策と運用手順を、将来いつでもさかのぼって確認・再発防止できるようにまとめた完全技術記録である。

---

## 目次
1. [ツールの最終目標と全体アーキテクチャ](#1-ツールの最終目標と全体アーキテクチャ)
2. [発生していた 3 大不具合の経緯と根本原因の完全解明](#2-発生していた-3-大不具合の経緯と根本原因の完全解明)
   - [不具合 1: 自キャラ変身・外見入れ替わりループ](#不具合-1-自キャラ変身外見入れ替わりループ)
   - [不具合 2: プラグインインストーラでのダウンロード失敗 (`repo.json` 破損)](#不具合-2-プラグインインストーラでのダウンロード失敗-repojson-破損)
   - [不具合 3: 3Dモデル不可視化（ギズモのみ表示）](#不具合-3-3dモデル不可視化ギズモのみ表示)
3. [確立された「4系統完全独立パイプライン」アーキテクチャ](#3-確立された4系統完全独立パイプラインアーキテクチャ)
4. [FF14ゲームエンジンにおける描画ライフサイクル (`EnableDraw` 黄金律)](#4-ff14ゲームエンジンにおける描画ライフサイクル-enabledraw-黄金律)
5. [リリースパイプラインの完全自動化 (`tools/release.ps1`)](#5-リリースパイプラインの完全自動化-toolsreleaseps1)
6. [将来のためのトラブルシューティング・診断フローチャート](#6-将来のためのトラブルシューティング診断フローチャート)

---

## 1. ツールの最終目標と全体アーキテクチャ

本ツール `Character-Spawn` の最終目標は以下の2本柱である：

### ① ローカルキャラクターの作成 (Local Character Creation)
* **Glamourer ＆ Penumbra ＆ Customize+ (オリジナルPC/パペット)**: 【参考：**AQR (AQuestReborn)**】
* **MCDF (ModPack / 外見パッケージ)**: 【参考：**AQR (McdfCharaFileManager)**】
* **NPC (人型 ENpc)**: 【参考：**HDM (HumanGuise)**】
* **Monster / MOB (非人型モデル)**: 【参考：**HDM (GuiseService)**】

### ② ステージ演出・シーンの作成 (Stage Scene Staging)
通常ワールド（非GPose）上において、自キャラの動作を一切妨害せず、登録されたローカルキャラクターを複数（または単一）配置し、以下の演出シーンを完全記録・再生する：
* **配置の記録・復元**: ワールド座標（X, Y, Z）、回転（Yaw / Rotation）、スケール、ギズモ直感操作。
* **アニメーション・演出**: エモート（BaseAnimation, Timeline）、表情（FacialExpression）、ループ設定、視線追従（HeadTracking: 自キャラ追従/カメラ追従/ターゲット追従/固定）。
* **ネームプレート制御**: 表示/非表示、カスタム表示名（CustomName）。
* **サウンド割り当て**: ボイス（3Dサウンド再生）、環境音、BGM。

---

## 2. 発生していた 3 大不具合の経緯と根本原因の完全解明

### 不具合 1: 自キャラ変身・外見入れ替わりループ

#### 【症状】
スポーンを実行すると、スポーンさせたアクターではなく操作中の自キャラ（LocalPlayer Index 0）が目的のキャラに変身してしまったり、自キャラとスポーンアクターの外見が互いに入れ替わったり、両方が同じキャラになってしまう現象が修正を繰り返してもループしていた。

#### 【根本原因（リバースエンジニアリング解析により判明）】
1. **不要なメモリ改変による Glamourer / Penumbra の識別器破壊**:
   過去のバージョンで、アクターに対して `ObjectKind = ObjectKind.BattleNpc`, `BattleNpcSubKind = Player`, `OwnerId = 0xE000_0000`, `NameId = 0` などを代入していた。
   Glamourer および Penumbra の `ActorIdentifierFactory` は、これらゲーム内部のフラグを参照して「このアクターが誰か」を解決する。不整合なフラグが書き込まれた結果、識別エンジンが混乱し、GlobalIndex 200 のパペットではなく操作中の LocalPlayer（Index 0）のステート辞書を誤って引き当ててしまっていた。
   **（AQR / Brio の公式実装では、これらのメモリ改変は一切行わず、素の `BattleCharacter` のまま保持している！）**
2. **`DisableDraw()` と遅延待機ポーリングによるレースコンディション**:
   HDM のモンスター切り替えに必要な `DisableDraw()` と `readyJobs`（数十フレームの待機）を一律に AQR 系アクターに適用したため、アクターの `DrawObject` 認識が遅延し、Glamourer が ObjectIndex 200 の描画オブジェクトを見失って LocalPlayer に誤爆していた。
3. **Glamourer ステートの残留**:
   一度自キャラに誤爆した Glamourer のステートが、自キャラの内部ステート辞書に残留していたため、パペット生成時に自キャラ素体をコピー（`CopyFromCharacter`）した瞬間に、自キャラもパペットも最初から変身後の姿になってしまっていた。

---

### 不具合 2: プラグインインストーラでのダウンロード失敗 (`repo.json` 破損)

#### 【症状】
ゲーム内 `/xlplugins` のプラグインインストーラで、「ダウンロードが失敗しました。(https://raw.githubusercontent.com/runte3221/Character-Spawn/main/repo.json)」という赤字エラーが発生し、アップデートできなくなった。

#### 【根本原因】
* **PowerShell `ConvertTo-Json` の自動アンラップ仕様**:
  Dalamud のカスタムリポジトリ規格では、`repo.json` は必ずプラグインのリスト（配列 `[ { ... } ]`）でなければならない。
  従来の `bump-version.ps1` 内で、`Get-Content repo.json | ConvertFrom-Json` したオブジェクトを `$repoList | ConvertTo-Json` で書き戻していた。PowerShell のパイプラインは**要素が1つの配列を自動的に単一オブジェクト（`{ ... }`）にアンラップして出力する**という言語仕様があるため、`repo.json` が配列から単一オブジェクトへと破壊されていた。
  その結果、Dalamud が `List<PluginInfo>` としてのデシリアライズに失敗し、赤字エラーを出していた。
* **GitHub raw CDN (Fastly) の 300秒キャッシュ**:
  `raw.githubusercontent.com` には `Cache-Control: max-age=300`（5分間）が設定されており、プッシュ直後にゲーム内で更新してもエッジサーバーが古いキャッシュを返し続けるため、「毎回アップデートが反映されない」状態になっていた。

---

### 不具合 3: 3Dモデル不可視化（ギズモのみ表示）

#### 【症状】
v0.1.42 で自キャラ誤爆が解消されたものの、スポーンさせたパペット（Kimo-1-Nude 等）の位置にギズモだけが表示され、キャラクターの 3D モデル（姿）が全く表示されない（不可視）。

#### 【根本原因】
* **`EnableDraw()`（描画有効化）の呼び出し欠落**:
  Brio の公式ソースコード（`ActorSpawnService.cs` / `ActorRedrawService.cs`）を逆アセンブル・ソース解析した結果、Brio ではアクター生成後に必ず `_actorRedrawService.DrawWhenReady()` を呼び、ゲームエンジンの描画準備完了（`IsReadyToDraw()`）に合わせて **`nativeChara->GameObject.EnableDraw()`** を実行している。
  v0.1.42 で旧ポーリングキュー（`readyJobs`）を削除した際、この `EnableDraw()` の呼び出しまで一緒に除去されてしまっていたため、ゲームエンジンが 3D メッシュのロード・レンダリングを開始せず、不可視のまま固まっていた。

---

## 3. 確立された「4系統完全独立パイプライン」アーキテクチャ

混同と競合を永久に根絶するため、アクターの種別（`SourceType` / `ModelCharaId`）に応じて 4 つの完全独立ルートを確立した：

```
                    ┌─────────────────────────┐
                    │ SpawnCharacter(template)│
                    └────────────┬────────────┘
                                 │
     ┌───────────────────────────┼───────────────────────────┐
     ▼                           ▼                           ▼
[Pipeline A: AQR Player] [Pipeline B: AQR MCDF]       [Pipeline C: HDM NPC]   [Pipeline D: HDM Monster]
(Glamourer / Penumbra)   (SourceType == Mcdf)        (SourceType == Npc)     (ModelCharaId > 0)
     │                           │                           │                       │
 1. 素のBattleChara生成      1. 素のBattleChara生成      1. 素のBattleChara生成  1. 素のBattleChara生成
 2. 自キャラ素体コピー       2. 自キャラ素体コピー       2. 自キャラ素体コピー   2. 自キャラ素体コピー
 3. 名: "{Name} Cnpc"        3. 名: "{Name} Cnpc"        3. 名: "{Name} Cnpc"    3. 名: "{Name} Cnpc"
 4. EnableDraw()             4. EnableDraw()             4. EnableDraw()         4. DisableDraw()
 5. 【即時直列適用】         5. 【即時直列適用】         5. 【HDM外見適用】      5. ModelCharaId / Scale設定
    - Penumbra(Guid)            - 一時コレクション作成      - 26B Customize注入     - CopyFromCharacter(None)
    - RedrawObject()            - AssignTempCollection      - 10S Equip注入         - MonsterRedrawJobエンキュー
    - Glamourer(Guid)           - Mcdf Base64無加工適用     - Strip(Param/Mat)   6. 【2フレーム待機後】
    - CustomizePlus()           - RedrawObject()            - RedrawObject()        - IsReadyToDraw()待機
 6. EnableDraw()             6. EnableDraw()             6. EnableDraw()             - EnableDraw()
 7. 【即時完了: IsReady】    7. 【即時完了: IsReady】    7. 【即時完了: IsReady】7. 【完了: IsReady】
```

### パイプライン独立性保証ルール
1. **自キャラ物理遮断ガード**:
   全適用メソッド冒頭で `globalIndex <= 0 || objectTable[0]?.Address == (nint)chara` を判定し、操作中キャラクターへの適用を物理的に完全遮断。
2. **命名規則の統一 (AQR黄金律)**:
   パペット名を必ず `"{Name} Cnpc"`（例: `"Kimo Cnpc"`）とし、FF14 の名前検証規則（名・姓それぞれ15文字以内、全体20文字以内）を完全遵守。
3. **不要なメモリ改変の全廃**:
   `ObjectKind`, `BattleNpcSubKind`, `OwnerId`, `NameId`, `HomeWorld` は一切改変せず、素の `BattleCharacter` のまま保持。

---

## 4. FF14ゲームエンジンにおける描画ライフサイクル (`EnableDraw` 黄金律)

Brio および AQuestReborn の実装から解明された、ゲームエンジンの 3D モデル描画ライフサイクル規則：

1. **アクター生成直後**:
   `ClientObjectManager.CreateBattleCharacter()` で生成されたアクターは、初期状態で描画が無効化または未初期化である。
   → **必ず `nativeChara->GameObject.EnableDraw()` を明示的に呼ぶ必要がある**。
2. **外見確定直後**:
   Glamourer や Penumbra の適用完了後、ゲームエンジンの描画キューを確実にキックするため、再度 `nativeChara->GameObject.EnableDraw()` を呼ぶ。
3. **`UpdateFrame` での継続的可視化保証**:
   ゲームエンジンが数フレームの間メッシュのロードを遅延させた場合でも、`UpdateFrame` 内で以下を毎フレーム保証する：
   ```csharp
   // DrawObject の非表示フラグ (0x10) があれば解除して描画を有効化
   if (chara->GameObject.DrawObject != null)
   {
       if ((chara->GameObject.DrawObject->Flags & 0x10) != 0)
       {
           chara->GameObject.DrawObject->Flags &= unchecked((byte)~0x10);
           chara->GameObject.EnableDraw();
       }
   }
   else
   {
       chara->GameObject.EnableDraw();
   }

   // 描画準備完了状態なら確実に EnableDraw を実行
   if (chara->GameObject.IsReadyToDraw())
   {
       chara->GameObject.EnableDraw();
   }
   ```
   ※ モンスターパイプライン（Pipeline D）の `DisableDraw` 待機中は、この監視ループから除外して干渉を防ぐ。

---

## 5. リリースパイプラインの完全自動化 (`tools/release.ps1`)

手動更新によるミス（配列破損、CDNキャッシュ未反映、ビルド失敗の見落とし）を永久に防止するため、ワンコマンド全自動リリーススクリプトを構築した。

### 実行方法
```powershell
powershell -File tools/release.ps1 <Version> "コミットメッセージ"
# 例: powershell -File tools/release.ps1 0.1.43.0 "fix: restore EnableDraw"
```

### スクリプトが自動保証する 5 つのステップ
1. **[Step 1/5] マニフェスト一括更新 ＆ 配列フォーマット保護 (`bump-version.ps1`)**:
   - `repo.json` を正規表現置換で更新し、配列 `[ { ... } ]` を 100% 保持（`ConvertTo-Json` 不使用）。
   - `.csproj`, `CharacterSpawn.json`, `repo.json` のバージョン一致と配列形式（`[` で始まり `]` で終わる）を厳格バリデーション。
2. **[Step 2/5] CHANGELOG.md 検証**:
   - 対象バージョンの見出しが存在するかを自動確認。
3. **[Step 3/5] Git コミット ＆ プッシュ**:
   - 自動ステージング、コミット、GitHub `main` へのプッシュ。
4. **[Step 4/5] GitHub Actions CI/CD の監視**:
   - GitHub API を自動ポーリングし、ビルド完了（`completed success`）と `latest.zip` アップロードを確認。
5. **[Step 5/5] ライブ CDN (`raw.githubusercontent.com`) のキャッシュ失効待機**:
   - 公開 URL をポーリングし、**CDN キャッシュ（最大300秒）が失効して世界中に最新のバージョン・配列形式が配信されたことを確認してからスクリプトが完了**。
   - **「開発者が完了報告をした瞬間＝ゲーム内で即座にアップデート可能な状態」が 100% 保証される**。

---

## 6. 将来のためのトラブルシューティング・診断フローチャート

もし将来、外見や描画に関して問題が生じた場合は、以下のフローチャートに従って即座に原因を切り分ける：

```
[症状発生]
  │
  ├─ Q1. 自キャラが変身してしまった、または入れ替わった？
  │    ├─ YES:
  │    │   1. Settings タブの「Revert Local Player」をクリック。
  │    │   2. ActorManager.cs の ApplyAppearanceDirect / ApplyNpcAppearance 冒頭にある
  │    │      自キャラ物理遮断ガード (globalIndex <= 0 || objectTable[0]?.Address == chara) が
  │    │      外れていないか確認。
  │    │   3. ObjectKind, OwnerId などのメモリ改変が復活していないか確認。
  │    └─ NO: 次へ
  │
  ├─ Q2. ギズモだけが表示されてアクターの姿が見えない（不可視）？
  │    ├─ YES:
  │    │   1. SpawnCharacter および UpdateFrame で EnableDraw() が呼ばれているか確認。
  │    │   2. DrawObject->Flags の 0x10（非表示フラグ）がクリアされているか確認。
  │    │   3. ログに「Glamourer ApplyDesign result: 0」が出ているか確認。
  │    └─ NO: 次へ
  │
  ├─ Q3. プラグインインストーラで「ダウンロードが失敗しました」と出る？
  │    ├─ YES:
  │    │   1. repo.json が配列 [ { ... } ] 形式になっているか確認（単一オブジェクト { ... } は厳禁）。
  │    │   2. tools/bump-version.ps1 のバリデーションを実行。
  │    └─ NO: 次へ
  │
  └─ Q4. プラグインの更新がゲーム内に表示されない／古いバージョンのまま？
       ├─ YES:
       │   1. GitHub raw CDN のキャッシュ（最大300秒）が残っている可能性大。
       │   2. 必ず tools/release.ps1 を使用して、CDN キャッシュ失効の自動確認まで待つ。
       └─ NO: 正常動作中
```
