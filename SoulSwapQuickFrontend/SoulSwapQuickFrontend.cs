#pragma warning disable CS8618, CS8600, CS8603, CS8625, CS8601, CS8604

using System;
using System.Collections.Generic;
using FrameWork.ModSystem;
using FrameWork.UISystem.UIElements;
using Game.Components.Avatar;
using Game.Views.SectInteract;
using GameData.Domains.Building;
using GameData.Domains.Character.AvatarSystem;
using GameData.Domains.Character.AvatarSystem.AvatarRes;
using GameData.Domains.Character.Display;
using HarmonyLib;
using TaiwuModdingLib.Core.Plugin;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SoulSwapQuickFrontend
{
    /// <summary>
    /// 一键化魂：在化魂阁（轮回台）的每个魂魄槽位上注入一个快捷按钮，
    /// 点击后把"结果人物外貌"中所有可设置项一键设为该魂魄的外貌项。
    /// 与"化形塑体"一致：只暂存（SetTemporaryPossessionCharacterAvatar）并刷新预览，
    /// 不实际替换，玩家点击"化魂仪式"后才真正替换。
    /// 可设置项集合参照游戏内"随机"按钮（RandomAvatarHandler）：体型、肤色、前后发、眉、
    /// 眼（含合法性校验）、鼻、嘴、胡须1/2、面部特征1/2。
    /// </summary>
    [PluginConfig(pluginName: "SoulSwapQuick", creatorId: "atakhalo", pluginVersion: "0.1.0.1")]
    public class SoulSwapQuickFrontendPlugin : TaiwuRemakePlugin
    {
        private Harmony? harmony;

        /// <summary>总开关：一键化魂</summary>
        public static bool pluginEnable = true;

        /// <summary>快捷按钮图标字符（圆形，表示应用/执行）</summary>
        public const string QuickIconChar = "●";

        /// <summary>快捷按钮图标字号</summary>
        public const float QuickIconSize = 40f;

        public override void Initialize()
        {
            Debug.Log("[SoulSwapQuick] Frontend Initialize");
            harmony = Harmony.CreateAndPatchAll(typeof(SoulSwapQuickFrontendPlugin));
        }

        public override void Dispose()
        {
            harmony?.UnpatchSelf();
        }

        public override void OnModSettingUpdate()
        {
            ModManager.GetSetting(ModIdStr, "pluginEnable", ref pluginEnable);
        }

        // ============================================================
        //  按钮注入：每次魂魄槽位数据刷新后确保快捷按钮存在
        // ============================================================

        /// <summary>
        /// SwapSoulCharacterItem.Set 在每次槽位数据刷新（含清空）时被调用。
        /// 仅在魂魄槽位（!IsBody）上注入快捷按钮；无数据时隐藏。
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(SwapSoulCharacterItem), "Set")]
        public static void OnSoulItemSet(SwapSoulCharacterItem __instance, CharacterDisplayData characterDisplayData, int soulLimit)
        {
            if (!pluginEnable) return;
            if (__instance == null || __instance.IsBody) return; // 只处理魂魄槽位

            // 锁定槽位（Index >= soulLimit）或无数据时不显示快捷按钮
            bool hasData = characterDisplayData != null && __instance.Index < soulLimit;
            EnsureQuickButton(__instance, hasData);
        }

        /// <summary>确保魂魄槽位上的快捷按钮存在且可见性正确（防重复注入）</summary>
        private static void EnsureQuickButton(SwapSoulCharacterItem soulItem, bool visible)
        {
            Transform slotRoot = soulItem.transform;
            if (slotRoot == null) return;

            // 查找已创建的按钮，避免重复
            Transform exist = slotRoot.Find("QuickSoulApplyBtn");
            if (exist != null)
            {
                exist.gameObject.SetActive(visible);
                return;
            }

            // 无数据且尚未创建过按钮时，无需创建
            if (!visible) return;

            // clone 槽位内已有的 deleteBtn 作为模板（有数据时才显示，与快捷按钮显示条件一致）
            var deleteBtn = AccessTools.Field(typeof(SwapSoulCharacterItem), "deleteBtn").GetValue(soulItem) as CButton;
            if (deleteBtn == null || deleteBtn.gameObject == null) return;

            GameObject template = deleteBtn.gameObject;
            GameObject go = UnityEngine.Object.Instantiate(template, template.transform.parent);
            go.name = "QuickSoulApplyBtn";

            // 清理 clone 可能带上的运行时监听
            var btn = go.GetComponent<CButton>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
            }

            // 把 clone 放到原按钮旁边（向左偏移 40，保持与删除按钮同尺寸）
            RectTransform btnRt = go.GetComponent<RectTransform>();
            RectTransform srcRt = deleteBtn.GetComponent<RectTransform>();
            if (btnRt != null && srcRt != null)
            {
                btnRt.anchoredPosition = srcRt.anchoredPosition + new Vector2(-40f, 0f);
            }

            // 用"↓"文本图标替换原来的 X 图标
            try
            {
                SetupTextIcon(go, soulItem);
            }
            catch (Exception e)
            {
                Debug.LogError("[SoulSwapQuick] SetupTextIcon failed: " + e);
            }

            // 移除克隆可能附带的 Tooltip 提示（避免错误的说明）
            var tip = go.GetComponent<TooltipInvoker>();
            if (tip != null)
            {
                UnityEngine.Object.Destroy(tip);
            }

            // 绑定点击：一键应用该魂魄的外貌
            if (btn != null)
            {
                SwapSoulCharacterItem captured = soulItem;
                btn.onClick.AddListener(delegate
                {
                    ApplySoulAvatar(captured);
                });
            }

            go.SetActive(visible);
        }

        /// <summary>在按钮上叠加"●"圆形图标文字。
        /// 不隐藏按钮原图（X 与背景是同一张 Image），文字作为子物体居中叠加显示。
        /// 文字优先复用已有 TMP；否则克隆槽位角色名 GameObject（自带字体材质）。</summary>
        private static void SetupTextIcon(GameObject go, SwapSoulCharacterItem soulItem)
        {
            // 1) 已有 TMP 文字（若 X 是字符）直接改文字
            TextMeshProUGUI[] tmps = go.GetComponentsInChildren<TextMeshProUGUI>(true);
            foreach (TextMeshProUGUI t in tmps)
            {
                SetupIconText(t);
                return;
            }

            // 2) 无 TMP 文字：克隆槽位角色名 GameObject（自带字体材质），作为按钮子物体显示"●"
            var charName = AccessTools.Field(typeof(SwapSoulCharacterItem), "characterName").GetValue(soulItem) as TextMeshProUGUI;
            if (charName == null) return;

            GameObject textGo = UnityEngine.Object.Instantiate(charName.gameObject, go.transform);
            textGo.name = "QuickSoulApplyIcon";

            // 清理克隆残留的布局/提示组件，避免文字被顶出按钮
            foreach (var cf in textGo.GetComponentsInChildren<ContentSizeFitter>(true)) UnityEngine.Object.Destroy(cf);
            foreach (var le in textGo.GetComponentsInChildren<LayoutElement>(true)) UnityEngine.Object.Destroy(le);
            foreach (var lg in textGo.GetComponentsInChildren<HorizontalOrVerticalLayoutGroup>(true)) UnityEngine.Object.Destroy(lg);
            foreach (var tip in textGo.GetComponentsInChildren<TooltipInvoker>(true)) UnityEngine.Object.Destroy(tip);

            TextMeshProUGUI tmp = textGo.GetComponent<TextMeshProUGUI>();
            if (tmp == null) return;

            SetupIconText(tmp);

            // 铺满按钮居中
            RectTransform rt = tmp.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.localPosition = Vector3.zero;
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;

            // 若按钮内部有布局组件，让文字忽略布局，独立居中定位
            if (go.GetComponent<HorizontalOrVerticalLayoutGroup>() != null)
            {
                LayoutElement ignore = textGo.AddComponent<LayoutElement>();
                ignore.ignoreLayout = true;
            }

            textGo.transform.SetAsLastSibling();
        }

        /// <summary>设置图标文字样式（圆形字符、居中、固定字号）</summary>
        private static void SetupIconText(TextMeshProUGUI t)
        {
            t.text = QuickIconChar;
            t.alignment = TextAlignmentOptions.Center;
            t.raycastTarget = false;
            t.fontSize = QuickIconSize;
            t.enableAutoSizing = false;
            t.color = Color.white;
        }

        // ============================================================
        //  一键应用魂魄外貌（参照 RandomAvatarHandler 去随机）
        // ============================================================

        /// <summary>获取魂魄槽位所属的化魂阁视图（SwapSoulCharacterItem.Parent 为私有属性，反射访问）</summary>
        private static ViewSwapSoul GetParentView(SwapSoulCharacterItem soulItem)
        {
            try
            {
                var prop = AccessTools.Property(typeof(SwapSoulCharacterItem), "Parent");
                return prop != null ? prop.GetValue(soulItem) as ViewSwapSoul : null;
            }
            catch (Exception e)
            {
                Debug.LogError("[SoulSwapQuick] GetParentView failed: " + e);
                return null;
            }
        }

        /// <summary>把结果人物外貌的全部可设置项设为该魂魄的对应项，并暂存 + 刷新预览</summary>
        private static void ApplySoulAvatar(SwapSoulCharacterItem soulItem)
        {
            try
            {
                AvatarData soulAvatar = soulItem.AvatarData;
                if (soulAvatar == null) return;

                ViewSwapSoul view = GetParentView(soulItem);
                if (view == null) return;

                // 躯壳 AvatarData（结果人物基底）
                var bodyItem = AccessTools.Field(typeof(ViewSwapSoul), "bodyCharacterItem").GetValue(view) as SwapSoulCharacterItem;
                AvatarData bodyAvatar = bodyItem != null ? bodyItem.AvatarData : null;
                if (bodyAvatar == null) return;

                // 以躯壳为基底
                AvatarData newAvatar = new AvatarData();
                newAvatar.Copy(bodyAvatar);

                // 体型
                newAvatar.ChangeBodyType(soulAvatar.GetBodyType());
                // 颜色（肤色 / 衣服色 / 发色×2 / 眉色 / 瞳色 / 唇色 / 胡须色×2 / 面部特征色×2）
                newAvatar.ColorSkinId = soulAvatar.ColorSkinId;
                newAvatar.ColorClothId = soulAvatar.ColorClothId;
                newAvatar.ColorFrontHairId = soulAvatar.ColorFrontHairId;
                newAvatar.ColorBackHairId = soulAvatar.ColorBackHairId;
                newAvatar.ColorEyebrowId = soulAvatar.ColorEyebrowId;
                newAvatar.ColorEyeballId = soulAvatar.ColorEyeballId;
                newAvatar.ColorMouthId = soulAvatar.ColorMouthId;
                newAvatar.ColorBeard1Id = soulAvatar.ColorBeard1Id;
                newAvatar.ColorBeard2Id = soulAvatar.ColorBeard2Id;
                newAvatar.ColorFeature1Id = soulAvatar.ColorFeature1Id;
                newAvatar.ColorFeature2Id = soulAvatar.ColorFeature2Id;
                // 前发 / 后发
                newAvatar.FrontHairId = soulAvatar.FrontHairId;
                newAvatar.BackHairId = soulAvatar.BackHairId;
                // 眉
                newAvatar.EyebrowId = soulAvatar.EyebrowId;
                // 眼（主眼 + 左右眼，整体采用魂魄的，并做合法性校验）
                newAvatar.EyesMainId = soulAvatar.EyesMainId;
                newAvatar.EyesLeftId = soulAvatar.EyesLeftId;
                newAvatar.EyesRightId = soulAvatar.EyesRightId;
                ValidateEyes(newAvatar);
                // 鼻 / 嘴
                newAvatar.NoseId = soulAvatar.NoseId;
                newAvatar.MouthId = soulAvatar.MouthId;
                // 胡须上 / 下
                newAvatar.Beard1Id = soulAvatar.Beard1Id;
                newAvatar.Beard2Id = soulAvatar.Beard2Id;
                // 面部特征 1 / 2
                newAvatar.Feature1Id = soulAvatar.Feature1Id;
                newAvatar.Feature2Id = soulAvatar.Feature2Id;

                // 五官微调参数（间距 / 高度 / 缩放 / 角度）：眼、眉、鼻、嘴，跟随魂魄
                newAvatar.EyesHeight = soulAvatar.EyesHeight;
                newAvatar.EyesDistance = soulAvatar.EyesDistance;
                newAvatar.EyesAngle = soulAvatar.EyesAngle;
                newAvatar.EyesScale = soulAvatar.EyesScale;
                newAvatar.EyebrowHeight = soulAvatar.EyebrowHeight;
                newAvatar.EyebrowDistance = soulAvatar.EyebrowDistance;
                newAvatar.EyebrowAngle = soulAvatar.EyebrowAngle;
                newAvatar.EyebrowScale = soulAvatar.EyebrowScale;
                newAvatar.NoseHeight = soulAvatar.NoseHeight;
                newAvatar.NoseScale = soulAvatar.NoseScale;
                newAvatar.MouthHeight = soulAvatar.MouthHeight;
                newAvatar.MouthScale = soulAvatar.MouthScale;

                // 暂存（与化形塑体"完成"一致：只暂存，不实际替换）
                BuildingDomainMethod.Call.SetTemporaryPossessionCharacterAvatar(newAvatar);

                // 刷新结果预览
                var resultAvatar = AccessTools.Field(typeof(ViewSwapSoul), "resultCharacterAvatar").GetValue(view) as Avatar;
                var preview = AccessTools.Field(typeof(ViewSwapSoul), "_resultPreview").GetValue(view) as PossessionPreview;
                if (resultAvatar != null && preview != null)
                {
                    resultAvatar.Refresh(newAvatar, preview.Age);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[SoulSwapQuick] ApplySoulAvatar failed: " + e);
            }
        }

        /// <summary>眼睛合法性校验：在当前 AvatarId 资源组中不存在的左右眼子项置 0（参照游戏随机逻辑）</summary>
        private static void ValidateEyes(AvatarData avatarData)
        {
            try
            {
                AvatarGroup group = SingletonObject.getInstance<AvatarManager>().GetAvatarGroup((int)avatarData.AvatarId);
                if (group == null) return;

                if (avatarData.EyesLeftId != 0)
                {
                    AvatarAsset asset = group.Get(EAvatarElementsType.Eye, new short[] { avatarData.EyesMainId, avatarData.EyesLeftId });
                    if (asset == null) avatarData.EyesLeftId = 0;
                }
                if (avatarData.EyesRightId != 0)
                {
                    AvatarAsset asset2 = group.Get(EAvatarElementsType.Eye, new short[] { avatarData.EyesMainId, avatarData.EyesRightId });
                    if (asset2 == null) avatarData.EyesRightId = 0;
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[SoulSwapQuick] ValidateEyes failed: " + e);
            }
        }
    }
}
