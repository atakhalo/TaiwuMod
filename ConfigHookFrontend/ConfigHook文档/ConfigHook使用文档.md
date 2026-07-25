# ConfigHook 使用文档

## 这是什么

ConfigHook 是一个框架式 Mod，允许**其他 Mod** 通过 CSV 文件直接修改游戏配置数据（如材料属性、道具图标、运载工具负重等），无需写代码。

## 安装

1. 把 `ConfigHookFrontend.dll` 放到 `Mod/配置修改/Plugins/`
2. 把 `ConfigHookBackend.dll` 放到 `Mod/配置修改/Plugins/Back/`
3. 在游戏 Mod 管理里启用「配置修改」

## 如何为你的 Mod 添加配置

在你 Mod 的文件夹下创建以下结构：

```
你的Mod/
├── configHook.yaml        ← 标记文件，内容可空
└── configHook/            ← CSV 配置文件夹
    ├── Carrier.csv
    ├── Material.csv
    └── ...
```

### configHook.yaml

只是一个标记文件，内容是空的就行。ConfigHook 扫描 Mod 时靠它来识别。

### CSV 文件

- **文件名** = 配置类名（如 `Carrier.csv`、`Material.csv`、`Food.csv`）
- **首行** = 字段名，必须包含 `TemplateId`
- **数据行** = 只填你要改的列，其他留空

**示例**：修改运载工具 0 号的图标

```csv
TemplateId,Icon
0,icon_Carrier_chiwen
```

**示例**：修改材料 5 号的价格和品级

```csv
TemplateId,BaseValue,Grade
5,99999,9
```

### CSV 格式说明

| 字段类型 | CSV 写法 | 示例 |
|----------|----------|------|
| 数值 (int/short/byte 等) | 直接写数字 | `99999` |
| 布尔 (bool) | `True` 或 `False` | `True` |
| 枚举 | 枚举名 | `EMaterialProperty.Wood` 或 `Wood` |
| 字符串 | 直接写文本 | `新名称` |
| 列表/数组/字典 | JSON 格式 | `[1,2,3]` 或 `{"key":1}` |

> 复杂类型（列表、字典等）的 CSV 格式参照[配置导出脚本](运行时助手/太吾终端插件/配置导出/)导出的 CSV。

## 测试验证

### 改前端显示（如图标、名称）

```
TemplateId,Icon
0,icon_Carrier_chiwen
```

- 进游戏 → 查看运载工具 → 图标应改变

### 改后端逻辑（如负重、速度）

```
TemplateId,BaseMaxInventoryLoadBonus
0,99999
```

- 进游戏 → 装备独轮车 → 打开背包看负重上限

> 注意：要看到效果可能需要载入存档或新开档，因为配置数据在游戏开始时加载。

## 常见问题

**Q: 修改没生效？**
A: 检查以下几点：
1. `configHook.yaml` 文件是否存在（内容可空）
2. CSV 文件名是否与配置类名完全一致（区分大小写）
3. `TemplateId` 列是否存在
4. 查看游戏日志看是否有 ConfigHook 的扫描日志
5. 后端日志在 `Logs/GameData_*.log`，搜索 `[ConfigHook]`

**Q: 前端日志在哪看？**
A: `C:\Users\你的用户名\AppData\LocalLow\Conchship\The Scroll of Taiwu\Player.log`

**Q: 支持哪些配置类？**
A: 在游戏里运行[配置导出脚本](运行时助手/太吾终端插件/配置导出/)的 `list_configs` 功能，可以列出所有可用的配置类名。
