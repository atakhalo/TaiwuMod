# ConfigHook 代码文档

## 项目结构

```
ConfigHookFrontend/
├── ConfigHookFrontend.cs       ← 前端插件（Unity）
├── ConfigHookFrontend.csproj   ← 前端项目文件（YamlDotNet NuGet 引用）
├── MyUtils.cs                  ← 前端工具方法
└── ConfigHook文档/
    ├── ConfigHook使用文档.md   ← 使用说明（写给 Mod 作者看）
    └── ConfigHook代码文档.md   ← 代码说明（写给开发者看）

ConfigHookBackend/
├── ConfigHookBackend.cs        ← 后端插件（.NET 8）
└── ConfigHookBackend.csproj    ← 后端项目文件（YamlDotNet NuGet 引用）
```

## 核心逻辑流程

```
OnModSettingUpdate()
  ├── [首次] initScan==false
  │     └── DelayCall / Timer ──→ ScanAndApplyConfigs()
  │                                ├── TryLoadModConfigs(mod)    ← 遍历每个 mod
  │                                │    ├── 读取 configHook.yaml
  │                                │    ├── ProcessModConfigs()  ← 解析 YAML
  │                                │    │    ├── 有 csv_files → ProcessYamlConfig()
  │                                │    │    │    ├── ExpandGroupEntries() ← 展开分组
  │                                │    │    │    ├── 按 enabled 过滤
  │                                │    │    │    ├── 确定目标目录
  │                                │    │    │    ├── LoadCsvOverrides() ← 解析 CSV
  │                                │    │    │    └── merge 策略（先写者胜）
  │                                │    │    └── 无 csv_files → LegacyScanCsvDir()
  │                                │    └── ApplyAllOverrides()   ← 写入 Config 实例
  │                                └── ...
  └── [非首次] reloadConfigs==true
        └── DelayCall / Timer ──→ ReloadConfigs()
                                   ├── RestoreOverrides()
                                   ├── 清空 _configOverrides
                                   └── ScanAndApplyConfigs()
```

## 模型类层次

```
HookConfig
├── Enabled          ← 全局开关表达式
├── CsvDir           ← 旧版：单目录路径
├── CsvDirs          ← 新版：多目录列表
│    └── CsvDirEntry
│         ├── Dir       ← 目录路径
│         └── Enabled   ← 开关表达式
└── CsvFiles         ← CSV 文件规则列表
     └── CsvFileEntry
          ├── Dir       ← 作用域（可选）
          ├── Name      ← 配置类名
          ├── File      ← CSV 文件名（可选，默认 Name+.csv）
          ├── Enabled   ← 开关表达式
          ├── Items     ← 条目级控制
          │    └── ItemEntry
          │         ├── Id
          │         └── Enabled
          └── Files     ← 子条目（分组语法）
               └── CsvFileEntry（同上，继承父级 Dir）
```

## 前后端差异

| 方面 | 前端 (Frontend) | 后端 (Backend) |
|------|----------------|----------------|
| 目标框架 | netstandard2.1 | net8.0 |
| 输出目录 | `Plugins/` | `Plugins/Back/` |
| 日志 | `Debug.Log()` (Unity) | `NLog` (文件日志) |
| Mod 遍历 | `ModManager.EnabledMods` | `ModDomain.GetLoadedModIds()` |
| Mod 信息 | `ModManager.GetModInfo(mod)` | `DomainManager.Mod.GetMod*()` |
| 延迟扫描 | `MyUtils.DelayCall` (协程) | `System.Threading.Timer` (100ms) |
| 设置读取 | `ModManager.GetSetting()` | `DomainManager.Mod.GetSetting()` |

## 同步修改说明

`#region ConfigHook Core` 中的代码两端完全一致，修改时必须同步两端：

```
// 数据模型
HookConfig / CsvDirEntry / CsvFileEntry / ItemEntry

// 核心处理
LoadCsvOverrides()    ← CSV 解析 → _configOverrides 字典（支持 skipIds 过滤）
ApplyAllOverrides()   ← 写入 Config 实例（保存原始值到 _originalValues）
RestoreOverrides()    ← 从 _originalValues 还原
ProcessYamlConfig()   ← 按 YAML 配置处理 csv_files
ExpandGroupEntries()  ← 展开分组 Files 子条目
ConvertValue()        ← 类型转换
GetConfigInstance()   ← 反射获取 Config 单例
GetItemType()         ← 获取泛型参数 T
FindDataArrayField()  ← 反射 _dataArray
FindIndexer()         ← 反射 this[int] 索引器
ParseCsvLine()        ← CSV 行解析
```

两端不同的代码（在 region 外）：

```
Initialize() / Dispose()
OnModSettingUpdate()
ReloadConfigs()
ScanAndApplyConfigs() / TryLoadModConfigs() / LegacyScanCsvDir()
ProcessModConfigs()     ← 因为异常处理 + 日志
EvaluateEnabled()       ← 因为 ModManager vs DomainManager.Mod
```

## 关键技术点

### 延迟扫描

首次扫描和重新加载都在 `OnModSettingUpdate` 中触发，延迟执行避免漏扫：

- **前端**：`MyUtils.DelayCall(fn, 0, true)` — 下一帧
- **后端**：`System.Threading.Timer` 延迟 100ms

### 自身 mod 的扫描

后端在 `Initialize()` 时当前 ModId 尚未加入 `ModDomain.GetLoadedModIds()`，所以在 `ScanAndApplyConfigs` 中用 `ModIdStr` 单独扫描自身。

### 类型转换

CSV 读到的都是字符串，`ConvertValue()` 按目标字段类型处理：

1. 可空类型 → 解包
2. 基本类型 → `int.Parse`、`short.Parse` 等
3. 布尔 → 支持 `True/False/1/0`
4. 枚举 → `Enum.Parse()`
5. 复杂类型（List/数组/字典）→ `Newtonsoft.Json` 反序列化

### 字段合并策略

多个 CSV 处理同一配置类时，按 `csv_files` 顺序合并，同一字段 **先写者胜**（`classDict[templateId]` 改为 merge 方式）。

### enabled 表达式

| 格式 | 含义 |
|------|------|
| `true` / `false` | 直接 |
| `Toggle:key` | `GetSetting(modIdStr, key, ref bool)` |
| `Dropdown:key:val` | `GetSetting(modIdStr, key, ref int)` 比较 |
| `complex:A&B` | 多个条件 AND |

### 还原与重新加载

第一次覆盖时保存原始值到 `_originalValues`，`ReloadConfigs` 时先还原全部字段、清空覆盖表，再重新扫描应用。

## 依赖

| 依赖 | 前端路径 | 后端路径 | 来源 |
|------|---------|---------|------|
| `0Harmony.dll` | Managed/ | Managed/ | 游戏自带 |
| `GameData.Shared.dll` | Managed/ | Backend/ | 游戏自带 |
| `Newtonsoft.Json.dll` | Managed/ | Backend/ | 游戏自带 |
| `TaiwuModdingLib.dll` | Managed/ | Managed/ | 游戏自带 |
| `YamlDotNet.dll` | 随插件输出 | 随插件输出 | NuGet 15.1.0 |
