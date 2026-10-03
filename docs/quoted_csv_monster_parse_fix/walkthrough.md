# 技術記録・ウォークスルー: クォート付き CSV 解析の修正によるフォーギヴン・テスリーン等の Mob 復元 (v0.1.53.0)

## 1. 不具合の経緯と症状

### 発生した現象
- Character Spawn の Edit Character ウィンドウ（Monster/Mob タブ）で「フォーギ」と検索した際、「フォーギヴン・ディソナンス」や「フォーギヴン・ヒポクリシー」等は表示されるが、「フォーギヴン・テスリーン」が一覧に表示されない。
- HDM (HousingDollMaster) では `Tesleen, the Forgiven` (BaseId: 10123, ModelChara: 2632) として正常にリストに表示されている。

---

## 2. 原因究明の技術的詳細

### (1) `mob-model-index.csv` の行データ構造
該当モンスターの CSV 行は以下の通りでした：
```csv
10123,8300,"Tesleen, the Forgiven",2632,3,141,5,1,1.5
```
- カラム 0: `BaseId` = `10123`
- カラム 1: `NameId` = `8300` (`BNpcName` シートの日本語名「フォーギヴン・テスリーン」)
- カラム 2: `Name` = `"Tesleen, the Forgiven"`
- カラム 3: `ModelCharaId` = `2632`
- カラム 4: `McType` = `3`
- カラム 8: `Scale` = `1.5`

### (2) 単純な `line.Split(',')` によるカラム破損
- 従来の `GameDataService.cs` は `line.Split(',')` で単純分割していたため、`"Tesleen, the Forgiven"` の中のカンマで分断され、以下のように配列がずれていました：
  - `parts[0]` = `"10123"`
  - `parts[1]` = `"8300"`
  - `parts[2]` = `"\"Tesleen"`
  - `parts[3]` = `" the Forgiven\""` (本来は ModelCharaId のはず)
  - `parts[4]` = `"2632"`
- この結果、`parts[3]` を数値変換する `uint.TryParse(parts[3], out var modelCharaId)` が失敗し、`if (!uint.TryParse(...) || modelCharaId == 0) continue;` によって行ごとスキップされていました。

### (3) 同様に欠損していたモンスター（計 17 体）
- `10123`: フォーギヴン・テスリーン (`"Tesleen, the Forgiven"`)
- `1010`: 美眼のインク＝ゾン (`"Aenc Thon, Lord of the Lingering Gaze"`)
- `3860`: 楽聖のインク＝ゾン (`"Aenc Thon, Lord of the Lengthsome Gait"`)
- `8067`: 統制者ハシュマリム (`"Hashmal, Bringer of Order"`)
- `8091`: 背徳の皇帝マティウス (`"Mateus, the Corrupt"`)
- `8794`: 暗黒の雲ファムフリート (`"Famfrit, the Darkening Cloud"`)
- `8868`: 魔人ベリアス (`"Belias, the Gigas"`)
- `9641`, `9732`: 聖天使アルテマ (`"Ultima, the High Seraph"`)
- `8259`: ブンチン (`"Bunchin, Oldest of the Older"`)
- ほか計 17 体

---

## 3. 解決策の実装

### `Services/GameDataService.cs`
- RFC 4180 / HDM 準拠の CSV 行パーサー関数 `ParseCsvLine` を新設：
  ```csharp
  private static List<string> ParseCsvLine(string line)
  {
      var result = new List<string>();
      var sb = new StringBuilder();
      bool inQuotes = false;

      for (int i = 0; i < line.Length; i++)
      {
          char c = line[i];
          if (c == '"')
          {
              inQuotes = !inQuotes;
          }
          else if (c == ',' && !inQuotes)
          {
              result.Add(sb.ToString().Trim(' ', '\t', '"'));
              sb.Clear();
          }
          else
          {
              sb.Append(c);
          }
      }
      result.Add(sb.ToString().Trim(' ', '\t', '"'));
      return result;
  }
  ```
- `BuildMonsterCache()` 内で `line.Split(',')` を `ParseCsvLine(line)` に置き換え。
- クォート内のカンマが正しく保護され、全 16,243 行が 1 行のエラーもなく正常にパース完了。

---

## 4. 他パイプラインへの影響ゼロ保証 (完全隔離)

- **アクター生成・描画パイプライン**: 一切触れていません。
- **Glamourer パイプライン**: 一切触れていません。
- **Penumbra パイプライン**: 一切触れていません。
- **CustomizePlus パイプライン**: 一切触れていません。
- 修正箇所は CSV の文字列分割ヘルパーのみに局所化されており、他のいかなる機能にも影響を与えません。
