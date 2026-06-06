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
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static FrameWork.AspectRatio.PlatformSpecific.Win32AspectRatioLock;
using static GameData.Domains.Item.ItemOperationType;
using static TMPro.SpriteAssetUtilities.TexturePacker_JsonArray;


namespace MouseKey
{
    public class MyUtils
    {
        public static void MyLog(string log)
        {
            Debug.Log($"[MouseKey] {log}");
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


    [PluginConfig(pluginName: "MouseKey", creatorId: "atakhalo", pluginVersion: "0.1.0.1")]
    public class MouseKeyFrontendPlugin : TaiwuRemakePlugin
    {
        private Harmony harmony;

        public static bool mouseKey = true; // 开关
        public static bool joystick = false; // 开关

        public static int mode = 0; // 0； 1 移动；3 滚动
        public static bool isHold = false;

        public static GameObject tipsObj; // tips文本的物体
        public static TextMeshProUGUI tipsText;
        public static GameObject topUI; // canvas

        public override void Initialize()
        {
            MyUtils.MyLog($"Initialize");
            harmony = Harmony.CreateAndPatchAll(typeof(MouseKeyFrontendPlugin));
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
            ModManager.GetSetting(ModIdStr, "mouseKey", ref mouseKey);
            ModManager.GetSetting(ModIdStr, "joystick", ref joystick);
            MyUtils.MyLog($"setting {mouseKey} {joystick}");
        }

        [HarmonyPostfix, HarmonyPatch(typeof(GameApp), "Update")]
        public static void GameUpdate(GameApp __instance)
        {
            if (!mouseKey) return;
			CheckEnter();
            CheckMove();
            CheckClick();
            CheckScroll();
        }

        public static void CheckEnter()
        {
            //if (mode != 0) return;
			var moveMode = (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.BackQuote));
            if(joystick) moveMode = moveMode || Input.GetKeyDown(KeyCode.JoystickButton6);
            var scrollMode = (Input.GetKey(KeyCode.LeftShift) && Input.GetKeyDown(KeyCode.BackQuote));
            if(joystick) scrollMode = scrollMode || Input.GetKeyDown(KeyCode.JoystickButton7);

            if (moveMode)
            {
                if(isHold) mouse_event(0x0004, 0, 0, 0, 0);
                if (mode != 1) ToEnter(1);
                else ToExit();
            }
            else if (scrollMode)
            {
                if(isHold) mouse_event(0x0004, 0, 0, 0, 0);
                if (mode != 3) ToEnter(3);
                else ToExit();
            }
        }
        public static void ToEnter(int toMode)
        {
            mode = toMode;
            OnEnter();
        }
        public static void OnEnter()
        {
            Init();
            string tips = "";
            if (mode == 1) tips = "移动模式";
            //if (mode == 1 && isHold) tips = "拖动模式";
            if (mode == 3) tips = "滚动模式";
            MyUtils.MyLog($"进入{tips}");
            tipsText.text = tips;
        }
        public static void ToHold()
        {

        }

        public static void ToExit()
        {
            mode = 0;
            OnExit();
        }
        public static void OnExit()
        {
            MyUtils.MyLog($"OnExit");
            CycleTipsText();
        }

        public static void Init()
        {
            if (!tipsObj) // 某些时候可能被消耗了
            {
                MyUtils.MyLog($"创建text");
                topUI = UIManager.Instance.transform.GetChild(0).gameObject; // UIManager 挂在相机 root上，下面才是canvas
                var t = topUI.transform.Find("LayerCursor");
                CreateTipsText(t);
                //haveInit = true;
            }
            else
            {
                tipsObj.SetActive(true);
            }
        }
        public static void CreateTipsText(Transform parent)
        {
            if (tipsObj) return;
            tipsObj = new GameObject("MouseKeyTips", new[] { typeof(RectTransform), });
            tipsObj.transform.SetParent(parent);
            var rect = tipsObj.GetComponent<RectTransform>();
            //// 居中
            //rect.anchorMin = new Vector2(0.5f, 0.5f);
            //rect.anchorMax = new Vector2(0.5f, 0.5f);
            //rect.SetPivot(new Vector2(0.5f, 0.5f));
            //rect.sizeDelta = new Vector2(200, 100);
            // 下方
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.SetPivot(new Vector2(0.5f, 0f));
            rect.sizeDelta = new Vector2(300, 100);
            // 归0
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
            rect.localPosition = Vector3.zero;
            rect.anchoredPosition = Vector2.zero;

            // 手动创建 TextMeshProUGUI（GameObjectCreationUtils 在游戏更新后被移除）
            tipsText = tipsObj.AddComponent<TextMeshProUGUI>();
            tipsText.name = "MouseKeyTipsText";
            tipsText.text = "V";
            tipsText.fontSize = 60f;
            tipsText.alignment = TextAlignmentOptions.Center;
            tipsText.color = Color.red;
            // 从游戏现有 UI 文本中获取正确的字体（LiberationSans 在游戏中不存在）
            // 使用 parent 所在 Canvas 下的文本来避免 DontDestroyOnLoad 场景问题
            var canvas = parent.GetComponentInParent<Canvas>(true);
            TextMeshProUGUI refText = null;
            if (canvas != null)
            {
                refText = canvas.GetComponentInChildren<TextMeshProUGUI>(true);
            }
            if (refText == null || refText.font == null)
            {
                // 兜底：从 UIManager 所在根节点查找
                refText = topUI.GetComponentInChildren<TextMeshProUGUI>(true);
            }
            if (refText != null && refText.font != null)
            {
                tipsText.font = refText.font;
                // fontMaterial 的 getter 内部会复制材质，可能抛出异常，只设置 font 即可
            }
            MyUtils.MyLog($"CreateMouseKeyTipsText {rect.anchoredPosition}  {rect.localPosition} {rect.localScale}");
        }
        public static void CycleTipsText()
        {
            if (!tipsObj)
            {
                MyUtils.MyLog($"CycleTipsText tipsObj 已经被销毁");
                return;
            }
            tipsObj.SetActive(false);
        }
        
        public static void CheckMove()
        {
            if (mode != 1) return;

            var fixDis = 30;
            //var speed = 1;
            var moveDisX = 0;
            var moveDisY = 0;

            var toMoveXL = Input.GetKey(KeyCode.LeftArrow);
            if (joystick) toMoveXL = toMoveXL || Input.GetAxis("Horizontal") < -0.1f;
            var toMoveXR = Input.GetKey(KeyCode.RightArrow);
            if (joystick) toMoveXR = toMoveXR || Input.GetAxis("Horizontal") > 0.1f;
            var toMoveYU = Input.GetKey(KeyCode.UpArrow);
            if (joystick) toMoveYU = toMoveYU || Input.GetAxis("Vertical") > 0.1f;
            var toMoveYD = Input.GetKey(KeyCode.DownArrow);
            if (joystick) toMoveYD = toMoveYD || Input.GetAxis("Vertical") < -0.1f;

            var slow = Input.GetKey(KeyCode.LeftControl);
            if (joystick) slow = slow || Input.GetKey(KeyCode.JoystickButton2);
            var quick = Input.GetKey(KeyCode.LeftShift);
            if (joystick) quick = quick || Input.GetKey(KeyCode.JoystickButton3);

            var dis = fixDis;
            if (quick) dis = 100;
            if (slow) dis = 3;

            if (toMoveXL) moveDisX -= dis;
            else if(toMoveXR) moveDisX += dis;
            if (toMoveYU) moveDisY -= dis;
            else if (toMoveYD) moveDisY += dis;

            if(moveDisX != 0 || moveDisY != 0)
            {
                if (GetCursorPos(out var currentPos))
                {
                    int posX = (int)(currentPos.X + moveDisX);
                    int posY = (int)(currentPos.Y + moveDisY);
                    //MyUtils.MyLog($"move {moveDisX},{moveDisY}; {posX},{posY}; <- {currentPos};");
                    SetCursorPos(posX, posY);
                }
            }
        }
        
        //public static void CheckHold()
        //{
        //    var drag = Input.GetKey(KeyCode.LeftShift) && Input.GetKeyDown(KeyCode.BackQuote);
        //    uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        //    uint MOUSEEVENTF_LEFTUP = 0x0004;
        //    uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        //    uint MOUSEEVENTF_RIGHTUP = 0x0010;
        //    if (drag)
        //    {
        //        if(isHold) mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
        //        else mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
        //    }
        //}
        public static void CheckClick()
        {
            if(joystick)
            {
                if (Input.GetKeyDown(KeyCode.JoystickButton0)) SimulateKeyPress(KeyCode.Space);
                if (Input.GetKeyDown(KeyCode.JoystickButton1)) SimulateKeyPress(KeyCode.Escape);
            }

            if (mode != 1) return;
            uint MOUSEEVENTF_LEFTDOWN = 0x0002;
            uint MOUSEEVENTF_LEFTUP = 0x0004;
            uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
            uint MOUSEEVENTF_RIGHTUP = 0x0010;

            var toClickDown = Input.GetKeyDown(KeyCode.BackQuote);
            if (joystick) toClickDown = toClickDown || Input.GetKeyDown(KeyCode.JoystickButton5);
            var toClickUp = Input.GetKeyUp(KeyCode.BackQuote);
            if (joystick) toClickUp = toClickUp || Input.GetKeyUp(KeyCode.JoystickButton5);

            if (toClickDown)
            {
                isHold = true;
                mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
            }
            else if(toClickUp)
            {
                isHold = false;
                mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
            }
        }
        public static void SimulateKeyPress(KeyCode keyCode)
        {
            byte virtualKey = (byte)keyCode;

            // 按下键
            keybd_event(virtualKey, 0, 0, 0);
            uint KEYEVENTF_KEYUP = 0x0002;
            // 释放键
            keybd_event(virtualKey, 0, KEYEVENTF_KEYUP, 0);
        }

        public static void CheckScroll()
        {
            if (mode != 3) return;
            int scrollAmount = 120; // 滚轮滚动量（标准值为120）
            int dis = 0;

            var toMoveYU = Input.GetKey(KeyCode.UpArrow) || Input.GetAxis("Vertical") > 0.1f;
            var toMoveYD = Input.GetKey(KeyCode.DownArrow) || Input.GetAxis("Vertical") < -0.1f;
            if (toMoveYU) dis = scrollAmount;
            else if (toMoveYD) dis = -scrollAmount;

            uint MOUSEEVENTF_WHEEL = 0x0800;
            mouse_event(MOUSEEVENTF_WHEEL, 0, 0, (uint)dis, 0);
        }
        [DllImport("user32.dll")]
        static extern bool SetCursorPos(int X, int Y);
        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out POINT point);
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }
        [DllImport("user32.dll")]
        static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, int dwExtraInfo);

        [DllImport("user32.dll")]
        static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);
    }
}
