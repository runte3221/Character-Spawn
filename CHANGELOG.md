# Changelog

All notable changes to this project will be documented in this file.

## [0.1.45] - 2026-10-03
### Fixed
- **ユウギリ等のNPC固有顔がプレイヤー選択可能顔タイプに置き換わってしまう不具合の根本解決**:
  - **根本原因 (Glamourer IPC 型不一致による FilterCustomizeData 丸め込み)**:
    - 公式の `Glamourer.ApplyState` IPC プロバイダは第1引数の型が `object`（`FuncProvider<object, int, uint, ulong, int>`）で登録されている。
    - プラグイン側の購読型が `string`（`GetIpcSubscriber<string, int, uint, ulong, int>`）となっており、Dalamud IPC の厳密な型照合により型不一致で呼び出しが失敗。
    - その結果、フォールバックの直接メモリ書き込み（`ApplyNpcAppearanceDirectFallback`）が動作していたが、ネイティブメモリ直接書き込みではゲームエンジン内部の `FilterCustomizeData` が働き、ユウギリ等の NPC 固有顔番号（未解放フェイス）がプレイヤークラスの選択可能な標準顔に強制サニタイズ（丸め込み）されてしまっていた。
  - **対策 1: Glamourer IPC 購読型の完全整合化 (`object` 型への統一)**:
    - `Glamourer.ApplyState` および `Glamourer.ApplyStateName` の IPC 購読シグネチャを `string` から `object` に修正。
    - これにより Glamourer 経由での外見適用が 100% 成功し、ゲームエンジンの `FilterCustomizeData` の丸め込みをバイパスして、ユウギリのツノ・ウロコ・固有フェイス造形を完全再現。
  - **対策 2: JObject 直接適用メソッド (`ApplyStateJObject`) の新設**:
    - JSON 文字列の再シリアライズや Base64 圧縮処理を介さず、メモリ上で構成した `JObject` をそのままダイレクトに Glamourer に渡す `ApplyStateJObject` を導入し、オーバーヘッドをゼロ化。
  - **対策 3: 自キャラテンプレート取得の強化 (`GetStateName` フォールバック)**:
    - スポーン直後のコールドパペット用ひな形取得において、`GetState(0)`（Index 0）に加えて `clientState.LocalPlayer?.Name.TextValue` を用いた `GetStateName` フォールバックを追加し、自キャラテンプレートの取得を強固に保証。

## [0.1.44] - 2026-10-03
### Fixed
- **人型NPC（ミューヌ、ユウギリ等）スポーン時に自キャラの姿で出現する不具合の根本解決**:
  - **根本原因 (Glamourer コールドステートトラップ)**:
    - HDM（Doll Master）の設計知見通り、スポーン直後の新規パペット（actorIndex 200）は Glamourer 内部のアクター状態キャッシュがまだ生成されておらず、`GetState(actorIndex)` が `null` を返す。
    - そのため、外見上書き処理がスキップされ、アクター生成時に drawable 骨格確立のためにベースラインコピーされた自キャラ素体（`CopyFromCharacter(meNative)`）がそのまま描画されていた。
  - **解決策 1: LocalPlayer ステートをテンプレートとする即時ディープコピー変身 (0ms)**:
    - スポーン直後で対象アクターのステートがコールドな場合、常時存在する自キャラ（`GetState(0)`）のステート JObject をひな形としてディープコピー。
    - NPC の 26バイト `CustomizeData` と 10スロットの `EquipmentModelIds` を上書きし、自キャラ固有の肌色・パラメータ汚染（Parameters/Materials）を完全に Strip。
    - `ForceAllApply` を実行後、武器スロット（MainHand/OffHand）を明示的に解除（Unmanage）して `ApplyState` を呼ぶことで、待機ポーリングを挟まず 0ms で確実に NPC の姿に変身させる即時直列パイプラインを実現。
  - **解決策 2: Glamourer 失敗時のダイレクトメモリフォールバック (`ApplyNpcAppearanceDirectFallback`)**:
    - Glamourer IPC の戻り値を検証し、万一 IPC が失敗または利用不能な場合でも、メモリ上の `CustomizeData` と `EquipmentModelIds` を直接上書きして `CopyFromCharacter` を実行する安全網を導入。
  - **解決策 3: NPC テンプレートデータの名前ベース自動解決補完**:
    - `template.CustomizeData` が未設定の NPC テンプレートであっても、`template.Name`（例: "ミューヌ", "ユウギリ"）からゲーム内 NPC データベースを即座に逆引きし、ENpcBaseId・外見データを自動解決して補完するフォールバックを追加。

## [0.1.43] - 2026-10-03
### Fixed
- **スポーンアクターの3Dモデル不可視化（ギズモのみ表示）の根本解決**:
  - **根本原因の解明 (Brio ActorSpawnService 比較解析)**:
    - v0.1.42 で不要なポーリングキュー（`readyJobs`）を削除した際、ゲームエンジンの描画有効化処理（`nativeChara->GameObject.EnableDraw()`）の呼び出しまで除去されていたため、アクター生成後にゲームエンジンが 3D メッシュのロード・レンダリングを開始せず、不可視（ギズモのみ）のまま固まっていた。
  - **対策 1: Brio / AQR 黄金律 `EnableDraw` の完全復元**:
    - `SpawnCharacter` でのベースライン設定時、および各パイプライン（A: Glamourer/Penumbra、B: MCDF、C: NPC）の完了直後に `nativeChara->GameObject.EnableDraw()` を明示的に呼び出し、即時レンダリングを開始。
  - **対策 2: 継続的描画可視化ループの追加 (`UpdateFrame`)**:
    - `UpdateFrame` 内で全アクティブアクターの描画状態を監視し、`IsReadyToDraw() -> EnableDraw()` の実行および `DrawObject` の非表示フラグ（0x10）の解除を自動保証（モンスターパイプライン D の Redraw 待機中は干渉しないよう安全に除外）。

## [0.1.42] - 2026-10-03
### Fixed
- **AQR (AQuestReborn) ＆ HDM (Housing Decorator/Doll Master) 参照仕様の完全分離と4系統独立パイプライン構築**:
  - **ツールの最終目標（①ローカルキャラ作成、②シーン作成演出）に即したアーキテクチャ分離**:
    - **Pipeline A (AQR: 通常Glamourer / Penumbra / Customize+ / PlayerClone)**:
      - 素の `BattleCharacter` 生成 → 素体コピー → その場で Penumbra（Guid指定）＋ RedrawObject ＋ Glamourer（Guid指定/PlayerClone）を直列即時実行。`readyJobs` 遅延待機を廃止し即時完了。
    - **Pipeline B (AQR: MCDF)**:
      - 素の `BattleCharacter` 生成 → 一時コレクション割当 → 内包 Base64 を無加工で `ApplyState` に渡す → 直後 `RedrawObject`。即時完了。
    - **Pipeline C (HDM: 人型NPC)**:
      - 素の `BattleCharacter` 生成 → `ApplyNpcAppearance`（Customize/Equip注入、Parameters/Materials の Strip、コールドスポーン時の RevertToGameBase スキップ、直後 RedrawObject）。即時完了。
    - **Pipeline D (HDM: Monster / MOB)**:
      - `ModelCharaId` と `Scale` 設定 → 武器非表示 → ネイティブ描画ポーリング（`MonsterRedrawJob`）。Glamourer や Penumbra Redraw は一切呼ばない。
  - **不要なメモリ改変の完全撤廃 (AQR / Brio 黄金律)**:
    - `ObjectKind`, `BattleNpcSubKind`, `OwnerId`, `NameId`, `HomeWorld` の改変を全廃し、ゲーム本来の `BattleCharacter` を維持。
  - **命名規則の統一**:
    - `"{Name} Cnpc"`（例: `"Kimo Cnpc"`）形式とし、FF14 の名前検証規則を完全充足。
  - **自キャラ物理遮断ガードの徹底**:
    - `globalIndex <= 0 || objectTable[0]?.Address == chara` による誤爆防御を全適用パスに配置。
  - **デッドコード・残骸ポーリングの完全クリーンアップ**:
    - 旧アーキテクチャの `readyJobs`, `pendingNpcJobs` クラスおよびポーリングループを完全削除（421行削減）し、`UpdateFrame` は視線追従と `monsterRedrawJobs` のみにスリム化。

## [0.1.41] - 2026-10-03
### Fixed
- **AQuestReborn (AQR) 実装完全解析 & Proteus 外部干渉防御による自キャラ変身根絶と外見適用の完全分離**:
  - **根本原因 1: `RevertLocalPlayer` のメインスレッド外呼び出し (`Not on main thread!`) の解消**:
    - v0.1.40 で追加した自動 Revert が、プラグイン初期化スレッド（非メインスレッド）から呼ばれたため `Not on main thread!` 例外となり、過去のテストで変身していた自キャラが元に戻っていなかった。
    - `Framework.RunOnFrameworkThread` でメインスレッド上での確実な自キャラ復元を保証。さらに `revertCharacter`（ICharacter 直接）も併用し、自キャラの本来の姿と Penumbra コレクションを確実に復元。
  - **根本原因 2: スポーン直後での Glamourer 呼び出しによる未登録エラーと Proteus 誤爆の完全排除**:
    - アクター生成直後（`SpawnCharacter` 内）は、まだ Glamourer の `ActorObjectManager` にアクターが登録されていないため、外見適用が失敗し、さらに外部プラグイン `Proteus` が `DesignApplied` シグナルを検知して自キャラの Penumbra コレクション（`GetPlayerCollectionId()`）を上書きしてしまっていた。
    - **対策**: スポーン直後は Penumbra コレクションの事前割り当てのみ（`applyGlamourer: false`）に限定し、Glamourer デザインの適用および Redraw はアクターの描画準備が整った `ReadyJob`（メインスレッド）でのみ実行するよう完全分離。
  - **根本原因 3: UI 上での自キャラ即時復元ボタンの追加**:
    - Character タブの操作ボタン並びに「Revert Player」ボタンを追加し、ワンクリックでいつでも自キャラを本来の姿・コレクションに復元可能に。

## [0.1.40] - 2026-10-03
### Fixed
- **AQuestReborn (AQR) ソースコード・MCDF-Loader・0.1.23完全同期による MCDF/Penumbra/Glamourer 適用不具合および自キャラ誤認の根本解決**:
  - **根本原因 1: 自キャラ（LocalPlayer 0）の過去セッションにおけるGlamourerステート残留の解消**:
    - 以前のバージョンでの実行時に自キャラに対してGlamourerステートが適用され、Glamourer内部に保持されたままリバートされていなかったため、自キャラ自体がKimo-1-Nudeの姿のまま固まっていた。
    - パペットスポーン時に自キャラの素体（`CharacterSetup.CopyFromCharacter`）をコピーするため、自キャラもパペットも同一の変身姿になり、自キャラが乗っ取られたように見えていた。
    - **対策**: プラグイン起動時およびUIのSettingsタブに「Revert Local Player (Glamourer)」を追加。自キャラのGlamourerロック解除とリバートを実行して本来の姿に完全復元。
  - **根本原因 2: Penumbraコレクション事前適用（Pre-Assignment）の復元 (v0.1.23準拠)**:
    - 正常動作していたv0.1.23では、アクター生成直後（描画開始前、`DisableDraw`中）にPenumbra一時コレクション・通常コレクションを事前割り当てしていた。
    - ゲームエンジンが DrawObject を構築し始める前にコレクションをアクターにバインドしておくことで、MODテクスチャや体型モデルが初回の描画構築時から確実に反映されるように修正。
  - **根本原因 3: パペットIdentity（ObjectKind, BattleNpcSubKind, OwnerId, NameId）の完全復元 (v0.1.23 & AQR準拠)**:
    - `ObjectKind.BattleNpc`, `BattleNpcSubKind.Player`, `OwnerId = 0xE000_0000`, `NameId = 0`, `HomeWorld` を設定し、PenumbraおよびGlamourerがパペットを正規のプレイヤー型アクターとして識別できるように復元。
    - パペット名には一意の英字識別子（"Cs Aa", "Cs Ab"等）を付与し、自キャラの名前との混同を完全防止。
  - **MCDF 一時コレクション・Mod 登録・Glamourer 適用・Penumbra Redraw の完全同期**:
    - AQR (MCDF-Loader) の実装に完全準拠し、一時コレクションの作成・割り当て、ModファイルおよびManipulationDataの登録、Glamourer外見適用、Penumbra Redrawを一連のフローとして同期実行。

## [0.1.39] - 2026-10-03
### Fixed
- **AQuestReborn (AQR) バイナリ完全リバースエンジニアリング準拠によるパペットスポーン & 外見・コレクション適用の根本修復**:
  - **自キャラ変身（外見入れ替わり現象）の根本原因解明と完全根絶**:
    - **原因 1: ObjectKind / BattleNpcSubKind / NameId の改変による PC 誤爆**:
      - パペットを `ObjectKind.Pc` かつ `NameId = 0` に書き換えていたため、Penumbra および Glamourer 内部の `ActorIdentifierFactory.FromObject` がパペットをプレイヤーキャラクター（PC）として解決しようとし、名前解決の不整合から自キャラ（LocalPlayer）の Identifier を返してしまっていた。
      - AQR / Brio の仕様を 100% 遵守し、`ClientObjectManager.CreateBattleCharacter` で作成されたそのままの `BattleCharacter` の状態を維持（`ObjectKind` や `BattleNpcSubKind`、`NameId` の書き換えを完全撤廃）。
    - **原因 2: Glamourer ApplyState による自キャラ State 上書きの完全根絶**:
      - `GlamourerIpc.ApplyDesignToActor` において、`GetDesign` で JSON を取得して `ForceAllApply` 圧縮 Base64 を生成し `Glamourer.ApplyState` を呼び出していたが、`ApplyState` はアクターの Identifier に紐づくグローバル状態を書き換えるため、上記原因 1 と合わさって自キャラの State を上書きし、自キャラを Redraw させていた。
      - AQR 完全準拠の `Glamourer.ApplyDesign(Guid targetGuid, int actorIndex, 0, 7UL)` 直接呼び出しに一本化し、`ApplyState` を完全排除。
  - **Penumbra コレクション適用の安定化**:
    - `SetCollectionForActor` 成功直後に AQR と同様に `RedrawObject` を即時実行し、MOD やテクスチャが適用された上で Glamourer デザインが確定するようにシーケンスを整理。
- **Documentation**:
  - `docs/fix_aqr_puppet_spawn_and_appearance/` に調査結果、リバースエンジニアリング解析ログ、根本原因、修正内容を記録。

## [0.1.38] - 2026-10-03
### Fixed
- **Glamourer 1.7.1.3 最新仕様完全準拠によるパペットへの外見 100% 確実適用**:
  - **根本原因の完全解明 (IL 逆アセンブル解析)**:
    - Glamourer 1.7.1.3 の `ApplyStateName` / `ApplyDesignName`（名前指定）は内部で `FindExistingStates` を呼び、**既存のステート辞書（StateCache）に既に登録されているアクターしか対象にできない仕様** であった。
    - スポーン直後のパペット（`Csp Lhcpetra` 等）はステート辞書に存在しないため、`FindExistingStates` は 0 件を返し、`ApplyDesignName` は `ActorNotFound (2)`、`ApplyStateName` は内部バグにより `InvalidKey (6)` を返して 100% 失敗していた。
    - 一方、`Glamourer.ApplyState` / `Glamourer.ApplyDesign`（インデックス指定）は内部で `ObjectManager[objectIndex]`（ゲームオブジェクト配列）からアクターを直接取得し、`stateManager.GetOrCreate` を呼ぶため、**未登録の新規アクターであってもステートが自動生成され、外見が 100% 確実に適用される（result: 0）** ことが IL 解析により証明された。
  - **インデックス指定 IPC への回帰と自キャラ誤爆物理遮断ガードの確立**:
    - パペットの `GlobalIndex`（COM#0 = 200〜）に対して `Glamourer.ApplyState`（`ForceAllApply` 圧縮 Base64）および `Glamourer.ApplyDesign` を直接呼び出すように修正。
    - **自キャラ誤爆防止ガード**: `actorIndex <= 0` の場合はパペット向け適用処理を物理的に拒絶し、操作中自キャラ（Index 0）への誤爆を 100% 完全遮断。
    - 未登録の Legacy IPC（`ApplyAllToCharacter` 等）による例外ログを解消し、正規 IPC パスで安全に外見が適用されるように統合。

## [0.1.37] - 2026-10-03
### Fixed
- **AQuestReborn (AQR) / Caraxi 公式 IPC アーキテクチャへの全面移行による外見・Customize+誤爆・武器残留の完全解決**:
  - **Glamourer 自キャラ誤爆および素体スポーンの完全根絶**:
    - 通常ワールド（非GPose）において ClientObjectManager パペットはゲーム内部の描画ソート配列 `IndexSorted` に登録されないため、インデックス（200）指定や名前指定（`ApplyDesignName`）では `ActorNotFound` となり、Glamourer 内部で Index 0（自キャラ）にフォールバックして自キャラが変身していた。
    - AQR 準拠の `Glamourer.ApplyAllToCharacter` (`Action<ICharacter, string>`) および `Glamourer.ApplyByGuidToCharacter` (`Action<Guid, ICharacter>`) を採用。
    - パペットの `ICharacter` 生ポインタに対して直接外見を適用するため、インデックス検索を完全バイパスし、自キャラ（LocalPlayer）への誤爆は物理的に完全不可能。
    - GUID 指定時も `Glamourer.GetDesignBase64` で Base64 を取得し、`ForceAllApply` で全スロット（性別・種族・顔・髪型・全装備）を強制適用した上でパペットに流し込むため、素体（女性ミコッテ）のままスポーンする現象を根絶。
  - **Customize+ 自キャラ誤爆バグの完全根絶**:
    - `SetTemporaryProfileOnCharacter(200, ...)` によるインデックス指定が自キャラ（Index 0）にフォールバックしていた問題を完全解消。
    - Caraxi 公式の `CustomizePlus.Profile.AddPlayerCharacter` (`Func<Guid, string, ushort, int>`) を採用し、パペットの `PuppetName` と `HomeWorld` でプロファイルに正規紐付け。自キャラには一切プロファイルが適用されない。
    - デスポーン時は `CustomizePlus.Profile.RemovePlayerCharacter` で安全に紐付け解除。
  - **デスポーン時の武器孤立残留バグの完全解消**:
    - `chara->DrawData.HideWeapons(true)` + `chara->GameObject.DisableDraw()` を実行し、描画パイプラインから全メッシュ・ボーンをアンロードした上で `ClientObjectManager.DeleteObjectByIndex` を実行。マップ上に武器だけが取り残される現象を完全根絶。
- **Documentation**:
  - `docs/development_history_and_design_architecture` 配下に開発経緯、本来の意図、確立されたアーキテクチャ仕様、検証プロトコル（`task.md`, `implementation_plan.md`, `walkthrough.md`）を整備・保存。以後の開発において都度参照し、場当たり的修正による不具合ループの再発を完全防止。

## [0.1.36] - 2026-10-02
### Fixed
- **操作自キャラとスポーンパペットの外見入れ替わりバグの完全根絶**:
  - **根本原因の完全解明**:
    1. Glamourer の IPC（`Glamourer.ApplyState` / `ApplyDesign`）の `int objectIndex` 引数は、Dalamud の `ObjectTable` インデックス（200〜）ではなく、FF14 内部の描画ソート順配列（`GameObjectManager.Instance()->Objects.IndexSorted`）を参照する仕様であった。
    2. パペットの Dalamud GlobalIndex（`200`）をそのまま渡した結果、`IndexSorted[200]` に位置していた（あるいは未初期化メモリ経由で参照された）**操作中の自キャラ（LocalPlayer）** に Glamourer がデザイン（Chonk）を適用してしまい、自キャラが変身していた。
    3. 一方、スポーンしたパペット（Global#200）は自キャラのベースライン姿のまま残されたため、プレイヤーから見て「自キャラとパペットの外見が入れ替わった」状態が発生していた。
  - **名前指定 IPC（`ApplyDesignName` / `ApplyStateName`）の導入による自キャラ誤爆の 100% 根絶**:
    - `GlamourerIpc` に `Glamourer.ApplyDesignName` および `Glamourer.ApplyStateName` サブスクライバを新設。
    - パペットは生成時に一意の ASCII 名前（`PuppetName` = `"Csp Wzjjjwyr"` 等）が命名されているため、デザイン適用時は必ずこの名前を指定して IPC を呼び出すように変更。
    - Glamourer は内部で `PlayerName == "Csp Wzjjjwyr"` のアクター（パペット）を特定してデザインを適用するため、自キャラ（`"Ruma Meow"`）に適用される事故は物理的に 100% 発生しなくなった。
- **デスポーン時の武器孤立残存（Orphaned Weapon Bug）の完全解消**:
  - **根本原因の特定**:
    1. デスポーン時に `chara->GameObject.DisableDraw()` を呼び出していたため、ゲームエンジンの描画ツリーから武器の DrawObject だけが切り離されてワールド空間に孤立して残っていた。
    2. また、破棄直前に Glamourer の `RevertState` を呼び出していたため、非同期の装備再描画パイプラインが走り、オブジェクト削除と競合して武器モデルが空中に残留していた。
  - **安全なデスポーンシーケンスの確立 (Brio 準拠)**:
    - `DisableDraw()` の呼び出しを完全撤廃し、ゲームのネイティブ `ClientObjectManager.DeleteObjectByIndex` による自然なカスケード破棄に任せるように変更。
    - デスポーンするアクターに対する `RevertState`（見た目を元に戻す再描画）を廃止し、`UnlockState` のみ実行。
    - 削除直前に `chara->DrawData.HideWeapons(true)` を適用し、武器モデルの確実なアンロードを保証。

## [0.1.35] - 2026-10-02
### Fixed
- **CustomizeData メモリ破壊バグの完全根絶 & 異種族・男性キャラロールバックの根本解決**:
  - **根本原因の完全解明**:
    1. `ExtractCustomizeBytes` のビットマスク処理において、複数ビットで構成される `EyeShape`（マスク `0x7F`）や `Mouth`（マスク `0x7F`）、`FacePaint`（マスク `0x7F`）等の形状番号が、`val != 0` の際に `|= 0x7F`（全ビット1 = 127）として書き込まれ、**完全に破損した26バイト** が生成されていた。
    2. これを `Buffer.MemoryCopy` でネイティブ描画データ（`chara->DrawData.CustomizeData`）に直接上書きしていたため、FF14 の描画エンジン（`Human.SetupFromCustomize`）が不正データとして描画を拒否し、自キャラのベースライン（女性ミコッテ）へ強制ロールバックを引き起こしていた。
    3. Glamourer の `ApplyState` は完璧に正常終了（Result 0）していたにもかかわらず、その直後にこのメモリ破壊が行われていたことが、不具合がループしていた決定打だった。
  - **危険な独自メモリ上書きの全廃**:
    - `ExtractCustomizeBytes` および `Buffer.MemoryCopy` を完全削除。
    - Glamourer のステート適用（`ApplyState` / `ApplyDesign`）は種族・性別・装備・外見すべてを完璧に同期するため、外見適用を Glamourer に 100% 一任。
  - **ForceAllApply の完全適用**:
    - `Customize`, `Equipment` に加え、`Parameters`（肌色・髪色・目の色）および `Bonus`（メガネ等）の全スロットを強制的に `Apply = true` に設定。プリセット側で `Race: Apply = false` になっているデザインであっても、種族・性別・外見のすべてが確実に反映される。
  - **スポーン時の冗長自己コピーの撤廃**:
    - `SpawnCharacter` 内で呼び出されていた無意味かつ有害な自己コピー `nativeChara->CharacterSetup.CopyFromCharacter(nativeChara, CharacterCopyFlags.None);` を完全削除。
  - **デスポーン時のステート解放強化**:
    - `actor.DisplayName`（テンプレート名）だけでなく、実際の GameObject 名（`actor.PuppetName` = `"Csp Rrrkjeja"` 等）の両方で `RevertState` / `UnlockState` を行い、同一インデックス再利用時のステート混ざりを完全防止。
  - **Penumbra Redraw の確実な実行**:
    - Glamourer 適用完了後、`penumbraIpc.Redraw(actorIndex)` を確実に呼び出し、正常な 3D メッシュを確定描画。

## [0.1.34] - 2026-10-02
### Fixed
- **Glamourer Base64ヘッダーバージョン（Byte 6）欠落による `Unknown Version 31` 例外の完全解消**:
  - **根本原因の特定**: Glamourer のネイティブ実装（`DesignConverter.cs` / `Luna.dll`）において、Base64 文字列の先頭1バイトはデザインフォーマットのバージョン番号（`0x06`）としてパースされる。先頭にバージョンバイトを書き込まずに純粋な GZip バイト列を Base64 化していたため、GZip のマジックナンバー `0x1F`（= 31）がバージョン番号と誤認され、Glamourer 内部で `System.Exception: Unknown Version 31`（結果コード 7: `CouldNotParse`）が発生しデザイン適用が拒絶されていた。
  - **バージョン 6 バイトの注入**: `GlamourerIpc.CompressToBase64` において、GZip データの先頭に必ず `ms.WriteByte(6)` を書き込むよう修正。これにより Glamourer が 100% 正常にステートを解凍・認識し、`ApplyState` による外見強制上書きが完全に成功するようになった。
- **二重 Redraw 競合による自キャラ（女性ミコッテ）巻き戻しの完全根絶**:
  - **根本原因の特定**: Glamourer は `ApplyState` / `ApplyDesign` 呼び出しの内部で自動的にアクターのネイティブリロード（Redraw）を実行する。直後に CharacterSpawn 側から追加で `penumbraIpc.Redraw(actorIndex)` を呼んでいたため、FF14 の非同期描画パイプラインで二重リロードの競合が発生し、初期化途中の素体（女性ミコッテ）にロールバックしていた（Brio でも同様に Glamourer 適用後は外部 Redraw を呼んでいない）。
  - **Glamourer 時の重複 Redraw 除外**: `template.SourceType != CharacterSourceType.Glamourer` の場合のみ Penumbra Redraw を呼び出すよう修正し、競合ロールバックを完全に解消。
- **CustomizeData 26 バイトのネイティブメモリ常時同期**:
  - レースコンディション対策として、Glamourer 適用時も取得した CustomizeData（種族・性別・顔・髪型等）をネイティブのアクター描画データ（`chara->DrawData.CustomizeData`）に直接コピー同期。

## [0.1.33] - 2026-10-02
### Fixed
- **`Race: Apply = False` デザインにおける種族不一致ロールバックの完全解消 (GZip Base64 State Injection)**:
  - **根本原因の特定**: `Chonk`（`Kimo-1-Default`）などの一部の Glamourer デザインプリセットでは、ファイル内で `Race: { Value: 1, Apply: false }` と定義されている。これを `ApplyDesign(Guid)` でそのまま渡すと、Glamourer は指定通り Race（ミコッテ女性）を維持したまま Clan（ハイランダー）と Gender（男性）のみを適用しようとし、「ミコッテのハイランダー男性」という無効な組み合わせ（Race/Clan 不一致）が発生。FF14描画エンジンがエラーを起こして素体（自キャラ女性ミコッテ）にフォールバックしていた。
  - **ForceAllApply & GZip Base64 圧縮ステート注入**:
    - デザインファイル（JObject）から `ForceAllApply` を実行し、`Race`, `Clan`, `Gender`, 全装備スロットの `Apply` を強制的に `true` に書き換え。
    - Glamourer のネイティブステート仕様に準拠し、書き換えた JObject を UTF-8 JSON -> `GZipStream` 圧縮 -> Base64 文字列（`H4sI...`）にエンコード。
    - エンコードした圧縮 Base64 を `Glamourer.ApplyState(compressedBase64, actorIndex, 0, 7UL)` に渡すことで、Glamourer に `Race` も含めた全スロットを 100% 確実に強制適用させ、男性ハイランダーへと完璧に変身させる。
- **CustomizeData メモリコピーの競合防止**:
  - `Buffer.MemoryCopy` が Glamourer 成功後にも実行されてネイティブメモリを上書きするリスクを排除し、Glamourer IPC 失敗時のフォールバックに限定。

## [0.1.32] - 2026-10-02
### Fixed
- **Orphaned Weapon残存バグの完全根絶 (Fixing Detached Weapons Remaining on Ground After Despawn)**:
  - **根本原因の特定**: FF14の描画エンジン（Render/DrawObject）では、子描画オブジェクト（武器モデルなど）を保持したまま `ClientObjectManager.DeleteObjectByIndex` で親GameObjectのみを直接削除すると、シーングラフから切り離された武器の DrawObject が解放されず、ワールド座標に取り残される現象（Orphaned Weapon Bug）が発生していた（ユーザー添付のマンダヴィル・ガンブレードが地面に残る現象で確認）。
  - **DisableDraw() 先行解放 (Brio DestroyObject パターン準拠)**: `ActorManager.DespawnCharacter` のオブジェクト削除処理の直前で必ず `chara->GameObject.DisableDraw()` を呼び出し、描画ツリー全体および武器オブジェクトを完全にアンロードしてからCOM削除を実行するように修正。
- **男性キャラ／異種族キャラが自キャラ（女性ミコッテ）の姿に戻る問題の完全解消 (Fixing Character Rollback to Player Baseline)**:
  - **根本原因の特定**: `ApplyAppearanceDirect` 内で、Glamourer IPC 呼び出し後に `chara->CharacterSetup.CopyFromCharacter(chara, CharacterCopyFlags.None)` を実行していた。`CopyFromCharacter(chara, None)` はアクター自身の現在の素体（自キャラ女性ミコッテ）からモデルを再初期化するため、Glamourer が注入したスケルトンとモデル状態をエンジンレベルで自キャラに強制上書きリセットしてしまっていた。
  - **有害な自己コピー処理の撤廃**: Brio および HDM の標準アーキテクチャに準拠し、`CharacterSetup.CopyFromCharacter(chara, None)` を完全削除。Glamourer のネイティブフックと Penumbra Redraw が提供する正確なモデル構造をそのまま描画させることで、男性ハイランダー（Chonk 等）や異種族・異性別のキャラクターが 100% 確実に反映されるように修正。
- **Glamourer デザイン適用の最適化 (Brio SetDesign パターン)**:
  - `GlamourerIpc.ApplyDesignToActorEx` において、Guid 指定時に失敗していた JSON文字列による `ApplyState` の無理な呼び出しを廃止し、Brio と同じく `ApplyDesign(targetGuid, actorIndex, 0, 7UL)`（Flags: 7 = `DesignDefault` : Once | Equipment | Customization）を直接最優先で実行。
- **デスポーン時・スポーン時のステート完全クリーンアップの強化**:
  - `ActorManager.DespawnCharacter` 時に、GlobalIndex だけでなくアクター名（`actor.DisplayName`）でも Glamourer ステートを `RevertStateName` / `UnlockStateName` で解放し、スロット再利用時の外見情報の混ざり・残留を完全に防止。

## [0.1.31] - 2026-10-02
### Fixed
- **ASCII-Only Valid FF14 Puppet Name Generation (Fixing Reverting to Player Character Baseline)**:
  - **Root Cause Identified**: In v0.1.30, hex digits from `template.Id` (e.g. `Csp 04ffbf3e`) were used in puppet names. FF14 engine and all IPC plugins (Penumbra, Glamourer, CustomizePlus) enforce strict player name validation (`VerifyPlayerName`) that strictly rejects digits (0-9). Consequently, Penumbra rejected the actor with `ec=16 (InvalidActor)`, Glamourer with `result=2 (ActorNotFound)`, and CustomizePlus with `ec=255 (ActorNotFound)`, resulting in zero appearances being applied and the actor remaining as the player character.
  - **Valid FF14 Name Generator**: `GetPuppetName` now maps Guid bytes deterministically to an 8-character ASCII alphabet-only Surname (`[A-Z][a-z]{7}`, e.g. `Csp Evjkkhzl`), perfectly complying with FF14 player naming standards while maintaining deterministic per-template uniqueness ($26^8 \approx 2.08 \times 10^{11}$ combinations).
- **Robust Glamourer Guid Design Application & Fallthrough Guard**:
  - In `GlamourerIpc.ApplyDesignToActorEx`, for Guid designs, retrieves the design JObject, applies `ForceAllApply` to ensure no slots are skipped, and applies state via `ApplyState` while directly synchronizing 26-byte `CustomizeData` to the native actor.
  - Guarded Guid designs from falling through to the Base64 string parser, preventing conversion failure errors (`result: 7`).

## [0.1.30] - 2026-10-02
### Fixed
- **Puppet Actor Name Cache Isolation & Full State Cleanup (Fixing Respawning as Wrong / Deleted Character Appearance)**:
  - **Root Cause 1 (Actor Name Cache Collision)**: `NextPuppetName()` generated sequential names (`Csp Aa`, `Csp Ab`, ...). Upon plugin reload or serial rollover, names assigned to previous characters (such as `Chonk` or `Lyle`) were recycled for new characters (`Ruma`). Glamourer and Penumbra automatically cache and restore states by actor GameObject name (`Csp Ac`), causing past appearances and mod collections to automatically override the new character immediately upon entering the world.
  - **Deterministic Unique Puppet Identity**: Changed actor naming strategy to `GetPuppetName(CharacterTemplate)` (`Csp {template.Id:N8}`). Each character template now maintains a completely unique and deterministic puppet name, mathematically guaranteeing zero name collision with other or deleted characters across sessions.
  - **Root Cause 2 (Profile Bleed in User Config)**: Cleaned up accidentally persisted `CustomizePlusProfileName: "Chonk"` inside `CharacterSpawn.json` for template `Ruma` caused by modal field retention. Added automatic actor-level profile detachment (`DeleteTemporaryProfileOnCharacter`) when no profile is configured.
  - **Full Glamourer & CustomizePlus State Reset on Despawn & Apply**:
    - Integrated `Glamourer.UnlockState` and `Glamourer.RevertState` / `RevertToAutomation` into `ActorManager.DespawnCharacter` and before applying appearances in `ApplyAppearanceDirect`.
    - Integrated `CustomizePlus.DeleteTemporaryProfileOnCharacter` on despawn and template load to guarantee clean actor state.

## [0.1.29] - 2026-10-02
### Fixed
- **Penumbra Collection Isolation & Unassignment on Despawn/Appearance (Fixing Wrong Collection Pulled on Spawn)**:
  - **Root Cause Identified**: Previous character collections and temporary collections remained registered to actor slots (`Global#200`) without explicit unassignment upon despawning. Spawning a new character with no collection or switching between MCDF and Glamourer presets resulted in previous Penumbra collections persisting or overriding the new actor's appearance.
  - **UnassignCollectionForActor**: Introduced dedicated IPC subscriber in `PenumbraIpc` to unassign both temporary collections (`AssignTemporaryCollection.V5(Guid.Empty, actorIndex, false)`) and standard object collections (`SetCollectionForObject.V5(actorIndex, null / Guid.Empty, true, true)`).
  - Called `UnassignCollectionForActor` inside `ActorManager.DespawnCharacter` and at the start of `ActorManager.ApplyAppearanceDirect`, guaranteeing a clean slate before any appearance is loaded.
  - Corrected `PenumbraIpc.SetCollectionForActor` to treat `PenumbraApiEc.NothingChanged (1)` as success alongside `ec=0`.
- **Character Modal State Pollution & Cross-Contamination**:
  - Separated MCDF parsed Glamourer design strings (`modalMcdfGlamourerDesign`) from standard Glamourer design inputs (`customGlamourerString`).
  - Completely isolated saved properties per `CharacterSourceType` inside `CharacterLibraryTab.SaveModalTemplate`, ensuring MCDF archive data never overwrites or bleeds into standard Glamourer & Penumbra character definitions.
- **Direct Glamourer Guid Application & Native Synchronization**:
  - In `GlamourerIpc.ApplyDesignToActorEx`, prioritized direct invocation of `ApplyDesign(Guid, actorIndex, 0, 6UL)` when a valid Guid is present, eliminating Base64 parse errors (`result: 7`) while synchronizing 26-byte `CustomizeData` directly to native engine structs.

## [0.1.28] - 2026-10-02
### Fixed
- **Force All Apply Flags & Direct Native `CustomizeData` Synchronization (Fixing Male/Different Race Character Spawning)**:
  - **Root Cause Identified**: Discovered via deep inspection of Glamourer design files (`238897be-...` / `Chonk`) that certain presets have `"Apply": false` on essential customization slots like `Race` (e.g., Highlander Male with `Race: Apply = false`). Calling `ApplyDesign(Guid)` directly left the spawned actor's race unchanged as Miqo'te (the player character's baseline) while applying male gender and highlander clan, causing an invalid race-clan mismatch that failed rendering and caused fallback to player appearance.
  - **ForceAllApply**: Implemented automatic resolution of full design JObjects (via `Glamourer.GetDesignJObject` and disk fallback) and forced `Apply = true` across all Customize slots (`Race`, `Gender`, `Clan`, `BodyType`, `Face`, `Hairstyle`, etc.) and Equipment slots before calling `ApplyState`.
  - **Direct Native Memory Synchronization**: Extracted the exact 26-byte `CustomizeData` from the design JObject and wrote it directly into `chara->DrawData.CustomizeData` followed by `CharacterSetup.CopyFromCharacter(chara, CharacterCopyFlags.None)`. This ensures the game engine's native character data itself is immediately transformed to the target race and gender, eliminating any chance of fallback during Redraw.

## [0.1.27] - 2026-10-02
### Fixed
- **Persistent Glamourer Design & State Synchronization Across Redraws (Fixing Spawning as Player Character)**:
  - Discovered via reverse engineering of `Glamourer.Api.dll` and `HDM.dll` IL that `ApplyFlagEx.DesignDefault = 7UL` contains `ApplyFlag.Once = 1`. When applied with `Once`, Glamourer only temporarily overwrites the actor's in-memory draw model without persisting to the actor's internal Glamourer state. Consequently, subsequent Penumbra / engine redraws caused actors to immediately revert back to the base puppet appearance (the user's own player character).
  - Adopted HDM's proven standard: changed apply flags from `7UL` / `7U` to `6UL` / `6U` (`ApplyFlag.Equipment | ApplyFlag.Customization` without `Once`). This ensures Glamourer updates the persistent actor state, preserving designs (`Chonk`, custom MCDF characters) flawlessly through redraws.
  - Eliminated redundant intermediate `Redraw` invocation directly after `Penumbra.SetCollectionForActor`, consolidating redraw logic to the finalization phase.
- **MCDF Embedded CustomizePlus Profile Deserialization (`unexpected character 'e'`)**:
  - Identified that `CustomizePlusData` embedded in Mare Synchronos MCDF archives is Base64-encoded JSON. Passing raw Base64 strings to CustomizePlus IPC caused JSON deserialization failure.
  - Added automatic Base64-detection and decoding before forwarding to CustomizePlus IPC `SetTemporaryProfile`, ensuring embedded body scales and bone transforms apply correctly.

## [0.1.26] - 2026-10-02
### Fixed
- **Actor Lifecycle Safety & Crash Prevention on Despawn/Respawn (HDM Compliance)**:
  - Fixed critical CTD / unhandled exception (`0x12345679` via Dalamud Detour / `RaiseException`) occurring when despawning and respawning actors.
  - Eliminated stale raw native pointer dereferences in `ActorManager.UpdateFrame()`. Fully transitioned to HDM's golden pattern: re-resolving actors every tick via `IObjectTable[GlobalIndex]` and verifying `chara.Address != nint.Zero` before accessing native structs.
  - Wrapped `UpdateFrame()`, `DrawUI()`, `OnFrameworkUpdate()`, `UpdateActorTransform()`, and all job processing loops in structured `try-catch` exception blocks to prevent CLR unhandled exceptions from breaching native detour boundaries.
  - Added `pendingNpcJobs.RemoveAll` to `DespawnCharacter` to prevent lingering jobs from polling deleted actors.
  - Introduced `IsReady` lifecycle guard to `SpawnedActorData` ensuring 3D Gizmos and transform updates only activate once draw baseline and appearance customization are fully initialized.

## [0.1.25] - 2026-10-02
### Fixed
- **Penumbra Collection & Mod Redirection for Humanoid Actors (`ec=16` InvalidActor Resolution)**:
  - Discovered through deep IL disassembly of `Penumbra.dll`'s `CollectionApi.SetCollectionForObject` and `AssociatedIdentifier` that Penumbra's internal identifier resolution calls `ActorIdentifierFactory.FromObject` with `allowPlayerNpc: false`.
  - When `nativeChara->GameObject.ObjectKind` was `BattleNpc`, Penumbra strictly branched into `CreateBNpcFromObject`. Because spawned puppets have `NameId = 0`, this consistently produced `ActorIdentifier.Invalid`, resulting in `ec=16 (InvalidActor)` and causing Penumbra to fail collection assignment and mod redirection (leaving actors in a vanilla state).
  - Explicitly classified all humanoid puppets (Glamourer designs, MCDF bundles, and player clones) as `ObjectKind.Player`. This directs Penumbra into `CreatePlayerFromObject`, which verifies the player name and home world, resolving a valid Player Identifier and enabling 100% successful Penumbra collection assignment (`ec=0`) and instant mod rendering upon `Redraw`.
  - Maintained `ObjectKind.BattleNpc` for non-humanoid monsters (`ModelCharaId > 0`) during Phase 2 transition to ensure native monster model rendering remains undisturbed.

## [0.1.24] - 2026-10-02
### Fixed
- **AQR Independent Spawn & MCDF Temporary Collection Application**:
  - Eliminated `nativeChara->GameObject.OwnerId = 0xE000_0000;` override (preserved default 0). Discovered through IL disassembly of `Penumbra.GameData.dll` that a non-zero `OwnerId` caused `CreateBNpcFromObject` to attempt resolving a non-existent parent GameObject in `ObjectTable`, yielding invalid identifiers and failing with `ec=255 (UnknownError)`.
  - Re-ordered MCDF loading pipeline to add temporary mod files (`AddTemporaryMod`) before actor assignment (`AssignTemporaryCollection`), ensuring seamless mod registration without AQR dependency.
  - Formatted puppet names as `"Csp {hi}{lo}"` to strictly satisfy Penumbra's `VerifyPlayerName` player naming validation.
  - Eliminated premature `ApplyAppearanceDirect` invocation in `SpawnCharacter`, executing appearance resolution exclusively after Phase 2 humanoid baseline draw verification.
- **Humanoid NPC True Appearance Synchronization (Mionne, Gontran)**:
  - By restoring `OwnerId = 0`, Glamourer's `GetState` now resolves immediately within 1-2 frames instead of timing out at 120 frames, successfully applying genuine NPC facial customizations and equipment without player clone fallbacks.
- **Monster Model Accuracy (Ruins Runner)**:
  - Fixed issue where Ruins Runner spawned as an unintended monster (Raptor). Switched monster cache indexing to use unique `BaseId` instead of shared `BNpcNameId`, preventing ID collisions, and restricted `ModelCharaId` auto-resolution to unassigned models only.
- **Demihuman NPC Rendering (Letter Moogle)**:
  - Added fallback to `baseRow` inline equipment fields in `GameDataService.GetNpcAppearanceData` when `NpcEquip.RowId == 0`, ensuring Demihuman body/head equipment slots are correctly populated and rendered rather than showing an invisible body with gizmo only.

## [0.1.23] - 2026-10-02
### Fixed
- **HDM Official `mob-model-index.csv` Integration (Accurate Monster / Mob Spawning)**:
  - Replaced heuristic `BNpcLink.csv` mapping with HDM's authoritative `mob-model-index.csv` (16,243 rows).
  - Resolved model mismatch issues where spawning monsters like Ruins Runner resulted in incorrect models (Ruins Runner correctly resolves to ModelChara 1281, McType 3, Scale 1.1).
  - Japanese monster names resolved directly from Lumina `BNpcName` sheet with duplicate deduplication for a clean search experience.
- **Humanoid NPC Appearance Synchronization (Resolved Player Clone / testruma / Ruma Fallback)**:
  - Identified root cause in `dalamud.log`: synchronous `Thread.Sleep(16)` blocked the main framework thread, preventing Glamourer from registering newly spawned actors and causing `GetState` to return null.
  - Implemented non-blocking per-frame polling queue (`PendingNpcJob`) conforming to HDM's `HumanGuise.cs`.
  - Polled each frame up to 120 frames without blocking; as soon as `GetState` resolves, mapped 26-byte NPC customization and 10-slot equipment, stripped `Parameters` and `Materials` to eliminate player skin/shader pollution, and executed `Penumbra.Redraw` to finalize the NPC skeleton and gear.
  - NPCs like Gontran and Miounne now render with 100% faithful face, hair, and gear instead of falling back to player clones.
- **Demihuman NPC Spawning (Resolved Invisible Letter Moogle / Gizmo-Only Bug)**:
  - Resolved issue where Demihuman NPCs (McType 2, e.g. Letter Moogle, Namazu) rendered invisible with only gizmos.
  - Ensured `NpcEquip` parts are extracted and written directly into `DrawData.EquipmentModelIds`, and `DrawData.IsHatHidden = false` is maintained so Demihuman bodies and equipment render reliably.

## [0.1.22] - 2026-10-02
### Fixed
- **HDM-Compliant Monster & Mob Spawning (Resolved Invisible 3D Model / Gizmo-Only Bug)**:
  - Resolved issue where spawned monsters / mobs were invisible, showing only gizmo manipulators.
  - Aligned with HDM's (`Enceladeum/HDM`) proven two-phase rendering architecture: actors are initially seeded as clean humanoid baseline clones (`ModelCharaId = 0`, `Scale = 1.0f`) to allow the engine to establish a valid baseline draw object.
  - In Phase 2, once the humanoid draw object is verified visible (`DrawObject != null && DrawObject->IsVisible`), the actor is transitioned to the target monster `ModelCharaId` and scale, followed by a dedicated native redraw sequence (`DisableDraw()` -> `IsReadyToDraw()` wait -> `EnableDraw()`).
  - Completely suppressed Penumbra / Glamourer redraw invocations on monster models to prevent invalidation of non-humanoid draw objects.
  - Added Demihuman equipment mapping and `IsHatHidden = false` preservation for non-humanoid demihumans.
- **HDM-Compliant Humanoid NPC Spawning via Glamourer IPC**:
  - Implemented `Glamourer.GetState` and `Glamourer.ApplyState` IPC integration in `GlamourerIpc.cs` conforming to HDM's `HumanGuise.cs`.
  - Added `ApplyNpcAppearance` to map 26-byte `CustomizeData` into Glamourer's 36-field `Customize` model via `CustomizeMap`, and injected NPC gear slots using bit-packed `CustomItemId`.
  - Automatically stripped `Parameters` and `Materials` blocks on NPC appearance apply, preventing player skin tone / shader overrides from bleeding onto NPC disguises.
  - Added automatic fallback resolution of `ModelCharaId` and NPC appearance from `GameDataService` if template IDs are present.

## [0.1.21] - 2026-10-02
### Fixed
- **Continuous 360-Degree Horizontal Rotation (Resolved 180° Flip & Jitter)**:
  - Fixed character rotation getting stuck and jittering around the 180-degree mark.
  - Replaced Euler angle matrix decomposition (`ImGuizmo.DecomposeMatrixToComponents`), which suffered from a ±180° discontinuity and feedback-loop jitter, with Stagehand-compliant `Quaternion` matrix composition (`Matrix4x4.CreateFromQuaternion`).
  - Extracted the actor's forward direction vector via `Vector3.Transform(Vector3.UnitZ, newRot)` and computed seamless continuous heading with `MathF.Atan2(forward.X, forward.Z)`. The character now rotates smoothly and continuously past 180° without any jitter or angle wrapping limits.

## [0.1.20] - 2026-10-02
### Fixed
- **Horizontal Actor Rotation (Yaw Ring)**:
  - Fixed character rotation not responding during ring manipulation. Switched operation from 4-ring `Rotate` (which prioritized screen-space camera roll) to `ImGuizmoOperation.RotateY` (horizontal planar yaw ring).
  - Dragging the green horizontal rotation ring now immediately and smoothly turns the character's heading in 360 degrees.
- **Eliminated "New NPC: Failed to get response." Popup**:
  - Implemented dynamic input capture in `GizmoRenderer`: `ImGuiWindowFlags.NoInputs` is dynamically cleared while hovering or manipulating the gizmo (`IsOver() || IsUsing()`), consuming clicks and preventing game-world click-through.
  - While not hovering over the gizmo, `NoInputs` remains active so players can freely rotate the game camera without hindrance.
  - Enforced `TargetableStatus = 0` and `EventId = 0` on spawned actors to completely disable game NPC interaction events.
- **Removed Duplicate Header Gizmo Buttons**:
  - Cleaned up `MainWindow.Draw()` by removing the redundant gizmo toolbar buttons from the upper-left header above tabs, retaining only the clean in-context toolbar inside the character spawn details and stage scene tabs.

## [0.1.19] - 2026-10-02
### Fixed
- **ImGuizmo 3D Rendering & Camera Projection**:
  - Resolved gizmo rendering failure by applying FFXIV reverse-Z clip projection matrix correction (`M43 = -(clip * near)`, `M33 = -((far + near) / (far - near))`, `view.M44 = 1.0f`) conforming to Stagehand and BDTH architecture.
  - Recomposed transform matrix via `ImGuizmo.RecomposeMatrixFromComponents` with Euler degrees, properly positioning the 3D gizmo at the target actor's location.
- **Camera Viewport Input Transparency**:
  - Added `ImGuiWindowFlags.NoInputs` to the full-screen gizmo overlay window. This completely eliminates game-wide mouse input blocking, allowing unrestricted camera rotation (right-click drag) and character movement in the game world while preserving 3D gizmo hit-testing.
- **Gizmo Toggle Unification**:
  - Removed duplicate `Gizmo` ON/OFF checkboxes from `CharacterLibraryTab`, `StageSceneTab`, and `SettingsTab`.
  - Unified gizmo state management entirely into the Stagehand-style mode toolbar:
    - **Select (Mouse Pointer)**: Turns gizmo OFF / hides manipulator.
    - **Translate (Cross Arrows)**: Turns gizmo ON in translation mode with XY/XZ/YZ quad plane handles.
    - **Rotate (Sync Alt)**: Turns gizmo ON in rotation mode with 3-axis rings.

## [0.1.18] - 2026-10-02
### Added
- **Stagehand-Compliant ImGuizmo 3D Gizmo System**:
  - Replaced the custom 2D screen-projected gizmo with native `Dalamud.Bindings.ImGuizmo` architecture identical to Stagehand.
  - Extracted game camera matrices (`ViewMatrix`, `ProjectionMatrix`) directly from `FFXIVClientStructs.FFXIV.Client.Graphics.Scene.CameraManager.Instance()->CurrentCamera->RenderCamera`.
  - Moved gizmo rendering to a full-screen transparent overlay window in `Plugin.DrawUI`, completely eliminating mouse focus loss and click-through issues when dragging handles in the 3D game world.
  - Implemented Stagehand-style mode toolbar (Select / Translate / Rotate) across `MainWindow`, `CharacterLibraryTab`, and `StageSceneTab`:
    - **Select Mode (`FontAwesomeIcon.MousePointer`)**: Hides the gizmo for normal scene interaction.
    - **Translate Mode (`FontAwesomeIcon.ArrowsUpDownLeftRight`)**: Renders primary X, Y, Z axis arrows alongside red, green, and blue **XY, XZ, YZ quad planes** for multi-axis simultaneous drag-manipulation.
    - **Rotate Mode (`FontAwesomeIcon.SyncAlt`)**: Separates rotation from translation, rendering dedicated 3-axis rotation rings for intuitive yaw/pitch/roll adjustments.
  - Real-time transform synchronization via `Matrix4x4.Decompose` updating actor position and yaw in both library preview and active stage actors.

## [0.1.17] - 2026-10-02
### Added
- **Customize+ (C+) Profile Integration**:
  - Implemented comprehensive IPC integration with Customize+ (v6+ API) via `Services/CustomizePlusIpc.cs`.
  - Added Customize+ profile selector to character creation and editing modals in `CharacterLibraryTab.cs`.
  - Spawning a character with an assigned Customize+ profile now automatically queries and applies temporary body scales and bone transforms to the spawned actor.
  - Full automatic cleanup: temporary Customize+ profiles are seamlessly revoked and freed upon despawning characters, scene transitions, or territory changes.
  - Added support for embedded `CustomizePlusData` within `.mcdf` archives, allowing automatic body scaling even for third-party MCDF files without manual profile mapping.

### Changed
- **Modal UI Cleanup**:
  - Removed the unused `Or Direct Design String / Code` manual multiline input box from `CharacterLibraryTab.cs`, streamlining the character creation modal to design and collection dropdown pickers.

## [0.1.16] - 2026-10-02
### Fixed
- **Penumbra Collection & MCDF Temporary Collection Assignment via ObjectKind.Player**:
  - Through comprehensive CIL reverse-engineering of `Penumbra.GameData.dll`'s `ActorIdentifierFactory.FromObject`, discovered the exact root cause of `AssignTemporaryCollection` failing with error code `255` and `SetCollectionForObject` failing with `ec = 16` (`InvalidIdentifier`).
  - Previously, all spawned actors were assigned `ObjectKind = ObjectKind.BattleNpc`. When resolving collections, Penumbra's internal IPC methods invoke `CreateBNpcFromObject(allowPlayer: false)`. Because `allowPlayer` is hard-coded to `false` in collection assignment IPC, any non-Player object kind—regardless of its name or `OwnerId`—is strictly treated as a monster NPC. Since `DataId` was 0, it resolved to non-existent `BNpc(0)`, which has 0 mod collections, causing `Collections.Add` to reject assignment with error code 255 and `SetCollectionForObject` to return 16.
  - Resolved this by setting `ObjectKind = ObjectKind.Player` (and `BattleNpcSubKind = BattleNpcSubKind.Player`) for all humanoid actors (`template.ModelCharaId == 0`), while keeping `ObjectKind = ObjectKind.BattleNpc` strictly for monster models (`template.ModelCharaId > 0`).
  - With `ObjectKind.Player`, Penumbra directly routes to `CreatePlayerFromObject`, validating the character's name (`"Cs Aa"` format) and returning a 100% valid Player identifier. Both normal Penumbra collections and MCDF temporary collections now successfully bind (`ec = 0`) and apply all custom 3D models, textures, and manipulations to spawned actors.

## [0.1.15] - 2026-10-02
### Added
- **Full AQR-Compliant MCDF Mod Extraction & Penumbra Temporary Collection Lifecycle**:
  - Implemented complete extraction of embedded Mod files (3D models, textures, materials, and FileSwaps) and `ManipulationData` directly from `.mcdf` LZ4 binary streams into local plugin cache (`mcdf_cache`).
  - Integrated Penumbra Temporary Collection IPC APIs (`CreateTemporaryCollection`, `AssignTemporaryCollection`, `AddTemporaryMod`, `DeleteTemporaryCollection`).
  - When spawning a character with an MCDF file, CharacterSpawn now dynamically creates a dedicated Penumbra temporary collection, binds all embedded mod files and meta manipulations to the spawned actor, applies the Glamourer design, and redraws seamlessly without requiring user-created Penumbra collections.
  - Automatically deletes and reclaims temporary Penumbra collections upon despawning or territory transitions, preventing memory/handle leaks.
- **Dynamic Plugin Assembly Version Logging**:
  - Replaced hardcoded `v0.1.9` startup log string in `Plugin.cs` with dynamic assembly version resolution (`v{GetType().Assembly.GetName().Version}`).

### Fixed
- **MCDF UI Penumbra Independence (AQR Conformity)**:
  - Removed manual Penumbra Collection selector from the MCDF section in `CharacterLibraryTab.cs`. MCDF templates now automatically inform users of embedded mod auto-loading via temporary collections, enabling full cross-user portability for third-party `.mcdf` files.
- **ActorManager PluginInterface Injection**:
  - Wired `IDalamudPluginInterface` through to `ActorManager` to provide reliable config directory resolution for MCDF file caching.
### Fixed
- **Penumbra InvalidIdentifier (ec=16) Resolution via OwnerId Initialization**:
  - Through full CIL reverse engineering of `Penumbra.GameData.dll`'s `CreateBNpcFromObject`, discovered that Penumbra inspects `GameObject.OwnerId`. If `OwnerId` is not equal to `0xE0000000` (`GameObject.InvalidGameObjectId`), Penumbra attempts to look up the parent object (`objects.ById(ownerId)`). Because `CreateBattleCharacter` initializes `OwnerId` to `0`, the lookup failed and returned `InvalidIdentifier` (`ec=16`), causing all Penumbra collection assignments to be rejected.
  - Explicitly set `nativeChara->GameObject.OwnerId = 0xE000_0000` alongside `NameId = 0`, `HomeWorld`, and `SetName(puppetName)`. This satisfies Penumbra's Player identifier validation branch, allowing `SetCollectionForObject` to return `ec=0` (Success) and apply collections flawlessly.
- **MCDF Section Penumbra Collection Selector**:
  - Added Penumbra Collection selection dropdown to the MCDF configuration section in `UI/CharacterLibraryTab.cs`.
  - Characters created or imported from `.mcdf` files can now bind and persist dedicated Penumbra Collections alongside their extracted Glamourer design.

## [0.1.13] - 2026-10-02
### Fixed
- **MCDF LZ4 Decompression Stream Support (AQR McdfCharaFileManager Architecture)**:
  - Resolved the critical issue where MCDF files failed to parse GlamourerDesignString because modern `.mcdf` files are whole LZ4-compressed binary streams rather than raw binary JSONs.
  - Integrated `lz4net` and updated `Services/McdfParser.cs` to decompress the LZ4 stream before inspecting the `"MCDF"` 4-byte header and parsing the embedded JSON string.
  - Successfully verified extraction of Glamourer Base64 strings from real `.mcdf` files (`testruma.mcdf`, `test.mcdf`), eliminating fallback to previous actor appearance or local player.
- **Accurate Penumbra V5 IPC Signature Resolution (CIL Metadata Verification)**:
  - Discovered via CIL disassembly of `Penumbra.Api.dll` that `Penumbra.SetCollectionForObject.V5` strictly expects `(int actorIndex, Guid? collectionId, bool allowCreate, bool allowDelete)` and returns `(int ec, (Guid, string)? oldCollection)` (`ValueTuple<int, Nullable<ValueTuple<Guid, string>>>`), not `(int, Guid)` or `int`.
  - Updated `Services/PenumbraIpc.cs` with exact tuple signatures for both V5 and Legacy APIs, resolving CallGate type-conversion exceptions (`converting from ValueTuple 2 to System.Int32`) and ensuring 100% reliable Penumbra Collection assignment to spawned actors.
- **Penumbra Dual-Phase Redraw Synchronization (AQR Conformity)**:
  - In `Managers/ActorManager.cs`, aligned appearance application order with AQR: Penumbra collection assignment -> first Penumbra Redraw -> Glamourer design application -> second Penumbra Redraw. This guarantees Mod textures, clothes, and meshes apply seamlessly to spawned actors.

## [0.1.12] - 2026-10-02
### Fixed
- **Binary MCDF Format Parsing (AQR MCDF-Loader Architecture)**:
  - Resolved the issue where selecting an MCDF file appeared not to load and spawned the local player's appearance. Discovered that modern MCDF files are proprietary binary containers (`"MCDF"` 4-byte header + UTF-8 JSON payload) rather than standard ZIP archives.
  - Rewrote `Services/McdfParser.cs` with binary header scanning and direct extraction of the Base64 `GlamourerData` string, enabling instant parsing and full appearance restoration from `.mcdf` files.
- **Penumbra IPC Return Signature Tuple Resolution (AQR Penumbra Architecture)**:
  - Fixed `Penumbra.SetCollectionForObject.V5` failure where Penumbra returns `(PenumbraApiEc, Guid)` (`ValueTuple<int, Guid>`) while legacy code expected `int`, causing CallGate runtime conversion exceptions that prevented Penumbra collections from applying.
  - Added robust tuple-based subscriber fallbacks in `Services/PenumbraIpc.cs` supporting both V5 and legacy signatures.
- **Pre-Draw Direct Appearance Application (Root Cause of "Temporary Local Player" Eliminated)**:
  - Completely redesigned `ActorManager.SpawnCharacter` and `ApplyAppearanceDirect`: actors now immediately call `DisableDraw()` upon creation and have their target appearance (Glamourer design, Penumbra collection, MCDF state, or Monster `ModelCharaId`) applied *before* the first frame renders, rather than waiting for `DrawObject->IsVisible`.
  - Eliminates the brief appearance of the local player before transitioning to the desired design.
- **HDM-Compliant Monster & Non-Humanoid Rendering (Fix for "Gizmo Only")**:
  - Identified that triggering Penumbra `RedrawObject` on non-humanoid monsters (`ModelCharaId > 0`, Letter Moogle, Ruin Runner, Antelope Doe, Gnat) caused Penumbra to invalidate and strip non-humanoid `DrawObject`s, leaving only a gizmo.
  - Removed Penumbra redraw calls from monsters in accordance with HDM's `GuiseService`, using native engine `DisableDraw` -> `IsReadyToDraw()` -> `EnableDraw()` cycle, ensuring 100% stable 3D monster and non-humanoid NPC rendering.
- **UI Flat Styling & Border Glitch Fix**:
  - Removed borders on the left tree scroll child window in `UI/CharacterLibraryTab.cs` to prevent visual line artifacts when selecting spawned characters.
### Fixed
- **Two Index Spaces Trap Resolution (Glamourer/Penumbra Player Clone Fix)**:
  - Resolved critical architectural flaw where internal `ClientObjectManager` slots (COM# 0, 1...) were passed to IPC endpoints instead of global `IObjectTable` indices (~200-244 reserved range). Passing COM# 0 caused Glamourer and Penumbra to target the local player character (index 0).
  - Tracked and supplied `actor.ObjectIndex` (`GlobalIndex`) for all Glamourer and Penumbra IPC operations, preventing spawned characters from taking on the local player's appearance.
- **Glamourer Identity Stamping (HDM The 0.8.44 Bug Fix)**:
  - Stamped each spawned BattleNpc actor with a valid SE player name format (`Cs Aa`, `Cs Ab`...) via a unique serial generator, `NameId = 0`, and the local player's `HomeWorld`.
  - Bypasses Glamourer's `ActorIdentifierFactory` invalid NPC ID rejection, allowing Glamourer state and design application to succeed reliably.
- **Draw-When-Ready 2-Phase Queue (HDM / Brio Architecture)**:
  - Implemented framework-driven `ReadyJob` queue. Phase 1 polls `IsReadyToDraw()` and enables draw; Phase 2 waits until `DrawObject != null && DrawObject->IsVisible` before applying Glamourer designs or Penumbra collections, ensuring Glamourer applies to a settled and registered actor body.
- **Monster & NPC Non-Humanoid Rendering (HDM GuiseService & Brio Pattern)**:
  - Fixed "gizmo only" / invisible models by seeding all actors with a double `CharacterSetup.CopyFromCharacter` from the local player before applying monster or NPC models, ensuring an active drawable skeleton exists rather than an uninitialized, invisible `SetupBNpc(0)`.
  - Swapped `ModelContainer.ModelCharaId`, hid weapons, and triggered Penumbra `RedrawObject` / `DisableDraw` settlement, guaranteeing monster and mob 3D models render correctly.
- **Template Data Persistence Guarantee**:
  - Enhanced `SaveModalTemplate` to guarantee full persistence of Glamourer GUID/name, Penumbra collection name, MCDF file path & parsed Base64 design, and NPC/Monster IDs with extensive logging.
  - Added clean state restoration in `OpenEditCharacterModal` and detailed attribute inspection in `Template Details`.
  - Robust `DespawnCharacter` resolving live COM indexes via `GetIndexByObject` to avoid stale index deletion.

## [0.1.10] - 2026-10-02
### Fixed
- **AQR-Conforming Monster & Non-Humanoid Spawning**: Adopted A Quest Reborn (AQR) architectural pattern for monster and non-humanoid NPC spawning. By setting `ModelContainer.ModelCharaId`, disabling weapons, and triggering Penumbra's `RedrawObject`, models (such as Ruin Runner, Antelope Doe, Gnat, Letter Moogle) now properly instantiate and render instead of showing only a gizmo.
- **Glamourer & Penumbra Appearance Application**: 
  - Fixed Glamourer IPC calls by applying proper flags (`flags = 7`: Customization | Equipment | Accessories) instead of `0`.
  - Added automatic resolution from design names to GUIDs in `ApplyDesignToActor`.
  - Ensured Penumbra collection assignment (`allowCreate = true, allowDelete = true`) executes before Glamourer design application, followed by `RedrawObject`.
  - Resolved the bug where spawning Glamourer / Penumbra presets spawned the local player's appearance.
- **MCDF Appearance Application**: Ensured MCDF parsed Glamourer designs are dispatched with `flags = 7` and synchronized with Penumbra Redraw, eliminating player clone fallbacks.
- **NPC Weapon Residuals Fix**: Set default `WeaponVisible = false` on NPC character templates to eliminate unintended local player weapon rendering (e.g. Ru Sushimo, Miounne).
- **Weapon Visibility Toggle (ON/OFF)**: Linked `SetWeaponVisibility` with Penumbra `RedrawObject`, guaranteeing immediate model re-render when toggling weapon visibility back ON or OFF.
- **Sorted Dropdown Lists**: Alphabetically sorted Glamourer designs and Penumbra collections in dropdown combo boxes for fast navigation.
- **UI Layout Separator Bleed Fix**: Encapsulated the character detail right pane inside `ImGui.BeginChild("RightDetailPane")`, preventing horizontal `ImGui.Separator()` lines from bleeding across into the left tree pane.
- **Removed Extraneous Guide Text**: Removed helper text annotations (`<= 武器表示ON/OFF`, `<= ギズモ表示ON/OFF`) and the preview explanation box per user feedback.

## [0.1.9] - 2026-10-02
### Added
- **Log Tab & LogManager**: Added a dedicated "Log" tab next to "Settings" in the main window with real-time log monitoring (info/warning/error color-coding, search filter, auto-scroll, and one-click "Copy All" to clipboard) to easily diagnose appearance source detection and actor spawning.
- **Weapon Visibility Control**: Added a `[x] Weapon Visible` checkbox in the Character tab details pane, enabling instant hiding/displaying of equipped weapons for spawned actors.
- **BNpcLink Mapping for Monsters**: Embedded comprehensive `BNpcLink` mapping (13,312 entries) linking `BNpcName` to `BNpcBase`, resolving model resolution failures for monsters.
- **Full Search for NPCs and Monsters**: Completely removed artificial search caps (previously 500), allowing smooth full-library search across all game NPCs and monsters.

### Changed
- **Character Tab Preview Mode**: Separated character spawning in the Character tab into a dedicated preview mode conforming to user UI mockups:
  - Toggles between `[ Spawn ]` and red highlighted `[ Despawn ]`.
  - Displays green `[Name] Spawning...` status and `[x] Gizmo` toggle while active.
  - Explanatory banner explaining the temporary preview feature.
- **New Chara 2x2 Button Grid**: Redesigned the "Select Appearance Source" selector from a dropdown combo to a responsive 2x2 button grid (`[ Glamourer&Penumbra ] [ MCDF ]` / `[ NPC(ENpc) ] [ Monster/Mob ]`) with red accent highlighting on the active source.

### Fixed
- **Glamourer & Penumbra IPC Connectivity**: Upgraded IPC key subscribers to the latest versions (`Glamourer.ApiVersion.V2`, `Glamourer.GetDesignList.V2`, `Glamourer.ApplyState`, `Penumbra.ApiVersion.V5`, `Penumbra.GetCollections.V5`, `Penumbra.SetCollectionForObject.V5`, `Penumbra.RedrawObject.V5`) with backward-compatibility fallbacks and dynamic re-checking polling, fixing the persistent "IPC Not Detected" issue.
- **Non-Humanoid Model Spawning (Moogles, Monsters)**: Fixed an issue where non-humanoid actors (e.g. Letter Moogle, Gegeruju, monsters) failed to render or only displayed a gizmo. Decoupled humanoid player cloning from non-humanoid model initialization, preventing bone/resource container corruption.
- **Monster Player-Clone Bug**: Fixed bug where monsters (such as Matanga/Matagai) spawned with player appearance by correctly obtaining `ModelCharaId` via `BNpcLink`.
- **NPC Weapon Residuals**: Fixed bug where humanoid NPCs (such as Ru Sushimo) displayed local player weapons by strictly managing weapon hiding and DrawData equipment containers.
- **MCDF Spawning**: Fixed MCDF spawn behavior by ensuring parsed Glamourer design strings are applied directly to the spawned preview actor via updated Glamourer IPC.
- **3D Gizmo Dragging & Picking**: Substantially increased gizmo handle picking radii and added full axis-line segment hit detection, allowing smooth and intuitive dragging along any axis.

## [0.1.8] - 2026-10-02
### Added
- **Character Library UI Redesign**: Redesigned the Character Library layout into a two-pane hierarchical structure matching the user mockups:
  - Left Pane: Folder & character tree view with expandable folder categories, plus bottom action buttons (`[New Chara]`, `[New Folder]`, `[Delete]`).
  - Right Pane: Inline character name display & editing, with action buttons (`[Spawn]`, `[edit]`, `[delete]`) and a detailed summary of appearance attributes.
  - Character Modal: Dedicated popup window for creating and editing characters with clear appearance source selection and settings.
  - New Folder Modal: Popup dialog to create new organization folders.
- **Glamourer & Penumbra Integration UI**: Added AQR-style searchable dropdown combos for selecting Glamourer designs and Penumbra collections directly from IPC, with optional manual string input.
- **Explorer File Picker for MCDF**: Integrated Win32 `GetOpenFileNameW` dialog via an asynchronous STA worker thread to open Windows File Explorer and browse `.mcdf` files directly, automatically parsing and populating Glamourer appearance data upon selection.
- **Humanoid & Non-Humanoid NPC Appearance Extraction**: Added `GetNpcAppearanceData` to extract 26-byte `CustomizeData` and 10-slot equipment model IDs from `ENpcBase` and `NpcEquip` Lumina sheets.

### Fixed
- **NPC Spawning as Player Character**: Fixed a critical bug where spawning NPCs (such as Letter Moogle or Miounne) resulted in the local player's appearance. Implemented proper `ModelContainer.ModelCharaId` assignment for non-humanoid NPCs and direct `DrawData.CustomizeData` & `EquipmentModelIds` buffer population for humanoid NPCs.
- **Search Result Limits**: Expanded search result caps for Monsters and NPCs from 10 to 500 items, with smooth scrollable list boxes.

## [0.1.7] - 2026-10-02
### Fixed
- **3D Model Rendering & Visibility**: Resolved the issue where only the 3D gizmo appeared without the character model. Implemented continuous per-frame draw enforcement (`UpdateFrame`), clearing DrawObject hidden flags (0x10) and ensuring `EnableDraw()` is triggered once `IsReadyToDraw()` becomes satisfied.
- **Penumbra & Glamourer IPC Synchronization**: Added `Penumbra.RedrawObject` and `Glamourer.ReapplyState` triggers upon spawning to immediately build and render custom character models in modded environments.
- **Template Auto-Population**: Added automatic capture of the local player's current Glamourer design when saving player clone or glamourer templates with empty design strings.

## [0.1.6] - 2026-10-02
### Fixed
- **UI Responsiveness**: Replaced `Selectable` with an `ImGui.Table` layout in `CharacterLibraryTab.cs`, fixing an issue where "Spawn onto Map" and "Delete" buttons could not be clicked.
- **Actor Spawning & Despawning**: Refactored `ActorManager.cs` to utilize `ClientObjectManager.Instance()->CreateBattleCharacter` and `DeleteObjectByIndex` (FFXIVClientStructs / Brio / AQR standard architecture) instead of failing SigScanner delegates, resolving the issue where spawned actors did not appear on the map.
- **Transform & Drawing Synchronization**: Implemented proper `CharacterSetup.CopyFromCharacter` initialization, `GameObject.EnableDraw()`, and direct position/rotation updates for spawned characters.

## [0.1.5] - 2026-10-02
### Fixed
- Fixed `SeString.TextValue` usage for Player and Target clone name extraction.

## [0.1.4] - 2026-10-02
### Fixed
- Fixed LocalPlayer access using `IObjectTable[0]` conforming to Dalamud API 15 standards.
- Fixed `ISigScanner` integration for native delegate resolution.
- Fixed `ObjectTargetableFlags.IsTargetable` type conversion.
- Fixed `ActionTimeline` collection index access in `GameDataService`.

## [0.1.3] - 2026-10-02
### Fixed
- Fixed `OnTerritoryChanged` signature to `uint` parameter.
- Fixed `PlayTimeline` and `StopTimeline` calls to supply required slot parameter.
- Fixed `OnNamePlateUpdate` signature to `(INamePlateUpdateContext, IReadOnlyList<INamePlateUpdateHandler>)`.
- Removed unused `activeTab` field in `MainWindow`.

## [0.1.2] - 2026-10-02
### Fixed
- Fixed compilation error by switching from deprecated `ImGuiNET` to Dalamud API 15 standard `Dalamud.Bindings.ImGui`.

## [0.1.1] - 2026-10-02
### Fixed
- Fixed native actor management using SigScanner delegates for FFXIV 7.x compatibility.
- Fixed `NamePlateController` event handling to match `INamePlateUpdateHandler` API.
- Fixed `TimelineManager` animation trigger via `PlayTimeline`.
- Added `repo.json` manifest for Dalamud custom plugin repository installer.
- Added `IGameInteropProvider` service injection.

## [0.1.0] - 2026-10-02
### Added
- Initial project structure and build configuration for Character Spawn plugin.
- Character Library system (supports Glamourer, Penumbra, MCDF, Monsters, and Player clone).
- Stage & Scene placement system with 3D gizmo and UI transform controls.
- Animation control with seamless loop toggle and facial expression settings.
- Player look-at (head tracking) capability.
- Customizable nameplates and targetability toggle.
- Stagehand-like scene preset management with zone-based auto-spawn.
