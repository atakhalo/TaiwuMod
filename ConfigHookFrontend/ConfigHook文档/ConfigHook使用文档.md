# ConfigHook 使用文档

## 如何为你的 Mod 添加配置

在你 Mod 的文件夹下创建以下结构：

```
你的Mod/
├── configHook.yaml        ← YAML 配置文件
└── configHook/            ← CSV 配置文件夹（按 csv_dirs 配置，不一定是 configHook）
    ├── Carrier.csv
    ├── Material.csv
    └── ...
```

## configHook.yaml 配置

### 简单用法（只扫目录下所有 CSV）

```yaml
# 空文件即可，自动扫描 configHook/*.csv
```

### 进阶用法

```yaml
# 全局开关
enabled: "Toggle:enableMod"

# 多目录，通过设置切换活跃目录
csv_dirs:
  - dir: configHook
    enabled: "Dropdown:profile:0"
  - dir: configHook_v2
    enabled: "Dropdown:profile:1"

# CSV 文件规则
csv_files:
  # 无 dir → 对当前所有活跃目录生效
  - name: Carrier
    enabled: true

  # 有 dir → 只对该目录生效
  - dir: configHook_v2
    name: Material
    enabled: "Toggle:matOverride"

  # 分组语法：多个 name/file 共用同一个 dir
  - dir: dir0
    files:
      - name: CombatSkill
        file: CombatSkill1.csv
        enabled: "Dropdown:type:0"
      - name: CombatSkill
        file: CombatSkill2.csv
        enabled: "Dropdown:type:1"

  # 条目级控制：只声明需要特殊处理的 ID
  - name: Food
    enabled: true
    items:
      - id: 5
        enabled: false
      - id: 8
        enabled: "Toggle:enableFood8"
```

### enabled 表达式

```
写法                     含义
──────────────────────────────────────────
true / 不填             启用
false                   禁用
"Toggle:key"           读取 Mod 设置 key 的 bool 值
"Dropdown:key:val"     读取 Mod 设置 key 的 int 值，等于 val 时启用
"complex:A&B&C"        多个条件 AND，全部满足才启用
```

### 多文件合并规则

同一配置类（`name`）的多个 CSV 都可同时生效，按在 `csv_files` 中的顺序合并：

| 情况 | 结果 |
|------|------|
| 字段只出现在一个 CSV | 正常使用 |
| 同一字段出现在多个 CSV | **先写者胜**，后面的不覆盖前面的 |
| 条目 `enabled: false` | 该条目被跳过 |

## CSV 文件

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

### CSV 字段类型

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

```csv
TemplateId,Icon
0,icon_Carrier_chiwen
```

- 进游戏 → 查看运载工具 → 图标应改变

### 改后端逻辑（如负重、速度）

```csv
TemplateId,BaseMaxInventoryLoadBonus
0,99999
```

- 进游戏 → 装备独轮车 → 打开背包看负重上限

> 注意：要看到效果可能需要载入存档或新开档。

## 常见问题

**Q: 修改没生效？**
A: 检查以下几点：
1. `configHook.yaml` 文件存在且格式正确
2. CSV 文件名是否与配置类名完全一致（区分大小写）
3. 检查 `enabled` 表达式是否正确（特别是 Dropdown 的值）
4. 查看游戏日志看是否有 ConfigHook 的扫描日志
5. 后端日志在 `Logs/GameData_*.log`，搜索 `[ConfigHook]`

**Q: 前端日志在哪看？**
A: `C:\Users\你的用户名\AppData\LocalLow\Conchship\The Scroll of Taiwu\Player.log`

**Q: 支持哪些配置类？**
A: 在游戏里运行[配置导出脚本](运行时助手/太吾终端插件/配置导出/)，可以列出所有可用的配置类名。
