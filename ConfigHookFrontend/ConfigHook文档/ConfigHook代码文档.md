# ConfigHook 代码文档

## 项目结构

```
ConfigHookFrontend/
├── ConfigHookFrontend.cs       ← 前端插件（Unity）
├── ConfigHookFrontend.csproj   ← 前端项目文件
├── MyUtils.cs                  ← 前端工具方法
└── ConfigHook文档/
    ├── ConfigHook使用文档.md   ← 使用说明（写给 Mod 作者看）
    └── ConfigHook代码文档.md   ← 代码说明（写给开发者看）

ConfigHookBackend/
├── ConfigHookBackend.cs        ← 后端插件（.NET 8）
└── ConfigHookBackend.csproj    ← 后端项目文件
```

## 核心逻辑流程

```
Initialize()
  └─ DelayCall / Timer ──→ ScanAndApplyConfigs()
                              ├─ TryLoadModConfigs()    ← 扫描单个 mod
                              │    ├─ 检查 configHook.yaml
                              │    ├─ 读取 configHook/*.csv
                              │    └─ LoadCsvOverrides() ← 解析 CSV 到内存
                              └─ ApplyAllOverrides()    ← 写入 Config 实例
                                   ├─ GetConfigInstance()  ← 反射找 Config.XXX.Instance
                                   ├─ 检查 _dataArray 是否就绪
                                   ├─ 遍历 CSV 条目
                                   │    ├─ 索引器取 Item
                                   │    ├─ ConvertValue()   ← 类型转换
                                   │    └─ FieldInfo.SetValue()
                                   └─ 日志输出结果
```

## 前后端差异

| 方面 | 前端 (Frontend) | 后端 (Backend) |
|------|----------------|----------------|
| 目标框架 | netstandard2.1 | net8.0 |
| 输出目录 | `Plugins/` | `Plugins/Back/` |
| 日志 | `Debug.Log()` (Unity) | `NLog` (文件日志) |
| Mod 遍历 | `ModManager.EnabledMods` | `ModDomain.GetLoadedModIds()` |
| Mod 信息 | `ModManager.GetModInfo(mod)` | `DomainManager.Mod.GetMod*()` |
| 延迟扫描 | `MyUtils.DelayCall` (协程) | `System.Threading.Timer` (1 秒) |

## 同步修改说明

`#region ConfigHook Core` 中的代码两端完全一致，修改时必须同步两端：

```
LoadCsvOverrides()    ← CSV 解析 → _configOverrides 字典
ApplyAllOverrides()   ← 写入 Config 实例
ConvertValue()        ← 类型转换
GetConfigInstance()   ← 反射获取 Config 单例
GetItemType()         ← 获取泛型参数 T
FindDataArrayField()  ← 反射 _dataArray
FindIndexer()         ← 反射 this[int] 索引器
ParseCsvLine()        ← CSV 行解析
```

两端不同的代码（在 region 外）：

```
Initialize() / Dispose() / OnModSettingUpdate()
ScanAndApplyConfigs() / TryLoadModConfigs()    ← 因为 Mod API 不同
```

## 关键技术点

### 延迟扫描

两端都在 `Initialize()` 中延迟执行扫描，避免漏掉在 ConfigHook **之后**初始化的 mod：

- **前端**：`MyUtils.DelayCall(ScanAndApplyConfigs, 0, true)` — 下一帧
- **后端**：`System.Threading.Timer` 延迟 1 秒

### 自身 mod 的扫描

后端插件的 `ModId` 在 `Initialize()` 时尚未加入 `GetLoadedModIds()`，所以先用 `ModIdStr` 单独扫描自身。前端没有这个问题，因为 `ModManager.EnabledMods` 总是完整的。

### 类型转换

CSV 读到的都是字符串，`ConvertValue()` 按目标字段类型处理：

1. 可空类型 → 解包
2. 基本类型 → `int.Parse`、`short.Parse` 等
3. 布尔 → 支持 `True/False/1/0`
4. 枚举 → `Enum.Parse()`
5. 复杂类型（List/数组/字典）→ `Newtonsoft.Json` 反序列化

## 依赖

| 依赖 | 前端路径 | 后端路径 |
|------|---------|---------|
| `0Harmony.dll` | Managed/ | Managed/ |
| `GameData.Shared.dll` | Managed/ | Backend/ |
| `Newtonsoft.Json.dll` | Managed/ | Backend/ |
| `TaiwuModdingLib.dll` | Managed/ | Managed/ |
