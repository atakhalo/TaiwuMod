using CharacterDataMonitor;
using Config;
using FrameWork;
using FrameWork.ModSystem;
using GameData.Domains.Character;
using GameData.Domains.Character.Display;
using GameData.Domains.Item;
using GameData.Domains.Item.Display;
using GameData.Domains.Map;
using GameData.Domains.Taiwu;
using GameData.Domains.Taiwu.Display;
using GameData.Domains.TaiwuEvent.DisplayEvent;
using GameData.Serializer;
using GameData.Utilities;
using HarmonyLib;
using HarmonyLib.Tools;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using TaiwuModdingLib.Core.Plugin;
using TaiwuModdingLib.Core.Utils;
using TMPro;
using UICommon.Character;
using UICommon.Character.Avatar;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
//using UnityEngine.Windows;
//using static FrameWork.AspectRatio.PlatformSpecific.Win32AspectRatioLock;
//using static GameData.Domains.Item.ItemOperationType;
//using static TMPro.SpriteAssetUtilities.TexturePacker_JsonArray;


namespace WinBetter
{
    public class MyUtils
    {
        public static void MyLog(string log)
        {
            Debug.Log($"[WinBetter] {log}");
        }

        public static void DelayCall(Action action, float delay, bool real)
        {
            //Game.Instance.StartCoroutine(DelayCoroutine(TrySetNpcFace, 0, avatar, null, relatedData));
            Game.Instance.StartCoroutine(DelayCoroutine(action, delay, real));
        }

        private static IEnumerator DelayCoroutine(Action action, float delay, bool real)
        {
            if(real)
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
            if( canvas != null )
            {
                var depth = 0;
                var cur = transform;
                while(cur != canvas.transform)
                {
                    ShowMonoOne(cur, depth, prefix:cur.GetSiblingIndex().ToString());
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

        public static void ShowMonoOne(Transform transform, int depth=0, Transform sp=null, string prefix="", string postfix = "")
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


    [PluginConfig(pluginName: "WinBetter", creatorId: "atakhalo", pluginVersion: "0.1.0.1")]
    public class WinBetterFrontendPlugin : TaiwuRemakePlugin
    {
        private Harmony harmony;

        public static bool avgGrade = true; // 装备开关
        public static bool exNeili = true; // 内力开关

        public override void Initialize()
        {
            MyUtils.MyLog($"Initialize");
            harmony = Harmony.CreateAndPatchAll(typeof(WinBetterFrontendPlugin));
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
            ModManager.GetSetting(ModIdStr, "avgGrade", ref avgGrade);
            ModManager.GetSetting(ModIdStr, "exNeili", ref exNeili);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(UI_CharacterMenuEquip), "OnEquipLoadChange")]
        public static void OnEquipLoadChange(UI_CharacterMenuEquip __instance)
        {
            if (!avgGrade) return;

            MyUtils.DelayCall(UpdateGrade, 0.1f, false);
        }

        private static void UpdateGrade()
        {
            var ui = UIElement.CharacterMenuEquip.UiBase as UI_CharacterMenuEquip;
            var grade = CaleGrade(ui);
            var gradeStr = $"{grade:f1}".SetGradeColor(Mathf.FloorToInt(grade));

            var _equipMonitor = Traverse.Create(ui).Field("_equipMonitor").GetValue<EquipmentMonitor>();
            var o = string.Format("{0:f1}", (float)_equipMonitor.MaxEquipmentLoad / 100f);
            ui.CGet<TextMeshProUGUI>("MaxLoad").text = $"{o} 品级:{gradeStr}";
        }

        private static float CaleGrade(UI_CharacterMenuEquip __instance)
        {
            // 参考 后端 PrepareCombat CombatDomain
            // this.SelfAvgEquipGrade = 0f 部分
            // 品级计算
            // 4 跟 11 不算
            var selfAvgEquipGrade = 0f;
			int selfEquipCount = 0;
            var equips = Traverse.Create(__instance).Field("_equipItems").GetValue<List<ItemDisplayData>>();
            if (equips.Count < 12) return 0; // 可能没初始化好
            for (sbyte slot = 0; slot < 12; slot += 1)
            {
                bool flag9 = slot == 4 || slot == 11;
                if (!flag9)
                {
                    var equip = equips[(int)slot];
                    ItemKey equipKey = equip.Key;
                    bool flag10 = !equipKey.IsValid();
                    if (!flag10)
                    {

                        bool flag11 = equip.MaxDurability >= 0 && equip.Durability <= 0;
                        if (!flag11)
                        {
                            sbyte grade = ItemTemplateHelper.GetGrade(equipKey.ItemType, equipKey.TemplateId);
                            selfAvgEquipGrade += (float)(grade + 1);
                            selfEquipCount++;
                        }
                    }
                }
            }
            selfAvgEquipGrade /= (float)Math.Max(1, selfEquipCount);
            return selfAvgEquipGrade;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(UI_CharacterMenuEquipCombatSkill), "UpdateNeiliAllocation")]
        public static void UpdateNeiliAllocation(UI_CharacterMenuEquipCombatSkill __instance)
        {
            if (!exNeili) return;

            // 参考 UI_CharacterMenuEquipCombatSkill UpdateNeiliAllocation 部分
            var ui = __instance;
            var _neiliAllocationHolder = Traverse.Create(ui).Field("_neiliAllocationHolder").GetValue<RectTransform>();
            var sum = 0;
            for (byte type = 0; type < 4; type += 1)
            {
                Refers allocationRefers = _neiliAllocationHolder.GetChild((int)type).GetComponent<Refers>();
                var o = allocationRefers.CGet<TextMeshProUGUI>("ExtraValue").text;
                if(int.TryParse(GetNumInColor(o), out var n))
                    sum += n;
            }

            var _neiliRefers = Traverse.Create(ui).Field("_neiliRefers").GetValue<Refers>();
            var _baseNeiliAllocation = Traverse.Create(ui).Field("_baseNeiliAllocation").GetValue<NeiliAllocation>();
            var cur = _baseNeiliAllocation.GetTotal();

            var _dataMonitor = Traverse.Create(ui).Field("_dataMonitor").GetValue<EquipCombatSkillMonitor>();
            var _featureIds = Traverse.Create(ui).Field("_featureIds").GetValue<List<short>>();
            var max = CombatHelper.GetMaxTotalNeiliAllocationConsideringFeature(_dataMonitor.ConsummateLevel, _featureIds);

            _neiliRefers.CGet<TextMeshProUGUI>("TotalNeiliAllocation").text = $"{cur}/{max}/{cur+sum}";
        }

        private static string GetNumInColor(string input)
        {
            // 查找开始和结束标签位置
            int startIndex = input.IndexOf('>');
            int endIndex = input.LastIndexOf('<');

            if (startIndex != -1 && endIndex != -1 && startIndex < endIndex)
            {
                return input.Substring(startIndex + 1, endIndex - startIndex - 1);
            }

            return input; // 如果没有找到标签，返回原字符串
        }
    }


}
