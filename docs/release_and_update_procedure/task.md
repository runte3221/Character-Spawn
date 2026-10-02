# タスク: Dalamudカスタムリポジトリによるアップデート手順の確立と記録

## 課題
- Dalamudのプラグインインストーラで「プラグイン Character Spawn のインストールに失敗しました」が発生。
- 原因: `repo.json` の `AssemblyVersion`（`0.1.40.0`）と、配布された zip（`latest.zip`）内の `CharacterSpawn.json` / dll のバージョン（`0.1.41.0`）が不一致だったため、Dalamud 内部のバリデーション `Distributed plugin version does not match repo version` によりインストールが拒絶されていた。

## 完了タスク
- [x] Dalamudログからインストール失敗の根本原因（バージョン不一致エラー）を特定
- [x] `repo.json` を `0.1.41.0` に更新
- [x] 今後のバージョン更新を一括自動化するスクリプト `tools/bump-version.ps1` を作成
- [x] アップデート手順の公式ガイド `docs/release_and_update_procedure/update_guide.md` を作成
- [x] リポジトリへのコミット & プッシュ
