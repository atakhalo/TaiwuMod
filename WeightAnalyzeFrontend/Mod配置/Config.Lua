return {
	Title = "重量分析",
	Source = 0,
	Version = "0.1.0.4",
	GameVersion = "0.84.46-test",
	Author = "风尘骑",
	Description = "背包、仓库重量分析 跟 当前页重量显示\n1. 在仓库界面，点击重量分析，会显示在上方显示各类的重量\na. 左边的仓库和右边的背包 是分开的\nb. 对于大类占总重量或小类占大类重量 超30%的，会标蓝；超60%的，标橙\n2. 下方重量显示增加当前分类的重量\n\n 注，因为数据缓存问题，放入拿出需要切仓库才会更新数据==更新记录==\n01.22 修复鸡舍报错\n2026.01.07 兼容正式分支",
	TagList = {
		[1] = "Modifications",
		[2] = "Compatible Mods",
		[3] = "Optimizations",
	},
	Visibility = 0,
	DefaultSettings = {
		[1] = {
			SettingType = "Toggle",
			Key = "weightAnalyze",
			DisplayName = "显示各类物品重量",
			Description = "显示各类物品重量",
			DefaultValue = true,
		},
		[2] = {
			SettingType = "Toggle",
			Key = "curWeight",
			DisplayName = "显示当前页重量",
			Description = "显示当前页重量",
			DefaultValue = true,
		},
	},
	UpdateLogList = {
		[1] = {
			Timestamp = 1767184721,
		},
		[2] = {
			Timestamp = 1767765791,
			LogList = {
				[1] = "兼容正式分支",
			},
		},
		[3] = {
			Timestamp = 1769084966,
			LogList = {
				[1] = "01.22 修复鸡舍报错",
			},
		},
	},
	ChangeConfig = false,
	HasArchive = false,
	NeedRestartWhenSettingChanged = false,
	FileId = 3635814322,
	FrontendPlugins = {
		[1] = "WeightAnalyzeFrontend.dll",
	},
	FrontendPluginsLegacy = {
		[1] = "../LegacyPlugins/WeightAnalyzeFrontend.dll",
	},
	Cover = "cover.png",
	WorkshopCover = "cover.png",
}
