using CharacterDataMonitor;
using Config;
using FrameWork;
using FrameWork.ModSystem;
using Game.Components.Character;
using Game.Components.Information;
using Game.Views.Bottom;
using Game.Views.CharacterMenu;
using Game.Views.Combat;
using Game.Views.MapBlockCharList;
using Game.Views.SettlementInformation;
using Game.Views.VillagerRoleView;
using GameData.Domains.Character;
using GameData.Domains.Character.Display;
using GameData.Domains.Item;
using GameData.Domains.Item.Display;
using GameData.Domains.Map;
using GameData.Domains.Mod;
using GameData.Domains.Taiwu;
using GameData.Domains.Taiwu.Display;
using GameData.Serializer;
using GameData.Utilities;
using HarmonyLib;
using HarmonyLib.Tools;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices.ComTypes;
using System.Text.RegularExpressions;
using TaiwuModdingLib.Core.Plugin;
using TaiwuModdingLib.Core.Utils;
using TMPro;
using UICommon.Character;
//using UICommon.Character.Avatar;
using UnityEngine;
using UnityEngine.UI;
using Spine.Unity;
using TaiwuAvatar = Game.Components.Avatar.Avatar;
using TaiwuAvatarSize = Game.Components.Avatar.AvatarSize;

namespace NpcFace
{
	public class MyUtils
	{
		public static void MyLog(string log)
		{
			Debug.Log($"[{nameof(NpcFace)}] {log}");
		}

		public static void DelayCall(Action action, float delay, bool real)
		{
			//Game.Instance.StartCoroutine(DelayCoroutine(TrySetNpcFace, 0, avatar, null, relatedData));
			GameApp.Instance.StartCoroutine(DelayCoroutine(action, delay, real));
		}

		private static IEnumerator DelayCoroutine(Action action, float delay, bool real)
		{
			if (real)
				yield return new WaitForSecondsRealtime(delay);
			else
				yield return new WaitForSeconds(delay);
			action?.Invoke();
		}

		public static void ShowMonoCur(GameObject gameObject)
		{
			ShowMonoHelper(gameObject.transform, 0, gameObject.transform);
		}

		public static void ShowMonoToParent(Transform transform)
		{
			var canvas = transform.GetComponentInParent<Canvas>();
			if (canvas != null)
			{
				var depth = 0;
				var cur = transform;
				while (cur != canvas.transform)
				{
					ShowMonoOne(cur, depth, prefix: cur.GetSiblingIndex().ToString());
					cur = cur.parent;
				}
				ShowMonoOne(canvas.transform, depth);
			}
		}

		public static void ShowMono(GameObject gameObject)
		{
			var canvas = gameObject.GetComponentInParent<Canvas>();
			if (canvas != null)
			{
				ShowMonoHelper(canvas.transform, 0, gameObject.transform);
			}
		}

		public static void ShowMonoHelper(Transform transform, int depth, Transform sp)
		{
			ShowMonoOne(transform, depth, sp);
			for (int i = 0; i < transform.childCount; i++)
			{
				var child = transform.GetChild(i);
				ShowMonoHelper(child, depth + 1, sp);
			}
		}

		public static void ShowMonoOne(Transform transform, int depth = 0, Transform sp = null, string prefix = "", string postfix = "")
		{
			// 构建缩进字符串
			var indent = new string('\t', depth);
			var specialMark = (sp == transform) ? "<<" : "";

			// 构建组件信息
			var monos = transform.GetComponents<MonoBehaviour>();
			var monoNames = monos == null ? "" : string.Join(",", monos.Select(m => m.GetType().Name));

			var btn = transform.GetComponent<Button>();
			var isbtn = btn == null ? "" : "(isbtn)";

			// 构建完整日志信息
			var str = $"{indent}{prefix}{transform.gameObject.name} {specialMark} ({monoNames}) {isbtn}{postfix}";

			// 先打印当前节点，再递归子节点
			MyLog(str);
		}

		public static void CopyBaseClassFieldsIncludingParents(Component source, Component destination, Type baseType)
		{
			Type currentType = baseType;

			// 遍历从baseType开始到MonoBehaviour的整个继承链
			while (currentType != null && currentType != typeof(MonoBehaviour) && currentType != typeof(System.Object))
			{
				// 获取当前类型的字段
				FieldInfo[] fields = currentType.GetFields(
					BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);

				foreach (FieldInfo field in fields)
				{
					// 跳过不应该复制的字段
					if (field.IsStatic) continue;

					try
					{
						field.SetValue(destination, field.GetValue(source));
						//MyLog($"复制字段 {field.Name} {field.GetValue(destination)}<-{field.GetValue(source)}");
					}
					catch (Exception ex)
					{
						//Debug.LogWarning($"复制字段 {field.Name} 时出错: {ex.Message}");
					}
				}

				// 移动到父类
				currentType = currentType.BaseType;
			}
		}

		public static bool isAnyKey(List<KeyCode> keyCodes, out KeyCode keyCode)
		{
			for (int i = 0; i < keyCodes.Count; i++)
			{
				if (Input.GetKeyDown(keyCodes[i]))
				{
					keyCode = keyCodes[i];
					return true;
				}
			}
			keyCode = KeyCode.None;
			return false;
		}

		public static Color Color16A(uint hex)
		{
			return new Color32(
					(byte)((hex >> 24) & 0xFF),
					(byte)((hex >> 16) & 0xFF),
					(byte)((hex >> 8) & 0xFF),
					(byte)(hex & 0xFF));
		}
	}


	[PluginConfig(pluginName: "NpcFace", creatorId: "atakhalo", pluginVersion: "0.3.0.4")]
    public class NpcFaceFrontendPlugin : TaiwuRemakePlugin
    {
        private Harmony harmony;


        public static bool npcFace; // 开关 是否开启
        public static bool forTaiwu; // 开关 太吾是否开启
        public static bool forNpc; // 开关 NPC是否开启


		// 提一些特殊的npc 出来到设置界面方便 玩家选择
        public static string[] npcName = {
            "NpcFace_yingjiao",//迎娇
            "NpcFace_wanquzhimin", // 螺舟
            "NpcFace_wudangya", // 武当鸭
            "NpcFace_wuhushanghui",// 五湖商人
            "NpcFace_wenshanshuhaige",// 文山书海商人
            "NpcFace_yiyihou",// 衣以侯
            "NpcFace_xiaoyiyihou_happy", // 衣以侯笑脸
            "NpcFace_monv",// 莫女
            "NpcFace_monv_happy", // 莫女笑脸
            "NpcFace_jinhuanger",// 金凰儿
            "NpcFace_jinhuanger_happy",// 金凰儿笑脸
            "NpcFace_shufang",// 术方
            "NpcFace_xiaoshufang",// 小术方
            "NpcFace_xiangshu1",//相枢
            "NpcFace_yifu",// "义父"
            "NpcFace_huanxin",//焕心
            "NpcFace_huanhuahuanxin",//小焕心
            "NpcFace_mutouren",// 木头人
            "NpcFace_xuxiangong",// 徐仙公
            "NpcFace_chicken_dawang",// 大王
            "NpcFace_huji",//胡姬
            "NpcFace_xiyunvhai",//西域女孩
            "NpcFace_guoquderanchenzi",// 年轻染尘子
            "NpcFace_ziwuxiao",// 紫无绡
            "NpcFace_yufu", // 龙语茯
            "NpcFace_mengmianshenminvzi", // 蒙面龙语茯
            "NpcFace_ranxindu", // 心毒
            "NpcFace_shenggu", // 圣姑
            "NpcFace_bailu",// 白鹿
            "NpcFace_baiwuyang",// 白无恙
            "NpcFace_baihuazhuxianfengqing",// 冯青
            "NpcFace_jixiyounian",// 姬穸小
            "NpcFace_jixishaonian",// 姬穸中
            "NpcFace_jixichengnian",// 姬穸大
            "NpcFace_changshengxuannv",// 筠儿
            "NpcFace_tiannvxuying",// 璇女天女
            "NpcFace_tongshengrendonghua",// 铜生
            "NpcFace_nvanian",// 女阿念
            "NpcFace_sancaidimo",// 三才地魔
            "NpcFace_sancaitianmo",// 三才人魔
            "NpcFace_dimo",// 地魔
            "NpcFace_xuanzhi",// 玄质
            "NpcFace_huaju",// 华居
            "NpcFace_jieyiseng",// 借“衣”僧

        }; // 资源名
        public static int npcNameIdx = 0; // 资源名序号

        public static bool customNpc; // 开关 是否自选
        public static string npcNameCustom; // 资源名 太吾使用 特殊npc图片的文件名

        public static bool showCharId = false; // 开关 是否显示为charid
        public static Dictionary<int, string> idRes = new Dictionary<int, string>();  // 需要显示立绘的 普通npc id 对应立绘; 不使用了
        public static Dictionary<string, string> npcRes = new Dictionary<string, string>();// 需要显示立绘的 普通npc 姓名 对应立绘

		public static bool toCreateFile = false;
        public static bool toReadFile = false;

        public static List<string> resDirs = new List<string>(); // 资源路径
        public static Dictionary<string, Texture2D> resCache = new Dictionary<string, Texture2D>();

        public static Dictionary<string, string> tagDirs = new Dictionary<string, string>(); // 资源tag对应路径

		public static Dictionary<int, string> idNameCache = new Dictionary<int, string>(); // 对一些id 跟 name进行缓存

		public static Dictionary<string, int> ImgTemplate = new Dictionary<string, int>(); // npc模板id缓存

		public static bool samllSpine = false;


		public static void MyLog(string log)
        {
			MyUtils.MyLog(log);
        }

        public override void Initialize()
        {
			MyUtils.MyLog("Initialize");
            harmony = Harmony.CreateAndPatchAll(typeof(NpcFaceFrontendPlugin));

			// 进行模板扫描
            GameApp.Instance.StartCoroutine(TryScanTemplate());

			GameApp.Instance.StartCoroutine(TryScanMod());

		}

        public override void Dispose()
        {
            if (harmony != null)
            {
                harmony.UnpatchSelf();
            }
        }

        public override void OnModSettingUpdate()
        {
            ModManager.GetSetting(ModIdStr, "npcFace", ref npcFace);
            ModManager.GetSetting(ModIdStr, "forTaiwu", ref forTaiwu);
            ModManager.GetSetting(ModIdStr, "forNpc", ref forNpc);
            ModManager.GetSetting(ModIdStr, "npcNameIdx", ref npcNameIdx);
            ModManager.GetSetting(ModIdStr, "customNpc", ref customNpc);
            ModManager.GetSetting(ModIdStr, "npcNameCustom", ref npcNameCustom);
            //MyLog($"{npcFace}, {npcNameIdx}, {customNpc}, {npcNameCustom}");

            npcRes.Clear();
            TryLoadNpc("npc1", "npcRes1", "npcAsset1");
            TryLoadNpc("npc2", "npcRes2", "npcAsset2");
            TryLoadNpc("npc3", "npcRes3", "npcAsset3");

            ModManager.GetSetting(ModIdStr, "toCreateFile", ref toCreateFile);
            TryCreateFile();
            ModManager.GetSetting(ModIdStr, "toReadFile", ref toReadFile);
            TryReadFile();

            resDirs.Clear();
            resDirs.Add(""); // 塞个空字段
            string resDirStr = "";
            ModManager.GetSetting(ModIdStr, "resDir1", ref resDirStr);
            //MyLog("OnModSettingUpdate");
            tagDirs["1"] = new string(resDirStr);
            if (!string.IsNullOrEmpty(resDirStr)) resDirs.Add(resDirStr);
            ModManager.GetSetting(ModIdStr, "resDir2", ref resDirStr);
            tagDirs["2"] = new string(resDirStr);
            if (!string.IsNullOrEmpty(resDirStr)) resDirs.Add(resDirStr);
            ModManager.GetSetting(ModIdStr, "resDir3", ref resDirStr);
            if (!string.IsNullOrEmpty(resDirStr)) resDirs.Add(resDirStr);
            tagDirs["3"] = new string(resDirStr);
			ModManager.GetSetting(ModIdStr, "npcNameCustom", ref npcNameCustom);

			// 目前小图片都没有 npcSkeleton，samllSpine没有用
			// ModManager.GetSetting(ModIdStr, "samllSpine", ref samllSpine); // 
		}

		/// <summary>
		/// 尝试扫描记录 立绘图名字 对应的模板id
		/// 数据大概800个项，立绘图大概500+
		/// </summary>
		public static IEnumerator TryScanTemplate()
		{
            yield return new WaitForSeconds(0);
			var _dataArray = Traverse.Create(Character.Instance).Field("_dataArray").GetValue<List<CharacterItem>>();
			for (int i = 0; i < _dataArray.Count; i++)
			{
				if(string.IsNullOrEmpty(_dataArray[i].FixedAvatarName)) continue;
				if(!ImgTemplate.ContainsKey(_dataArray[i].FixedAvatarName))
					ImgTemplate[_dataArray[i].FixedAvatarName] = _dataArray[i].TemplateId;
			}
		}

		/// <summary>
		/// 根据 TaiwuYingjiao.txt 收集tag跟目录
		/// </summary>
		/// <returns></returns>
		public static IEnumerator TryScanMod()
        {
            yield return new WaitForSeconds(0);
            //MyLog("TryScanMod");

            //tagDirs.Clear();
            foreach (ModId mod in ModManager.EnabledMods)
            {
                ModInfo modInfo = ModManager.GetModInfo(mod);
                var configPath = Path.Combine(modInfo.DirectoryName, "TaiwuYingjiao.txt");
                if (!File.Exists(configPath)) continue;
                var s = File.ReadAllLines(configPath, System.Text.Encoding.UTF8);
                if (s.Length == 0)
                    continue;
                var dir = "TaiwuYingjiao";
                var tag = "";
                if(s.Length > 0) tag = s[0].Trim();
                if(s.Length > 1) dir = s[1].Trim();
                if(!string.IsNullOrEmpty(tag))
                {
                    var dirPath = Path.Combine(modInfo.DirectoryName, dir);
                    tagDirs[tag] = dirPath;
                    MyLog($"太吾迎娇 收集到图片目录 {tagDirs.Count} {tag}:{dirPath} ");
                }
            }
        }

		/// <summary>
		/// 读取 mod 设置 中 普通npc 配置 （可能是tag形式进行 文件配置）
		/// </summary>
        public  void TryLoadNpc(string nameKey, string resKey, string assetKey)
        {
            string npcNameStr = "";
            int npcResIdx = 0;
            string npcAssetStr = "";
            ModManager.GetSetting(ModIdStr, nameKey, ref npcNameStr);
            if (string.IsNullOrEmpty(npcNameStr))
                return;
            ModManager.GetSetting(ModIdStr, resKey, ref npcResIdx);
            if (npcName.Length > npcResIdx) npcRes[npcNameStr] = npcName[npcResIdx];
            ModManager.GetSetting(ModIdStr, assetKey, ref npcAssetStr);
            if (!string.IsNullOrEmpty(npcAssetStr)) npcRes[npcNameStr] = npcAssetStr;
            //MyLog($"TryLoadNpc {npcNameStr} {npcRes[npcNameStr]} ({npcResIdx},{npcAssetStr}) ");
        }

		/// <summary>
		/// 创建 mod 普通npc配置文件
		/// </summary>
		public static void TryCreateFile()
        {
            if(toCreateFile)
            {
                var dirPath = ModManager.GetModRootFolder();
                var filePath = Path.Combine(dirPath, "太吾迎娇(npc立绘)", "npcFace.txt");
                if (!File.Exists(filePath)) {
                    var str1 = $"// 说明:1. 首行跳过； 2. 格式:名字,资源名;（即英文逗号分隔，英文分号结尾）3. 资源名在mod的下载目录下找 C:\\Program Files (x86)\\Steam\\steamapps\\workshop\\content\\838350\\3593734737\\npcName.txt\n";
                    var str2 = $"我是示例,NpcFace_yingjiao;\n";
                    var s = str1 + str2;
                    File.WriteAllText(filePath, s, System.Text.Encoding.UTF8);
                    //MyLog($"创建 {filePath} npcFace.txt");
                }
            }
        }

		/// <summary>
		/// 创建 mod 普通npc配置文件
		/// </summary>
		public static void TryReadFile()
        {
            if (toReadFile)
            {
                var dirPath = ModManager.GetModRootFolder();
                var filePath = Path.Combine(dirPath, "太吾迎娇(npc立绘)", "npcFace.txt");
                if (File.Exists(filePath))
                {
                    var s = File.ReadAllLines(filePath, System.Text.Encoding.UTF8);
                    for (int i = 0; i < s.Length; i++)
                    {
                        if (i == 0) continue;
                        var line = s[i];
                        if(string.IsNullOrEmpty(line)) continue;
                        var r = line.Split(',');
                        if (r.Length > 1)
                        {
                            var n = r[0].Trim();
                            var res = r[1].Split(';')[0].Trim();
                            npcRes[n] = res;
                            //MyLog($"读取 npcFace.txt {n} {res}");
                        }
                    }
                }
            }
        }

		#region 使用id进行查找（ 不使用了）
		/// <summary>
		/// 根据接口信息中的id 尝试设置 普通npc  立绘
		/// </summary>
        public static void TrySetNpcFace(TaiwuAvatar avatar, CharacterAvatar? instance, int charId)
        {
            if (avatar == null) return;
            var curId = charId;
            if (curId != -1)
            {
                MyUtils.MyLog($"charId 找到id {curId}");
                if (idRes.ContainsKey(curId))
                    resLoad(avatar, instance, isTaiwu: false, idRes[curId]);
            }
        }
		/// <summary>
		/// 根据接口信息 中的 id 尝试设置 普通npc  立绘
		/// </summary>
		public static void TrySetNpcFace(TaiwuAvatar avatar, CharacterAvatar? instance, CharacterDisplayData data)
        {
            if (avatar == null) return;
            var curId = data.CharacterId;
            if (curId != -1)
            {
				MyUtils.MyLog($"CharacterDisplayData 找到id {curId}");
				if (idRes.ContainsKey(curId))
                    resLoad(avatar, instance, isTaiwu: false, idRes[curId]);
            }
        }

		/// <summary>
		/// 尝试设置 普通npc  立绘
		/// 接口信息中无id， 需要根据 mousetips 找id
		/// </summary>
		public static void TrySetNpcFace(TaiwuAvatar avatar, CharacterAvatar? instance, AvatarRelatedData relatedData)
        {
			MyUtils.MyLog("TrySetNpcFace");
            if(avatar == null) return;
            var curId = TryFindId(avatar.transform, maxUp: 3);
            if(curId != -1)
            {
                var taiwuCharId = SingletonObject.getInstance<BasicGameData>().TaiwuCharId;
                if(curId == taiwuCharId)
                {
                    resLoad(avatar, instance, isTaiwu: true);
                }
                if (idRes.ContainsKey(curId))
                {
                    resLoad(avatar, instance, isTaiwu: false, idRes[curId]);
                }
                else
                {
                }
            }
        }

		// 尝试寻找id， maxUp 控制向上寻找的层数
		public static int TryFindId(Transform transform, int maxUp)
        {
			MyUtils.MyLog($"当前查找{transform}, {maxUp}");
			var s = TryGetNpcId(transform);
            if (s == -1)
            {
                if (transform.parent && maxUp != 0)
                {
                    return TryFindId(transform.parent, maxUp - 1);
                }
            }
            return s;
        }

		// 尝试在当前层级及子层级寻找id， 
		public static int TryGetNpcId(Transform transform)
        {
			// 尝试获取 TooltipInvoker 并从 RuntimeParam 中提取 id
			var r = transform.GetComponent<TooltipInvoker>();
            if(r == null) r = transform.GetComponentInChildren<TooltipInvoker>();
            int charId = -1;
            if(r != null && r.RuntimeParam != null)
            {

                r.RuntimeParam.Get("charId", out charId);
                if (charId == -1) r.RuntimeParam.Get("CharId", out charId);
                if (charId == -1) r.RuntimeParam.Get("NpcCharId", out charId);
				MyUtils.MyLog($"找到 TooltipInvoker {charId}");
            }
            return charId;
        }
		#endregion

		# region 使用 name 进行查找 (泛用性较高)
		/// <summary>
		/// 根据接口信息 中的 name 尝试设置 普通npc  立绘
		/// </summary>
		public static bool TrySetNpcFaceByName(TaiwuAvatar avatar, CharacterAvatar? instance, AvatarRelatedData relatedData)
        {
            MyUtils.MyLog($"TrySetNpcFaceByName relatedData {avatar}");
            if (avatar == null) return false;
            var curName = TryFindName(avatar.transform, maxUp: 3);
            if (curName != "")
            {
                //MyLog($"TrySetNpcFaceByName 找到名字 {curName}");
                var taiwuDisplayName = SingletonObject.getInstance<BasicGameData>().TaiwuMonasticTitleOrDisplayName;
                if (curName == taiwuDisplayName)
                {
                    return resLoad(avatar, instance, isTaiwu: true);
                }
                else
                {
                    if (npcRes.ContainsKey(curName))
                    { 
                        return resLoad(avatar, instance, isTaiwu: false, npcRes[curName]); 
                    }
                    else
                    {
						MyUtils.MyLog($"TrySetNpcFaceByName 名字不对 {curName}");
						return false;
                    }
                }
            }
			MyUtils.MyLog($"TrySetNpcFaceByName 没找到名字");
			return false;
        }

		/// <summary>
		/// 根据接口信息 中的 name 尝试设置 普通npc  立绘
		/// </summary>
		public static bool TrySetNpcFaceByName(TaiwuAvatar avatar, CharacterAvatar? instance, CharacterDisplayData displayData)
        {
            MyUtils.MyLog($"TrySetNpcFaceByName displayData  {avatar}");
            if (avatar == null) return false;
            string curName = NameCenter.GetMonasticTitleOrDisplayName(displayData, isTaiwu: false);
            if (curName != "")
            {
				MyUtils.MyLog($"TrySetNpcFaceByName 找到名字 {curName}");
                if (npcRes.ContainsKey(curName))
                    return resLoad(avatar, instance, isTaiwu: false, npcRes[curName]);
            }
            return false;
        }

		// 尝试寻找 name， maxUp 控制向上寻找的层数
		public static string TryFindName(Transform transform, int maxUp)
        {
            if(transform.name.Contains("TaiwuChar")) // FillElementPost 调用过来的，会走到这里，ui_bottom下可以用这个判断
            {
                var taiwuDisplayName = SingletonObject.getInstance<BasicGameData>().TaiwuMonasticTitleOrDisplayName;
                return taiwuDisplayName;
            }
            var ui = transform.GetComponentInParent<UIBase>();
            //if (ui is UI_LifeSkillCombatBegin)
            //    MyLog("$正在查找 UI_LifeSkillCombatBegin");
            var s = TryGetNpcName(transform);
            if (s == null || s == "")
            {
                if (transform.parent && maxUp != 0)
                {
                    return TryFindName(transform.parent, maxUp - 1);
                }
            }
            return s;
        }

		// 尝试在当前层级及子层级寻找 name， 
		public static string TryGetNpcName(Transform transform)
        {
			var sp = TryGetNpcNameSp(transform);
			if (sp != null) return sp;

			var nameframe = transform.GetComponentInChildren<CommonCharacterNameFrame>();
			if (nameframe)
			{
				return nameframe.NameLabel.text;
			}

			TextMeshProUGUI t = null;
            var ts = transform.GetComponentsInChildren<TextMeshProUGUI>();
			MyUtils.MyLog($" {transform} 下 tmp总个数为 {ts.Count()} ");
			foreach (var t1 in ts)
            {
                if (t1.name.Contains("name") || t1.name.Contains("Name")
                    && t1.name != "OrganizationName" && t1.name != "SkillName"
                    && !t1.text.Contains("ID:") && t1.text!="剩余潜力")  // mod添加的控件
                {
                    t = t1;
                    break;
                }
				if(t1.transform.parent.name == "NameHolder")
				{
					t = t1;
					break;
				}
            }
            if (t)
            {
                MyUtils.MyLog($"找到 {transform}下的tmp  {t} {t.text}");
                return t.text;
            }
			MyUtils.MyLog($" {transform} 无 命名tmp ");
			


			var mouseTipDisplayer = transform.GetComponent<TooltipInvoker>();
            if (mouseTipDisplayer == null) mouseTipDisplayer = transform.GetComponentInChildren<TooltipInvoker>();
            CharacterDisplayData charData = null;
            if (mouseTipDisplayer != null && mouseTipDisplayer.RuntimeParam != null)
            {
                mouseTipDisplayer.RuntimeParam.Get("CharData", out charData);
                if (charData != null)
                {
                    var taiwuId = SingletonObject.getInstance<BasicGameData>().TaiwuCharId;
                    string curName = NameCenter.GetMonasticTitleOrDisplayName(charData, isTaiwu: charData.CharacterId == taiwuId);
					MyUtils.MyLog($"找到 {transform}下的 MouseTipDisplayer CharData {curName}");
                    return curName;
                }
				mouseTipDisplayer.RuntimeParam.Get("characterId", out int charId);
				if(idNameCache.ContainsKey(charId))
				{
					return idNameCache[charId];
				}
			}
			var r = transform.GetComponent<Refers>();
            if (r is Avatar) r = null;
            if (r != null)
            {
				MyUtils.MyLog($"找到refer {r} {r.Names.Count}");
                r.CTryGet<TextMeshProUGUI>("Name", out t);
                if(t == null) r.CTryGet<TextMeshProUGUI>("CharacterName", out t);
                if(t == null) r.CTryGet<TextMeshProUGUI>("CharName", out t);
            }
            if (t)
            {
				MyUtils.MyLog($"找到 refer 下的tmp {t} {t.text}");
				return t.text;
            }
            return "";
        }

		public static string TryGetNpcNameSp(Transform transform)
		{
			// 地图人物列表
			if (transform.GetComponent<MapBlockChar>()) 
			{
				return transform.GetChild(2).GetComponent<TextMeshProUGUI>().text;
			}
			// 地图界面下方人物
			if(transform.GetComponentInParent<ViewBottom>())
			{
				if (transform.name.StartsWith("MainChar"))
				{
					return SingletonObject.getInstance<BasicGameData>().TaiwuMonasticTitleOrDisplayName;
				}
				if (transform.name.StartsWith("Teammate1")) return TryGetTeammateName(1);
				if (transform.name.StartsWith("Teammate2")) return TryGetTeammateName(2);
				if (transform.name.StartsWith("Teammate3")) return TryGetTeammateName(3);
			}
			// 装备界面
			if (transform.parent.name == "SoftMask")
			{
				if(transform.parent.parent.parent.name == "CharacterCircle") // 装备界面
				{
					// 如果是装备界面 或人物界面 直接 获取 ViewCharacterMenu 的 _viewCharacterMenuDisplayData
					if(UIElement.CharacterMenuEquip.Exist)
					{
						var e = UIElement.CharacterMenuEquip.UiBaseAs<ViewCharacterMenuEquip>();
						if (transform.IsChildOf(e.transform))
						{
							var m = UIElement.CharacterMenu.UiBaseAs<ViewCharacterMenu>();
							if (m.CurrentCharacterIsTaiwu) 
							{
								return SingletonObject.getInstance<BasicGameData>().TaiwuMonasticTitleOrDisplayName;
							}
							else
							{
								if(idNameCache.ContainsKey(m.CurCharacterId))
									return idNameCache[m.CurCharacterId];
							}
						}
					}

					// // 太吾有点搞，名字没转化
					// var cc = transform.parent.parent.parent.GetComponent<CharacterCircle>();
					// var characterNameAndTitle = Traverse.Create(cc).Field("characterNameAndTitle").GetValue<NameAndTitle>();
					// var name = Traverse.Create(characterNameAndTitle).Field("characterName").GetValue<Name>();
					// var label = Traverse.Create(name).Field("label").GetValue<TextMeshProUGUI>();
					// return label.text;
				}
			}
			//  战斗界面同道 治疗界面 势力界面 秘闻界面
			if (transform.parent.name == "AvatarMask") 
			{
				//  战斗界面同道
				var ct = transform.parent.parent.GetComponent<CombatTeammate>();
				if (ct)
				{
					return Traverse.Create(ct).Field("teammateName").GetValue<TextMeshProUGUI>().text;
				}

				// 治疗界面
				var hc = transform.parent.parent.GetComponent<HealChar>();
				if(hc)
				{
					var n = Traverse.Create(hc).Field("nameFrame").GetValue<CommonCharacterNameFrame>();
					return n.NameLabel.text;
				}

				// 势力界面
				var sc = transform.parent.parent.GetComponent<SettlementChar>();
				if(sc)
				{
					return sc.CharName;
				}

				// 秘闻界面
				var p3 = transform.parent.parent.parent;
				if (p3.name == "Avatar")
				{
					var ss = p3.parent.GetComponent<SecretInformationSourceItem>();
					if (ss)
					{
						return Traverse.Create(ss).Field("nameLabel").GetValue<TextMeshProUGUI>().text;
					}
				}

			}
			// 较艺准备界面
			if (UIElement.LifeSkillCombatBegin.Exist)
			{
				var nh = transform.parent.Find("NameHolder");
				if(nh) return nh.GetChild(0).GetComponent<TextMeshProUGUI>().text;
			}
			// 战斗界面 敌方
			if(transform.parent.name == "AvatarBack")
			{
				var t = transform.parent.GetChild(2).GetChild(0).GetChild(1).GetComponent<TextMeshProUGUI>();
				if (t)
				return t.text;
			}
			// 身份名册
			if(transform.parent.name == "ShowMainCharacterMenu")
			{
				var p4 = transform.parent.parent.parent.parent;
				if(p4.GetComponent<AssignPageVillagerView>())
				{
					return p4.GetChild(4).GetChild(0).GetComponent<TextMeshProUGUI>().text;
				}
			}
			// 关注界面
			if (transform.parent.name == "AvatarRect")
			{
				var fc = transform.parent.parent.GetComponent<Game.Views.MapBlockCharList.FollowingChar>();
				if(fc)
				{
					return fc.transform.GetChild(3).GetChild(2).GetComponent<TextMeshProUGUI>().text;
				}
			}

			return null;

		}

		public static string TryGetTeammateName(int index)
		{
			CharacterMonitorModel monitor = SingletonObject.getInstance<CharacterMonitorModel>();
			List<int> combatIds = monitor.GetTaiwuCombatTeamCharIds();
			if(idNameCache.TryGetValue(combatIds[index], out var r)) return r;
			return null;
		}
		#endregion

		#region hook 游戏中获取姓名的地方进行缓存
		[HarmonyPrefix, HarmonyPatch(typeof(ViewBottom), "RefreshChar")]
		public static void ViewBottom_NameHook(ViewBottom __instance, List<CharacterDisplayData> data, int index)
		{
			if(index == 0) return; // 太吾跳过
			if(data.Count > index && data[index].CharacterId != -1)
			{
				var name = NameCenter.GetMonasticTitleOrDisplayName(data[index], isTaiwu: false);
				MyUtils.MyLog($"ViewBottom__NameHook {name}");
				if (npcRes.ContainsKey(name))
					idNameCache[data[index].CharacterId] = NameCenter.GetMonasticTitleOrDisplayName(data[index], isTaiwu: false);
			}
		}

		// NameAndTitle 里也有set
		// 直接hook GetMonasticTitleOrDisplayName
		[HarmonyPostfix, HarmonyPatch(typeof(NameCenter), "GetMonasticTitleOrDisplayName", argumentTypes: new Type[2] { typeof(CharacterDisplayData), typeof(bool) })]
		public static void NameCenter_NameHook(CharacterDisplayData displayData, bool isTaiwu, string __result)
		{
			if(isTaiwu) return;
			if(npcRes.ContainsKey(__result))
			{
				MyUtils.MyLog($"GetMonasticTitleOrDisplayName {__result}");
				idNameCache[displayData.CharacterId] = __result;
			}
		}

		#endregion

		#region 
		#endregion

		#region hook 游戏中的加载接口，然后寻找name进行立绘替换
		[HarmonyPostfix, HarmonyPatch(typeof(TaiwuAvatar), "Refresh", argumentTypes: new Type[2] { typeof(CharacterDisplayData), typeof(bool) })]
        public static void OnRefreshChar(TaiwuAvatar __instance, CharacterDisplayData displayData, bool isShowGrave)
        {
            if (!npcFace) return;
			if(isShowGrave) return;

            var charId = displayData.CharacterId;
            var taiwuCharId = SingletonObject.getInstance<BasicGameData>().TaiwuCharId;
            if(displayData.CharacterId == taiwuCharId)
            {
                resLoad(__instance, null, isTaiwu:true);
                return;
            }
            TrySetNpcFaceByName(__instance, null, displayData);
            //TrySetNpcFace(__instance, null, displayData);
        }

        // 关系界面 主体
        [HarmonyPostfix, HarmonyPatch(typeof(TaiwuAvatar), "Refresh", argumentTypes: new Type[1] { typeof(AvatarRelatedData) })]
        public static void OnRefreshCharRelated(TaiwuAvatar __instance, AvatarRelatedData relatedData)
        {
            if (!npcFace) return;
            //MyLog("OnRefreshCharRelated");
            DelayCall(__instance, relatedData);
        }

        public static void DelayCall(TaiwuAvatar avatar, AvatarRelatedData relatedData)
        {
            //Game.Instance.StartCoroutine(DelayCoroutine(TrySetNpcFace, 0, avatar, null, relatedData));
            GameApp.Instance.StartCoroutine(DelayCoroutine(TrySetNpcFaceByName, 0, avatar, null, relatedData));
        }

        private static IEnumerator DelayCoroutine(Func<TaiwuAvatar, CharacterAvatar, AvatarRelatedData, bool> action, float delay, TaiwuAvatar avatar, CharacterAvatar instance, AvatarRelatedData relatedData)
        {
            yield return null;
            //yield return new WaitForSeconds(delay);
            action?.Invoke(avatar, instance, relatedData);
        }

        // 人物界面, 
        [HarmonyReversePatch, HarmonyPatch(typeof(CharacterAvatar), "FillElement")]
        public static void FillElementOrigin(CharacterAvatar __instance)
        {
            return;
        }

        // 人物界面, 
        [HarmonyPostfix, HarmonyPatch(typeof(CharacterAvatar), "FillElement")]
        public static void FillElementPost(CharacterAvatar __instance)
        {
            if (!npcFace) return;
            MyUtils.MyLog($"FillElementPost");
            if (__instance == null) return;
            var taiwuCharId = SingletonObject.getInstance<BasicGameData>().TaiwuCharId;
            if (__instance.CharacterId == taiwuCharId)
            {
                var avatar = Traverse.Create(__instance).Field("_avatar").GetValue<TaiwuAvatar>();
                resLoad(avatar, __instance, isTaiwu: true);
                return;
            }
            else
            {

                var avatar = Traverse.Create(__instance).Field("_avatar").GetValue<TaiwuAvatar>();
                //MyLog($"FillElementPost 1");
                TrySetNpcFaceByName(avatar, __instance, relatedData: null);
                //MyLog($"FillElementPost 1-1");
                DelayCall2(avatar, __instance, null);
            }
        }

        public static void DelayCall2(TaiwuAvatar avatar, CharacterAvatar instance, AvatarRelatedData relatedData)
        {
            //Game.Instance.StartCoroutine(DelayCoroutine(TrySetNpcFace, 0, avatar, null, relatedData));
            GameApp.Instance.StartCoroutine(DelayCoroutine2(TrySetNpcFaceByName, avatar, instance, relatedData));
        }

        private static IEnumerator DelayCoroutine2(Func<TaiwuAvatar, CharacterAvatar, AvatarRelatedData, bool> action, TaiwuAvatar avatar, CharacterAvatar instance, AvatarRelatedData relatedData)
        {
            yield return new WaitForSecondsRealtime(0);
            if (avatar == null || instance == null) yield break;
            //MyLog($"FillElementPost 2");
            if (!action.Invoke(avatar, instance, relatedData)) { try { FillElementOrigin(instance); } catch { } }
            //MyLog($"FillElementPost 2-1");

            //yield return new WaitForSecondsRealtime(0.01f);
            //if (avatar == null) yield break;
            //MyLog($"FillElementPost 3");
            //if (!action.Invoke(avatar, instance, relatedData)) { avatar.Refresh(); }
            //MyLog($"FillElementPost 3-1");

            yield return new WaitForSecondsRealtime(0.05f);
            if (avatar == null || instance == null) yield break;
            //MyLog($"FillElementPost 4");
            if (!action.Invoke(avatar, instance, relatedData)) { try { FillElementOrigin(instance); } catch { } }
            //MyLog($"FillElementPost 4-1");

            //yield return new WaitForSecondsRealtime(0.1f);
            //if (avatar == null) yield break;
            //if (!action.Invoke(avatar, instance, relatedData)) { avatar.Refresh(); }
            //yield return new WaitForSecondsRealtime(0.5f);
            //if (avatar == null) yield break;
            //MyLog($"FillElementPost 6");
            //if (!action.Invoke(avatar, instance, relatedData)) { avatar.Refresh(); }
            //MyLog($"FillElementPost 6-1");
        }

		#endregion

		#region 资源加载
        private static bool resLoad(TaiwuAvatar avatar, CharacterAvatar? instance, bool isTaiwu, string res=null)
        {
            //MyLog($"LoadModOrGameResource 0 ");
            if (!npcFace) return false;
            if (isTaiwu && !forTaiwu) return false;
            if (!isTaiwu && !forNpc) return false;

            //MyLog($"LoadModOrGameResource 1 ");
            if (avatar == null) return false;
			//MyLog($"LoadModOrGameResource 2");

			var avatarAssetName = "NpcFace_yingjiao";
            if(isTaiwu)
            {
                if (customNpc)
                {
                    if (string.IsNullOrEmpty(npcNameCustom)) return false;
                    avatarAssetName = npcNameCustom;

                }
                else
                {
                    avatarAssetName = npcName[npcNameIdx];
                }
            }
            else
            {
                //MyLog($"LoadModOrGameResource 3");

                if (string.IsNullOrEmpty(res)) return false;
                avatarAssetName = res;
            }
            //MyLog($"LoadModOrGameResource 4");


            var loadMod = GetResPath(avatarAssetName, avatar.Size, out var resPath);
            if (string.IsNullOrEmpty(resPath)) return false;// 报错出来
            //MyLog($"load {resPath}");
            //MyLog($"LoadModOrGameResource");

			// 游戏内资源
            if (!loadMod)
            {
				// 尝试进行动态立绘
				var toSpine = false;
				var isSpine = false;
				if (avatar.PreferDynamicAvatar)
				{
					if(samllSpine || (!samllSpine && avatar.size != TaiwuAvatarSize.Small))
					{
						var npcSkeleton = Traverse.Create(avatar).Field("npcSkeleton").GetValue<SkeletonGraphic>();
						if (npcSkeleton != null)
							toSpine = true;
					}
				}
				if(toSpine)
				{
					if(ImgTemplate.TryGetValue(avatarAssetName, out int characterTemplateId))
					{
						CharacterItem config = Character.Instance[characterTemplateId];
						string spineName = config.FixedAvatarSpineName;
						string skinName = config.FixedAvatarSpineSkin;
						if(!string.IsNullOrEmpty(spineName))
						{
							isSpine = true;
							avatar.RefreshAsSpine(spineName, skinName);
							return true;
						}
					}
				}

				// 否则静态
                ResLoader.LoadModOrGameResource<Texture2D>(resPath, delegate (Texture2D tex)
                {
                    if (avatar == null) return;
                    avatar.Refresh(tex);
                    if (instance == null) return;
                    instance.OnFillAvatar?.Invoke(); // CharacterAvatar 非空时触发回调
				}, (tex) => {
                });
                return true;
            }
			// 自定义资源
			else
			{
                var tex = TryLoadImg(resPath);
                if(tex == null) return false;
                avatar.Refresh(tex);
                if (instance == null) return true; // 上面已经替换成功了，这里只是检查是否要回调
                instance.OnFillAvatar?.Invoke();
                return true;
            }
        }

		/// <summary>
		/// 解析 资源名配置， 尝试读取资源
		/// 如果 资源名有`:`，则表示是自定义路径资源
		/// 没有则是游戏内资源
		/// bool 返回是否自定义还是游戏内
		/// </summary>
        private static bool GetResPath(string avatarAssetName, TaiwuAvatarSize avatarSize, out string resPath)
        {
            //MyLog($"GetResPath {avatarAssetName}");
            var dir = TryLoadResDir(avatarAssetName, out var relName);
            //MyLog($"GetResPath {dir}");
			// 路径非空 为自定义资源
            if (!string.IsNullOrEmpty(dir)) // resDirs 0 是空字符串
            {
                // 先检查 BigTexture mod路径，再检查 BigFace 路径
                var size1 = GetSizeFolder(avatarSize, 1);
                var res1 = Path.Combine(dir, size1, relName);
                //MyLog($"GetResPath res1 {res1}");
                if (File.Exists(res1)) { resPath = res1; return true; }
                var size0 = GetSizeFolder(avatarSize, 0);
                var res0 = Path.Combine(dir, size0, relName);
                //MyLog($"GetResPath res0 {res0}");
                if (File.Exists(res0)) { resPath = res0; return true; }
                //MyLog($"GetResPath no");
                resPath = "";
                return true;
            }
			// 空路径为游戏内资源
            else
            {
                string sizeFolder = CharacterAvatar.GetAvatarSizeFolder(avatarSize);
                string resPath1 = CharacterAvatar.GetNpcFaceResPath(sizeFolder, avatarAssetName);
                resPath = resPath1; 
                return false;
            }
            resPath = "";
            return false;
        }

		/// <summary>
		/// 解析获取自定义路径
		/// 根据`:`前的tag，从 tagDirs 中 找到对应的路径
		/// </summary>
		public static string TryLoadResDir(string avatarAssetName, out string relName)
        {
            //MyLog($"TryLoadResDir {avatarAssetName}");
            var r = avatarAssetName.Split(':');
            if (r.Length > 1)
            {
                //MyLog($"TryLoadResDir {r[0]} {r[1]}");
                relName = r[1] + ".png";
                return tagDirs[r[0]];
            }
            relName = "";
            return "";
        }

		/// <summary>
		/// 获取 图片大小对应的文件夹，
		/// 游戏内使用 Face系列， Texture主要为了适配立绘框架mod
		/// </summary>
		private static string GetSizeFolder(TaiwuAvatarSize avatarSize, int type)
        {
            if(type == 0)
            {
                //return CharacterAvatar.GetAvatarSizeFolder(avatar.Size);
                return avatarSize switch
                {
					TaiwuAvatarSize.Normal => "NormalFace",
					TaiwuAvatarSize.Small => "SmallFace",
                    _ => "BigFace",
                };
            }
            else if (type == 1)
            {
                return avatarSize switch
                {
					TaiwuAvatarSize.Normal => "NormalTexture",
					TaiwuAvatarSize.Small => "SmallTexture",
                    _ => "BigTexture",
                };
            }
            return "";
        }

		/// <summary>
		/// 尝试加载图片，并 按路径 放入 图片缓存 resCache
		/// </summary>
		private static Texture2D TryLoadImg(string resPath)
        {
            if(resCache.TryGetValue(resPath, out Texture2D img)) { return img; }
            //MyLog($"tryLoad {resPath}");
            byte[] fileData = File.ReadAllBytes(resPath);
            Texture2D texture = new Texture2D(1, 1);
            texture.name = Path.GetFileName(resPath);

            if (texture.LoadImage(fileData))
            {
                resCache[resPath] = texture;
                return texture;
            }
            return null;
        }
        #endregion

    }
}
