# 実装計画: 独立パイプライン設計とAQR/HDM完全分離アーキテクチャ

## 1. 本ツールの最終目標と全体構造

### ① ローカルキャラクターの作成 (Local Character Creation)
キャラクターの種別・データ形式に応じて、適切な参照プラグインの仕様を厳格に適用し、混同を完全に防ぐ。

1. **Glamourer ＆ Penumbra ＆ Customize+ (オリジナルPC/パペット)**:
   - **参考基盤**: **AQR (AQuestReborn)**
   - プレイヤー型素体（BattleCharacter）のクローン、Guid指定のコレクション/デザイン適用、Customize+ プレイヤー紐付け。
2. **MCDF (ModPack / 外見パッケージ)**:
   - **参考基盤**: **AQR (McdfCharaFileManager)**
   - 一時コレクション作成・割当、内包 Base64 データの無加工直接適用。
3. **NPC (人型 ENpc)**:
   - **参考基盤**: **HDM (HumanGuise)**
   - 人型 (c-skeleton) の 26バイト CustomizeData + 10スロット EquipmentModelIds を Glamourer JObject 経由で適用（Parameters/Materials の Strip による自キャラ汚染防止）。
4. **Monster / MOB (非人型モデル)**:
   - **参考基盤**: **HDM (GuiseService)**
   - `Character.ModelContainer.ModelCharaId` と `Scale` のネイティブ書き換え、ゲームエンジンの 2フェーズ描画（DisableDraw -> IsReadyToDraw -> EnableDraw）。
   - **絶対ルール**: Glamourer / Penumbra Redraw は一切呼ばない。

---

### ② シーンの作成 (Stage Scene Staging)
登録されたローカルキャラクターを複数（または単一）ステージ上にスポーンさせ、その演出シーンを完全記録・再生する。

1. **配置の記録・復元**:
   - ワールド座標（X, Y, Z）、回転（Yaw / Rotation）、スケール。
   - ギズモ操作による直感的な配置移動。
2. **アニメーション・演出の設定**:
   - エモート（BaseAnimation, Timeline）。
   - 表情（FacialExpression）、ループ設定。
   - 視線追従（HeadTracking: プレイヤー追従 / カメラ追従 / ターゲット追従 / 固定）。
3. **ネームプレートの制御**:
   - 表示 / 非表示、カスタム表示名（CustomName）の割り当て。
4. **サウンドの割り当て**:
   - ボイス（AnamcoreManager / PlaySound3D）、環境音、BGM。
   - **参考基盤**: **AQR** (InteractiveNpc / AnamcoreManager), **HDM** (SoundEffects)。

---

## 2. AQR修正案とHDM側の競合分析（徹底解明）

現在の `Character-Spawn` では、単一の `SpawnCharacter` / `ReadyJob` / `ApplyAppearanceDirect` の中に、AQR の処理と HDM の処理が混在・ツギハギになっていたため、互いの前提条件を破壊し合っていました。

### 競合ポイントの詳細と発生メカニズム

| 項目 | AQR パイプライン (Glamourer/Penumbra/MCDF) | HDM パイプライン (NPC / Monster) | 混在による致命的競合 |
| :--- | :--- | :--- | :--- |
| **描画停止 (`DisableDraw`)** | **不要（呼んではならない）**。<br>自キャラの素体をコピー後、すぐに Penumbra/Glamourer で上書きするため描画を止める必要がない。 | **必須**。<br>モンスターモデル（ModelCharaId）をゲームネイティブで切り替える際、描画停止と2フレーム待機が必須。 | 全アクター一律に `DisableDraw()` を呼んだ結果、AQR系アクターが「描画未完了状態」になり、Glamourer が ObjectIndex 200 の描画オブジェクトを認識できず LocalPlayer（Index 0）に誤爆した。 |
| **適用タイミング** | **スポーン直後の即時直列実行**。<br>同一フレーム内で Penumbra Guid割当 -> Glamourer デザイン適用を完了する。 | **フレーム待機ポーリング**。<br>ネイティブ描画パイプラインの `IsReadyToDraw()` を待つ必要がある。 | AQR系アクターまで `readyJobs` で何十フレームも遅延待機させた結果、レースコンディションが発生し自キャラ変身の引き金となった。 |
| **Penumbra Redraw** | **必須**。<br>MODテクスチャの反映のため `RedrawObject` を呼ぶ。 | **厳禁（モンスター時）**。<br>呼ぶとモンスターの DrawObject が破棄され、人間骨格に戻るかアクターが消滅する。 | 共通の完了処理で Penumbra Redraw を呼ぶと、モンスターアクターが即座に破壊される。 |
| **メモリ改変 (`ObjectKind`等)** | **厳禁**。<br>素の `BattleCharacter` でなければ Glamourer のアクター識別器（ActorIdentifierFactory）が壊れる。 | **厳禁（基本不要）**。<br>HDM でも `ObjectKind` や `OwnerId` の強制改変は行っていない。 | 勝手に `OwnerId = 0xE000_0000` や `BattleNpcSubKind = Player` を入れたため、全パイプラインでアクター識別が破壊された。 |

---

## 3. 解決策：4つの完全独立パイプラインの設計

各キャラクターの `SourceType` に応じて、生成から外見適用、描画完了までを完全に分離した専用パイプラインへルーティングする。

```
                    ┌─────────────────────────┐
                    │ SpawnCharacter(template)│
                    └────────────┬────────────┘
                                 │
     ┌───────────────────────────┼───────────────────────────┐
     ▼                           ▼                           ▼
[AQR Pipeline: Player]   [AQR Pipeline: MCDF]        [HDM Pipeline: NPC]     [HDM Pipeline: Monster]
(PlayerClone/Glamourer)  (SourceType == Mcdf)        (SourceType == Npc)     (ModelCharaId > 0)
     │                           │                           │                       │
 1. 素のBattleChara生成      1. 素のBattleChara生成      1. 素のBattleChara生成  1. 素のBattleChara生成
 2. 自キャラ素体コピー       2. 自キャラ素体コピー       2. 自キャラ素体コピー   2. 自キャラ素体コピー
 3. 名: "{Name} Cnpc"        3. 名: "{Name} Cnpc"        3. 名: "{Name} Cnpc"    3. 名: "{Name} Cnpc"
 4. 【即時直列適用】         4. 【即時直列適用】         4. 【HDM外見適用】      4. 【HDMネイティブ描画】
    - Penumbra(Guid)            - 一時コレクション作成      - GetState()取得        - ModelCharaId設定
    - RedrawObject()            - AssignTempCollection      - 26B Customize注入     - Scale設定
    - Glamourer(Guid)           - Mcdf Base64無加工適用     - 10S Equip注入         - 武器非表示
    - CustomizePlus()           - RedrawObject()            - Strip(Param/Mat)      - CopyFromCharacter(None)
 5. 【完了: IsReady=true】   5. 【完了: IsReady=true】      - ApplyState()          - DisableDraw()
                                                         5. 【完了: IsReady=true】 - 2フレーム待機
                                                                                 - IsReadyToDraw待機
                                                                                 - EnableDraw()
                                                                                 5. 【完了: IsReady=true】
```

### パイプライン間の独立性保証ルール
1. **AQR系 (Player/MCDF)**:
   - `readyJobs` や `DisableDraw()` は一切使用しない。
   - スポーン直後にその場で Penumbra（Guid指定）と Glamourer を直列実行して即時完了する。
2. **HDM系 (NPC)**:
   - Glamourer JObject の差分更新（HumanGuise方式）のみを行い、自キャラ肌色・シェーダーパラメータ（Parameters/Materials）は必ず Strip する。
3. **HDM系 (Monster)**:
   - Glamourer IPC や Penumbra Redraw は一切呼ばない。
   - ゲームエンジンのネイティブ描画状態遷移（DisableDraw -> IsReadyToDraw -> EnableDraw）のみで完結させる。
4. **共通の自キャラ保護ガード**:
   - いかなるパイプラインにおいても、`actorIndex <= 0` または `LocalPlayer.Address` と一致するアクターへの処理は物理的に遮断する。

---

## 4. 実装ロードマップ

- **Phase 1**: `ActorManager.cs` の不要なメモリ改変（`ObjectKind`等）を完全撤廃。
- **Phase 2**: `PenumbraIpc.cs` のコレクション指定を Guid 渡しに改修し、直後 Redraw を整備。
- **Phase 3**: AQR パイプライン（通常GlamourerおよびMCDF）を即時直列実行化。
- **Phase 4**: HDM パイプライン（NPC、Monster）の独立ルーティングを確立し、競合を排除。
- **Phase 5**: シーン管理（配置、アニメーション、視線追従、ネームプレート）が4つのパイプラインすべてで正常に機能することを検証。
