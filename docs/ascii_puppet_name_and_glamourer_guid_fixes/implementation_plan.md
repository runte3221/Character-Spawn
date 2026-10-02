# 実装計画: ASCII英字パペット名生成とGlamourer Guid適用の堅牢化 (v0.1.31)

## 概要
数字混入による FF14 プレイヤー名検証（VerifyPlayerName）の拒否を解消し、Glamourer の Guid 指定デザイン適用を強化する。

## 修正詳細

### 1. ASCII 英字のみのパペット名生成 (`Managers/ActorManager.cs`)
- 旧: `template.Id.ToString()[..8]`（例: `04ffbf3e`）-> 数字が含まれるため FF14 の `VerifyPlayerName` で弾かれ `InvalidActor` になる。
- 新: `template.Id` のバイト列から 8 文字の英字 Surname を生成（例: `Evjkkhzl`）。
  ```csharp
  char[] surname = new char[8];
  surname[0] = (char)('A' + (bytes[0] % 26));
  for (int i = 1; i < 8; i++)
  {
      int b = i < bytes.Length ? bytes[i] : bytes[i % bytes.Length];
      surname[i] = (char)('a' + (b % 26));
  }
  return $"Csp {new string(surname)}";
  ```
  - 先頭大文字 + 小文字 7 文字の合法な FF14 プレイヤー名。
  - $26^8 \approx 2088$ 億通りの組み合わせによりテンプレート固有の一意性を担保。

### 2. Glamourer Guid 処理の強化 (`Services/GlamourerIpc.cs`)
- `ApplyDesignToActorEx`:
  - Guid 指定時、`GetDesign(targetGuid)` で JObject を取得して `ForceAllApply` を実行。
  - `ApplyState(jsonString)` を呼び出すことで `Apply: false` スロットの無視を防止。
  - ネイティブメモリへの 26バイト `CustomizeData` 同期を確実に実施。
  - Guid 失敗時に Base64 デコード処理へフォールスルーせず即時 `return`。

### 3. バージョン更新とデプロイ
- `0.1.31` / `0.1.31.0`
- GitHub リポジトリへのプッシュと GitHub Actions 自動ビルド。
- `%APPDATA%\XIVLauncher\installedPlugins\CharacterSpawn\0.1.31.0` への配置。
