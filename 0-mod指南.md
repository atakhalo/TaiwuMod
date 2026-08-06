# 新建mod项目流程
1. 复制模版项目 (按需复制前后端)
	1. 前端 AaTemplateFrontend
	2. 后端 AaTemplateBackend
2. 修改名字
	1. 文件夹名、文件名、类名
3. 修改输出路径 csproj
4. 添加项目到 解决方案


# 常用代码
获取mod文件夹
```cs
ModInfo modInfo = ModManager.LocalMods[ModIdStr];
var filePath = Path.Combine(modInfo.DirectoryName, "npcFace.txt");
```

扫描文件夹
```cs
public void ScanConfigs()
{
	foreach (var mod in ModManager.EnabledMods)
	{
		var modInfo = ModManager.GetModInfo(mod);
		if (modInfo == null) continue;
		string configPath = Path.Combine(modInfo.DirectoryName, "uabhook.xml");
		if (!File.Exists(configPath)) continue;
	}
}
```

获取设置
```cs
public override void OnModSettingUpdate()
{
	ModManager.GetSetting(ModIdStr, "showEfficiency", ref showEfficiency);
}
```


# mod 部署结构（已发布到 Mod 目录的规范）
- DLL 输出到 `游戏目录\Mod\<Mod名>\Plugins\`（后端 DLL 也可直接放 `Plugins\` 根目录；`了然` 用了 `Plugins\Back\` 子目录，后端相对路径写 `"Back/xxx.dll"`）
- `Config.Lua`：mod 清单（Title / BackendPlugins / FrontendPlugins / Version / GameVersion / DefaultSettings 等）
	- 后端设置项格式：`SettingType = "Toggle", Key = "xxx", DisplayName, Description, DefaultValue`
	- 游戏当前版本：`GameVersion = "1.0.67.0"`
- `Settings.Lua`：设置值，格式 `return { key = value }`
- 后端插件类继承 `TaiwuRemakePlugin`，`Harmony.CreateAndPatchAll`；`OnModSettingUpdate` 中读设置
- 前端插件类同样继承 `TaiwuRemakePlugin`，设置读取用 `ModManager.GetSetting`（`using FrameWork.ModSystem`）；前端项目 `netstandard2.1`，引用 Assembly-CSharp + UnityEngine.CoreModule 即可


# 分析经验：限制校验的位置（前后端分离）
- 游戏数据在后端，但**部分玩法限制只在前端 UI 判断，后端执行方法本身无校验**（如改制的造诣需求、解锁限制都可能在前端）
- 做"去掉限制"类 mod 时，先确认限制判断在哪个端，再决定 patch 前端还是后端：
	- 数据来源/执行入口：patch 后端（如改制可选款式列表下发 `GetBuildingMakeDisplayData`）
	- 纯 UI 判断/校验：patch 前端即可，后端不用动
- patch 通用方法时必须**精准限定范围**，避免影响其他调用者：如改制页的造诣需求全经 `ViewMake.GetAttainmentByBuildingEffect(10, ...)`（10=织锦，改制专属，其他制造传 8 走静态方法），patch 时判断 `lifeSkillType == 10` 只影响改制


# 后端 DLC 判断
- `GameData.DLC.DlcManager.IsDlcInstalled(ulong appId)` 静态方法，appId 为 Steam DLC AppId
- 前端 `DlcManager` 里可查到各 DLC 的 AppId 常量
- 常见 DLC AppId：GiftFromConchShip1=2241120, GiftFromConchShip2=2172690, FiveLoong=2764950, HappyNewYear2024=2764960, YearOfSnakeCloth=3464590, HappyNewYear2026=4395170, EightYears=4834440, GreenHillsRemain=4834450


# DLC 专属内容经验（易踩坑）
- DLC 专属**衣装/外观**的 avatar 图集只在安装对应 DLC 时才加载（前端 `AvatarAtlasAssets.TryLoadDlcAvatars` 按 `IsDlcInstalled` 加载 `{dlc}_avatarpackers`）
- 若未安装 DLC 就引用其外观，前端渲染会报错：`Failed to find atlas avatar_6_cloth_30012_normal`
- 配置表里 DLC 专属物品通常有 `DlcName` 字段标记（衣装为 `ClothingItem.DlcName`，public readonly 字段）
- 做"解锁全部 xxx"类 mod 时，必须过滤掉 `DlcName` 非空且对应 DLC 未安装的内容


# 调试技巧
- dnspy 导出的源码可能滞后于实际 DLL，动手前先用 PowerShell 反射验证方法/字段签名：
```powershell
$g = [System.Reflection.Assembly]::LoadFrom("C:\Program Files (x86)\Steam\steamapps\common\The Scroll Of Taiwu\Backend\GameData.dll")
$t = $g.GetType("GameData.Domains.Building.BuildingDomain")
$t.GetMethod("GetBuildingMakeDisplayData", [System.Reflection.BindingFlags]"Public,Instance")
```
- 注意区分静态**字段**与**属性**（如 `Config.Clothing.Instance` 是字段，用 GetField 验证）
