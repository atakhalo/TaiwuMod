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

using System.Reflection;
using System.Text.RegularExpressions;
using TaiwuModdingLib.Core.Plugin;
using TaiwuModdingLib.Core.Utils;
using TMPro;
using UICommon.Character;
using UICommon.Character.Avatar;
using UnityEngine;


namespace Minutiae
{
    [PluginConfig(pluginName: "NpcFace", creatorId: "atakhalo", pluginVersion: "0.2.0.0")]
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
        //public static Dictionary<string, string> npcRes = new Dictionary<string, string>();

        //public static Dictionary<int, CharacterDisplayData> idData = new Dictionary<int, CharacterDisplayData>();

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

            string npcNameStr = "";
            int npcId = -1;
            //string npcRes = "";
            int npcResIdx = 0;
            ModManager.GetSetting(ModIdStr, "npc1", ref npcNameStr);
            int.TryParse(npcNameStr, out npcId);
            ModManager.GetSetting(ModIdStr, "npcRes1", ref npcResIdx);
            if (npcId != -1 && npcName.Length > npcResIdx) idRes[npcId] = npcName[npcResIdx];
            ModManager.GetSetting(ModIdStr, "npc2", ref npcNameStr);
            int.TryParse(npcNameStr, out npcId);
            ModManager.GetSetting(ModIdStr, "npcRes2", ref npcResIdx);
            if (npcId != -1 && npcName.Length > npcResIdx) idRes[npcId] = npcName[npcResIdx];
            ModManager.GetSetting(ModIdStr, "npc3", ref npcNameStr);
            int.TryParse(npcNameStr, out npcId);
            ModManager.GetSetting(ModIdStr, "npcRes3", ref npcResIdx);
            if (npcId != -1 && npcName.Length > npcResIdx) idRes[npcId] = npcName[npcResIdx];

            //string npcNameStr = "";
            ////string npcRes = "";
            //int npcResIdx = 0;
            //ModManager.GetSetting(ModIdStr, "npc1", ref npcNameStr);
            //ModManager.GetSetting(ModIdStr, "npcRes1", ref npcResIdx);
            //if(!string.IsNullOrEmpty(npcNameStr) && npcName.Length > npcResIdx) NpcFaceFrontendPlugin.npcRes[npcNameStr] = npcName[npcResIdx];
            //ModManager.GetSetting(ModIdStr, "npc2", ref npcNameStr);
            //ModManager.GetSetting(ModIdStr, "npcRes2", ref npcResIdx);
            //if(!string.IsNullOrEmpty(npcNameStr) && npcName.Length > npcResIdx) NpcFaceFrontendPlugin.npcRes[npcNameStr] = npcName[npcResIdx];
            //ModManager.GetSetting(ModIdStr, "npc3", ref npcNameStr);
            //ModManager.GetSetting(ModIdStr, "npcRes3", ref npcResIdx);
            //if(!string.IsNullOrEmpty(npcNameStr) && npcName.Length > npcResIdx) NpcFaceFrontendPlugin.npcRes[npcNameStr] = npcName[npcResIdx];
        }

        public static void TrySetNpcFace(Avatar avatar, CharacterAvatar? instance, int charId)
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

        public static void TrySetNpcFace(Avatar avatar, CharacterAvatar? instance, CharacterDisplayData data)
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

        public static void TrySetNpcFace(Avatar avatar, CharacterAvatar? instance, AvatarRelatedData relatedData)
        {
            MyLog("TrySetNpcFace");
            if(avatar == null) return;
            var curId = TryFindId(avatar.transform, maxUp: 3);
            if(curId != -1)
            {
                var taiwuCharId = SingletonObject.getInstance<BasicGameData>().TaiwuCharId;
                MyLog($"relater 找到id {curId}");
                if(curId == taiwuCharId)
                {
                    MyLog($"relater 找到太吾id");
                    resLoad(avatar, instance, isTaiwu: true);
                }
                if (idRes.ContainsKey(curId))
                {
                    MyLog($"idRes 包含id {curId}");
                    resLoad(avatar, instance, isTaiwu: false, idRes[curId]);
                }
                else
                {
                    MyLog($"idRes 不包含id {curId}");
                }
            }

            //var curName = TryFindName(avatar.transform, maxUp:3);
            //if(curName != "")
            //{
            //    MyLog($"找到名字 {curName}");
            //    if (npcRes.ContainsKey(curName))
            //        resLoad(avatar, instance, isTaiwu: false, npcRes[curName]);
            //}
        }

        public static int TryFindId(Transform transform, int maxUp)
        {
            MyLog($"当前查找{transform}, {maxUp}");
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
                MyLog($"找到 MouseTipDisplayer {charId}");
            }
            return charId;
        }

        public static string TryFindName(Transform transform, int maxUp)
        {
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
            var r = transform.GetComponent<Refers>();
            TextMeshProUGUI t = null;
            if (r != null)
            {
                MyLog($"找到refer {r} {r.Names.Count}");
                r.CTryGet<TextMeshProUGUI>("Name", out t);
                if(t == null) r.CTryGet<TextMeshProUGUI>("CharacterName", out t);
            }
            else
            {
                if (t == null)
                {
                    var t1 = transform.GetComponentInChildren<TextMeshProUGUI>();
                    if (t1 && (t1.name.Contains("name") || t1.name.Contains("Name")))
                        t = t1;
                }
            }
            if (t)
            {
                MyLog($"找到 tmp {t} {t.text}");
                return t.text;
            }
            return "";
        }

        #region
        [HarmonyPostfix, HarmonyPatch(typeof(Avatar), "Refresh", argumentTypes: new Type[1] { typeof(CharacterDisplayData) })]
        public static void OnRefreshChar(Avatar __instance, CharacterDisplayData displayData)
        {
            if (!npcFace) return;
            var charId = displayData.CharacterId;
            var taiwuCharId = SingletonObject.getInstance<BasicGameData>().TaiwuCharId;
            if(displayData.CharacterId == taiwuCharId)
            {
                resLoad(__instance, null, isTaiwu:true);
                return;
            }
            TrySetNpcFace(__instance, null, displayData);
        }

        // 关系界面 主体
        [HarmonyPostfix, HarmonyPatch(typeof(Avatar), "Refresh", argumentTypes: new Type[1] { typeof(AvatarRelatedData) })]
        public static void OnRefreshCharRelated(Avatar __instance, AvatarRelatedData relatedData)
        {
            if (!npcFace) return;
            MyLog("OnRefreshCharRelated");
            //var taiwuCharId = SingletonObject.getInstance<BasicGameData>().TaiwuCharId;
            //if (UIElement.CharacterMenuRelationShip.Exist)
            //{
            //    var ui = UIElement.CharacterMenuRelationShip.UiBase as UI_CharacterMenuRelationShip;

            //    var taiwuData = Traverse.Create(ui).Field("_taiwuCharacterDisplayData").GetValue<CharacterDisplayData>();
            //    if (taiwuData != null)
            //    {
            //        if (taiwuData.AvatarRelatedData == relatedData)
            //        {
            //            resLoad(__instance, null, isTaiwu: true);
            //            return;
            //        }
            //    }
            //}

            //CheckIdRelated(__instance, relatedData);
            DelayCall(__instance, relatedData);
        }

        public static void DelayCall(Avatar avatar, AvatarRelatedData relatedData)
        {
            Game.Instance.StartCoroutine(DelayCoroutine(TrySetNpcFace, 0, avatar, null, relatedData));
        }
        private static IEnumerator DelayCoroutine(Action<Avatar, CharacterAvatar, AvatarRelatedData> action, float delay, Avatar avatar, CharacterAvatar instance, AvatarRelatedData relatedData)
        {
            yield return new WaitForSeconds(delay);
            action?.Invoke(avatar, instance, relatedData);
        }


        //// 关系界面
        //[HarmonyPostfix, HarmonyPatch(typeof(Avatar), "Refresh", argumentTypes: new Type[2] { typeof(AvatarRelatedData), typeof(short) })]
        //public static void OnRefreshCharRelatedChar(Avatar __instance, AvatarRelatedData relatedData, short characterTemplateId)
        //{
        //    if (!npcFace) return;
        //    MyLog("OnRefreshCharRelatedChar");

        //    var taiwuCharId = SingletonObject.getInstance<BasicGameData>().TaiwuCharId;
        //    var extraData = SingletonObject.getInstance<CharacterMonitorModel>().GetCharacterAvatarExtraData(taiwuCharId);
        //    if (extraData != null && extraData == __instance.AvatarExtraData)
        //    {
        //        resLoad(__instance, null, isTaiwu: true);
        //        return;
        //    }
        //    CheckIdRelated(__instance, relatedData);
        //    //TrySetNpcFace(__instance, null);
        //}

        // 人物界面, 
        // 返回的时候，判断是否tips在请求，是的话 发起计算请求
        [HarmonyPostfix, HarmonyPatch(typeof(CharacterAvatar), "FillElement")]
        public static void FillElementPost(CharacterAvatar __instance)
        {
            if (!npcFace) return;

            if (__instance == null) return;
            var taiwuCharId = SingletonObject.getInstance<BasicGameData>().TaiwuCharId;
            if (__instance.CharacterId == taiwuCharId)
            {
                var avatar = Traverse.Create(__instance).Field("_avatar").GetValue<Avatar>();
                resLoad(avatar, __instance, isTaiwu: true);
                return;
            }
            else
            {
                var avatar = Traverse.Create(__instance).Field("_avatar").GetValue<Avatar>();
                TrySetNpcFace(avatar, __instance, __instance.CharacterId);
            }
        }
        // tips界面
        [HarmonyPostfix, HarmonyPatch(typeof(MouseTipCharacterOnMapBlock), "SetAvatar")]
        public static void SetAvatar(MouseTipCharacterOnMapBlock __instance, CharacterDisplayDataForMapBlock data)
        {
            var taiwuCharId = SingletonObject.getInstance<BasicGameData>().TaiwuCharId;
            Refers _mainPanel = Traverse.Create(__instance).Field("_mainPanel").GetValue<Refers>();
            Avatar avatar = _mainPanel.CGet<Avatar>("Avatar"); 
            if (data.CharacterId == taiwuCharId)
            {
                //resLoad(avatar, null, isTaiwu: true);
            }
            else
            {
                if (idRes.ContainsKey(data.CharacterId))
                {
                    resLoad(avatar, null, isTaiwu: false, idRes[data.CharacterId]);
                }
            }
        }

        private static void resLoad(Avatar avatar, CharacterAvatar? instance, bool isTaiwu, string res=null)
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

            string sizeFolder = CharacterAvatar.GetAvatarSizeFolder(avatar.Size);
            string resPath = CharacterAvatar.GetNpcFaceResPath(sizeFolder, avatarAssetName);
            //MyLog($"load {resPath}");
            //MyLog($"LoadModOrGameResource");

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
        #endregion

    }
}
