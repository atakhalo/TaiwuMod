using CharacterDataMonitor;
using Config;
using FrameWork;
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
    [PluginConfig(pluginName: "NpcFace", creatorId: "atakhalo", pluginVersion: "0.1.0.0")]
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
        }

        #region
        [HarmonyPostfix, HarmonyPatch(typeof(Avatar), "Refresh", argumentTypes: new Type[1] { typeof(CharacterDisplayData) })]
        public static void OnRefreshChar(Avatar __instance, CharacterDisplayData displayData)
        {
            if (!npcFace) return;

            var taiwuCharId = SingletonObject.getInstance<BasicGameData>().TaiwuCharId;
            if(displayData.CharacterId == taiwuCharId)
            {
                resLoad(__instance, null);
            }
        }

        // 关系界面 主体
        [HarmonyPostfix, HarmonyPatch(typeof(Avatar), "Refresh", argumentTypes: new Type[1] { typeof(AvatarRelatedData) })]
        public static void OnRefreshCharRelated(Avatar __instance, AvatarRelatedData relatedData)
        {
            if (!npcFace) return;

            var taiwuCharId = SingletonObject.getInstance<BasicGameData>().TaiwuCharId;
            if (UIElement.CharacterMenuRelationShip.Exist)
            {
                var ui = UIElement.CharacterMenuRelationShip.UiBase as UI_CharacterMenuRelationShip;

                var taiwuData = Traverse.Create(ui).Field("_taiwuCharacterDisplayData").GetValue<CharacterDisplayData>();
                if (taiwuData != null)
                {
                    if (taiwuData.AvatarRelatedData == relatedData)
                    {
                        resLoad(__instance, null);
                    }
                }
            }
        }

        // 关系界面
        [HarmonyPostfix, HarmonyPatch(typeof(Avatar), "Refresh", argumentTypes: new Type[2] { typeof(AvatarRelatedData), typeof(short) })]
        public static void OnRefreshCharRelatedChar(Avatar __instance, AvatarRelatedData relatedData, short characterTemplateId)
        {
            if (!npcFace) return;

            var taiwuCharId = SingletonObject.getInstance<BasicGameData>().TaiwuCharId;
            var extraData = SingletonObject.getInstance<CharacterMonitorModel>().GetCharacterAvatarExtraData(taiwuCharId);
            if (extraData != null && extraData == __instance.AvatarExtraData)
            {
                resLoad(__instance, null);
            }
        }

        // 人物界面
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
                resLoad(avatar, __instance);
                return;
            }
            else
            {
                //var item = __instance.GetMonitor<AvatarInfoMonitor>();
                //if (item == null) { Debug.Log($"[Minutiae] FillElement item null"); return; }
                //var avatar = Traverse.Create(__instance).Field("_avatar").GetValue<Avatar>();
                //if (avatar == null) { Debug.Log($"[Minutiae] FillElement avatar null"); return; }
                //var config = Character.Instance[item.TemplateId];
                //if (config == null) { Debug.Log($"[Minutiae] FillElement config null"); return; }

                //if (string.IsNullOrEmpty(config.FixedAvatarName))
                //{
                //    if (avatar.Data == null) { Debug.Log($"[Minutiae] FillElement avatar.Data null"); return; }
                //    var gender = avatar.Data.Gender;
                //    if (item.Character.IsDead)
                //        return;
                //}
            }
        }

        private static void resLoad(Avatar avatar, CharacterAvatar? instance)
        {
            if (!npcFace) return;
            if (avatar == null) return;

            var avatarAssetName = "NpcFace_yingjiao";
            if (customNpc)
                avatarAssetName = npcNameCustom;
            else
            {
                avatarAssetName = npcName[npcNameIdx];
            }

            string sizeFolder = CharacterAvatar.GetAvatarSizeFolder(avatar.Size);
            string resPath = CharacterAvatar.GetNpcFaceResPath(sizeFolder, avatarAssetName);
            //MyLog($"load {resPath}");
            ResLoader.LoadModOrGameResource<Texture2D>(resPath, delegate (Texture2D tex)
            {
                if (avatar == null) return;
                avatar.Refresh(tex);
                if (instance == null) return;
                instance.OnFillAvatar?.Invoke();
            }, (tex) => {
            });
        }
        #endregion

    }
}
