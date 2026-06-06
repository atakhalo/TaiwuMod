return {
	Title = "键盘手柄模拟鼠标",
	Source = 0,
	Version = "0.1.0.2",
	GameVersion = "0.84.46-test",
	Author = "风尘骑",
	Description = " 支持移动（可按住进行拖拽）和滚动，会在屏幕下方显示对应模式\n键盘按键\r\n\t1. `键 点击（或者按住）\r\n\t3. ctrl+` 移动模式；方向键移动；左 ctrl 慢速；左shift快速\r\n\t2. shift+` 滚动模式；上下键滚动\r\n手柄按键\r\n\t1.  右肩键 点击（或者按住）\r\n\t2.  中间两个键 左中键 移动模式； 右中键 滚动模式；\r\n\t3.  摇杆移动或滚动；x 慢速；y 快速\r\n\t4.  额外映射键盘键 a 空格；b esc",
	TagList = {
		[1] = "Modifications",
		[2] = "Compatible Mods",
		[3] = "Optimizations",
	},
	Visibility = 0,
	DefaultSettings = {
		[1] = {
			SettingType = "Toggle",
			Key = "mouseKey",
			DisplayName = "功能开关",
			Description = "功能开关",
			DefaultValue = true,
		},
		[2] = {
			SettingType = "Toggle",
			Key = "joystick",
			DisplayName = "手柄功能开关",
			Description = "手柄功能开关",
			DefaultValue = true,
		},
	},
	UpdateLogList = {
		[1] = {
			Timestamp = 1762674465,
			LogList = {
				[1] = "键盘手柄模拟鼠标",
			},
		},
	},
	ChangeConfig = false,
	HasArchive = false,
	NeedRestartWhenSettingChanged = false,
	FileId = 3602456422,
	FrontendPlugins = {
		[1] = "MouseKeyFrontend.dll",
	},
	Cover = "cover.png",
	WorkshopCover = "cover.png",
}
