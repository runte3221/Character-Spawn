# 変更内容の確認 (Walkthrough): アップデート手順の確立

## 変更ファイル一覧
1. `repo.json`:
   - `AssemblyVersion` を `0.1.41.0` に更新。
   - これにより Dalamud インストーラが `latest.zip`（0.1.41.0）と完全一致を認識し、インストールが正常に通るようになります。
2. `tools/bump-version.ps1`:
   - 新規作成。`CharacterSpawn.csproj`, `CharacterSpawn.json`, `repo.json` の3ファイルを1コマンドで同期更新するツール。
3. `docs/release_and_update_procedure/update_guide.md`:
   - 今後の開発者が絶対に参照すべき公式アップデート手順書を確立。
