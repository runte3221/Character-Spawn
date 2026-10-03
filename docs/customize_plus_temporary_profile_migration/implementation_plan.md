# 実装計画: Customize+ 一時プロファイル方式への完全移行と恒久設定汚染根絶 (v0.1.52.0)

## 1. 概要
Character Spawn において Customize+ のプロファイルを設定したパペットをスポーンさせた際、体型が変わらなかったり、CustomizePlus のユーザー正規プロファイル設定ファイルにパペット名（`"Actor Ac"` 等）が勝手に追記・保存されてしまう問題を根本解決する。

## 2. 根本原因の技術的分析

1. **恒久プロファイル書き換え (`AddPlayerCharacter`) の副作用**:
   - `CustomizePlus.Profile.AddPlayerCharacter` は、ディスク上の `profiles/*.json` の `Characters` 配列に `"Actor XX"` を追記し、即座に `SaveProfile()` を実行する。
   - スポーン・デスポーンを繰り返すたびにユーザーの正規設定ファイルが書き換わり、クラッシュ時や高速切り替え時に削除が追いつかないと `"Actor XX"` が永続的なゴミとして残骸化していた。
   - プロファイル変更通知がゲーム全体に送信されるため、自キャラを含む他アクターのボーン再計算が発生し、カクつきや予期せぬポーズ崩れを誘発していた。

2. **プロファイル無効化（Disabled）の壁**:
   - ユーザーが CustomizePlus 側で対象プロファイルを一時的に無効（`"Enabled": false`）にしている場合、`AddPlayerCharacter` でパペットを紐付けても CustomizePlus はパペットの骨格変形を一切実行しない。

3. **MCDF 外部プロファイルの未登録失敗**:
   - MCDF ファイルに内包されている Customize+ データはユーザー環境に登録されていないため、`AddPlayerCharacter` が `ec=3`（ProfileNotFound）で失敗し、完全に無視されていた。

4. **公式一時プロファイル IPC (`SetTemporaryProfileOnCharacter`) 未使用**:
   - CustomizePlus 2.2+ には、まさにパペットや召喚アクター用として `CustomizePlus.Profile.SetTemporaryProfileOnCharacter(gameObjectIndex, profileJson)` が用意されている。
   - これを使用すれば、ユーザーの設定ファイルを 1 文字も汚染せず、メモリ上だけでパペットにプロファイルを注入でき、プロファイルが Disabled でも確実に適用される。

## 3. 実装方針と改修箇所

### ① `Services/CustomizePlusIpc.cs`: 一時プロファイル適用の強化と自己修復
- **`SetTemporaryProfileByGuid(ushort gameObjectIndex, Guid uniqueId)` の強化**:
  - `GetProfileJson(uniqueId)` でプロファイル完全 JSON を取得後、JSON 内の `"Enabled": false` を `"Enabled": true` に強制書き換え。
  - これにより、CustomizePlus 側でオフに設定されているプロファイルであっても、パペットには一時プロファイルとして確実に適用される。
  - `setTemporaryProfileOnCharacter.InvokeFunc(gameObjectIndex, modifiedJson)` を呼び出し、生成された一時プロファイルの Guid を返す。
- **過去の汚染ゴミデータの自己修復 (`CleanupPuppetArtifacts`)**:
  - プラグイン起動時またはプロファイル一覧取得時に、各プロファイルの `Characters` 配列を走査。
  - パペット名ルール（`"Actor "` で始まる名前）のエントリがユーザーの正規プロファイルに残骸として残っている場合、自動的に `RemovePlayerCharacter` を呼び出して設定ファイルを元の綺麗な状態に修復。

### ② `Managers/ActorManager.cs`: 一時プロファイル方式への完全移行
- **`ApplyCustomizePlusProfile` の改修**:
  - 恒久設定変更（`AddPlayerCharacter`）の呼び出しを完全撤廃。
  - テンプレート指定プロファイルの場合: `customizePlusIpc.SetTemporaryProfileByGuid((ushort)actorIndex, profileGuid)` を呼び出し、返された Guid を `spawned.TemporaryCustomizePlusGuid` に格納。
  - MCDF 内包プロファイルの場合: 抽出した `cPlusJson` の `"Enabled"` を `true` に補正し、`customizePlusIpc.SetTemporaryProfile((ushort)actorIndex, cPlusJson)` を呼び出して直接注入。
- **`DespawnCharacter` の改修**:
  - パペット破棄時、`DeleteTemporaryProfileOnCharacter((ushort)actor.GlobalIndex)` および `DeleteTemporaryProfile(guid)` を呼び出し、一時プロファイルを即座に破棄。
  - ユーザーの正規プロファイルには一切手を加えないため、ディスク I/O や不要な通知イベントがゼロになる。

### ③ 他パイプラインへの影響ゼロ保証
- Glamourer パイプライン（MCDF, 通常Glamourer, 人型NPC）には一切触れない。
- Penumbra パイプラインにも一切触れない。
- Monster / Demihuman パイプラインにも一切触れない。

## 4. 検証手順 (v0.1.52.0)
1. `tools/release.ps1 0.1.52.0` で全自動リリース。
2. ゲーム内で Customize+ プロファイル（Lyle 等、あるいは Chonk 等）を設定したテンプレートをスポーンさせ、パペットの骨格変形が即座に正しく適用されることを確認。
3. CustomizePlus 本体の設定ファイル（`profiles/*.json`）に `"Actor XX"` が書き込まれない（汚染ゼロ）ことを確認。
4. デスポーン時に一時プロファイルが綺麗に消去されることを確認。
5. MCDF、Glamourer、人型NPC、モンスターが引き続き正常に動作することを確認。
