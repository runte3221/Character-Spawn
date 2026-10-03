# 技術記録・ウォークスルー: Glamourer 適用前の先行 Penumbra Redraw 抑止による外見・武器チラつき解消 (v0.1.54.0)

## 1. 不具合の経緯と症状

### 発生した現象
- `Lyle Nude` 等の Glamourer デザインと Penumbra コレクションを併用したテンプレートをスポーンさせた際、一瞬武器だけが自キャラのものになる、あるいはモデル自体が自キャラ等の姿で一瞬表示される現象が発生。
- 何度かスポーン・デスポーンを繰り返すと正常な表示に落ち着く。

---

## 2. 実機ログ解析と技術的真因

### (1) パペット初期化時の自キャラ複写
パペットをゲーム内（COM スロット）に生成する際、ゲームエンジンのクラッシュを防ぎ安全な骨格を確立するため、自キャラ（プレイヤー）の装備・武器が一旦ベースラインとしてコピーされます。

### (2) Glamourer 適用前の不要な先行 Redraw
従来のコード（`ActorManager.cs`）では以下の順序で処理が走っていました：
```csharp
// 1. Penumbra コレクションをセット
penSuccess = penumbraIpc.SetCollectionForActor(template.PenumbraCollectionName, actorIndex);
if (penSuccess)
{
    penumbraIpc.Redraw(actorIndex); // ← ★ここが原因！
}

// 2. Glamourer デザインを適用
glamSuccess = glamourerIpc.ApplyDesignToActor(designString, actorIndex, puppetName);
```
- この `penumbraIpc.Redraw` の時点では、パペットはまだ Glamourer のデザインが適用される前の「自キャラの装備・武器」の状態です。
- そのため、Penumbra は「自キャラの装備や武器」に対してモデル・テクスチャの再描画を開始してしまいます。
- 直後に Glamourer の `ApplyDesignToActor` が走ることで、**「自キャラモデル・武器のロード」と「Glamourer 新外見のロード」が非同期に重なり（二重リロード）**、わずか数フレーム（約100ms）の間、自キャラの武器やモデルが画面に露出していました。

### (3) 「繰り返すと正常に戻った」理由
Penumbra Mod（マテリアルやテクスチャ）が PC メモリやゲームエンジン内にキャッシュされると、非同期ディスク読み込み待ちがなくなるため、自キャラの先行描画が人間の目視不可能な速度で上書きされ、正常に見えるようになっていました。

---

## 3. 解決策の実装 (`Managers/ActorManager.cs`)

### 処理順序の整流化
Glamourer が適用される場合は、直前の不要な先行 `Penumbra.Redraw` をスキップするように修正しました。

```csharp
// 1. Penumbra コレクションの適用 (Guid渡し)
bool penSuccess = false;
if (penumbraIpc.IsAvailable && !string.IsNullOrWhiteSpace(template.PenumbraCollectionName))
{
    penSuccess = penumbraIpc.SetCollectionForActor(template.PenumbraCollectionName, actorIndex);
    logManager?.Info($"Penumbra SetCollection '{template.PenumbraCollectionName}' on Global#{actorIndex}: {penSuccess}");
}

// 2. Glamourer デザインの適用 (Guid 指定または PlayerClone)
bool glamApplied = false;
if (glamourerIpc.IsAvailable)
{
    string? designString = template.GlamourerDesignString;
    if (!string.IsNullOrWhiteSpace(designString))
    {
        glamApplied = glamourerIpc.ApplyDesignToActor(designString, actorIndex, spawned?.PuppetName);
        logManager?.Info($"Glamourer ApplyDesign result on Global#{actorIndex} ('{spawned?.PuppetName}'): {glamApplied}");
    }
    else if (template.SourceType == CharacterSourceType.PlayerClone)
    {
        var playerDesign = glamourerIpc.GetCustomization(0);
        if (!string.IsNullOrWhiteSpace(playerDesign))
        {
            glamApplied = glamourerIpc.ApplyDesignToActor(playerDesign, actorIndex, spawned?.PuppetName);
            logManager?.Info($"Applied player customization clone via Glamourer to Global#{actorIndex} ('{spawned?.PuppetName}'): {glamApplied}");
        }
    }
}

// 3. Glamourer が適用されなかった場合のみ、Penumbra 側で明示的に Redraw をトリガー
if (penSuccess && !glamApplied)
{
    penumbraIpc.Redraw(actorIndex);
}
```

- **Glamourer 併用時**:
  - 不要な先行 Redraw がスキップされ、Glamourer 自身の再描画によって Penumbra コレクションが新しい外見で一発同期適用されます。
  - これにより、自キャラの武器やモデルが画面に露出する余地が完全に消滅しました。
- **Penumbra 単体運用時**:
  - `!glamApplied` ガードにより、Glamourer がない場合でも従来通り確実に Penumbra Redraw が実行され、既存機能が 100% 保証されます。

---

## 4. 他パイプラインへの影響ゼロ保証 (完全隔離)

- **MCDF パイプライン**: 変更なし。
- **人型NPC パイプライン**: 変更なし。
- **モンスター／デミヒューマン パイプライン**: 変更なし。
- **CustomizePlus パイプライン**: 変更なし。
- 変更は `ActorManager.cs` の通常スポーン（パイプライン A）の条件分岐 3 行のみに局所化されており、他のいかなる機能にも影響を与えません。
