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
- [x] ゲーム内実機でのミューヌ・ユウギリ正常スポーン確認（ミューヌ成功、ユウギリの衣装・体は成功、顔がプレイヤー汎用顔に置換される事象を検知）

## 4. NPC固有顔（ユウギリ等）サニタイズ問題の解決フェーズ (v0.1.45.0)
- [x] **原因究明**:
  - `dalamud.log` 解析により、`Glamourer ApplyNpcAppearance` が `False` となり、直接メモリ書き込みフォールバックが走っていたことを特定。
  - ゲームエンジンの `FilterCustomizeData` により、直接メモリ書き込みされた未解放NPC顔番号がプレイヤー選択可能顔に強制サニタイズされていた。
  - Glamourer `ApplyState` が失敗していた原因は、IPC 購読型が `string` で Provider（`object`）と型不一致になっていたこと。
- [x] **コード改修**:
  - [x] `ApplyState` / `ApplyStateName` の購読型を `object` に修正（`GlamourerIpc.cs`）。
  - [x] JObject 直接適用メソッド `ApplyStateJObject` を新設しダイレクト適用。
  - [x] `GetStateName` による自キャラ名ベースのテンプレート取得フォールバックを追加。
  - [x] `ActorManager.cs` から `localPlayerName` を渡すように連携。
- [x] `CHANGELOG.md` 更新（v0.1.45.0）
- [x] 全自動リリースパイプライン実行 (`tools/release.ps1 0.1.45.0`)
- [x] ゲーム内実機でのログ検証: `GetState(0)` で `ValueTuple<int, JObject>` の型不一致例外（AssemblyLoadContext / Newtonsoft.Json バージョン境界）が発生し、依然として Glamourer 適用がスキップされていたことを特定！

## 5. ValueTuple 型境界問題の根絶と GetStateBase64 黄金律導入フェーズ (v0.1.46.0)
- [x] **原因究明**:
  - Dalamud プラグイン間で `JObject` を ValueTuple で受け取ると、AssemblyLoadContext（ALC）や Newtonsoft.Json の参照境界により `converting from ValueTuple'2 to System.ValueTuple'2[System.Int32,Newtonsoft.Json.Linq.JObject]` 例外が発生する。
  - 公式 IPC `Glamourer.GetStateBase64` および `Glamourer.GetStateBase64Name` は `(int, string?)` を返すため、型衝突が 100% 発生しない。
- [x] **コード改修 (`GlamourerIpc.cs`)**:
  - [x] `Glamourer.GetStateBase64` および `Glamourer.GetStateBase64Name` の IPC サブスクライバーを追加。
  - [x] `GetState(actorIndex)` および `GetStateByName(actorName)` で `GetStateBase64` を最優先で呼び出し、取得した Base64 文字列を既存の `ParseDesignString` で `JObject` に復号する。
  - [x] `ApplyStateJObject` において、`CompressToBase64` で Base64 文字列（`string`）を生成して `ApplyState` に渡すことで、適用時も型境界トラブルを完全回避。
- [x] **ドキュメント・リリース**:
  - [x] `CHANGELOG.md` 更新（v0.1.46.0）
  - [x] 全自動リリースパイプライン実行 (`tools/release.ps1 0.1.46.0`)
  - [x] 実機検証: MCDF 適用時に影響（自キャラが出現）が発生したことを検知。直ちにロールバックを決定。

## 6. 安定版復元（ロールバック）と独立アプローチによる慎重分析フェーズ (v0.1.47.0)
- [x] **即時ロールバック**:
  - `Services/GlamourerIpc.cs` および `Managers/ActorManager.cs` を MCDF が確実に動作していた v0.1.44.0 のコードベースに直ちに復元。
- [x] **リリース実行**:
  - `CHANGELOG.md` 更新（v0.1.47.0）
  - 全自動リリースパイプライン実行 (`tools/release.ps1 0.1.47.0`)
- [x] **MCDF・NPC・Monster の競合ゼロ保証分析**:
  - MCDF パイプライン（Pipeline A/B）、Monster パイプライン（Pipeline D）、NPC パイプライン（Pipeline C）の各パイプラインの独立性を再確認。
  - HDM（`HumanGuise.cs`）の逆アセンブル解析により、Glamourer の冷態認識遅延（Cold-Spawn Race）とフレーム毎リトライ監視（`OnUpdate`）のメカニズムを完全解明。
  - MCDF などの他パイプラインに一切手を加えない、独立した安全確実な修正案を設計。

## 8. HDM徹底逆アセンブル解析とASCIIパペット名・スポーンシーケンス完全同期フェーズ (v0.1.49.0)
- [x] **HDM（HousingDollMaster）の完全逆アセンブル解析**:
  - `SpawnService.TrySpawn`、`SpawnService.OnUpdate`、`HumanGuise.Apply`、`HumanGuise.TryApplyOnce`、`HumanGuise..cctor`、`HumanGuise.WriteCustomize` を 1 命令単位で完全解析。
  - **真因の特定**:
    1. パペット名に日本語が含まれていると、Glamourer の `ActorIdentifier` 検証（`VerifyPlayerName`）で弾かれ、`id.IsValid == false` となり `ActorNotFound` が返ってきていた。HDM は純粋な ASCII 英字 `"Hdm Aa"` を設定していた。
    2. HDM はスポーン直後に `EnableDraw()` を呼ばず、`IsReadyToDraw()` を待って `EnableDraw()` を呼び、さらに `DrawObject` の可視化準備が完了してから Glamourer の `ApplyState` を呼んでいた。
    3. HDM の `CustomizeMap`（36エントリ）と Character Spawn の実装は 100% 完全一致していることを証明。
- [x] **コード改修 (`Managers/ActorManager.cs`)**:
  - [x] `GetPuppetName` を HDM 準拠の純粋な ASCII 英字プレイヤー名（`$"Actor {c1}{c2}"`）に修正。
  - [x] `HumanoidNpcApplyJob` に `DrawEnabled` フラグを追加。
  - [x] パイプライン C（人型NPC）でスポーン時に `DisableDraw()` を実行し、`HumanoidNpcApplyJob` にエンキュー。
  - [x] `UpdateFrame` 内で `IsReadyToDraw()` を待機して `EnableDraw()` を呼び、`DrawObject` 安定後に Glamourer 外見適用を実行。
- [x] **コード改修 (`Services/GlamourerIpc.cs`)**:
  - [x] `getStateBase64Name`（`Glamourer.GetStateBase64Name`）の購読と `GetStateByName` を追加。
  - [x] `TryApplyNpcAppearance` 内で Index 解決に失敗した場合に Name ベースのステート取得フォールバックを追加。
- [x] **ドキュメント・リリース**:
  - [x] `CHANGELOG.md` 更新（v0.1.49.0）
  - [x] 全自動リリースパイプライン実行 (`tools/release.ps1 0.1.49.0`)
  - [x] 実機検証（カヌ・エ・センナ、ユウギリ、ミューヌで自キャラが出現する事象を検知）

## 9. HDM完全照合による二大根本原因の解決フェーズ (v0.1.50.0)
- [x] **根本原因 1: ENpc ResidentId と BaseId の ID 空間乖離**:
  - UI 検索が `ENpcResident` を走査していたため、ユウギリの ID が ResidentId `1007097` になっていた。
  - しかし `ENpcBase` の行は `1011896` であり一致せず、テンプレート保存時に自キャラデータで汚染されていた。
  - HDM は `ENpcBase` を走査して `ENpcBase.RowId` をリスト ID に採用している。
- [x] **根本原因 2: Glamourer ApplyFlag (6UL) と DrawObject 強制再構築 (`RedrawGuise`)**:
  - HDM は `ApplyState` に `6UL`（Equipment | Customization）を渡し、`Once (1)` を除外して永続適用していた。
  - さらに `ApplyState` 直後に `DisableDraw` → 2 ticks 待機 → `EnableDraw`（`RedrawGuise`）を実行し、ゲームエンジンの DrawObject を NPC 外見で強制再構築していた。
- [x] **コード改修 (`Services/GameDataService.cs`)**:
  - [x] `BuildNpcCache` を `ENpcBase` 主ループに変更し、リスト ID を `ENpcBase.RowId`（BaseId）にする。
  - [x] 既存 ResidentId や名前から正しい BaseId を解決するフェイルセーフを追加。
- [x] **コード改修 (`Services/GlamourerIpc.cs`)**:
  - [x] `ApplyState` のフラグを HDM と同一の `6UL`（Equipment | Customization）に変更。
  - [x] 不要な全スロット強制 `ForceAllApply` を排除し、NPC スロットのみ確実に適用。
- [x] **コード改修 (`Managers/ActorManager.cs`)**:
  - [x] 人型NPCスポーン時、保存済みテンプレートが自キャラデータで汚染されている場合の自動リフレッシュを追加。
  - [x] `HumanoidNpcApplyJob` で Glamourer 適用成功後に `DisableDraw` → 2 ticks 待機 → `EnableDraw`（DrawObject 強制再構築）を実行。
- [x] **ドキュメント更新とリリース**:
  - [x] `docs/humanoid_npc_appearance_fix/` の 3 ファイル更新
  - [x] `CHANGELOG.md` 更新（v0.1.50.0）
  - [x] `tools/release.ps1 0.1.50.0` 実行



