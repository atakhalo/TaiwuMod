1. 复制模版项目 
	1. 前端 AaTemplateFrontend
	2. 后端 AaTemplateBackend
2. 修改名字
	1. 文件夹名、文件名、类名
3. 修改输出路径 csproj
4. 添加项目到 解决方案


常用代码
mod路径
```cs
ModInfo modInfo = ModManager.LocalMods[ModIdStr];
var filePath = Path.Combine(modInfo.DirectoryName, "npcFace.txt");
```

扫描
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
