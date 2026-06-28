---
description: 获取太吾绘卷游戏中当前打开商店的所有页的所有商品。
---
执行此 Skill 时，按以下步骤操作：

# 商店全页商品获取

## 步骤 1：发现翻页节点

```
front_code
  entry: UnityEngine.GameObject
  chain:
    - Find("Camera_UIRoot/Canvas/LayerPopUp/ViewShop/Root/MiddleContent/NpcRect/ShopProgressBar/ShopProgressBar/PointContainer")
    - GetComponent("CToggleGroup")
    - GetAll()
  resultDepth: 1
```

`GetAll()` 返回 CToggle 数组，需要取出每个 toggle 所在父节点的名称（通过 transform.parent.name）。

由于 `GetAll()` 在一次调用中无法直接拿到父节点名，分两次获取效率更高：

**方式 A（逐个查）**：对索引 0..Count-1 循环调用：
```
chain: [Find(PointContainer), GetComponent("CToggleGroup"), Get(i), transform.parent.name]
```
跳过没有任何返回的索引。

**方式 B（已知规律）**：`Point`、`Point_2`、`Point_4`、`Point_6`、`Point_8`、`Point_9`、`Point_10`... 这些都是可能的翻页节点。逐个尝试验证：对每个 Point 名查询 `childCount`，只有 `childCount > 0` 的才有可点击的 Toggle（入口在 `<Point名>/Toggle`）。

## 步骤 2：逐页翻页 + 抓取

对每个有效的 Point 名，顺序执行：

```
1. simulate_click
     gameObjectPath = "Camera_UIRoot/Canvas/LayerPopUp/ViewShop/Root/MiddleContent/NpcRect/ShopProgressBar/ShopProgressBar/PointContainer/{Point名}/Toggle"

2. shop_buy_filtered
     filters = [{"name": ""}]
```

`shop_buy_filtered` 返回的 `items` 就是当前页全部商品。

## 步骤 3：汇总展示

将所有页的商品按页汇总，标注每页的 Point 名、件数、品级范围。

## 注意事项

- 商店 UI 必须已打开（ViewShop 活跃）
- 只有玩家已解锁的好感度节点才有可点击的 Toggle，未解锁节点不会出现
- `shop_buy_filtered` 会把商品加入交换列表，需要汇总后自行决定是否交易
- 如果中间某页的 `simulate_click` 失败（找不到 Toggle），说明该节点未解锁，跳过即可
- 翻页与抓取之间建议适当间隔（0.5s），避免页面尚未刷新
