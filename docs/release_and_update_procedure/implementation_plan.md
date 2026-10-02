# 実装計画: アップデート手順の標準化と自動同期ツールの配備

## 1. 目的
Dalamud カスタムリポジトリ経由のプラグインインストールにおいて、`repo.json` と `latest.zip` 内の `CharacterSpawn.json` のバージョン不一致によるインストール拒絶（エラー）を根絶し、恒久的に安全なアップデート運用を確立する。

## 2. 実施手順
1. `repo.json` のバージョンを `0.1.41.0` に即時更新。
2. 今後の更新漏れをゼロにするための自動化スクリプト `tools/bump-version.ps1` を配備。
3. `docs/release_and_update_procedure/update_guide.md` に公式手順書を保存。
4. Git へのコミット & プッシュを実行し、Dalamud インストーラが `0.1.41.0` を正規に認識・インストールできるようにする。
