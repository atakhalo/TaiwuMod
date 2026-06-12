return {
	Title = "胜强助手",
	Source = 0,
	Version = "0.1.0.2",
	GameVersion = "0.84.46-test",
	Author = "风尘骑",
	Description = "在装备界面显示平均品级(相差大于等于3时触发装备胜强);<br>内力界面显示总真气(大于等于1.25倍时真气胜强)",
	TagList = {
		[1] = "Modifications",
		[2] = "Compatible Mods",
		[3] = "Optimizations",
	},
	Visibility = 0,
	DefaultSettings = {
		[1] = {
			SettingType = "Toggle",
			Key = "avgGrade",
			DisplayName = "显示平均品级",
			Description = "显示平均品级",
			DefaultValue = true,
		},
		[2] = {
			SettingType = "Toggle",
			Key = "exNeili",
			DisplayName = "显示总真气",
			Description = "显示总真气",
			DefaultValue = true,
		},
	},
	UpdateLogList = {
		[1] = {
			Timestamp = 1767102710,
		},
	},
	ChangeConfig = false,
	HasArchive = false,
	NeedRestartWhenSettingChanged = false,
	FileId = 3635079634,
	FrontendPlugins = {
		[1] = "WinBetterFrontend.dll",
	},
	Cover = "cover.png",
	WorkshopCover = "cover.png",
}
