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
	- 游戏当前版本：`GameVersion = "1.1.3"`（2026-09-22 长期测试分支；mod 与游戏 **Major/Minor 必须一致**，否则判定 Outdated、需白名单才加载；游戏版本串带 `-test` 后缀，mod 写三段的 `1.1.3` 与游戏解析结果一致）
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
- `GameData.DLC.DlcManager.IsDlcInstalled(ulong appId)` 静态方法，appId 为 Steam DLC AppId（未初始化/游戏外调用时 `_dlcInfoList` 为 null，会 NRE）
- **DlcName → AppId 不要写死**：查游戏配置表 `Config.ImplementedDlc`（`ImplementedDlcItem.Name` = DlcName，`.AppId` = Steam AppId；后端启动时 `GlobalDomain.ReloadAllConfigData()` → `Parallel.ForEach(ConfigCollection.Items, item => item.Init())` 已把全表初始化，含 `ImplementedDlc.Instance`）
	- 该表随游戏更新，游戏新增 DLC 时自带新行 ⇒ mod 无需随版本补映射
	- 表里查不到的 DlcName ⇒ 不是 DLC 内容（mod 自加），可按"保留"处理
	- 注意 `ConfigData.Init()` 只有部分表是"清空+重建"，`ImplementedDlc.Init()` 是 `_dataArray.Add`，**不要主动重复调用**（会翻倍），只读即可
- 前端 `DlcManager` 里也有各 DLC 的 AppId/名字常量（`DlcIdXxx` / `DlcNameXxx`），仅在需要前端判断时用
- 判断某个 DLC 是否真装了（排除"目录存在但没买"的干扰）：
	- 最准：读 `steamapps\appmanifest_838350.acf` 的 `InstalledDepots` 里带 `dlcappid` 的条目
	- 辅助：后端日志 `Start loading Dlc Events:` 之后的 `DLC: <appid>_<ver>, Path: ...` 行


# 游戏更新后的 mod 适配清单（实测流程）
1. 更新 `Config.Lua`：`GameVersion` 对齐当前游戏主要版本、`Version` 递增（不改 GameVersion 会被判 Outdated，需要玩家手动加白名单）
2. 对比新旧配置表（`太吾配置表旧-0922` vs `太吾配置表`）看新增行是否触发 mod 的硬编码假设：衣装新增 `DlcName=TaiwuAsXiangshu`（玄相法身 30015）就是本次 1.1.3 适配的坑
3. 用旧源码目录（`TaiwuSourceFront2026Old`）diff 新源码（`TaiwuSourceFront2026`），确认 patch 目标方法仍存在、签名未变
4. 重新编译（csproj 的 OutputPath 已指向游戏 Mod 目录，编译即部署）
5. 游戏日志位置：后端 `游戏目录\Logs\GameData_*.log`；前端 `%USERPROFILE%\AppData\LocalLow\Conchship\The Scroll of Taiwu\Player.log`（上一轮 `Player-prev.log`）


# DLC 专属内容经验（易踩坑）
- DLC 专属**衣装/外观**的 avatar 图集只在安装对应 DLC 时才加载（前端 `AvatarAtlasAssets.TryLoadDlcAvatars` 按 `IsDlcInstalled` 加载 `{dlc}_avatar_packers`）
- 若未安装 DLC 就引用其外观，前端渲染会报错：`Failed to load Resource GameAtlas/AvatarPackers/avatar_6_cloth_30015_normal`（图集名 = `avatar_{avatarId}_cloth_{DisplayId}_{big|normal|small}`）
- 配置表里 DLC 专属物品通常有 `DlcName` 字段标记（衣装为 `ClothingItem.DlcName`，public readonly 字段）
- 做"解锁全部 xxx"类 mod 时，必须过滤掉 `DlcName` 非空且对应 DLC 未安装的内容；**用配置表 `ImplementedDlc` 查 AppId，不要硬编码映射表**（游戏新增 DLC 时不必改 mod；查不到的名字按"非 DLC 内容"保留）
- 判断代码要**容错**：读配置表失败/未就绪时只记日志并跳过过滤，不能让异常从 Harmony patch 抛出去（会连累整个界面的数据下发）


# 调试技巧
- dnspy 导出的源码可能滞后于实际 DLL，动手前先用 PowerShell 反射验证方法/字段签名：
```powershell
$g = [System.Reflection.Assembly]::LoadFrom("C:\Program Files (x86)\Steam\steamapps\common\The Scroll Of Taiwu\Backend\GameData.dll")
$t = $g.GetType("GameData.Domains.Building.BuildingDomain")
$t.GetMethod("GetBuildingMakeDisplayData", [System.Reflection.BindingFlags]"Public,Instance")
```
- 注意区分静态**字段**与**属性**（如 `Config.Clothing.Instance` 是字段，用 GetField 验证）


# Unity UI 运行时按钮注入（前端 mod 常用）
给现有界面动态添加按钮/控件时，用 clone 已有按钮 GameObject 作模板最省事（自动继承图集/字体/样式），但有几个坑：

1. **绑定监听**：clone 的按钮不会带运行时 `AddListener` 的监听，需 `btn.onClick.RemoveAllListeners()` 后重新绑定
2. **隐藏原图**：若按钮的图标与背景是同一张 Image（挂在根节点），**不能 `enabled = false`**——Unity Button 靠根 Graphic 参与点击检测，禁用后按钮点不动；应 `img.sprite = null`（隐藏但不失点击），或直接保留原图、叠加子物体文字
3. **动态加文字（易 NRE）**：`go.AddComponent<TextMeshProUGUI>()` 后手动赋 `font`/`fontSharedMaterial` 会因字体未初始化报空引用。应 **clone 界面里已有 TMP 的 GameObject**（自带字体材质），只改 `text`
4. **文字被顶出按钮**：clone 的 TMP 可能残留 `ContentSizeFitter`/`LayoutElement`/`LayoutGroup` 组件，需先 Destroy 再设置 `anchorMin/Max=(0,0)-(1,1)`、`offset=0`、`pivot=(0.5,0.5)`、`localPosition=0`；若父物体有布局组件，给文字加 `LayoutElement.ignoreLayout = true`
5. **图标字符**：中文游戏字体一般内置常见 Unicode 符号（`●`/`▼`/`✓` 等），避免用生僻字形；实测显示方框/空白即字体缺字形，换常用字符
6. **防重复注入**：注入前先 `transform.Find("固定名字")` 判断是否已存在；创建后改名固定标识
7. 访问游戏私有字段/属性：`AccessTools.Field(typeof(X), "name")` / `AccessTools.Property(typeof(X), "name")`（注意有些 public 属性在反编译里其实是 private，如 `SwapSoulCharacterItem.Parent`）


# AvatarData 外貌字段速查（做"一键设置外貌"类 mod 用）
- 主体：`AvatarId`（= 体型×2+性别+1，性别=AvatarId%2，体型=(AvatarId-1)/2）、`HeadId`、部件 ID（前后发/眉/眼/鼻/嘴/胡须1·2/面部特征1·2）
- **颜色 10 个（byte）**：`ColorSkinId`/`ColorClothId`/`ColorFrontHairId`/`ColorBackHairId`/`ColorEyebrowId`/`ColorEyeballId`/`ColorMouthId`/`ColorBeard1Id`/`ColorBeard2Id`/`ColorFeature1Id`/`ColorFeature2Id`
- **五官微调 12 个（short）**：眼/眉各 `Height`/`Distance`/`Angle`/`Scale`，鼻/嘴各 `Height`/`Scale`（对应化形塑体界面的间距/高度/缩放/角度滑块）
- 眼睛合法性校验：`AvatarManager.Instance.GetAvatarGroup(AvatarId).Get(EAvatarElementsType.Eye, [EyesMainId, EyesLeftId])` 返回 null 则左右眼置 0
- `Copy()` 是**实例方法**：`new AvatarData().Copy(other)`
- 资源按 `AvatarId`（体型+性别）组织：性别/体型不一致时，对方的外貌项可能不在本组资源里（不可全选）
