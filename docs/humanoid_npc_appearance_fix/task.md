# タスクリスト: 人型NPC（ミューヌ、ユウギリ等）外見適用不具合の根本解決 (v0.1.44.0)

## 1. 不具合の事象と原因究明フェーズ
- [x] **事象確認**:
  - Monster（レストレス・ラプトル等）や Demihuman（レターモーグリ等）は正常に描画される。
  - 人型NPC（ミューヌ、ユウギリ等、`ModelCharaId == 0`）をスポーンさせると、NPCの姿にならず操作中の自キャラ（LocalPlayer）の姿でスポーンしてしまう。
- [x] **原因究明 (Glamourer コールドステートトラップ)**:
  - HDM（Doll Master）の `HumanGuise.cs` 解析により、新規スポーン直後のパペット（Index 200）は Glamourer 内部キャッシュがまだコールドであり、`GetState(actorIndex)` が `null` を返すことが判明。
  - 旧実装の `TryApplyNpcAppearance` では `if (state == null) return StateNull;` と即座に失敗し、外見上書きがスキップされていた。
  - その結果、drawable 骨格確立のためにベースラインコピーされた自キャラ素体（`CopyFromCharacter(meNative)`）がそのまま残存し、自キャラがスポーンしたように見えていた。

## 2. 設計・実装フェーズ
- [x] **即時ディープコピー変身アプローチの設計 (`GlamourerIpc.cs`)**:
  - スポーン直後でパペットのステートがコールドな場合、常時キャッシュが存在する自キャラ（`GetState(0)`）のステート JObject をひな形としてディープコピー。
  - NPC の 26バイト `CustomizeData` と 10スロットの `EquipmentModelIds` を上書き。
  - 自キャラ固有の肌色・パラメータ汚染（Parameters/Materials）を完全に Strip。
  - `ForceAllApply` を実行後、武器スロット（MainHand/OffHand）を明示的に解除（Unmanage）して自キャラ武器の混入を防止。
  - `ApplyState` を呼ぶことで、待機ポーリングを挟まず 0ms で確実に NPC の姿に変身させる。
- [x] **ダイレクトメモリフォールバックの実装 (`ActorManager.cs`)**:
  - Glamourer IPC の戻り値を検証し、万一 IPC が失敗または利用不能な場合でも、メモリ上の `CustomizeData` と `EquipmentModelIds` を直接上書きして `CopyFromCharacter` を実行する安全網（`ApplyNpcAppearanceDirectFallback`）を追加。
- [x] **NPC テンプレートデータの自動補完強化 (`ActorManager.cs`)**:
  - `template.CustomizeData` が未設定のテンプレートであっても、`template.Name`（例: "ミューヌ", "ユウギリ"）からゲーム内 NPC データベースを即座に逆引きし、ENpcBaseId・外見データを自動解決して補完するフェイルセーフを追加。

## 3. リリース・検証フェーズ
- [x] `CHANGELOG.md` 更新（v0.1.44.0）
- [x] 全自動リリースパイプライン実行 (`tools/release.ps1 0.1.44.0`)
- [x] GitHub Actions ビルド成功＆Fastly CDN キャッシュ失効の自動確認
- [ ] ゲーム内実機でのミューヌ・ユウギリ正常スポーン確認
