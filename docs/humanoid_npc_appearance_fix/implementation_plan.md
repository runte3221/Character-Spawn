# 実装計画: 人型NPC（ミューヌ、ユウギリ等）外見適用不具合の根本解決 (v0.1.44.0)

## 1. 概要
モンスターやデミヒューマン（レターモーグリ等）は正常にスポーン・描画される一方、人型NPC（ミューヌ、ユウギリ等、`ModelCharaId == 0`）をスポーンさせた際に、NPCの外見にならず操作中の自キャラ（LocalPlayer）の姿でスポーンしてしまう問題を根本解決する。

## 2. 根本原因の技術的分析
1. **Glamourer コールドステートトラップ**:
   - スポーン直後の新規パペット（Index 200）は、Glamourer 内部のアクター状態キャッシュがまだ生成されていないため、`GetState(actorIndex)` が `null` を返す。
   - 旧実装の `TryApplyNpcAppearance` は `if (state == null) return StateNull;` と即座に失敗し、外見上書き処理が行われていなかった。
2. **ベースライン素体の残存**:
   - アクター生成時に drawable 骨格を確立するため、自キャラから `CopyFromCharacter(meNative, CharacterCopyFlags.WeaponHiding)` で素体をコピーしている。
   - 外見上書きがスキップされた結果、この自キャラの姿がそのまま表示され、「自キャラがスポーンした」ように見えていた。

## 3. 実装方針と改修箇所

### ① `Services/GlamourerIpc.cs`: LocalPlayer テンプレートによる即時ディープコピー変身 (0ms)
- `GetState(actorIndex)` が `null` の場合、常時存在する自キャラ（`GetState(0)`）の JObject をひな形としてディープコピー。
- NPC の 26バイト `CustomizeData` と 10スロットの `EquipmentModelIds` を上書き。
- 自キャラ固有の肌色・パラメータ汚染（`Parameters`, `Materials`）を完全に Strip。
- `ForceAllApply` を実行後、武器スロット（`MainHand`, `OffHand`, `Weapon`）を明示的に解除（Unmanage, `Apply: false`）して自キャラ武器の混入を防止。
- `ForceAllApply` 内で明示的に `Apply == false` と設定された武器スロットを上書きしないよう保護ガードを追加。
- `ApplyState` を呼ぶことで、待機ポーリングを挟まず 0ms で確実に NPC の姿に変身させる。

### ② `Managers/ActorManager.cs`: Glamourer 失敗時のダイレクトメモリフォールバック
- `ApplyNpcAppearance` 内で Glamourer の戻り値を検証。
- 失敗または Glamourer 未起動時は `ApplyNpcAppearanceDirectFallback` を呼び出し、アクターメモリの `DrawData.CustomizeData` および `EquipmentModelIds` を直接上書きし、`CopyFromCharacter` を実行する。

### ③ `Managers/ActorManager.cs`: NPC テンプレートデータの名前ベース自動解決補完
- テンプレートの `CustomizeData` または `NpcEquipmentModelIds` が未設定の場合、`template.Name`（例: "ミューヌ", "ユウギリ"）からゲーム内 NPC データベースを即座に逆引きし、ENpcBaseId・外見データを自動解決して補完する。

## 4. 検証手順
1. `tools/release.ps1 0.1.44.0` を実行し、全自動リリース（バージョン一括更新、Git コミット・プッシュ、CI/CD ビルド監視、マニフェスト整合性確認）。
2. ゲーム内でプラグインを v0.1.44.0 に更新。
3. ミューヌ、ユウギリをそれぞれスポーンさせ、自キャラの姿ではなく正常な NPC の顔・髪型・衣装でスポーンすることを確認。
4. レターモーグリ（デミヒューマン）およびモンスターが引き続き正常にスポーンできることを確認。
