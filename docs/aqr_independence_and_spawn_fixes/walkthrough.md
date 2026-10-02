# 修正内容の確認 (Walkthrough): AQR 依存脱却 & スポーン不具合修正

## 1. 修正概要
本修正（v0.1.25）では、Penumbra の通常コレクション（'OC-RUMA' 等）および MCDF を適用した際に、ゲーム内で MOD が反映されずバニラの状態になっていた問題（`ec=16: InvalidActor`）の根本原因を Penumbra の IL 逆アセンブルによって特定し、完全修正を行いました。

## 2. 実施した修正点

### 1) `ActorManager.cs`
- **ObjectKind を Player に変更**:
  - Penumbra の `SetCollectionForObject` および `AssignTemporaryCollection` は、内部で `allowPlayerNpc: false` でアクター識別を行うため、`ObjectKind.BattleNpc` だと必ず `CreateBNpc` 経由で `ActorIdentifier.Invalid`（`ec=16` / `ec=255`）となり、コレクションの割り当てが拒否されていました。
  - 人型アクター（Glamourer / MCDF / PlayerClone / 人型NPC）の `ObjectKind` を **`ObjectKind.Player`** に設定することで、Penumbra が正当な Player Identifier（名前 `Csp {hi}{lo}` + ワールド）として認識し、コレクションが確実に割り当てられて MOD がゲーム内で描画されるようになりました。
  - モンスター（`ModelCharaId > 0`）については、Phase 2 のモンスター遷移時に `ObjectKind.BattleNpc` に切り替えることで、ネイティブモンスターモデルの描画を保証。

## 3. テスト・検証項目
1. **通常コレクション（Penumbra 指定）のスポーン**:
   - Ruma などのキャラクターテンプレートで Penumbra コレクション（`OC-RUMA` 等）を指定してスポーン。
   - `Penumbra SetCollectionForObject` が `ec=0` で成功し、服やテクスチャの MOD が正常にゲーム内に反映されることを確認。
2. **MCDF のロード**:
   - AQR を無効化した状態で MCDF キャラクターをスポーン。
   - Penumbra の Temporary Collection が正常に割り当てられ、Mod が反映されることを確認。
3. **人型 NPC / Demihuman / モンスター**:
   - 各種族が意図通りの外見・モデルで描画されることを確認。
