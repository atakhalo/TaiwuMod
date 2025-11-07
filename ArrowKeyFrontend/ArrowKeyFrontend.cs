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
using static FrameWork.AspectRatio.PlatformSpecific.Win32AspectRatioLock;
using static GameData.Domains.Item.ItemOperationType;


namespace ArrowKey
{
    public class MyUtils
    {
        public static void MyLog(string log)
        {
            Debug.Log($"[ArrowKey] {log}");
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


    [PluginConfig(pluginName: "ArrowKey", creatorId: "atakhalo", pluginVersion: "0.1.0.1")]
    public class ArrowKeyFrontendPlugin : TaiwuRemakePlugin
    {
        private Harmony harmony;

        public static bool arrowKey = true; // 开关

        public static KeyCode enterKey = KeyCode.BackQuote; // 进入点击模式
        public static List<KeyCode> enterKeys = new List<KeyCode>()
        { 
            //KeyCode.Return, KeyCode.KeypadEnter, KeyCode.Space,
            KeyCode.BackQuote,
            KeyCode.JoystickButton4 //LB
        };
        public static List<KeyCode> clickKey = new List<KeyCode>()
        {
            KeyCode.Return, KeyCode.KeypadEnter,
            KeyCode.JoystickButton0,//a
        };


        public static bool isEnter = false;
        public static bool haveInit = false; // 开关
        public static GameObject tipsObj; // 当前挂着的按钮

        public static GameObject topUI; // 向上寻找到这个就不用找了

        public static List<GameObject> pagePath;
        public static GameObject lastPage;
        public static Selectable curButton; // 当前挂着的按钮
        public static Image curButtonBg; // 当前挂着的按钮的背景
        public static Color btnColor; // 当前挂着的按钮原来的颜色

        public override void Initialize()
        {
            MyUtils.MyLog($"Initialize");
            harmony = Harmony.CreateAndPatchAll(typeof(ArrowKeyFrontendPlugin));
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
            ModManager.GetSetting(ModIdStr, "arrowKey", ref arrowKey);
            MyUtils.MyLog($"setting {arrowKey}");
        }
        #region 处理ui变化时，退出导航
        [HarmonyPrefix, HarmonyPatch(typeof(UIManager), "ShowUI")]
        public static void ShowUI(UIManager __instance) { if (isEnter) { ToExit(); } else { RelButton(); } }
        [HarmonyPrefix, HarmonyPatch(typeof(UIManager), "HideUI")]
        public static void HideUI(UIManager __instance) { if (isEnter) { ToExit(); } else { RelButton(); } }
        [HarmonyPrefix, HarmonyPatch(typeof(UIManager), "ChangeToUI")]
        public static void ChangeToUI(UIManager __instance) { if (isEnter) { ToExit(); } else { RelButton(); } }
        [HarmonyPrefix, HarmonyPatch(typeof(UIManager), "StackToUI")]
        public static void StackToUI(UIManager __instance) { if (isEnter) { ToExit(); } else { RelButton(); } }
        [HarmonyPrefix, HarmonyPatch(typeof(UIManager), "StackBack")]
        public static void StackBack(UIManager __instance) { if (isEnter) { ToExit(); } else { RelButton(); } }
        #endregion

        [HarmonyPostfix, HarmonyPatch(typeof(Game), "Update")]
        public static void GameUpdate(Game __instance)
        {
            if (!arrowKey) return;
            //if (!Input.anyKey) return;
            //List<KeyCode> joyKey = new List<KeyCode>()
            //{
            //    KeyCode.JoystickButton0,
            //    KeyCode.JoystickButton1,
            //    KeyCode.JoystickButton2,
            //    KeyCode.JoystickButton3,
            //    KeyCode.JoystickButton4,
            //    KeyCode.JoystickButton5,
            //    KeyCode.JoystickButton6,
            //    KeyCode.JoystickButton7,
            //    KeyCode.JoystickButton8,
            //    KeyCode.JoystickButton9,
            //    KeyCode.JoystickButton10,
            //    KeyCode.JoystickButton11,
            //    KeyCode.JoystickButton12,
            //    KeyCode.JoystickButton13,
            //    KeyCode.JoystickButton14,
            //    KeyCode.JoystickButton15,
            //    KeyCode.JoystickButton16,
            //    KeyCode.JoystickButton17,
            //    KeyCode.JoystickButton18,
            //    KeyCode.JoystickButton19,
            //};
            //if(MyUtils.isAnyKey(joyKey, out var key))
            //{
            //    MyUtils.MyLog($"按了 {key}");
            //}
            CheckEnter();
            CheckClickKey();
            CheckMoveKey();

        }

        public static void CheckEnter()
        {
            if (MyUtils.isAnyKey(enterKeys, out _))
            {
                if (!isEnter)
                {
                    ToEnter();
                }
                else
                {
                    ToExit(rel: false);
                }
            }
        }
        public static void ToEnter()
        {
            isEnter = true;
            MyUtils.MyLog($"进入导航模式");
            OnEnter();
        }
        public static void OnEnter()
        {
            Init();
            TryReGetButton();
            if (curButton == null)
            {
                TryFindButton();
            }
            if (curButton == null)
            {
                ToExit();
            }
        }
        public static void ToExit(bool rel = true)
        {
            isEnter = false;
            MyUtils.MyLog($"退出导航模式");
            OnExit(rel);
        }
        public static void OnExit(bool rel = true)
        {
            MyUtils.MyLog($"OnExit {rel}");
            RelButton(rel);
            CycleTipsText();
        }
        public static void RelButton(bool rel = true)
        {
            if (curButtonBg)
            {
                curButtonBg.color = btnColor;
                curButtonBg = null;
            }
            if (rel && curButton)
            {
                curButton = null;
            }
        }

        public static void Init()
        {
            if (!tipsObj) // 某些时候可能被消耗了
            {
                MyUtils.MyLog($"创建text");
                topUI = UIManager.Instance.transform.GetChild(0).gameObject; // UIManager 挂在相机 root上，下面才是canvas
                CreateTipsText(topUI.transform);
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
            tipsObj = new GameObject("ArrowKeyTips", new[] { typeof(RectTransform), });
            tipsObj.transform.SetParent(parent);
            var rect = tipsObj.GetComponent<RectTransform>();
            // 居中
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.SetPivot(new Vector2(0.5f, 0.5f));
            rect.sizeDelta = new Vector2(30, 30);
            // 归0
            rect.anchoredPosition = Vector2.zero;
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
            rect.localPosition = Vector3.zero;

            var tipsText = GameObjectCreationUtils.UGUICreateTMPText(tipsObj.transform, new Vector2(0.5f, 0.5f), new Vector2(30f, 30f), 100f, "V");
            tipsText.name = "ArrowKeyTipsText";
            tipsText.color = Color.red;

            MyUtils.MyLog($"CreateTipsText {rect.anchoredPosition}  {rect.localPosition} {rect.localScale}");
        }
        public static void CycleTipsText()
        {
            if (!tipsObj)
            {
                MyUtils.MyLog($"CycleTipsText tipsObj 已经被销毁");
                return;
            }
            tipsObj.SetActive(false);
            tipsObj.transform.SetParent(topUI.transform);

            var rect = tipsObj.GetComponent<RectTransform>();
            rect.anchoredPosition = Vector2.zero;
            rect.localPosition = Vector2.zero;
            rect.localScale = Vector2.one;
            //MyUtils.MyLog($"CycleTipsText {rect.anchoredPosition}  {rect.localPosition} {rect.localScale}");
        }

        public static void TipsParent(Transform parent)
        {
            // 挂在左上
            var rect = tipsObj.GetComponent<RectTransform>();
            tipsObj.transform.SetParent(parent);
            rect.anchoredPosition = Vector2.zero;
            rect.localPosition = Vector2.zero;
            rect.localScale = Vector2.one;
            //MyUtils.MyLog($"TipsParent {rect.anchoredPosition}  {rect.localPosition} {rect.localScale}");
        }
        public static void TryReGetButton()
        {
            if (curButton != null)
            {
                if (CheckButton(curButton))
                {
                    MyUtils.MyLog($"{curButton}可用，恢复原位");
                    MoveTo(curButton);
                }
                else
                {
                    MyUtils.MyLog($"{curButton}不可用，尝试寻找替代");
                    var parent = curButton.transform.parent;
                    var index = curButton.transform.GetSiblingIndex();
                    if (FindButtonLast(parent, out var button1, out var _, out var _, checkSelf: false, rStartIndex: index - 1, loop: true))
                    {
                        MyUtils.MyLog($"{curButton}寻找到替代{button1}");
                        MoveTo(button1);
                    }
                    else
                    {
                        MyUtils.MyLog($"{curButton}无替代");
                        curButton = null;
                    }
                }
            }
            else
            {
                MyUtils.MyLog($"无旧按钮");
            }
        }
        public static void TryFindButton()
        {
            if (FindButtonLast(topUI.transform, out var button, out var isSelf, out var index))
            {
                MyUtils.MyLog($"找到最末按钮 {button.name}");
                MyUtils.ShowMonoToParent(button.transform);
                var buttonUI = button.GetComponentInParent<UIBase>();
                if (buttonUI != null)
                {
                    MyUtils.MyLog($"找到最末按钮所属 ui {buttonUI}");
                    MyUtils.ShowMonoOne(buttonUI.transform);
                    if (FindButtonFirst(buttonUI.transform, out var buttonOut, out var isUISelf, out var firstIndex))
                    {
                        MyUtils.MyLog($"找到最末按钮所属 ui 的 起始按钮 {buttonOut.name}");
                        MyUtils.ShowMonoToParent(buttonOut.transform);
                        MoveTo(buttonOut);
                    }
                    else
                    {
                        MyUtils.MyLog($"无最末按钮所属 ui 的 起始按钮, 以该最末按钮为准");
                        MoveTo(button);
                    }
                }
                else
                {
                    if (GetButtonSibiFirst(button, out var buttonOut, out var firstIndex))
                    {
                        MyUtils.MyLog($"找到起始按钮 {buttonOut.name}");
                        MyUtils.ShowMonoToParent(buttonOut.transform);
                        MoveTo(buttonOut);
                    }
                    else
                    {
                        MyUtils.MyLog($"无起始按钮, 以该最末按钮为准");
                        MoveTo(button);
                    }
                }
            }
            else
            {
                MyUtils.MyLog($"没找到最末按钮");
            }
        }
        public static bool FindUIBaseLast(Transform transform, out UIBase uiBase, out bool isSelf, out int index)
        {
            uiBase = default;
            isSelf = false;
            index = -1;
            if (!CheckObjShow(transform.gameObject)) return false;
            for (int i = transform.childCount - 1; i >= 0; i++)
            {
                if (FindUIBaseLast(transform.GetChild(i), out uiBase, out var child, out var childIndex))
                {
                    isSelf = false;
                    index = i;
                    return true;
                }
            }
            var ui = transform.GetComponent<UIBase>();
            if (ui != null)
            {
                uiBase = ui;
                isSelf = true;
                index = -2;
                return true;
            }
            return false;
        }
        public static bool FindButtonFirst(Transform transform, out Selectable button, out bool isSelf, out int index,
            bool checkSelf = true, int startIndex = 0, int endIndex = -1, bool loop = false)
        {
            button = default;
            isSelf = false;
            index = -1;
            if (!CheckObjShow(transform.gameObject)) {
                MyUtils.MyLog($"FindButtonFirst {transform.name} 不显示，跳过");
                return false;
            }
            if (checkSelf)
            {
                var btn = transform.GetComponent<Selectable>();
                if (CheckButton(btn))
                {
                    MyUtils.MyLog($"FindButtonFirst 找到 button {btn.name}");
                    button = btn;
                    isSelf = true;
                    index = -2;
                    return true;
                }
            }
            if (endIndex == -1) endIndex = transform.childCount;
            if (loop && startIndex >= transform.childCount) startIndex = 0;

            MyUtils.MyLog($"FindButtonFirst {transform.name} 有{transform.childCount}个， 查找范围[{startIndex}, {endIndex})");
            for (int i = startIndex; i < endIndex; i++)
            {
                if (FindButtonFirst(transform.GetChild(i), out button, out var child, out var childIndex, checkSelf = true, loop: loop))
                {
                    isSelf = false;
                    index = i;
                    return true;
                }
            }
            return false;
        }
        public static bool FindButtonLast(Transform transform, out Selectable button, out bool isSelf, out int index,
            bool checkSelf = true, int rStartIndex = -2, int rEndIndex = 0, bool loop = false)
        {
            button = default;
            isSelf = false;
            index = -1;
            if (loop && rStartIndex == -1) rStartIndex = transform.childCount - 1;
            if (rStartIndex == -1) return false;
            if (!CheckObjShow(transform.gameObject)) return false;
            if (rStartIndex == -2) rStartIndex = transform.childCount - 1;
            MyUtils.MyLog($"FindButtonLast {transform.name} 有{transform.childCount}个， 查找范围[{rEndIndex}, {rStartIndex})");
            for (int i = rStartIndex; i >= rEndIndex; i--)
            {
                if (FindButtonLast(transform.GetChild(i), out button, out var child, out var childIndex, checkSelf = true, loop: loop))
                {
                    isSelf = false;
                    index = i;
                    return true;
                }
            }
            if (checkSelf)
            {
                var btn = transform.GetComponent<Selectable>();
                if (CheckButton(btn))
                {
                    MyUtils.MyLog($"FindButtonLast 找到 button {btn.name}");
                    button = btn;
                    isSelf = true;
                    index = -2;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 找到同级的第一个按钮
        /// </summary>
        public static bool GetButtonSibiFirst(Selectable buttonIn, out Selectable buttonOut, out int index)
        {
            buttonOut = buttonIn;
            index = -1;
            var parent = buttonIn.transform.parent;
            var endIdx = buttonIn.transform.GetSiblingIndex();
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (!CheckObjShow(child.gameObject))
                    continue;
                var btn = child.GetComponent<Selectable>();
                if (CheckButton(btn))
                {
                    buttonOut = btn;
                    index = i;
                    return true;
                }
            }

            return false;
        }
        public static bool CheckObjShow(GameObject obj)
        {
            var cond1 = obj.activeInHierarchy;
            var cond2 = CheckTransformScale(obj.transform);
            var cond3 = CheckTooHeight(obj.transform);
            var r = cond1 && cond2 && cond3;
            if (!r)
                MyUtils.MyLog($"CheckObjShow {obj.name} {cond1} {cond2} {cond3}");
            return r;
            //return obj.activeInHierarchy && CheckTransformScale(obj.transform) && CheckTooHeight(obj.transform);
        }
        /// <summary>
        /// 有些界面是通过直接y值设置到9000来“隐藏”的
        /// </summary>
        public static bool CheckTooHeight(Transform transform)
        {
            // 只对界面进行y值判断
            if (transform.GetComponent<UIBase>()) return transform.localPosition.y <= 5000; // 判断个5000应该够了
            else return true;
        }

        public static bool CheckTransformScale(Transform transform)
        {
            if (transform == topUI.transform) return true; // canvas 0.0069f 特殊处理
            if (transform.localScale.x > 0.1f && transform.localScale.y > 0.1f && transform.localScale.z > 0.1f)
                return true;
            return false;
        }
        public static bool CheckButton(Selectable btn)
        {
            if (btn != null && btn.gameObject.activeInHierarchy && btn.interactable)
            {
                return CheckTransformScale(btn.transform);
            }
            return false;
        }
        /// <summary>
        /// 将 tipstext 移动到该按钮
        /// </summary>
        /// <param name="button"></param>
        public static void MoveTo(Selectable button)
        {
            RelButton(); // 先释放当前的
            curButton = button;
            var bg = GetButtonBg(button);
            if (bg)
            {
                curButtonBg = bg;
                btnColor = bg.color;
                bg.color = Color.cyan;
            }
            else
            {
                curButtonBg = null;
            }
            TipsParent(button.transform);
            MyUtils.MyLog($"moveto {button.name}");
            TryScrollTo(button);
        }
        /// <summary>
        /// 判断按钮是否在 滚动列表里，如果是的话调用 
        /// </summary>
        public static void TryScrollTo(Selectable btn)
        {
            if (curButton.GetComponent<Refers>())
            {
                var p = btn.transform.parent?.parent?.parent;
                if (p && p.GetComponent<InfinityScroll>())
                {
                    if (int.TryParse(btn.name, out var index))
                    {
                        var indexTo = Math.Max(0, index - 1);
                        p.GetComponent<InfinityScroll>().ScrollTo(indexTo);
                    }
                }
            }
        }



        public static Image GetButtonBg(Selectable button)
        {
            if (button.GetComponent<Image>())
            {
                return button.GetComponent<Image>();
            }
            else
            {
                var bg = button.transform.Find("Normal");
                if (bg)
                {
                    if (bg.GetComponent<Image>())
                    {
                        return bg.GetComponent<Image>();
                    }
                    else
                    {
                        return bg.GetComponentInChildren<Image>();
                    }
                }
                else
                {
                    return button.GetComponentInChildren<Image>();
                }
            }
            return null;
        }

        public static void CheckClickKey()
        {
            if (!isEnter) return;
            if (MyUtils.isAnyKey(clickKey, out var keyCode) && CheckButton(curButton))
            {
                MyUtils.MyLog($"按了 {keyCode}");
                ClickButton();
            }
        }
        public static void ClickButton()
        {
            var button = curButton;
            ToExit(rel: false);
            var btn = button.GetComponent<Button>();
            if (btn != null) btn.onClick?.Invoke();
            else
            {
                var t = button.GetComponent<Toggle>();
                if (t)
                {
                    var ct = button.GetComponent<CToggle>();
                    if (ct)
                    {
                        var cToggleGroup = Traverse.Create(ct).Field("_toggleGroup").GetValue<CToggleGroup>();
                        if (cToggleGroup)
                        {
                            cToggleGroup.Set(ct.Key, !ct.isOn);
                        }
                        else
                        {
                            t.isOn = !t.isOn;
                        }
                    }
                    else
                    {
                        t.isOn = !t.isOn;
                        //t.OnSubmit(null);
                    }
                }
            }
        }
        public static void CheckMoveKey()
        {
            if (!isEnter) return;
            var toNext = Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.RightArrow);
            toNext = toNext || Input.GetKeyDown(KeyCode.JoystickButton3); // x

            var toLast = Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.LeftArrow);
            toLast = toLast || Input.GetKeyDown(KeyCode.JoystickButton2);//y

            var toJump = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
                || Input.GetKey(KeyCode.JoystickButton6);
            var toJumpUI = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)
                || Input.GetKey(KeyCode.JoystickButton7);
            var toUp = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)
                || Input.GetKey(KeyCode.JoystickButton5);
            if (toJump)
            {
                if (toNext)
                {
                    MoveJumpNext();
                }
                else if (toLast)
                {
                    MoveJumpLast();
                }
            }
            else if (toJumpUI)
            {
                if (toNext)
                {
                    MoveUiNext();
                }
                else if (toLast)
                {
                    MoveUiLast();
                }
            }
            else if(toUp)
            {
                if (toNext)
                {
                    MoveDown();
                }
                else if (toLast)
                {
                    MoveUp();
                }
            }
            else
            {
                if (toNext)
                {
                    MoveNext();
                }
                else if(toLast)
                {
                    MoveLast();
                }
            }
            //MyUtils.MyLog($"按了 move");
        }

        public static Selectable IsObjFindBtn(Transform transform)
        {
            if (!CheckObjShow(transform.gameObject))
                return null;
            var btn = transform.GetComponent<Selectable>();
            if (CheckButton(btn))
            {
                return btn;
            }
            return null;
        }
        /// <summary>
        /// 只在父级下查找按钮
        /// </summary>
        public static Selectable FindBtnNext(Transform button)
        {
            var index = button.transform.GetSiblingIndex();
            var parent = button.transform.parent;
            if (FindButtonFirst(parent, out var button1, out var _, out var _, checkSelf: false, startIndex: index + 1))
            {
                return button1;
            }
            return null;
        }
        public static Selectable FindBtnLast(Transform button)
        {
            var index = button.transform.GetSiblingIndex();
            var parent = button.transform.parent;
            if (FindButtonLast(parent, out var button1, out var isSelf, out var _, checkSelf: false, rStartIndex: index - 1))
            {
                return button1;
            }
            return null;
        }
        public static Selectable FindBtnUp(Transform button)
        {
            var index = button.transform.GetSiblingIndex();
            var parent = button.transform.parent;
            var cur = parent;
            while(cur && cur != topUI.transform)
            {
                if (cur.GetComponent<UIBase>()) return null;
                var btn = IsObjFindBtn(cur);
                if (btn) return btn;
                cur = cur.parent;
            }
            return null;
        }
        public static Selectable FindBtnDown(Transform button)
        {
            if (FindButtonFirst(button.transform, out var button1, out var isSelf, out var index1, checkSelf: false))
            {
                return button1;
            }
            return null;
        }

        public static Selectable FindBtnJumpNext(Transform button)
        {
            var parent = button.transform.parent; // 不判断父节点
            MyUtils.MyLog($"FindBtnJumpNext 跳过{parent.name}  从 {parent.parent.name}查找");
            if (parent == null) return null;
            return JumpNextUp(parent);
        }
        public static Selectable JumpNextUp(Transform transform)
        {
            if (!transform) return null;
            if(transform == topUI.transform) { MyUtils.MyLog("JumpNextUp 已到 topui 节点，结束"); return null; }
            if (transform.GetComponent<UIBase>()) { MyUtils.MyLog("JumpNextUp 已到ui节点，结束"); return null; }
            var index = transform.GetSiblingIndex();
            var parent = transform.parent;
            MyUtils.MyLog($"JumpNextUp 开始查找{parent.name}下 {index} 后的节点");
            if (FindButtonFirst(parent, out var button1, out var _, out var _, checkSelf: false, startIndex: index + 1))
            {
                MyUtils.MyLog($"JumpNextUp 找到{button1.name}");
                return button1;
            }
            return JumpNextUp(parent);
        }

        public static Selectable FindBtnJumpLast(Transform button)
        {
            var parent = button.transform.parent; // 不判断父节点
            if (parent == null) return null;
            MyUtils.MyLog($"FindBtnJumpLast 跳过{parent.name} 从 {parent.parent.name}查找");
            return JumpLastUp(parent);
        }
        public static Selectable JumpLastUp(Transform transform)
        {
            if (!transform) return null;
            if(transform == topUI.transform) { MyUtils.MyLog("JumpLastUp 已到 topui 节点，结束"); return null; }
            if (transform.GetComponent<UIBase>()) { MyUtils.MyLog("JumpLastUp 已到ui节点，结束"); return null; }
            var index = transform.GetSiblingIndex();
            var parent = transform.parent;
            MyUtils.MyLog($"JumpLastUp 开始查找{parent.name}下 {index} 前的节点");
            if (FindButtonLast(parent, out var button1, out var _, out var _, checkSelf: false, rStartIndex: index - 1))
            {
                MyUtils.MyLog($"JumpLastUp 找到{button1.name}");
                return button1;
            }
            return JumpLastUp(parent);
        }

        public static Selectable FindBtnNextUI(Transform button)
        {
            Selectable button1 = null;
            var oldUi = button.GetComponentInParent<UIBase>();
            if(oldUi)
            {
                button1 = FindBtnNext(oldUi.transform);
                if(!button1)
                {
                    button1 = FindBtnJumpNext(oldUi.transform);
                }
            }
            return button1;
        }
        public static Selectable FindBtnLastUI(Transform button)
        {
            Selectable button1 = null;
            var oldUi = button.GetComponentInParent<UIBase>();
            if (oldUi)
            {
                button1 = FindBtnLast(oldUi.transform);
                if (!button1)
                {
                    button1 = FindBtnJumpLast(oldUi.transform);
                }
            }
            return button1;
        }

        public static void MoveNext()
        {
            var btn = FindBtnNext(curButton.transform);
            if (btn) { MoveTo(btn); } else { MyUtils.MyLog($"跳转 Next 无效"); }
        }
        public static void MoveLast()
        {
            var btn = FindBtnLast(curButton.transform);
            if (btn) { MoveTo(btn); } else { MyUtils.MyLog($"跳转 Last 无效"); }
        }
        /// <summary>
        /// 跨级跳转
        /// </summary>
        public static void MoveJumpNext()
        {
            var btn = FindBtnJumpNext(curButton.transform);
            if (btn) { MoveTo(btn); } else { MyUtils.MyLog($"跳转 JumpNext 无效"); }
        }
        public static void MoveJumpLast()
        {
            var btn = FindBtnJumpLast(curButton.transform);
            if (btn) { MoveTo(btn); } else { MyUtils.MyLog($"跳转 JumpLast 无效"); }
        }

        public static void MoveUp()
        {
            var btn = FindBtnUp(curButton.transform);
            if (btn) { MoveTo(btn); } else { MyUtils.MyLog($"跳转 Up 无效"); }
        }
        public static void MoveDown()
        {
            var btn = FindBtnDown(curButton.transform);
            if (btn) { MoveTo(btn); } else { MyUtils.MyLog($"跳转 Down 无效"); }
        }
        public static void MoveUiNext()
        {
            var btn = FindBtnNextUI(curButton.transform);
            if (btn) { MoveTo(btn); } else { MyUtils.MyLog($"跳转 UiNext 无效"); }
        }
        public static void MoveUiLast()
        {
            var btn = FindBtnLastUI(curButton.transform);
            if (btn) { MoveTo(btn); } else { MyUtils.MyLog($"跳转 UiLast 无效"); }
        }
    }
}
