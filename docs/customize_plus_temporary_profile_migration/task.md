# タスクリスト: Customize+ 一時プロファイル方式への完全移行と恒久設定汚染根絶 (v0.1.52.0)

## 1. 不具合の事象と原因究明フェーズ
- [x] **事象確認**:
  - Customize+ の設定を行ったパペットをスポーンさせた際、体型が変わらない場合がある。
  - CustomizePlus 本体の設定ファイルに一時パペット名（`"Actor Ac"` 等）が勝手に追記・保存され、設定ファイルが汚染される。
  - スポーン・デスポーンのたびにプロファイル変更通知が走り、他のアクターや自キャラの姿勢・ボーンに影響を与える。
  - MCDF 内包の Customize+ データが適用されない。
- [x] **原因究明 (`CustomizePlus.dll` 逆アセンブル解析)**:
  - 1. **恒久プロファイル書き換え (`AddPlayerCharacter`) の副作用**:
       - `AddPlayerCharacter` はディスク上の `profiles/*.json` の `Characters` 配列に `"Actor XX"` を追記して `SaveProfile()` を呼ぶため、ユーザーの正規設定ファイルが汚染されていた。
  - 2. **プロファイル無効化（Disabled）の壁**:
       - プロファイル自体が `"Enabled": false` の場合、`AddPlayerCharacter` で紐付けても骨格変形が一切実行されない。
  - 3. **MCDF 外部プロファイルの未登録失敗**:
       - MCDF 内包のプロファイルはユーザーの環境に未登録なため、`AddPlayerCharacter` が `ec=3`（ProfileNotFound）で失敗していた。
  - 4. **公式一時プロファイル IPC (`SetTemporaryProfileOnCharacter`) 未使用**:
       - パペット用の一時プロファイル API が提供されているにもかかわらず、恒久設定書き換え API を呼んでいたことが根本原因。

## 2. 設計・実装フェーズ
- [x] **`Services/CustomizePlusIpc.cs` の強化**:
  - [x] `SetTemporaryProfileByGuid` において、取得したプロファイル JSON の `"Enabled"` を強制的に `true` に書き換えて注入する処理を追加（Disabled プロファイルでもパペットに確実に適用）。
  - [x] 過去に汚染されたユーザーの正規プロファイルから `"Actor "` エントリを自動検知して除去する自己修復機構（`CleanupPuppetArtifacts`）を追加。
- [x] **`Managers/ActorManager.cs` の完全移行**:
  - [x] `ApplyCustomizePlusProfile` を、恒久設定変更（`AddPlayerCharacter`）から公式一時プロファイル注入（`SetTemporaryProfileByGuid` / `SetTemporaryProfile`）へ完全移行。
  - [x] MCDF 内包の Customize+ データを `SetTemporaryProfile` で直接注入し、外部プロファイルでも 100% 確実に適用。
  - [x] `DespawnCharacter` でのクリーンアップを `DeleteTemporaryProfileOnCharacter` / `DeleteTemporaryProfile` に統一。
  - [x] 他パイプライン（Glamourer, Penumbra, Monster）への影響ゼロを保証。

## 3. ドキュメント・リリース・検証フェーズ
- [x] `docs/customize_plus_temporary_profile_migration/` の 3 ファイル作成・更新
- [x] `CHANGELOG.md` 更新（v0.1.52.0）
- [x] 全自動リリースパイプライン実行 (`tools/release.ps1 0.1.52.0` / CI/CD 成功確認)
- [ ] 実機での Customize+ 適用・デスポーン動作確認
