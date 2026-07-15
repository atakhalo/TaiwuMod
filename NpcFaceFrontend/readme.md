

1. public void Refresh(AvatarRelatedData relatedData, short characterTemplateId)
	1. if (CreatingType.IsFixedPresetType(characterItem.CreatingType))
		1. RefreshAsSpine
		2. ResLoader.LoadModOrGameResource 
	2. Refresh(relatedData);

野兽 BeastCarrier

# 文件路径读取
TaiwuYingjiao.txt
中 第一行为 tag， 第二行文件夹
在 mod设置中，为人物配置是 以 tag:文件名 进行自定义文件配置

文件夹 内按 BigFace、NormalFace、SmallFace 放置静态图，Spine 放置动态spine文件
+ BigFace 等 也支持 BigTexture （为了适配其他mod框架）

# spine 配置
config.xml 放在spine下人物文件夹里
config.xml 用来配置spine 的信息

# 测试界面
1. 正常显示 指定立绘
	1. 	人物界面 （动态）
			1. 关系界面 （主人物 动态）
			2. 队伍界面 （无动态）
			3. 秘闻界面 （无动态） 
	2. 对话、事件界面 （动态）
		1. 蛐蛐
		2. 较艺界面 （无动态）
			1.  较艺准备 （动态）
			2. 较艺结算（无动态）
		3. 战斗 
			1. 准备界面（动态）
			2. 战斗内操作盘 （无动态）
			3. 战斗界面 (非动态)
	3. 地图界面
		1. 人物浮窗 （无动态）
		2. 地图列表 （无动态）
		3. 关注界面 （无动态）
		4. 地块右键（无动态）
	4. 产业
		1. 经营界面 （无动态）
			1. 派遣界面 （无动态）
		2. 地格标记 派遣界面（无动态）
		3. 村民册 （无动态）
		4. 身份册（无动态）
		5. 势力界面 （无动态）
	5. 月报界面 （无动态）
