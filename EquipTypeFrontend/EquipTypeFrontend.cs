//#define taiwuNormal
//#define taiwuTest

using CharacterDataMonitor;
using Config;
using Config.Common;
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
using System.Collections.Specialized;
using System.Linq;
using System.Reflection;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using TaiwuModdingLib.Core.Plugin;
using TaiwuModdingLib.Core.Utils;
using TMPro;
using UICommon.Character;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static Unity.IO.LowLevel.Unsafe.AsyncReadManagerMetrics;
//using UnityEngine.Windows;
//using static FrameWork.AspectRatio.PlatformSpecific.Win32AspectRatioLock;
//using static GameData.Domains.Item.ItemOperationType;
//using static TMPro.SpriteAssetUtilities.TexturePacker_JsonArray;


namespace EquipType
{
    public class MyUtils
    {
        public static void MyLog(string log)
        {
            Debug.Log($"[{nameof(EquipType)}] {log}");
        }

        public static void DelayCall(Action action, float delay, bool real)
        {
            //Game.Instance.StartCoroutine(DelayCoroutine(TrySetNpcFace, 0, avatar, null, relatedData));
            GameApp.Instance.StartCoroutine(DelayCoroutine(action, delay, real));
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


    [PluginConfig(pluginName: nameof(EquipType), creatorId: "atakhalo", pluginVersion: "0.1.0.1")]
    public class WeightAnalyzeFrontendPlugin : TaiwuRemakePlugin
    {
        private Harmony harmony;

        public static bool equipType = true; // 开关

        public static Dictionary<int, int> subToType = new Dictionary<int, int>();  //存一下 maketype 跟 subtype 的映射

        public override void Initialize()
        {
            MyUtils.MyLog($"Initialize");
            harmony = Harmony.CreateAndPatchAll(typeof(WeightAnalyzeFrontendPlugin));
            MapSubType();
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
            ModManager.GetSetting(ModIdStr, "equipType", ref equipType);
            MyUtils.MyLog($"setting equipType {equipType}");
        }

        private static void MapSubType()
        {
            var _itemData = Traverse.Create(MakeItemType.Instance).Field("_dataArray").GetValue<List<MakeItemTypeItem>>();
            if (_itemData == null) return;
            foreach (var item in _itemData)
            {
                foreach (var subType in item.MakeItemSubTypes)
                {
                    subToType[subType] = item.TemplateId;
                }
            }
        }


        [HarmonyPostfix, HarmonyPatch(typeof(MouseTipWeapon), "ShowData")]
        public static void MouseTipWeapon_ShowData(MouseTipWeapon __instance)
        {
            if (!equipType) return;
            var _itemData = Traverse.Create(__instance).Field("_itemData").GetValue<ItemDisplayData>();

            WeaponItem configData = Weapon.Instance[_itemData.Key.TemplateId];
            SetTypeStr(__instance.CGet<TextMeshProUGUI>("SubType"), configData.ResourceType, configData.ItemSubType, configData.MakeItemSubType);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(MouseTipArmor), "Init")]
        public static void MouseTipArmor_Init(MouseTipArmor __instance)
        {
            if (!equipType) return;
            var _itemData = Traverse.Create(__instance).Field("_itemData").GetValue<ItemDisplayData>();

            var configData = Armor.Instance[_itemData.Key.TemplateId];
            SetTypeStr(__instance.CGet<TextMeshProUGUI>("SubType"), configData.ResourceType, configData.ItemSubType, configData.MakeItemSubType);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(MouseTipAccessory), "Init")]
        public static void MouseTipAccessory_Init(MouseTipAccessory __instance)
        {
            if (!equipType) return;
            var _itemData = Traverse.Create(__instance).Field("_itemData").GetValue<ItemDisplayData>();

            var configData = Accessory.Instance[_itemData.Key.TemplateId];
            SetTypeStr(__instance.CGet<TextMeshProUGUI>("SubType"), configData.ResourceType, configData.ItemSubType, configData.MakeItemSubType);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(MouseTipCarrier), "Init")]
        public static void MouseTipCarrier_Init(MouseTipCarrier __instance)
        {
            if (!equipType) return;
            var _itemData = Traverse.Create(__instance).Field("_itemData").GetValue<ItemDisplayData>();
            
            var configData = Carrier.Instance[_itemData.Key.TemplateId];
            SetTypeStr(__instance.CGet<TextMeshProUGUI>("SubType"), configData.ResourceType, configData.ItemSubType, configData.MakeItemSubType);
        }

        private static void SetTypeStr(TextMeshProUGUI text, sbyte resType, short subType, short makeType)
        {
            bool flag2 = resType >= 0;
            StringBuilder strBuilder = new StringBuilder();
            if (flag2)
            {
                strBuilder.Append(Config.ResourceType.Instance[resType].Name);
            }
            strBuilder.Append(LocalStringManager.Get(string.Format("LK_ItemSubType_{0}", subType)));
            if (flag2)
            {
                if(subToType.ContainsKey(makeType))
                {
                    var config = MakeItemType.Instance[subToType[makeType]];

                    MakeItemSubTypeItem subConfig = MakeItemSubType.Instance[makeType];
                    if(subConfig.Name.IsNullOrEmpty())
                        strBuilder.Append($"({config.Name})");
                    else
                        strBuilder.Append($"({config.Name} {subConfig.Name})");
                }
            }
            text.text = strBuilder.ToString();
        }
    }
}
