# 技術記録・ウォークスルー: 人型NPC外見適用不具合の経緯・原因分析と根本解決策 (v0.1.44.0)

## 1. 不具合の経緯と症状

### 発生した現象
- **Monster / MOB（レストレス・ラプトル等）**: パイプライン D（HDM GuiseService 準拠）により正常にスポーン・描画される。
- **Demihuman NPC（レターモーグリ等、`ModelCharaId > 0`）**: パイプライン D により正常にスポーン・描画される。
- **人型NPC（ミューヌ、ユウギリ等、`SourceType == Npc` かつ `ModelCharaId == 0`）**:
  - スポーンを実行した際、ミューヌやユウギリの姿にならず、現在操作中のプレイヤー（自キャラ）の姿のままスポーンしてしまう。

---

## 2. 原因の深掘り解析 (Glamourer コールドステートトラップ)

### (1) なぜ自キャラの姿でスポーンしたのか？
1. **アクター生成時のベースラインコピー**:
   - ゲームエンジン内で安全に 3D モデルを描画可能にするため、アクター生成直後に `meNative`（自キャラ）からスケルトン・骨格情報を `CopyFromCharacter(meNative, CharacterCopyFlags.WeaponHiding)` でコピーする。
2. **Glamourer キャッシュのコールド状態**:
   - スポーンされたばかりの新規パペット（Global Index 200）は、Glamourer 内部のアクター状態キャッシュがまだ生成されていない。
   - `GlamourerIpc.cs` の `TryApplyNpcAppearance` 内で `GetState(actorIndex)` を呼び出すと `null` が返る。
3. **外見上書き処理のスキップ**:
   - 旧実装では `if (state == null) return StateNull;` となっており、即座に関数を抜けていた。
   - その結果、NPC の 26バイト `CustomizeData` や衣装の `EquipmentModelIds` が一度も適用されず、最初にコピーされた自キャラの素体がそのまま描画されていた。

---

## 3. 解決策の設計と実装

### ① LocalPlayer ステートをテンプレートとする即時ディープコピー変身 (0ms)
- 待機ポーリングを復活させるのではなく、**常時キャッシュが存在する自キャラ（`GetState(0)`）のステート JObject をひな形としてディープコピー** する。
- その JObject に対して NPC の外見データをマッピング：
  1. `CustomizeData`（26バイト）を `custObj` に書き込み。
  2. `EquipmentModelIds`（10スロット）を `equipObj` に書き込み。
  3. 自キャラ固有の肌色・パラメータ汚染（`Parameters`, `Materials`）を完全に削除（Strip）。
  4. `ForceAllApply` を実行して全スロットの強制適用を保証。
  5. 武器スロット（`MainHand`, `OffHand`, `Weapon`）を明示的に解除（Unmanage, `Apply: false`）し、自キャラの武器が NPC に渡るのを防止。
  6. `ForceAllApply` 内でも、明示的に `Apply: false` とされた武器スロットは上書きしないよう保護。
  7. 完成したステートを `ApplyState` に渡して一括適用。
- これにより、コールド状態のパペットであっても待機ゼロ（0ms）で即座に NPC の姿に変身できる。

### ② Glamourer 失敗時のダイレクトメモリフォールバック
- `ActorManager.cs` の `ApplyNpcAppearance` において、万一 Glamourer IPC が失敗または利用不能な場合でも、メモリ上の `chara->DrawData.CustomizeData` および `EquipmentModelIds` を直接上書きし、`chara->CharacterSetup.CopyFromCharacter(chara, CharacterCopyFlags.None)` を実行する安全網（`ApplyNpcAppearanceDirectFallback`）を追加。

### ③ NPC テンプレートデータの名前ベース自動解決補完
- テンプレートの `CustomizeData` または `NpcEquipmentModelIds` が未設定の場合、`template.Name`（例: "ミューヌ", "ユウギリ"）からゲーム内 NPC データベース（`gameDataService.SearchNpcs`）を即座に逆引きし、ENpcBaseId・外見データを自動解決して補完するフォールバックを追加。

---

## 4. 修正対象ファイル

1. [Services/GlamourerIpc.cs](file:///C:/Users/RYO/Desktop/Character-Spawn/Services/GlamourerIpc.cs)
   - `TryApplyNpcAppearance`: コールドステート時に `GetState(0)?.DeepClone()` をひな形として使用。
   - `ForceAllApply`: 武器スロットの `Apply: false` を尊重するガードを追加。
   - 武器管理解除（Unmanage）の適用順序を最適化。
2. [Managers/ActorManager.cs](file:///C:/Users/RYO/Desktop/Character-Spawn/Managers/ActorManager.cs)
   - `ApplyNpcAppearance`: Glamourer 実行結果を検証し、失敗時に `ApplyNpcAppearanceDirectFallback` を実行。
   - `SpawnCharacter`: `template.Name` からの NPC データ自動逆引き補完を追加。
3. [CHANGELOG.md](file:///C:/Users/RYO/Desktop/Character-Spawn/CHANGELOG.md)
   - v0.1.44 の変更点を記載。

---

## 5. 今後の参照用ノウハウ・チェックリスト
- スポーン直後の新規パペット（Index 200）に対して Glamourer の `GetState` を呼ぶと、キャッシュ生成前で必ず `null` を返す。
- したがって、スポーン直後に動的 JObject を組み立てる際は、**`GetState(0)`（常時存在する自キャラ）のステートをひな形としてディープコピーして利用する** のが最も確実で高速なアプローチである。
- `ForceAllApply` を呼ぶ際は、NPC やモンスターなど武器を持たせないキャラクターに対して、自キャラの武器情報が誤って強制適用されないよう、武器スロットの除外ガードを徹底すること。
