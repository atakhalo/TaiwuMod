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
using Spine;
using Spine.Unity;
using TaiwuAvatar = Game.Components.Avatar.Avatar;
using TaiwuAvatarSize = Game.Components.Avatar.AvatarSize;
using System.Xml.Linq;
using GameData.Domains.Character.Creation;
using Game.Views.EventWindow;
using System.Diagnostics;

namespace NpcFace
{
	public class MyUtils
	{
		public static void MyLog(string log)
		{
			UnityEngine.Debug.Log($"[{nameof(NpcFace)}] {log}");
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

		private static bool showEntryLog = false;


        public static bool npcFace; // 开关 是否开启
        public static bool forTaiwu; // 开关 太吾是否开启
        public static bool forNpc; // 开关 NPC是否开启


		// 提一些特殊的npc 出来到设置界面方便 玩家选择
        public static string[] npcName = {
            "NpcFace_yingjiao",//迎娇
            "NpcFace_wanquzhimin", // 螺舟
            "NpcFace_wudangya", // 武当鸭
            "NpcFace_wuhushanghuizongbu",// 五湖商人
            "NpcFace_wenshanshuhaigezongbu",// 文山书海商人
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
        public static Dictionary<string, Sprite> resCache = new Dictionary<string, Sprite>();

        public static Dictionary<string, string> tagDirs = new Dictionary<string, string>(); // 资源tag对应路径

		public static Dictionary<int, string> idNameCache = new Dictionary<int, string>(); // 对一些id 跟 name进行缓存

		public static Dictionary<string, int> ImgTemplate = new Dictionary<string, int>(); // npc模板id缓存
		public static Dictionary<string, int> spineTemplate = new Dictionary<string, int>(); // npc模板id缓存

		public static bool samllSpine = false;

		public static SkeletonDataAsset skeletonDataTemp;

		public class SpineConfig
		{
			public string fileDir; // 用来找 skel， altas，png， 默认是 tag 路径/Spine/fileName
			public string skelName;

			public string fileName;
			public string keyName;
			public List<string> altas;
			public string skinName;
			public string animName;
			public int idleRepeat = 0; // 播多少次 idle anim 后播 actionAnim，0=连续循环
			public string actionAnim = ""; // idleRepeat 次后播放的动作动画
			public Dictionary<string, string> attachments;
			public float scaleBig = 1.0f;
			public float scaleNormal = 0.5f;
			public float scaleSmall = 1.0f;
			public float bigOffsetX = 0f;
			public float bigOffsetY = 0f;
			public float normalOffsetX = 0f;
			public float noramlOffsetY = 0f;
			public float smallOffsetX = 0f;
			public float smallOffsetY = 0f;


		}
		// spine 配置缓存； 设置变化的是否清零；
		// 记录 配置文件名 对应 spine 配置 （如 皮肤、皮肤图集、默认动画）
		public static Dictionary<string, SpineConfig> spineConfigs = new Dictionary<string, SpineConfig>();

		/// <summary>
		/// Spine 运行时资源缓存，缓存 AtlasAsset + SkeletonDataAsset
		/// 避免每次刷新都新建 Texture2D / SpineAtlasAsset / SkeletonDataAsset 造成内存泄漏
		/// OnModSettingUpdate 中清理
		/// </summary>
		public class SpineCachedAssets
		{
			public SpineAtlasAsset AtlasAsset;
			public SkeletonDataAsset SkeletonAsset;
			public List<Texture2D> Textures = new List<Texture2D>();

			public void Destroy()
			{
				// 先销毁 SkeletonDataAsset
				if (SkeletonAsset != null)
				{
					UnityEngine.Object.Destroy(SkeletonAsset);
					SkeletonAsset = null;
				}
				// 再销毁 SpineAtlasAsset（它持有 atlas 文本 Asset）
				if (AtlasAsset != null)
				{
					UnityEngine.Object.Destroy(AtlasAsset);
					AtlasAsset = null;
				}
				// 最后销毁原始纹理（SpineAtlasAsset 内部可能已引用，但 Destroy 后仍要确保释放）
				foreach (var tex in Textures)
				{
					if (tex != null)
						UnityEngine.Object.Destroy(tex);
				}
				Textures.Clear();
			}
		}
		public static Dictionary<string, SpineCachedAssets> spineCache = new Dictionary<string, SpineCachedAssets>();

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

			// 从最新的 npcRes + 太吾设置中收集仍在使用的 spine key，跳过销毁
			var activeKeys = new HashSet<string>();
			if (npcFace)
			{
				activeKeys.Add(GetActiveAvatarAssetName());
				foreach (var v in npcRes.Values)
					if (!string.IsNullOrEmpty(v))
						activeKeys.Add(v);
			}
			// 清空 spine 配置缓存
			spineConfigs.Clear();
			// 清空 spine 运行时资源缓存，销毁未在使用的资源
			var keysToRemove = new List<string>();
			foreach (var kv in spineCache)
			{
				if (!activeKeys.Contains(kv.Key))
				{
					kv.Value.Destroy();
					keysToRemove.Add(kv.Key);
				}
			}
			foreach (var k in keysToRemove)
				spineCache.Remove(k);

			// 清空 sprite 纹理缓存，销毁 Sprite 及其引用的 Texture2D
			foreach (var kv in resCache)
			{
				if (kv.Value != null)
				{
					if (kv.Value.texture != null)
						UnityEngine.Object.Destroy(kv.Value.texture);
					UnityEngine.Object.Destroy(kv.Value);
				}
			}
			resCache.Clear();
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
				if(_dataArray[i].FixedAvatarName != null && !ImgTemplate.ContainsKey(_dataArray[i].FixedAvatarName))
					ImgTemplate[_dataArray[i].FixedAvatarName] = _dataArray[i].TemplateId;
				if (_dataArray[i].FixedAvatarSpineName != null && !spineTemplate.ContainsKey(_dataArray[i].FixedAvatarSpineName))
					spineTemplate[_dataArray[i].FixedAvatarSpineName] = _dataArray[i].TemplateId;
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
                    MyUtils.MyLog($"太吾迎娇 收集到图片目录 {tagDirs.Count} {tag}:{dirPath} ");
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
		public void TryCreateFile()
        {
            if(toCreateFile)
            {
				ModInfo modInfo = ModManager.LocalMods[ModIdStr];
				var filePath = Path.Combine(modInfo.DirectoryName, "npcFace.txt");
                if (!File.Exists(filePath)) {
                    var str1 = $"// 说明:1. 首行跳过； 2. 格式:名字,资源名;（即英文逗号分隔，英文分号结尾）3. 资源名在mod的下载目录下找 C:\\Program Files (x86)\\Steam\\steamapps\\workshop\\content\\838350\\3593734737\\npcName.txt\n";
                    var str2 = $"我是示例,NpcFace_yingjiao;\n";
                    var s = str1 + str2;
                    File.WriteAllText(filePath, s, System.Text.Encoding.UTF8);
                    MyUtils.MyLog($"创建 npcFace.txt： {filePath}");
                }
				else
				{
					MyUtils.MyLog($"已存在 npcFace.txt，不再创建： {filePath}");
				}
			}
		}

		/// <summary>
		/// 创建 mod 普通npc配置文件
		/// </summary>
		public void TryReadFile()
        {
            if (toReadFile)
            {
				ModInfo modInfo = ModManager.LocalMods[ModIdStr];
				var filePath = Path.Combine(modInfo.DirectoryName, "npcFace.txt");
				if (File.Exists(filePath))
                {
					MyUtils.MyLog($"尝试读取 npcFace.txt {filePath}");
					var s = File.ReadAllLines(filePath, System.Text.Encoding.UTF8);
					var nCount = 0;
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
							nCount += 1;
							MyUtils.MyLog($"读取 npcFace.txt {n} {res}");
						}
                    }
					MyUtils.MyLog($"读取 npcFace.txt 完毕，共{nCount}位");
				}
				else
				{
					MyUtils.MyLog($"读取 npcFace.txt 失败，文件不存在该路径： {filePath}");
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
                // MyUtils.MyLog($"charId 找到id {curId}");
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
				// MyUtils.MyLog($"CharacterDisplayData 找到id {curId}");
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
			// MyUtils.MyLog("TrySetNpcFace");
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
			// MyUtils.MyLog($"当前查找{transform}, {maxUp}");
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
				// MyUtils.MyLog($"找到 TooltipInvoker {charId}");
            }
            return charId;
        }
		#endregion

		#region 使用 name 进行查找 (泛用性较高)
		/// <summary>
		/// 根据Id 从 id姓名缓存中拿 姓名进行匹配
		/// </summary>
		public static bool TrySetNpcFaceByIdName(TaiwuAvatar avatar, CharacterAvatar? instance, int charId)
		{
			if (avatar == null) return false;
			if (idNameCache.TryGetValue(charId, out var curName))
			{
				if (curName != "")
				{
					if (npcRes.ContainsKey(curName))
						return resLoad(avatar, instance, isTaiwu: false, npcRes[curName]);
				}
			}
			return false;
		}

		/// <summary>
		/// 根据接口信息 中的 name 尝试设置 普通npc  立绘
		/// </summary>
		public static bool TrySetNpcFaceByName(TaiwuAvatar avatar, CharacterAvatar? instance, AvatarRelatedData relatedData)
        {
            // MyUtils.MyLog($"TrySetNpcFaceByName relatedData {avatar}");
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
						// MyUtils.MyLog($"TrySetNpcFaceByName 名字不对 {curName}");
						return false;
                    }
                }
            }
			// MyUtils.MyLog($"TrySetNpcFaceByName 没找到名字");
			return false;
        }

		/// <summary>
		/// 根据接口信息 中的 name 尝试设置 普通npc  立绘
		/// </summary>
		public static bool TrySetNpcFaceByName(TaiwuAvatar avatar, CharacterAvatar? instance, CharacterDisplayData displayData)
        {
            // MyUtils.MyLog($"TrySetNpcFaceByName displayData  {avatar}");
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
			// MyUtils.MyLog($" {transform} 下 tmp总个数为 {ts.Count()} ");
			foreach (var t1 in ts)
            {
                if (t1.name.Contains("name") || t1.name.Contains("Name")
                    && t1.name != "OrganizationName" && t1.name != "SkillName" && t1.name != "ProfessionName"
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
                // MyUtils.MyLog($"找到 {transform}下的tmp  {t} {t.text}");
                return t.text;
            }
			// MyUtils.MyLog($" {transform} 无 命名tmp ");
			


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
					// MyUtils.MyLog($"找到 {transform}下的 MouseTipDisplayer CharData {curName}");
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
				// MyUtils.MyLog($"找到refer {r} {r.Names.Count}");
                r.CTryGet<TextMeshProUGUI>("Name", out t);
                if(t == null) r.CTryGet<TextMeshProUGUI>("CharacterName", out t);
                if(t == null) r.CTryGet<TextMeshProUGUI>("CharName", out t);
            }
            if (t)
            {
				// MyUtils.MyLog($"找到 refer 下的tmp {t} {t.text}");
				return t.text;
            }
            return "";
        }

		public static string TryGetNpcNameSp(Transform transform)
		{
			// MyUtils.MyLog("TryGetNpcNameSp");
			// 事件界面
			if (transform.parent.name == "AvatarArea")
			{
				if(transform.parent.parent.name == "CanvasChanger")
				{
					var r = transform.parent.parent?.GetChild(4)?.GetChild(0)?.GetChild(0);
					if(r)
					{
						var t = r.GetComponent < TextMeshProUGUI>();
						if (t) return t.text;
						else { MyUtils.MyLog("事件窗口更新，需要适配"); return null; }
					}
				}
			}

			// 地图人物列表 关注界面
			if (transform.parent.name == "AvatarRect")
			{
				// 地图人物列表
				var mbc = transform.parent.parent.GetComponent<MapBlockChar>();
				if (mbc)
				{
					var t = Traverse.Create(mbc).Field("nameText").GetValue<TextMeshProUGUI>();
					if (t) return t.text;
					else { MyUtils.MyLog("地块人物列表更新， 需要适配"); return null; }
				}
				var fc = transform.parent.parent.GetComponent<Game.Views.MapBlockCharList.FollowingChar>();
				if (fc)
				{
					var t = Traverse.Create(fc).Field("nameText").GetValue<TextMeshProUGUI>();
					if (t) return t.text;
					else { MyUtils.MyLog("关注界面 需要适配"); return null; }
				}
			}
			// 地图界面下方人物
			if(UIElement.Bottom.Exist)
			{
				if (transform.GetComponentInParent<ViewBottom>())
				{
					if (transform.name.StartsWith("MainChar"))
					{
						return SingletonObject.getInstance<BasicGameData>().TaiwuMonasticTitleOrDisplayName;
					}
					if (transform.name.StartsWith("Teammate1")) return TryGetTeammateName(1);
					if (transform.name.StartsWith("Teammate2")) return TryGetTeammateName(2);
					if (transform.name.StartsWith("Teammate3")) return TryGetTeammateName(3);
				}
			}
			// 装备界面 战斗界面同道
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
				}
				//  战斗界面同道
				var ct = transform.parent.parent.parent.GetComponent<CombatTeammate>();
				if (ct)
				{
					return Traverse.Create(ct).Field("teammateName").GetValue<TextMeshProUGUI>().text;
				}
			}
			//  治疗界面 势力界面 秘闻界面
			if (transform.parent.name == "AvatarMask") 
			{
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
					var t = p4.GetChild(4).GetChild(0).GetComponent<TextMeshProUGUI>();
					if(t) return t.text;
					else { MyUtils.MyLog("身份名册 需要适配"); return null; }
				}
			}
			// 地块右键界面（派遣）
			if (UIElement.BlockOperation.Exist)
			{
				if (transform.parent.parent.name == "AssignedCharacterButton")
				{
					var p3 = transform.parent.parent.parent;
					if (p3.GetComponent<AssignedCharacterButton>())
					{
						var t = p3.GetChild(2).GetComponent<TextMeshProUGUI>();
						if (t) return t.text;
						else { MyUtils.MyLog("地块右键界面（派遣） 需要适配"); return null; }
					}
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
				// MyUtils.MyLog($"ViewBottom__NameHook {name}");
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
				// MyUtils.MyLog($"GetMonasticTitleOrDisplayName {__result}");
				idNameCache[displayData.CharacterId] = __result;
			}
		}

		#endregion

		#region 
		#endregion

		#region hook 游戏中的加载接口，然后寻找name进行立绘替换
		// 之前游戏默认有开启的，更新后没有了，现在不需要了
		public static bool QuickCheckNoInit(TaiwuAvatar __instance, out bool tobreak)
		{
			tobreak = false;

			var ac = Traverse.Create(__instance).Field("avatarContainer").GetValue<GameObject>();
			var gc = Traverse.Create(__instance).Field("gravestoneContainer").GetValue<GameObject>();
			bool noInit = false;
			if(ac == null || gc == null) // 有些地方没有两个？直接返回
			{
				noInit = false;
				return true;
			}
			if(ac.activeSelf && gc.activeSelf) noInit = true;
			else return false;
			for (int i = 0; i < __instance.transform.childCount; i++)
			{
				__instance.transform.GetChild(i).gameObject.SetActive(false);
			}
			return true;
		}

		[HarmonyPrefix, HarmonyPatch(typeof(TaiwuAvatar), "Refresh", argumentTypes: new Type[2] { typeof(CharacterDisplayData), typeof(bool) })]
		public static bool OnRefreshChar_Dis_Pre(TaiwuAvatar __instance, CharacterDisplayData displayData, bool isShowGrave)
		{
			if (!npcFace) return true;
            string curName = NameCenter.GetMonasticTitleOrDisplayName(displayData, isTaiwu: false);
			if (showEntryLog) MyUtils.MyLog($"OnRefreshChar_Dis_Pre, id = {displayData.CharacterId}, name = {curName}");

			QuickCheckNoInit(__instance, out var tobreak);
			if(tobreak) return false;
			OnRefreshChar_Dis_Wrapper(__instance, displayData, isShowGrave);
			// GameApp.Instance.StartCoroutine(DelayCoroutine_OnRefreshChar_Dis(OnRefreshChar_Dis_Wrapper, 0, __instance, displayData, isShowGrave));
			return false;
		}

		private static IEnumerator DelayCoroutine_OnRefreshChar_Dis(Func<TaiwuAvatar, CharacterDisplayData, bool, bool> action, float delay, TaiwuAvatar avatar, CharacterDisplayData displayData, bool isShowGrave)
		{
			yield return null;
			// if(delay > 0f) yield return new WaitForSeconds(delay);
			action?.Invoke(avatar, displayData, isShowGrave);
			yield break;
		}

		public static bool OnRefreshChar_Dis_Wrapper(TaiwuAvatar __instance, CharacterDisplayData displayData, bool isShowGrave)
		{
			if (OnRefreshChar_Dis(__instance, displayData, isShowGrave)) return true;
			else OnRefreshChar_Dis_Origin(__instance, displayData, isShowGrave); return false;
		}

		[HarmonyReversePatch, HarmonyPatch(typeof(TaiwuAvatar), "Refresh", argumentTypes: new Type[2] { typeof(CharacterDisplayData), typeof(bool) })]
		public static void OnRefreshChar_Dis_Origin(TaiwuAvatar __instance, CharacterDisplayData displayData, bool isShowGrave)
		{
			return;
		}

		// [HarmonyPostfix, HarmonyPatch(typeof(TaiwuAvatar), "Refresh", argumentTypes: new Type[2] { typeof(CharacterDisplayData), typeof(bool) })]
        public static bool OnRefreshChar_Dis(TaiwuAvatar __instance, CharacterDisplayData displayData, bool isShowGrave)
        {
            if (!npcFace) return false;
			if(isShowGrave) return false;

            var charId = displayData.CharacterId;
            var taiwuCharId = SingletonObject.getInstance<BasicGameData>().TaiwuCharId;
            if(displayData.CharacterId == taiwuCharId)
            {
                return resLoad(__instance, null, isTaiwu:true);
            }
            return TrySetNpcFaceByName(__instance, null, displayData);
        }

		[HarmonyPrefix, HarmonyPatch(typeof(TaiwuAvatar), "Refresh", argumentTypes: new Type[1] { typeof(AvatarRelatedData) })]
		public static bool OnRefreshChar_Related_Pre(TaiwuAvatar __instance, AvatarRelatedData relatedData)
		{
			if (!npcFace) return true;
			if(showEntryLog) MyUtils.MyLog("OnRefreshChar_Related_Pre");
			QuickCheckNoInit(__instance, out var tobreak);
			if (tobreak) return false;
			GameApp.Instance.StartCoroutine(DelayCoroutine_OnRefreshChar_Related(OnRefreshChar_Related_Wrapper, 0, __instance, relatedData));
			return false;
		}

		private static IEnumerator DelayCoroutine_OnRefreshChar_Related(
			Func<TaiwuAvatar, AvatarRelatedData, bool> action, float delay, TaiwuAvatar avatar, AvatarRelatedData relatedData)
		{
			yield return null;
			if (delay > 0f) yield return new WaitForSeconds(delay);
			action?.Invoke(avatar, relatedData);
			yield break;
		}

		public static bool OnRefreshChar_Related_Wrapper(TaiwuAvatar __instance, AvatarRelatedData relatedData)
		{
			if(!__instance || !__instance.gameObject) return true;
			if (OnRefreshChar_Related(__instance, relatedData)) return true;
			else OnRefreshChar_Related_Origin(__instance, relatedData); return false;
		}

		[HarmonyReversePatch, HarmonyPatch(typeof(TaiwuAvatar), "Refresh", argumentTypes: new Type[1] { typeof(AvatarRelatedData) })]
		public static void OnRefreshChar_Related_Origin(TaiwuAvatar __instance, AvatarRelatedData relatedData)
		{
			return;
		}

		// 关系界面 主体
		// [HarmonyPostfix, HarmonyPatch(typeof(TaiwuAvatar), "Refresh", argumentTypes: new Type[1] { typeof(AvatarRelatedData) })]
		public static bool OnRefreshChar_Related(TaiwuAvatar __instance, AvatarRelatedData relatedData)
        {
            if (!npcFace) return false;
			//MyLog("OnRefreshCharRelated");
			return TrySetNpcFaceByName(__instance, null, relatedData);
        }

		[HarmonyPrefix, HarmonyPatch(typeof(TaiwuAvatar), "Refresh", argumentTypes: new Type[2] { typeof(AvatarRelatedData), typeof(short) })]
		public static bool OnRefreshChar_Related_Pre(TaiwuAvatar __instance, AvatarRelatedData relatedData, short characterTemplateId)
		{
			if (!npcFace) return true;
			if (showEntryLog) MyUtils.MyLog("OnRefreshChar_Related_Pre Template");

			CharacterItem characterItem = Config.Character.Instance[characterTemplateId];
			// MyUtils.MyLog($"OnRefreshChar_Related_Pre  tem {characterItem.GivenName} {characterItem.FixedAvatarName}, {CreatingType.IsFixedPresetType(characterItem.CreatingType)}");
			if (!CreatingType.IsFixedPresetType(characterItem.CreatingType)) // 放行到普通refresh
				return true;

			// 对于特殊npc，进行特殊处理, 可以用立绘名
			var curName = characterItem.GivenName;
			var npcFaceName = characterItem.FixedAvatarName;
			if (npcRes.ContainsKey(curName))
				return !resLoad(__instance, null, isTaiwu: false, npcRes[curName]);
			else if(npcFaceName != null && npcRes.ContainsKey(npcFaceName))
				return !resLoad(__instance, null, isTaiwu: false, npcRes[npcFaceName]);
			else
			{
				return true;
			}
		}


		/// <summary>
		/// hook 直接走动态立绘的
		/// </summary>
		[HarmonyPrefix, HarmonyPatch(typeof(TaiwuAvatar), "RefreshAsSpine")]
		public static bool RefreshAsSpine_Pre(TaiwuAvatar __instance, string spineName, string skinName)
		{
			if (!npcFace) return true;
			if (showEntryLog) MyUtils.MyLog("RefreshAsSpine_Pre");

			// 走原加载逻辑
			if (spineTemplate.TryGetValue(spineName, out int characterTemplateId))
			{
				CharacterItem characterItem = Character.Instance[characterTemplateId];
				// 对于特殊npc，进行特殊处理, 可以用立绘名
				var curName = characterItem.GivenName;
				var npcFaceName = characterItem.FixedAvatarName;
				if (npcRes.ContainsKey(curName))
					return !resLoad(__instance, null, isTaiwu: false, npcRes[curName]);
				else if (npcFaceName != null && npcRes.ContainsKey(npcFaceName))
					return !resLoad(__instance, null, isTaiwu: false, npcRes[npcFaceName]);
				else
				{
					return true;
				}
			}
			return true;
		}

		[HarmonyReversePatch, HarmonyPatch(typeof(TaiwuAvatar), "RefreshAsSpine")]
		public static void RefreshAsSpine_Origin(TaiwuAvatar __instance, string spineName, string skinName)
		{
			return;
		}


		[HarmonyPrefix, HarmonyPatch(typeof(CharacterAvatar), "FillElement")]
		public static bool FillElement_Pre(CharacterAvatar __instance)
		{
			if (!npcFace) return true;
			if (showEntryLog) MyUtils.MyLog("FillElement_Pre");
			var avatar = Traverse.Create(__instance).Field("_avatar").GetValue<TaiwuAvatar>();
			QuickCheckNoInit(avatar, out var tobreak);
			if (tobreak) return false;
			FillElement_Wrapper(__instance);
			// GameApp.Instance.StartCoroutine(DelayCoroutine_FillElement(FillElement_Wrapper, 0, __instance));
			return false;
		}
		private static IEnumerator DelayCoroutine_FillElement(Func<CharacterAvatar, bool> action, float delay, CharacterAvatar avatar)
		{
			yield return null;
			//yield return new WaitForSeconds(delay);
			action?.Invoke(avatar);
		}

		public static bool FillElement_Wrapper(CharacterAvatar avatar)
		{
			if(avatar == null) return true;
			if (FillElement_Post(avatar)) return true;
			else FillElement_Origin(avatar); return false;
		}

		// 人物界面, 
		[HarmonyReversePatch, HarmonyPatch(typeof(CharacterAvatar), "FillElement")]
        public static void FillElement_Origin(CharacterAvatar __instance)
        {
            return;
        }

        // 人物界面, 
        // [HarmonyPostfix, HarmonyPatch(typeof(CharacterAvatar), "FillElement")]
        public static bool FillElement_Post(CharacterAvatar __instance)
        {
            if (!npcFace) return false;
            // MyUtils.MyLog($"FillElementPost");
            if (__instance == null) return false;
            var taiwuCharId = SingletonObject.getInstance<BasicGameData>().TaiwuCharId;
            if (__instance.CharacterId == taiwuCharId)
            {
                var avatar = Traverse.Create(__instance).Field("_avatar").GetValue<TaiwuAvatar>();
                return resLoad(avatar, __instance, isTaiwu: true);
            }
            else
            {
				var avatar = Traverse.Create(__instance).Field("_avatar").GetValue<TaiwuAvatar>();
				return TrySetNpcFaceByIdName(avatar, __instance, __instance.CharacterId);
            }
        }

		// /// <summary>
		// /// 事件界面商人, 理论上应该处理，但是现在原版都走的动态，就统一在 refreshAsSpine里处理
		// /// </summary>
		// [HarmonyPrefix, HarmonyPatch(typeof(EventWindowCharacter), "RefreshAsMerchant")]
		// public static bool RefreshAsMerchant_Pre(sbyte merchantTemplateId)
		// {
		// 	return true;
		// }

		// /// <summary>
		// /// 商店界面商人, 理论上应该处理，但是现在原版都走的动态，就统一在 refreshAsSpine里处理
		// /// </summary>
		// [HarmonyPrefix, HarmonyPatch(typeof(EventWindowCharacter), "SetTargetCharacterDisplayData")]
		// public static bool SetTargetCharacterDisplayData_Pre(EventWindowCharacter __instance, CharacterDisplayData displayData, LanguageKey targetTitle)
		// {
		// 	return true;
		// }


		#endregion

		/// <summary>
		/// 获取当前设置中太吾使用的 avatarAssetName，用于 OnModSettingUpdate 保留对应 spine 缓存
		/// </summary>
		private static string GetActiveAvatarAssetName()
		{
			if (customNpc && !string.IsNullOrEmpty(npcNameCustom))
				return npcNameCustom;
			return npcName[npcNameIdx];
		}

		#region 资源加载
		/// <summary>
		/// 太吾： resLoad(avatar, __instance, isTaiwu: true);
		/// npc： resLoad(avatar, instance, isTaiwu: false, npcRes[curName]);
		/// 加载成功返回 true
		/// </summary>
		private static bool resLoad(TaiwuAvatar avatar, CharacterAvatar? instance, bool isTaiwu, string res=null)
        {
            if (!npcFace) return false;
            if (isTaiwu && !forTaiwu) return false;
            if (!isTaiwu && !forNpc) return false;
            if (avatar == null) return false;

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
                if (string.IsNullOrEmpty(res)) return false;
                avatarAssetName = res;
            }

			// 判断是否进行动态立绘
			var toSpine = false;
			if (avatar.PreferDynamicAvatar)
			{
				if (samllSpine || (!samllSpine && avatar.size != TaiwuAvatarSize.Small))
				{
					var npcSkeleton = Traverse.Create(avatar).Field("npcSkeleton").GetValue<SkeletonGraphic>();
					if (npcSkeleton != null)
						toSpine = true;
				}
			}
			// MyUtils.MyLog($"resLoad spine? {toSpine}, {avatar.PreferDynamicAvatar}");
			if (toSpine)
			{
				if(TrySpine(avatar, instance, avatarAssetName, isTaiwu))
					return true;
			}
			return TryTexture(avatar, instance, avatarAssetName);
        }

		private static bool TrySpine(TaiwuAvatar avatar, CharacterAvatar? instance, string avatarAssetName, bool isTaiwu)
		{
			// MyUtils.MyLog("TrySpine 开始尝试走spine");
			// 先尝试获取 spine 文件（spine 骨骼，spine atlas, spine png）
			var dir = TryLoadResDir(avatarAssetName, out var fileName);
			// MyUtils.MyLog($"TrySpine {avatarAssetName} 读取到1 {dir}, {fileName}");

			// if (!isTaiwu) // 调试时，只显示太吾
			// 	return TrySpineBuiltIn(avatar, instance, avatarAssetName);

			// 尝试从 dir 路径下 获取 spine 文件夹，并获取 fileName 文件夹 下 三个同名文件
			// 如果三个文件都齐全，构造 SkeletonDataAsset， 并缓存
			// 如果不齐全， 尝试加载游戏内 同名 SkeletonDataAsset，并替换相应资源（主要是图集）
			// 尝试从 dir 路径下获取 spine 文件夹中的三个同名文件
			if (!string.IsNullOrEmpty(dir) && !string.IsNullOrEmpty(fileName))
			{
				var config = LoadSpineConfig(dir, fileName, avatarAssetName);
				GetSpinePath(config, out string skelPath, out string atlasPath);
				if(skelPath == "" && atlasPath == "") // 如果都没有，就是静态图片
					return false;
				// MyUtils.MyLog($"TrySpine 读取到20 {avatarAssetName}");
				// MyUtils.MyLog($"TrySpine 读取到21 {dir}, {fileName}");
				// MyUtils.MyLog($"TrySpine 读取到22 {skelPath}");
				// MyUtils.MyLog($"TrySpine 读取到23 {atlasPath}");
				if (TrySpineFromFiles(avatar, instance, config, skelPath, atlasPath))
					return true;
			}
			// MyUtils.MyLog($"TrySpine 走内置流程");
			return TrySpineBuiltIn(avatar, instance, avatarAssetName);
		}

		private static SpineConfig LoadSpineConfig(string dir, string fileName, string avatarAssetName)
		{
			if(spineConfigs.TryGetValue(avatarAssetName, out var s))
				return s;

			var sc = new SpineConfig
			{
				fileDir = Path.Combine(dir, "Spine", fileName),
				keyName = avatarAssetName,
				fileName = fileName,
				skelName = fileName,
				altas = new List<string>(),
				skinName = "",
				animName = "",
			};
			spineConfigs[avatarAssetName] = sc;

			var config = Path.Combine(dir, "Spine", fileName, "config.xml");
			if(!File.Exists(config)) return sc;

			XDocument doc = XDocument.Load(config);
			// 获取 altas 下的所有 file 元素值，若不存在则得到空数组
			sc.altas = doc.Descendants("altas")
								.Elements("file")
								.Select(e => e.Value)
								.ToList();

			// MyUtils.MyLog("try load fileDir");

			var fileDir = doc.Descendants("fileDir").FirstOrDefault()?.Value ?? "";
			if(fileDir != "")
			{
				if(Path.IsPathRooted(fileDir))
					sc.fileDir = fileDir;
				else
					sc.fileDir = Path.Combine(dir, "Spine", fileName, fileDir);
				// MyUtils.MyLog($"try load fileDir {fileDir} -> {sc.fileDir}");
			}
			sc.skelName = doc.Descendants("skelName").FirstOrDefault()?.Value ?? "";
			if(sc.skelName == "") sc.skelName = sc.fileName ;
			// MyUtils.MyLog($"try get skelName {sc.skelName}");

			sc.skinName = doc.Descendants("skin").FirstOrDefault()?.Value ?? "";
			sc.animName = doc.Descendants("anim").FirstOrDefault()?.Value ?? "";
			sc.idleRepeat = int.Parse(doc.Descendants("idleRepeat").FirstOrDefault()?.Value ?? "0");
			sc.actionAnim = doc.Descendants("actionAnim").FirstOrDefault()?.Value ?? "";
			sc.scaleBig = float.Parse(doc.Descendants("scaleBig").FirstOrDefault()?.Value ?? "1");
			sc.scaleNormal = float.Parse(doc.Descendants("scaleNormal").FirstOrDefault()?.Value ?? "0.5");
			sc.scaleSmall = float.Parse(doc.Descendants("scaleSmall").FirstOrDefault()?.Value ?? "1");
			sc.bigOffsetX = float.Parse(doc.Descendants("bigOffsetX").FirstOrDefault()?.Value ?? "0");
			sc.bigOffsetY = float.Parse(doc.Descendants("bigOffsetY").FirstOrDefault()?.Value ?? "0");
			sc.normalOffsetX = float.Parse(doc.Descendants("normalOffsetX").FirstOrDefault()?.Value ?? "0");
			sc.noramlOffsetY = float.Parse(doc.Descendants("noramlOffsetY").FirstOrDefault()?.Value ?? "0");
			sc.smallOffsetX = float.Parse(doc.Descendants("smallOffsetX").FirstOrDefault()?.Value ?? "0");
			sc.smallOffsetY = float.Parse(doc.Descendants("smallOffsetY").FirstOrDefault()?.Value ?? "0");

			sc.attachments = doc.Descendants("attachments")
				.Elements("item")
				.Where(e => e.Element("slot") != null && e.Element("attach") != null)
				.ToDictionary(
					e => e.Element("slot").Value,
					e => e.Element("attach").Value
				);
			return sc;
		}

		private static void GetSpinePath(SpineConfig spineConfig, out string skel, out string altasTxt)
		{
			// 加载skel， 可能是 .json 后缀， .skel 后缀，或者 .skel.bytes 后缀
			var skel1 = Path.Combine(spineConfig.fileDir, spineConfig.skelName + ".json");
			if (File.Exists(skel1)) { skel = skel1; }
			else 
			{
				skel1 = Path.Combine(spineConfig.fileDir, spineConfig.skelName + ".skel.bytes");
				if (File.Exists(skel1)) { skel = skel1;  }
				else
				{
					skel1 = Path.Combine(spineConfig.fileDir, spineConfig.skelName + ".skel");
					if (File.Exists(skel1)) { skel = skel1; }
					else skel = "";
				}
			}

			// 加载 altasTxt, 可能是 .atlas 后缀，或者 .atlas.txt 后缀
			var altasTxt1 = Path.Combine(spineConfig.fileDir, spineConfig.skelName + ".atlas.txt");
			if (File.Exists(altasTxt1)) { altasTxt = altasTxt1; }
			else
			{
				altasTxt1 = Path.Combine(spineConfig.fileDir, spineConfig.skelName + ".atlas");
				if (File.Exists(altasTxt1)) { altasTxt = altasTxt1; }
				else altasTxt = "";
			}

			if(spineConfig.altas.Count == 0)
			{
				// 加载 altasPng, .png 后缀
				var altasPng1 = Path.Combine(spineConfig.fileDir, spineConfig.skelName + ".png");
				if (File.Exists(altasPng1)) 
				{
					spineConfig.altas.Add(spineConfig.skelName);
				}
			}
		}

		private static bool TrySpineBuiltIn(TaiwuAvatar avatar, CharacterAvatar? instance, string avatarAssetName)
		{
			var n = avatarAssetName.Split('$');
			if(n.Length > 1)
			{
				RefreshAsSpine_Origin(avatar, $"{n[1]}", "");
				return true;
			}
			// 走原加载逻辑
			if (ImgTemplate.TryGetValue(avatarAssetName, out int characterTemplateId))
			{
				CharacterItem config = Character.Instance[characterTemplateId];
				string spineName = config.FixedAvatarSpineName;
				string skinName = config.FixedAvatarSpineSkin;
				if (!string.IsNullOrEmpty(spineName))
				{
					// MyUtils.MyLog($"TrySpineBuiltIn {spineName}");
					RefreshAsSpine_Origin(avatar, spineName, skinName);
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// 从文件系统加载 spine 资源（.skel + .atlas + .png）并应用到 avatar
		/// 使用 spineCache 缓存 SkeletonDataAsset + SpineAtlasAsset，避免重复创建导致内存泄漏
		/// </summary>
		private static bool TrySpineFromFiles(TaiwuAvatar avatar, CharacterAvatar? instance, SpineConfig spineConfig,
			string skelPath, string atlasPath)
		{
			// MyUtils.MyLog($"开始走自定义流程 TrySpineFromFiles");

			var cacheKey = spineConfig.keyName;

			// 缓存未命中或已失效，重新加载
			if (!spineCache.TryGetValue(cacheKey, out var cached) || cached == null || cached.SkeletonAsset == null)
			{
				if (!TryLoadSpineAtlas(avatar, spineConfig, atlasPath, out var atlasAsset, out var atlas, out var createdTextures))
					return false;

				if (!TryCreateSkeletonDataAsset(spineConfig, skelPath, atlas, atlasAsset, out var asset))
					return false;

				cached = new SpineCachedAssets
				{
					AtlasAsset = atlasAsset,
					SkeletonAsset = asset,
					Textures = createdTextures
				};
				spineCache[cacheKey] = cached;
				// MyUtils.MyLog($"TrySpineFromFiles: 缓存新建 {cacheKey}");
			}
			// else { MyUtils.MyLog($"TrySpineFromFiles: 命中缓存 {cacheKey}"); }

			if (!TryApplySpineAsset(avatar, instance, spineConfig, cached.SkeletonAsset))
				return false;

			// MyUtils.MyLog($"TrySpineFromFiles: success {Path.GetFileName(skelPath)}");
			return true;
		}

		/// <summary>
		/// 检测 atlas 文本中是否包含 pma:true（预乘Alpha）
		/// 兼容 "pma: true"（带空格）和 "pma:true"（无空格）两种写法
		/// </summary>
		private static bool HasPmaInAtlas(string atlasContent)
		{
			foreach (var line in atlasContent.Split('\n'))
			{
				var trimmed = line.Trim();
				// 移除 pma: 后的空格再比较
				if (trimmed.StartsWith("pma:") && trimmed.Substring(4).Trim() == "true")
					return true;
			}
			return false;
		}

		/// <summary>
		/// 从 atlas 文本中移除 pma 设置行
		/// </summary>
		private static string RemovePmaFromAtlas(string atlasContent)
		{
			var lines = atlasContent.Split('\n');
			return string.Join("\n", lines.Where(l => !l.Trim().StartsWith("pma:")));
		}

		/// <summary>
		/// 将预乘Alpha纹理转换为直通Alpha纹理（RGB /= A）
		/// </summary>
		private static void ConvertPremultipliedToStraightAlpha(Texture2D texture)
		{
			var pixels = texture.GetPixels();
			for (int i = 0; i < pixels.Length; i++)
			{
				var p = pixels[i];
				if (p.a > 0f)
				{
					p.r = Mathf.Clamp01(p.r / p.a);
					p.g = Mathf.Clamp01(p.g / p.a);
					p.b = Mathf.Clamp01(p.b / p.a);
				}
				pixels[i] = p;
			}
			texture.SetPixels(pixels);
			texture.Apply();
		}

		/// <summary>
		/// 颜色扩张（alpha bleed）：对 a=0 且 RGB=0 的透明像素，
		/// 在逐层扩张的方形环中查找最近的非透明像素，取其 RGB 平均值填充。
		/// 防止 GPU 双线性采样时透明边缘与黑色混合出黑边。
		/// 对所有纹理（无论是否 PMA）都可安全使用。
		/// </summary>
		private static void ApplyAlphaBleed(Texture2D texture)
		{
			var pixels = texture.GetPixels();
			int w = texture.width;
			int h = texture.height;
			int radius = Mathf.Max(1, Mathf.Min(w, h) / 256 + 1);
			int bleedCount = 0;

			for (int y = 0; y < h; y++)
			{
				for (int x = 0; x < w; x++)
				{
					int idx = y * w + x;
					if (pixels[idx].a > 0f) continue;
					if (pixels[idx].r > 0f || pixels[idx].g > 0f || pixels[idx].b > 0f) continue;

					Color acc = Color.black;
					int count = 0;
					for (int r = 1; r <= radius; r++)
					{
						for (int dx = -r; dx <= r; dx++)
						{
							TryBleedSample(pixels, w, h, x + dx, y - r, ref acc, ref count);
							TryBleedSample(pixels, w, h, x + dx, y + r, ref acc, ref count);
						}
						for (int dy = -r + 1; dy <= r - 1; dy++)
						{
							TryBleedSample(pixels, w, h, x - r, y + dy, ref acc, ref count);
							TryBleedSample(pixels, w, h, x + r, y + dy, ref acc, ref count);
						}
						if (count > 0)
							break;
					}
					if (count > 0)
					{
						pixels[idx].r = acc.r / count;
						pixels[idx].g = acc.g / count;
						pixels[idx].b = acc.b / count;
						bleedCount++;
					}
				}
			}

			texture.SetPixels(pixels);
			texture.Apply();

			if (bleedCount > 0)
				MyUtils.MyLog($"Alpha bleed 处理了 {bleedCount} 个透明像素");
		}

		/// <summary>
		/// 辅助：如果目标像素为非透明则累加颜色
		/// </summary>
		private static void TryBleedSample(Color[] pixels, int w, int h, int x, int y, ref Color acc, ref int count)
		{
			if (x < 0 || x >= w || y < 0 || y >= h) return;
			int i = y * w + x;
			if (pixels[i].a > 0f)
			{
				acc.r += pixels[i].r;
				acc.g += pixels[i].g;
				acc.b += pixels[i].b;
				count++;
			}
		}

		/// <summary>
		/// 加载图集：读取 .atlas 文本和 .png 纹理，创建 SpineAtlasAsset
		/// 自动检测并处理预乘Alpha（pma: true）的图集
		/// </summary>
		private static bool TryLoadSpineAtlas(TaiwuAvatar avatar, SpineConfig spineConfig, string atlasPath, 
			out SpineAtlasAsset atlasAsset, out Atlas atlas, out List<Texture2D> createdTextures)
		{
			atlasAsset = null;
			atlas = null;
			createdTextures = new List<Texture2D>();
			string atlasContent = File.ReadAllText(atlasPath);

			// 检测是否需要处理预乘Alpha
			bool hasPma = HasPmaInAtlas(atlasContent);
			if (hasPma)
				MyUtils.MyLog("检测到预乘Alpha图集，将转换为直通Alpha");

			for (int i = 0; i < spineConfig.altas.Count; i++)
			{
				var item = spineConfig.altas[i];
				var pngPath = Path.Combine(spineConfig.fileDir, item + ".png");
				// MyUtils.MyLog($"加载图集 {pngPath}");
				byte[] pngBytes = File.ReadAllBytes(pngPath);
				Texture2D texture = new Texture2D(1, 1);
				texture.LoadImage(pngBytes);
				texture.name = item;

				// 如果 atlas 标记了 pma:true，将纹理从预乘Alpha转换为直通Alpha
				if (hasPma)
					ConvertPremultipliedToStraightAlpha(texture);

				// 对所有纹理做颜色扩张，防止透明边缘与黑色混合出黑边
				ApplyAlphaBleed(texture);

				createdTextures.Add(texture);
			}

			// 如果有预乘Alpha标记，从 atlas 文本中移除 pma 行
			if (hasPma)
			{
				atlasContent = RemovePmaFromAtlas(atlasContent);
				MyUtils.MyLog("已移除 atlas 中的 pma 标记并转换纹理");
			}

			var textAsset = new TextAsset(atlasContent);

			// 从 avatar 的 npcSkeleton 克隆材质
			var npcSkeleton = Traverse.Create(avatar).Field("npcSkeleton").GetValue<SkeletonGraphic>();
			UnityEngine.Material templateMat = npcSkeleton.material;

			atlasAsset = SpineAtlasAsset.CreateRuntimeInstance(textAsset, createdTextures.ToArray(), templateMat, initialize:true);
			atlas = atlasAsset.GetAtlas();

			// MyUtils.MyLog($"TryLoadSpineAtlas: success {Path.GetFileName(atlasPath)}");
			return true;
		}

		/// <summary>
		/// 读取骨骼数据并生成 SkeletonDataAsset
		/// </summary>
		private static bool TryCreateSkeletonDataAsset(SpineConfig spineConfig, string skelPath, Atlas atlas, SpineAtlasAsset atlasAsset,
			out SkeletonDataAsset asset)
		{
			asset = null;
			if(skelPath.EndsWith(".json"))
			{
				string skeletonJsonContent = File.ReadAllText(skelPath);
				TextAsset skeletonJsonTextAsset = new TextAsset(skeletonJsonContent);
				asset = SkeletonDataAsset.CreateRuntimeInstance(skeletonJsonTextAsset, atlasAsset, false, 0.01f);
			}
			else 
			{
				if (skelPath != "") // 是 二进制文件
				{
					// 读取骨骼数据
					var binary = new SkeletonBinary(atlas);
					binary.Scale = 0.01f;
					var skeletonData = binary.ReadSkeletonData(skelPath);
					// 创建 SkeletonDataAsset（skeletonJSON 传 占位值，因为 .skel 是二进制格式）
					asset = SkeletonDataAsset.CreateRuntimeInstance(new TextAsset(), atlasAsset, false, 0.01f);
					Traverse.Create(asset).Field("skeletonData").SetValue(skeletonData);
					Traverse.Create(asset).Field("stateData").SetValue(new AnimationStateData(skeletonData));
				}
				// 按游戏内文件
				else
				{
					CharacterItem config = Character.Instance[ImgTemplate[spineConfig.fileName]];
					string spineName = config.FixedAvatarSpineName;
					string spineDataPath = "RemakeResources/SpineAnimations/" + spineName + "_SkeletonData";
					ResLoader.Load<SkeletonDataAsset>(spineDataPath, delegate (SkeletonDataAsset skeletonData)
					{
						skeletonDataTemp = skeletonData;
					});
					asset = skeletonDataTemp;
					skeletonDataTemp = null;
				}
			}
			// MyUtils.MyLog($"TryCreateSkeletonDataAsset: success {Path.GetFileName(skelPath)}");
			return true;
		}

		/// <summary>
		/// 将 SkeletonDataAsset 应用到 avatar 的 SkeletonGraphic 上
		/// </summary>
		private static bool TryApplySpineAsset(TaiwuAvatar avatar, CharacterAvatar? instance, SpineConfig spineConfig,
			SkeletonDataAsset asset)
		{
			// 获取 npcSkeleton
			var npcSkeleton = Traverse.Create(avatar).Field("npcSkeleton").GetValue<SkeletonGraphic>();
			if (npcSkeleton == null)
			{
				MyUtils.MyLog("TryApplySpineAsset: npcSkeleton is null");
				return false;
			}

			// 禁用 avatarSkeleton，激活 avatarContainer
			var avatarSkeleton = Traverse.Create(avatar).Field("avatarSkeleton").GetValue<Game.Components.Avatar.AvatarSkeleton>();
			if (avatarSkeleton != null)
				avatarSkeleton.gameObject.SetActive(false);

			var avatarContainer = Traverse.Create(avatar).Field("avatarContainer").GetValue<GameObject>();
			if (avatarContainer != null)
				avatarContainer.SetActive(true);

			var spineName = spineConfig.keyName;
			var skinName = spineConfig.skinName;
			var _currentSpineName = Traverse.Create(avatar).Field("_currentSpineName").GetValue<string>();
			var _currentSpineSkin = Traverse.Create(avatar).Field("_currentSpineSkin").GetValue<string>();
			bool isSameSpine = _currentSpineName == spineName && _currentSpineSkin == skinName;
			// MyUtils.MyLog($"TryApplySpineAsset {_currentSpineName} :{spineName}; {_currentSpineSkin} == {skinName}");
			if(isSameSpine && npcSkeleton.gameObject.activeSelf)
			{
				float spineScale = GetSpineScale(spineConfig, avatar.Size);
				npcSkeleton.transform.localScale = Vector3.one * spineScale;
				if(!npcSkeleton.gameObject.activeSelf)
					npcSkeleton.gameObject.SetActive(true);

				// 检查动画是否改变，变了则更新
				var targetAnim = spineConfig.animName;
				if (string.IsNullOrEmpty(targetAnim))
				{
					var sd = npcSkeleton.skeletonDataAsset?.GetSkeletonData(false);
					if (sd != null && sd.Animations.Count > 0)
						targetAnim = sd.Animations.Items[0].Name;
				}
				if (!string.IsNullOrEmpty(targetAnim) && npcSkeleton.AnimationState != null)
				{
					var currentTrack = npcSkeleton.AnimationState.GetCurrent(0);
					bool animMatch = currentTrack != null && currentTrack.Animation.Name == targetAnim;
					// 轮播模式下，当前播 actionAnim 也算匹配，不重置循环
					if (!animMatch && spineConfig.idleRepeat > 0 && !string.IsNullOrEmpty(spineConfig.actionAnim))
						animMatch = currentTrack != null && currentTrack.Animation.Name == spineConfig.actionAnim;
					if (!animMatch)
						PlayAnimCycle(npcSkeleton, targetAnim, spineConfig.idleRepeat, spineConfig.actionAnim);
				}

				// 应用附件
				// ApplySpineAttachments(npcSkeleton, spineConfig);
				// 应用偏移（使用 Traverse 调用私有方法）
				var o = GetSpineOffset(spineConfig, avatar.Size);
				Traverse.Create(avatar).Method("ApplyAvatarOffset", o).GetValue();
			}
			else
			{
				if(!isSameSpine)
					avatar.ResetToBlank(false); // 重置到空白状态，清除旧的头像部件精灵

				// 设置 spine 数据到 SkeletonGraphic
				if (!isSameSpine || npcSkeleton.skeletonDataAsset != asset)
				{
					npcSkeleton.skeletonDataAsset = asset;
					npcSkeleton.initialSkinName = skinName;
					npcSkeleton.Initialize(true);
					npcSkeleton.UnscaledTime = true;
				}

				// 播放动画
				var skelData = asset.GetSkeletonData(false);
				var animations = skelData.Animations;
				if (animations.Count > 0)
				{
					if(spineConfig.animName != "")
						npcSkeleton.startingAnimation = spineConfig.animName;
					else
						npcSkeleton.startingAnimation = animations.Items[0].Name;
					if (npcSkeleton.AnimationState != null)
						PlayAnimCycle(npcSkeleton, npcSkeleton.startingAnimation, spineConfig.idleRepeat, spineConfig.actionAnim);
				}

				// 应用附件
				ApplySpineAttachments(npcSkeleton, spineConfig);

				// 设置缩放和激活
				npcSkeleton.transform.localScale = Vector3.one * GetSpineScale(spineConfig, avatar.Size);
				npcSkeleton.gameObject.SetActive(true);

				Traverse.Create(avatar).Field("_currentSpineName").SetValue(spineName);
				Traverse.Create(avatar).Field("_currentSpineSkin").SetValue(skinName);
				// 应用偏移
				var o = GetSpineOffset(spineConfig, avatar.Size);
				Traverse.Create(avatar).Method("ApplyAvatarOffset", o).GetValue();
			}

			// 触发 CharacterAvatar 回调
			if (instance != null)
				instance.OnFillAvatar?.Invoke();

			// MyUtils.MyLog("TryApplySpineAsset: success");
			return true;
		}

		/// <summary>
		/// 应用附件：先重置到默认皮肤，再遍历 spineConfig.attachments 调用 SetAttachment
		/// </summary>
		private static void ApplySpineAttachments(SkeletonGraphic npcSkeleton, SpineConfig spineConfig)
		{
			if (spineConfig.attachments == null || spineConfig.attachments.Count == 0) return;
			if (npcSkeleton.Skeleton == null) return;

			var skeleton = npcSkeleton.Skeleton;

			// // 先重置到默认皮肤，清除旧的附件覆盖（关键：切换蛐蛐时防止残留）
			// skeleton.SetSkin(skeleton.Data.DefaultSkin);
			// skeleton.SetSlotsToSetupPose();

			foreach (var kv in spineConfig.attachments)
			{
				var slotName = kv.Key;
				var attachName = kv.Value;
				try
				{
					if (string.IsNullOrEmpty(attachName))
					{
						// 空字符串 = 隐藏该槽位（用于可选槽无匹配时）
						var slot = skeleton.FindSlot(slotName);
						if (slot != null) slot.A = 0f;
					}
					else
					{
						skeleton.SetAttachment(slotName, attachName);
					}
				}
				catch (Exception ex)
				{
					MyUtils.MyLog($"SetAttachment failed slot={slotName} attach={attachName}: {ex.Message}");
				}
			}
		}

		/// <summary>
		/// 在 SkeletonGraphic 上播放动画循环。
		/// idleRepeat=0 或 actionAnim="" 时：连续循环 animName
		/// idleRepeat>0 且 actionAnim!="" 时：animName 播放 idleRepeat 次 → actionAnim 1 次 → 重复
		/// </summary>
		private static void PlayAnimCycle(SkeletonGraphic npcSkeleton, string animName, int idleRepeat, string actionAnim)
		{
			if (npcSkeleton == null || npcSkeleton.AnimationState == null) return;

			if (idleRepeat <= 0 || string.IsNullOrEmpty(actionAnim))
			{
				// 连续循环原动画
				npcSkeleton.AnimationState.SetAnimation(0, animName, true);
			}
			else
			{
				// 启动 idle→action 循环，从第 1 次 idle 开始
				PlayIdleInCycle(npcSkeleton, animName, idleRepeat, actionAnim, 1);
			}
		}

		/// <summary>
		/// 在 idle→action 循环中播一次 idle
		/// </summary>
		private static void PlayIdleInCycle(SkeletonGraphic npcSkeleton, string animName, int idleRepeat, string actionAnim, int count)
		{
			if (npcSkeleton == null || npcSkeleton.AnimationState == null) return;

			var track = npcSkeleton.AnimationState.SetAnimation(0, animName, false);
			track.Complete += (entry) =>
			{
				if (npcSkeleton == null || npcSkeleton.AnimationState == null) return;
				if (count < idleRepeat)
				{
					// 继续播 idle
					PlayIdleInCycle(npcSkeleton, animName, idleRepeat, actionAnim, count + 1);
				}
				else
				{
					// idle 播够了，播一次 actionAnim，播完重启循环
					var actionTrack = npcSkeleton.AnimationState.SetAnimation(0, actionAnim, false);
					actionTrack.Complete += (entry2) =>
					{
						if (npcSkeleton == null || npcSkeleton.AnimationState == null) return;
						PlayIdleInCycle(npcSkeleton, animName, idleRepeat, actionAnim, 1);
					};
				}
			};
		}

		/// <summary>
		/// 根据 AvatarSize 获取 spine 位移比例
		/// </summary>
		private static Vector2 GetSpineOffset(SpineConfig spineConfig, TaiwuAvatarSize size)
		{
			switch (size)
			{
				case TaiwuAvatarSize.Big:
					return new Vector2(spineConfig.bigOffsetX, spineConfig.bigOffsetY);
				case TaiwuAvatarSize.Normal:
					return new Vector2(spineConfig.normalOffsetX, spineConfig.noramlOffsetY);
				case TaiwuAvatarSize.Small:
					return new Vector2(spineConfig.smallOffsetX, spineConfig.smallOffsetY);
				default:
					return new Vector2(0,0);
			}
		}

		/// <summary>
		/// 根据 AvatarSize 获取 spine 缩放比例
		/// </summary>
		private static float GetSpineScale(SpineConfig spineConfig, TaiwuAvatarSize size)
		{
			switch (size)
			{
				case TaiwuAvatarSize.Big:
					return spineConfig.scaleBig;
				case TaiwuAvatarSize.Normal:
					return spineConfig.scaleNormal;
				case TaiwuAvatarSize.Small:
					return spineConfig.scaleSmall;
				default:
					return spineConfig.scaleBig;
			}
		}

		private static bool TryTexture(TaiwuAvatar avatar, CharacterAvatar? instance, string avatarAssetName)
		{
			// resPath是资源路径，fallBig 时指向bigsize资源
			// oriPath是原请求路径
			var loadMod = GetResPath(avatarAssetName, avatar.Size, out var resPath, out var fallBig, out var oriPath);
			if (string.IsNullOrEmpty(resPath)) return false;// 报错出来

			// 游戏内资源
			if (!loadMod)
			{
				// 否则静态
				ResLoader.LoadModOrGameResource<Texture2D>(resPath, delegate (Texture2D tex)
				{
					if (avatar == null) return;
					avatar.Refresh(tex);
					if (instance == null) return;
					instance.OnFillAvatar?.Invoke(); // CharacterAvatar 非空时触发回调
				}, (tex) =>
				{
				});
				return true;
			}
			// 自定义资源
			else
			{
				Sprite sprite;
				// 获取缓存 sprite，根据 resPath 缓存
				// 如果 fallBig 时 根据不同size创建不同sprite，则用 oriPath 做key
				if (!resCache.TryGetValue(resPath, out sprite))  
				{
					Texture2D texture = TryLoadImg(resPath);
					if (texture == null) return false;
					sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), 0.5f * Vector2.one);
					sprite.name = texture.name;
					resCache[resPath] = sprite;
				}
				if (sprite == null) return false;
				avatar.Refresh(sprite);
				if(fallBig)
				{
					var i = Traverse.Create(avatar).Field("cloth").GetValue<CImage>();
					i.rectTransform.sizeDelta = GetSizeWH(avatar.Size);
				}

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
        private static bool GetResPath(string avatarAssetName, TaiwuAvatarSize avatarSize, out string resPath, out bool fallBig, out string oriPath)
        {
            var dir = TryLoadResDir(avatarAssetName, out var fileName);
			var relName = fileName + ".png";
			fallBig = false;
			oriPath = null;
			// 路径非空 为自定义资源
			if (!string.IsNullOrEmpty(dir)) // resDirs 0 是空字符串
            {
				resPath = CheckSizePath(avatarSize, 0, dir, relName, out fallBig, out oriPath);
				if (resPath != null) { return true;}
				resPath = CheckSizePath(avatarSize, 1, dir, relName, out fallBig, out oriPath);
				if (resPath != null) { return true; }
				resPath = "";
                return true;
            }
			// 空路径为游戏内资源
            else
            {
                string sizeFolder = CharacterAvatar.GetAvatarSizeFolder(avatarSize);
				var n = avatarAssetName.Split("$"); // 处理立绘跟动态立绘 用$分割
				string resPath1 = CharacterAvatar.GetNpcFaceResPath(sizeFolder, n[0]);
                resPath = resPath1; 
                return false;
            }
            resPath = "";
            return false;
        }

		public static string CheckSizePath(TaiwuAvatarSize avatarSize, int pathType, string dir, string relName, out bool fallBig, out string oriPath)
		{
			fallBig = false;
			var size0 = GetSizeFolder(avatarSize, pathType);
			var res0 = Path.Combine(dir, size0, relName);
			oriPath = res0;
			if(File.Exists(res0)) return res0;
			else
			{
				fallBig = true;
				size0 = GetSizeFolder(TaiwuAvatarSize.Big, pathType);
				res0 = Path.Combine(dir, size0, relName);
				if (File.Exists(res0)) return res0;
				return null;
			}
		}

		/// <summary>
		/// 解析获取自定义路径
		/// 根据`:`前的tag，从 tagDirs 中 找到对应的路径
		/// </summary>
		public static string TryLoadResDir(string avatarAssetName, out string fileName)
        {
            //MyLog($"TryLoadResDir {avatarAssetName}");
            var r = avatarAssetName.Split(':');
            if (r.Length > 1)
            {
				//MyLog($"TryLoadResDir {r[0]} {r[1]}");
				fileName = r[1];
                return tagDirs[r[0]];
            }
			fileName = "";
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

		private static Vector2 GetSizeWH(TaiwuAvatarSize avatarSize)
		{
			if(avatarSize == TaiwuAvatarSize.Big) return new Vector2(720, 880);
			if(avatarSize == TaiwuAvatarSize.Normal) return new Vector2(360, 440);
			if (avatarSize == TaiwuAvatarSize.Small) return new Vector2(180, 220);
			return new Vector2(720, 880);
		}


		/// <summary>
		/// 尝试加载图片
		/// </summary>
		private static Texture2D TryLoadImg(string resPath)
        {
            //MyLog($"tryLoad {resPath}");
            byte[] fileData = File.ReadAllBytes(resPath);
            Texture2D texture = new Texture2D(1, 1);
            texture.name = Path.GetFileName(resPath);

            if (texture.LoadImage(fileData))
            {
                return texture;
            }
            return null;
        }
        #endregion

    }
}
