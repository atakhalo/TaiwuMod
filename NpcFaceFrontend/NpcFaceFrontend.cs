using CharacterDataMonitor;
using Config;
using FrameWork;
using GameData.Domains.Character;
using GameData.Domains.Character.Display;
using GameData.Domains.Item;
using GameData.Domains.Item.Display;
using GameData.Domains.Map;
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
using System.Reflection;
using System.Text.RegularExpressions;
using TaiwuModdingLib.Core.Plugin;
using TaiwuModdingLib.Core.Utils;
using TMPro;
using UICommon.Character;
//using UICommon.Character.Avatar;
using UnityEngine;

using TaiwuAvatar = UICommon.Character.Avatar.Avatar;

namespace NpcFace
{
    [PluginConfig(pluginName: "NpcFace", creatorId: "atakhalo", pluginVersion: "0.3.0.0")]
    public class NpcFaceFrontendPlugin : TaiwuRemakePlugin
    {
        private Harmony harmony;


        public static bool npcFace; // 开关 是否开启

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
        public static string npcNameCustom; // 资源名

        public static bool showCharId = false; // 开关 是否显示为charid
        public static Dictionary<int, string> idRes = new Dictionary<int, string>();
        public static Dictionary<string, string> npcRes = new Dictionary<string, string>();

        public static bool toCreateFile = false;
        public static bool toReadFile = false;

        public static List<string> resDirs = new List<string>(); // 资源路径
        public static Dictionary<string, Texture2D> resCache = new Dictionary<string, Texture2D>();

        public static void MyLog(string log)
        {
            Debug.Log($"[NpcFace] {log}");
        }

        public override void Initialize()
        {
            MyLog("Initialize");
            harmony = Harmony.CreateAndPatchAll(typeof(NpcFaceFrontendPlugin));
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
            //MyLog($"resDir1 {resDirStr}");
            if (!string.IsNullOrEmpty(resDirStr)) resDirs.Add(resDirStr);
            ModManager.GetSetting(ModIdStr, "resDir2", ref resDirStr);
            //MyLog($"resDir2 {resDirStr}");
            if (!string.IsNullOrEmpty(resDirStr)) resDirs.Add(resDirStr);
            ModManager.GetSetting(ModIdStr, "resDir3", ref resDirStr);
            //MyLog($"resDir3 {resDirStr}");
            if (!string.IsNullOrEmpty(resDirStr)) resDirs.Add(resDirStr);
        }

        public  void TryLoadNpc(string nameKey, string resKey, string assetKey)
        {
            string npcNameStr = "";
            int npcResIdx = 0;
            string npcAssetStr = "";
            ModManager.GetSetting(ModIdStr, "npc1", ref npcNameStr);
            if (string.IsNullOrEmpty(npcNameStr))
                return;
            ModManager.GetSetting(ModIdStr, "npcRes1", ref npcResIdx);
            if (npcName.Length > npcResIdx) npcRes[npcNameStr] = npcName[npcResIdx];
            ModManager.GetSetting(ModIdStr, "npcAsset1", ref npcAssetStr);
            if (!string.IsNullOrEmpty(npcAssetStr)) npcRes[npcNameStr] = npcAssetStr;
        }

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

        public static void TrySetNpcFace(TaiwuAvatar avatar, CharacterAvatar? instance, int charId)
        {
            if (avatar == null) return;
            var curId = charId;
            if (curId != -1)
            {
                //MyLog($"charId 找到id {curId}");
                if (idRes.ContainsKey(curId))
                    resLoad(avatar, instance, isTaiwu: false, idRes[curId]);
            }
        }

        public static void TrySetNpcFace(TaiwuAvatar avatar, CharacterAvatar? instance, CharacterDisplayData data)
        {
            if (avatar == null) return;
            var curId = data.CharacterId;
            if (curId != -1)
            {
                //MyLog($"CharacterDisplayData 找到id {curId}");
                if (idRes.ContainsKey(curId))
                    resLoad(avatar, instance, isTaiwu: false, idRes[curId]);
            }
        }

        /// <summary>
        /// 根据 mousetips 找id
        /// </summary>
        public static void TrySetNpcFace(TaiwuAvatar avatar, CharacterAvatar? instance, AvatarRelatedData relatedData)
        {
            //MyLog("TrySetNpcFace");
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

        public static int TryFindId(Transform transform, int maxUp)
        {
            //MyLog($"当前查找{transform}, {maxUp}");
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
        public static int TryGetNpcId(Transform transform)
        {
            var r = transform.GetComponent<MouseTipDisplayer>();
            if(r == null) r = transform.GetComponentInChildren<MouseTipDisplayer>();
            int charId = -1;
            if(r != null && r.RuntimeParam != null)
            {

                r.RuntimeParam.Get("charId", out charId);
                if (charId == -1) r.RuntimeParam.Get("CharId", out charId);
                if (charId == -1) r.RuntimeParam.Get("NpcCharId", out charId);
                //MyLog($"找到 MouseTipDisplayer {charId}");
            }
            return charId;
        }

        public static void TrySetNpcFaceByName(TaiwuAvatar avatar, CharacterAvatar? instance, AvatarRelatedData relatedData)
        {
            //MyLog($"TrySetNpcFaceByName relatedData {avatar}");
            if (avatar == null) return;
            var curName = TryFindName(avatar.transform, maxUp: 3);
            if (curName != "")
            {
                //MyLog($"TrySetNpcFaceByName 找到名字 {curName}");
                var taiwuDisplayName = SingletonObject.getInstance<BasicGameData>().TaiwuDisplayName;
                if(curName == taiwuDisplayName)
                    resLoad(avatar, instance, isTaiwu: true);
                else
                {
                    if (npcRes.ContainsKey(curName))
                        resLoad(avatar, instance, isTaiwu: false, npcRes[curName]);
                }
            }
        }

        public static void TrySetNpcFaceByName(TaiwuAvatar avatar, CharacterAvatar? instance, CharacterDisplayData displayData)
        {
            //MyLog($"TrySetNpcFaceByName displayData  {avatar}");
            if (avatar == null) return;
            string curName = NameCenter.GetMonasticTitleOrDisplayName(displayData, isTaiwu: false);
            if (curName != "")
            {
                //MyLog($"TrySetNpcFaceByName 找到名字 {curName}");
                if (npcRes.ContainsKey(curName))
                    resLoad(avatar, instance, isTaiwu: false, npcRes[curName]);
            }
        }

        public static string TryFindName(Transform transform, int maxUp)
        {
            if(transform.name.Contains("TaiwuChar")) // FillElementPost 调用过来的，会走到这里，ui_bottom下可以用这个判断
            {
                var taiwuDisplayName = SingletonObject.getInstance<BasicGameData>().TaiwuDisplayName;
                return taiwuDisplayName;
            }
            //var ui = transform.GetComponentInParent<UIBase>();
            //if (ui is UI_CharacterMenuInfo)
            //    MyLog("$正在查找 UI_CharacterMenuInfo");
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

        public static string TryGetNpcName(Transform transform)
        {
            TextMeshProUGUI t = null;
            var ts = transform.GetComponentsInChildren<TextMeshProUGUI>();
            foreach(var t1 in ts)
            {
                if (t1.name.Contains("name") || t1.name.Contains("Name")
                    && t1.name != "OrganizationName" && t1.name != "SkillName"
                    && !t1.text.Contains("ID:") && t1.text!="剩余潜力")  // mod添加的控件
                {
                    t = t1;
                    break;
                }
            }
            if (t)
            {
                //MyLog($"找到 {transform}下的tmp  {t} {t.text}");
                return t.text;
            }
            //MyLog($" {transform} 无 tmp ");
            var r = transform.GetComponent<Refers>();
            if (r is Avatar) r = null;
            if (r != null)
            {
                //MyLog($"找到refer {r} {r.Names.Count}");
                r.CTryGet<TextMeshProUGUI>("Name", out t);
                if(t == null) r.CTryGet<TextMeshProUGUI>("CharacterName", out t);
                if(t == null) r.CTryGet<TextMeshProUGUI>("CharName", out t);
            }
            if (t)
            {
                //MyLog($"找到 refer 下的tmp {t} {t.text}");
                return t.text;
            }
            return "";
        }

        #region
        [HarmonyPostfix, HarmonyPatch(typeof(TaiwuAvatar), "Refresh", argumentTypes: new Type[1] { typeof(CharacterDisplayData) })]
        public static void OnRefreshChar(TaiwuAvatar __instance, CharacterDisplayData displayData)
        {
            if (!npcFace) return;
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
            Game.Instance.StartCoroutine(DelayCoroutine(TrySetNpcFaceByName, 0, avatar, null, relatedData));
        }

        private static IEnumerator DelayCoroutine(Action<TaiwuAvatar, CharacterAvatar, AvatarRelatedData> action, float delay, TaiwuAvatar avatar, CharacterAvatar instance, AvatarRelatedData relatedData)
        {
            yield return new WaitForSeconds(delay);
            action?.Invoke(avatar, instance, relatedData);
        }

        // 人物界面, 
        // 返回的时候，判断是否tips在请求，是的话 发起计算请求
        [HarmonyPostfix, HarmonyPatch(typeof(CharacterAvatar), "FillElement")]
        public static void FillElementPost(CharacterAvatar __instance)
        {
            if (!npcFace) return;
            //MyLog($"FillElementPost");
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
                //TrySetNpcFaceByName(avatar, null, relatedData:null);
                DelayCall(avatar, null);

                //TrySetNpcFace(avatar, __instance, __instance.CharacterId);
            }
        }

        //// tips界面
        //[HarmonyPostfix, HarmonyPatch(typeof(MouseTipCharacterOnMapBlock), "SetAvatar")]
        //public static void SetAvatar(MouseTipCharacterOnMapBlock __instance, CharacterDisplayDataForMapBlock data)
        //{
        //    if (!npcFace) return;
        //    var taiwuCharId = SingletonObject.getInstance<BasicGameData>().TaiwuCharId;
        //    Refers _mainPanel = Traverse.Create(__instance).Field("_mainPanel").GetValue<Refers>();
        //    Avatar avatar = _mainPanel.CGet<Avatar>("Avatar");
        //    if (data.CharacterId == taiwuCharId)
        //    {
        //        //resLoad(avatar, null, isTaiwu: true);
        //    }
        //    else
        //    {
        //        //TrySetNpcFace(avatar, null, data.CharacterId);
        //    }
        //}

        private static void resLoad(TaiwuAvatar avatar, CharacterAvatar? instance, bool isTaiwu, string res=null)
        {
            //MyLog($"LoadModOrGameResource 0 ");
            if (!npcFace) return;
            //MyLog($"LoadModOrGameResource 1 ");
            if (avatar == null) return;
            //MyLog($"LoadModOrGameResource 2");

            var avatarAssetName = "NpcFace_yingjiao";
            if(isTaiwu)
            {
                if (customNpc)
                    avatarAssetName = npcNameCustom;
                else
                {
                    avatarAssetName = npcName[npcNameIdx];
                }
            }
            else
            {
                //MyLog($"LoadModOrGameResource 3");

                if (string.IsNullOrEmpty(res)) return;
                avatarAssetName = res;
            }
            //MyLog($"LoadModOrGameResource 4");


            var loadMod = GetResPath(avatarAssetName, avatar.Size, out var resPath);
            //if (resPath == null) return;// 报错出来
            //MyLog($"load {resPath}");
            //MyLog($"LoadModOrGameResource");

            if(!loadMod)
            {
                ResLoader.LoadModOrGameResource<Texture2D>(resPath, delegate (Texture2D tex)
                {
                    //MyLog($"LoadModOrGameResource");
                    if (avatar == null) return;
                    //var cloth = avatar.CGet<CImage>("Cloth");
                    //MyLog($"LoadModOrGameResource {cloth} {cloth?.sprite}");
                    avatar.Refresh(tex);
                    if (instance == null) return;
                    instance.OnFillAvatar?.Invoke();
                }, (tex) => {
                });
            }
            else
            {
                var tex = TryLoadImg(resPath);
                if(tex == null) return;
                avatar.Refresh(tex);
                if (instance == null) return;
                instance.OnFillAvatar?.Invoke();
            }
        }

        private static bool GetResPath(string avatarAssetName, UICommon.Character.Avatar.AvatarSize avatarSize, out string resPath)
        {
            //MyLog($"GetResPath {avatarAssetName}");
            var dirIdx = -1;
            var r = avatarAssetName.Split(':');
            if (r.Length != 0)
            {
                int.TryParse(r[0], out dirIdx);
                //MyLog($"GetResPath dirIdx {dirIdx}");
            }
            if (dirIdx >= 1 && dirIdx < resDirs.Count) // resDirs 0 是空字符串
            {
                var relName = r[1] + ".png";
                // 先检查 BigTexture mod路径，再检查 BigFace 路径
                var size1 = GetSizeFolder(avatarSize, 1);
                var res1 = Path.Combine(resDirs[dirIdx], size1, relName);
                if (File.Exists(res1)) { resPath = res1; return true; }
                var size0 = GetSizeFolder(avatarSize, 0);
                var res0 = Path.Combine(resDirs[dirIdx], size1, relName);
                if (File.Exists(res0)) { resPath = res0; return true; }
                resPath = "";
                return true;
            }
            else if(dirIdx == -1)
            {
                string sizeFolder = CharacterAvatar.GetAvatarSizeFolder(avatarSize);
                string resPath1 = CharacterAvatar.GetNpcFaceResPath(sizeFolder, avatarAssetName);
                if (File.Exists(resPath1))
                {
                    resPath = resPath1; return false;
                }
            }
            resPath = "";
            return false;
        }

        private static string GetSizeFolder(UICommon.Character.Avatar.AvatarSize avatarSize, int type)
        {
            if(type == 0)
            {
                //return CharacterAvatar.GetAvatarSizeFolder(avatar.Size);
                return avatarSize switch
                {
                    UICommon.Character.Avatar.AvatarSize.Normal => "NormalFace",
                    UICommon.Character.Avatar.AvatarSize.Small => "SmallFace",
                    _ => "BigFace",
                };
            }
            else if (type == 1)
            {
                return avatarSize switch
                {
                    UICommon.Character.Avatar.AvatarSize.Normal => "NormalTexture",
                    UICommon.Character.Avatar.AvatarSize.Small => "SmallTexture",
                    _ => "BigTexture",
                };
            }
            return "";
        }
        private static Texture2D TryLoadImg(string resPath)
        {
            if(resCache.TryGetValue(resPath, out Texture2D img)) { return img; }

            byte[] fileData = File.ReadAllBytes(resPath);
            Texture2D texture = new Texture2D(1, 1);
            texture.name = Path.GetFileName(resPath);

            if (texture.LoadImage(fileData))
            {
                resCache[resPath] = texture;
                return texture;
            }
            return null;
            //UnityEngine.image
            //UnityEngine.ImageConversion
            
            //if (texture.LoadImage(fileData))
            //{
            //    return texture;
            //}
        }
        #endregion

    }
}
