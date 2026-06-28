---
name: adventure-block-edit
description: 查询和修改奇遇内方块——图标覆盖(SpecialIcon)、装饰物(Decorates)、状态、坐标转换。用于奇遇调试和测试。
version: 1.0.0
---

# 奇遇方块查询与修改

通过 ingame-helper MCP 的 `back_cs`/`front_cs` 对奇遇内方块进行运行时查询和修改。

## 前置条件

- 游戏正在运行，ingame-helper MCP 已连接
- 太吾当前处于某个奇遇中（已进入奇遇界面）

## 步骤 1：查询当前奇遇信息

用 `back_code` 获取奇遇 ID 和太吾位置：

```
back_code
  entry: GameData.Domains.DomainManager
  chain:
    - { step: field, name: Adventure }
    - { step: field, name: _adventureTaiwu }
  result_depth: 5
```

返回 `adventureId` 和 `internalIndex`（x, y, i 坐标）。

## 步骤 2：获取奇遇方块列表

```
back_code
  entry: GameData.Domains.DomainManager
  chain:
    - { step: field, name: Adventure }
    - { step: field, name: _adventures }
    - { step: method, name: get_Item, argTypes: ["System.Int32"], args: [<adventureId>] }
    - { step: field, name: _blocks }
    - { step: property, name: Count }
  result_depth: 2
```

返回方块总数（如 117）。注意 `_blocks` 是 `List<AdventureBlock>`，**列表索引与 AdventureBlockIndex(x,y,i) 不是简单公式关系**，需遍历查找。

## 步骤 3：查询方块的地格和装饰物信息

### 3a. 查询运行时方块状态（后端 back_cs）

```csharp
var domain = GameData.Domains.DomainManager.Adventure;
var adventures = typeof(GameData.Domains.Adventure.AdventureDomain)
    .GetField("_adventures", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
    .GetValue(domain);
var adventure = adventures.GetType().GetMethod("get_Item")
    .Invoke(adventures, new object[] { <adventureId> });
var blocks = (System.Collections.Generic.List<GameData.Domains.Adventure.AdventureBlock>)
    adventure.GetType().GetField("_blocks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
    .GetValue(adventure);

var results = new System.Collections.Generic.List<object>();
foreach (var b in blocks)
{
    var idx = b.Index;
    // 筛选特定方块组或全部
    if (idx.X == <targetX> && idx.Y == <targetY>)
    {
        results.Add(new {
            x = idx.X, y = idx.Y, i = idx.I,
            specialIcon = b.SpecialIcon ?? "(null)",  // null=使用配置默认值
            specialParticle = b.SpecialParticle ?? "(null)",
            inCloud = b.InCloud,
            entryPriority = b.EntryPriority
        });
    }
}
return results;
```

返回 `specialIcon` 为 `(null)` 表示使用配置中的默认 `Icon`，非 null 则表示已覆盖。

### 3b. 查询配置中的地格图标和装饰物（.advd 文件）

从 `.advd` 文件读取每个方块的原始配置数据：

```csharp
var basePath = "D:/SteamLibrary/steamapps/common/The Scroll Of Taiwu/The Scroll of Taiwu_Data/StreamingAssets/AdventureCore/";
var path = System.IO.File.Exists(basePath + "Core/" + coreId + ".advd")
    ? basePath + "Core/" + coreId + ".advd"
    : basePath + "Custom/" + coreId + ".advd";
GameData.Adventure.AdventureData.TryLoad(path, out var advData);

var results = new System.Collections.Generic.List<object>();
for (int g = 0; g < advData.Groups.Count; g++)
{
    var group = advData.Groups[g];
    for (int b = 0; b < group.Blocks.Count; b++)
    {
        var block = group.Blocks[b];
        var idx = block.Index;
        if (idx.X == <targetX> && idx.Y == <targetY>)
        {
            results.Add(new {
                x = idx.X, y = idx.Y, i = idx.I,
                blockType = block.BlockType.ToString(),  // None/In/Out/InOut
                icon = block.Icon,                        // 地格图标名
                decorates = block.Decorates.ToArray(),    // 装饰物列表
                height = block.Height,                    // 方块高度
                inCloud = block.InCloud,
                entryPriority = block.EntryPriority
            });
        }
    }
}
return results;
```

### 3c. 查询前端实际渲染的装饰物

查看当前方块的 `AdventureUnitMicro` 实际渲染状态：

```csharp
// front_cs
var micros = UnityEngine.Object.FindObjectsOfType<Game.Views.Migrate.AdventureUnitMicro>(true);
foreach (var m in micros)
{
    var idx = m.RenderBlockIndex;  // 渲染坐标（数据坐标+7）
    if (idx.X == <renderX> && idx.Y == <renderY> && idx.I == <targetI>)
    {
        return new {
            renderX = (int)idx.X, renderY = (int)idx.Y, i = (int)idx.I,
            groundSprite = m.groundSurface?.spriteName ?? "(null)",  // 当前地格图标
            canvasAlpha = m.blockDecoratesCanvasGroup?.alpha,        // 装饰物画布透明度
            cloudActive = m.cloud?.gameObject?.activeInHierarchy,    // 是否在云雾中
            blockName = m.gameObject.name
        };
    }
}
return new { error = "render block not found" };
```

## 步骤 4：查找目标方块的列表索引

遍历查找目标坐标对应的列表索引（用于步骤 5 修改时定位方块）：

遍历查找目标坐标对应的列表索引：

```csharp
// back_cs
var domain = GameData.Domains.DomainManager.Adventure;
var adventures = typeof(GameData.Domains.Adventure.AdventureDomain)
    .GetField("_adventures", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
    .GetValue(domain);
var adventure = adventures.GetType().GetMethod("get_Item")
    .Invoke(adventures, new object[] { <adventureId> });
var blocks = (System.Collections.Generic.List<GameData.Domains.Adventure.AdventureBlock>)
    adventure.GetType().GetField("_blocks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
    .GetValue(adventure);

for (int idx = 0; idx < blocks.Count; idx++)
{
    var b = blocks[idx];
    if (b.Index.X == <targetX> && b.Index.Y == <targetY> && b.Index.I == <targetI>)
    {
        return new { listIndex = idx, specialIcon = b.SpecialIcon ?? "(null)", inCloud = b.InCloud };
    }
}
return new { error = "not found" };
```

## 步骤 5a：修改 SpecialIcon（后端 back_cs）

```csharp
var domain = GameData.Domains.DomainManager.Adventure;
var adventures = typeof(GameData.Domains.Adventure.AdventureDomain)
    .GetField("_adventures", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
    .GetValue(domain);
var adventure = adventures.GetType().GetMethod("get_Item")
    .Invoke(adventures, new object[] { <adventureId> });
var blocks = (System.Collections.Generic.List<GameData.Domains.Adventure.AdventureBlock>)
    adventure.GetType().GetField("_blocks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
    .GetValue(adventure);
blocks[<listIndex>].SpecialIcon = "<icon_name>";  // 设为 null 则恢复默认
return new { success = true };
```

### SpecialIcon 可用值

来源 `AdventureTerrain[auto].tsv`，22 种地形各两种变体：
- `adventure_terrain_{1~22}` — 立体版
- `adventure_terrain_flat_{1~22}` — 扁平版

| ID | 地形 |
|----|------|
| 1 | 山岭 |
| 2 | 峡谷 |
| 3 | 洞穴 |
| 4 | 江河 |
| 5 | 湖泊 |
| 6 | 潭泽 |
| 7 | 茅庐 |
| 8 | 乡村 |
| 9 | 镇集 |
| 10 | 花海 |
| 11 | 树林 |
| 12 | 林野 |
| 13 | 营寨 |
| 14 | 庙宇 |
| 15 | 道观 |
| 16 | 古迹 |
| 17 | 荒野 |
| 18 | 废墟 |
| 19 | 沙漠 |
| 20 | 古墓 |
| 21 | 绝壁 |
| 22 | 深渊 |

另有 `adventure_block_default`（回退值）。

## 步骤 5b：修改装饰物 Decorates（前端 front_cs）

**⚠️ 前提**：必须先清除 SpecialIcon（设为 null），否则装饰物被 HideDecorates() 隐藏、画布 alpha=0。

### 坐标转换

数据坐标 → 渲染坐标：**偏移 +7**

| 数据坐标 | 渲染坐标 |
|----------|----------|
| (0, -2) | (7, 5) |
| (-1, -1) | (6, 6) |
| (0, 0) | (7, 7) |
| (1, 1) | (8, 8) |
| (2, 0) | (9, 7) |

通用公式：`renderX = dataX + 7, renderY = dataY + 7`。i 索引不变。

### 设置装饰物

```csharp
// front_cs
var micros = UnityEngine.Object.FindObjectsOfType<Game.Views.Migrate.AdventureUnitMicro>(true);
var setDecorate = typeof(Game.Views.Migrate.AdventureUnitMicro).GetMethod("SetDecorate");
foreach (var m in micros)
{
    var idx = m.RenderBlockIndex; // public 属性
    if (idx.X == <renderX> && idx.Y == <renderY> && idx.I == <targetI>)
    {
        var decorates = new System.Collections.Generic.List<string> { "<decorate_name>" };
        setDecorate.Invoke(m, new object[] { decorates });
        if (m.blockDecoratesCanvasGroup != null)
            m.blockDecoratesCanvasGroup.alpha = 1f;
        return new { found = true, decorateSet = true };
    }
}
return new { found = false };
```

### 装饰物名称格式

`adventure_decorate_{类别}_{编号}`，约 400+ 种。常见类别：

| 类别 | 说明 | 示例数量 |
|------|------|----------|
| banquethall | 宴会厅 | 75+ |
| courtyard | 庭院 | 65+ |
| vendor | 摊贩 | 36+ |
| restaurant | 餐馆 | 30+ |
| cabinet | 柜子 | 27+ |
| drunkery | 酒馆 | 34+ |
| withered_grass | 枯草 | 50+ |
| yellow_grass | 黄草 | 21+ |
| grave | 坟墓 | 18 |
| coffin | 棺材 | 34 |
| corpse | 尸体 | 14 |
| joss_paper | 纸钱 | 12 |
| wedding_carpet | 婚地毯 | 13 |
| stone_brick_floor | 砖石地面 | 16 |
| loess_pavement | 黄土路 | 14 |
| tongyong_chuang | 床 | 1 |
| tongyong_huaji | 花几 | 4 |
| tongyong_shuigang | 水缸 | 3 |
| tongyong_yaoluzi | 药炉子 | 4 |
| lantern_stand | 灯架 | 10 |
| damaged_railing | 破栏杆 | 11 |
| deep_green_shrubbery | 深绿灌木 | 5 |
| broken_sword | 断剑 | 2 |
| alphago, book, guqin, oldpaint | 特殊物品 | 各1 |

## 步骤 6：读取 .advd 配置文件查看原始方块数据

```csharp
// back_cs — 路径：Core/ 下为游戏内置模板，Custom/ 下为用户自定义模板
var basePath = "D:/SteamLibrary/steamapps/common/The Scroll Of Taiwu/The Scroll of Taiwu_Data/StreamingAssets/AdventureCore/";
var path = System.IO.File.Exists(basePath + "Core/" + coreId + ".advd")
    ? basePath + "Core/" + coreId + ".advd"
    : basePath + "Custom/" + coreId + ".advd";
GameData.Adventure.AdventureData.TryLoad(path, out var advData);
var results = new System.Collections.Generic.List<object>();
for (int g = 0; g < advData.Groups.Count; g++)
{
    var group = advData.Groups[g];
    for (int b = 0; b < group.Blocks.Count; b++)
    {
        var block = group.Blocks[b];
        var idx = block.Index;
        if (idx.X == <targetX> && idx.Y == <targetY>)
        {
            results.Add(new {
                x = idx.X, y = idx.Y, i = idx.I,
                blockType = block.BlockType.ToString(),
                icon = block.Icon,
                decorates = block.Decorates.ToArray(),
                height = block.Height,
                inCloud = block.InCloud
            });
        }
    }
}
return results;
```

## 注意事项

- `_adventures`、`_blocks`、`_adventureTaiwu` 都是 private 字段，需用 `BindingFlags.NonPublic`
- `AdventureBlock.StatusType` 属性 getter 非 public，用字段 `_internalStatusType` 读取
- `AdventureUnitMicro.RenderBlockIndex` 是 public 属性，`_renderBlockIndex` 是 private 字段
- `FindObjectsOfType` 有 909 个对象，遍历约需 1-2 秒，不会超时
- `SpecialIcon` 非空时装饰物会被隐藏（`decorateCanvas.alpha = 0`），两者互斥
- 修改后可能需要移动视角或触发刷新才能看到变化
- `.advd` 文件优先查 `Core/`（317 个游戏内置模板），找不到再查 `Custom/`（用户自建模板，如 900001）
