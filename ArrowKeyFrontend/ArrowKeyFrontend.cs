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
        public static List<KeyCode> clickKey = new List<KeyCode>()
        { 
            //KeyCode.Return, KeyCode.KeypadEnter, KeyCode.Space,
            KeyCode.KeypadEnter, 
        };

        public static KeyCode runKey = KeyCode.Return;

        public static bool isEnter = false;
        public static bool haveInit = false; // 开关
        public static GameObject tipsObj; // 当前挂着的按钮

        public static GameObject topUI; // 向上寻找到这个就不用找了

        public static List<GameObject> pagePath;
        public static GameObject lastPage;
        public static Button curButton; // 当前挂着的按钮
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
        public static void ShowUI(UIManager __instance) { if (isEnter) { ToExit(); } }
        [HarmonyPrefix, HarmonyPatch(typeof(UIManager), "HideUI")]
        public static void HideUI(UIManager __instance) { if(isEnter) { ToExit(); } }
        [HarmonyPrefix, HarmonyPatch(typeof(UIManager), "ChangeToUI")]
        public static void ChangeToUI(UIManager __instance) { if (isEnter) { ToExit(); } }
        [HarmonyPrefix, HarmonyPatch(typeof(UIManager), "StackToUI")]
        public static void StackToUI(UIManager __instance) { if (isEnter) { ToExit(); } }
        [HarmonyPrefix, HarmonyPatch(typeof(UIManager), "StackBack")]
        public static void StackBack(UIManager __instance) { if (isEnter) { ToExit(); } }
        #endregion

        [HarmonyPostfix, HarmonyPatch(typeof(Game), "Update")]
        public static void GameUpdate(Game __instance)
        {
            if (!arrowKey) return;
            if (!Input.anyKeyDown) return;
            CheckEnter();
            CheckClickKey();
            CheckMoveKey();
        }

        public static void CheckEnter()
        {
            if(Input.GetKeyDown(enterKey))
            {
                if (!isEnter)
                {
                    ToEnter();
                }
                else
                {
                    ToExit();
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
            TryFindButton();
        }
        public static void ToExit()
        {
            isEnter = false;
            MyUtils.MyLog($"退出导航模式");
            OnExit();
        }
        public static void OnExit()
        {
            RelButton();
            CycleTipsText();
        }
        public static void RelButton()
        {
            if (curButtonBg)
            {
                curButtonBg.color = btnColor;
                curButtonBg = null;
            }
            if (curButton)
            {
                curButton = null;
            }
        }

        public static void Init()
        {
            if(!tipsObj) // 某些时候可能被消耗了
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
            tipsObj = new GameObject("ArrowKeyTips", new[] {typeof(RectTransform),});
            tipsObj.transform.SetParent(parent);
            var rect = tipsObj.GetComponent<RectTransform>();
            // 居中
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.SetPivot(new Vector2(0.5f, 0.5f));
            rect.sizeDelta = new Vector2(30,30);
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
        public static void TryFindButton()
        {
            if(FindButtonLast(topUI.transform, out var button, out var isSelf, out var index))
            {
                MyUtils.MyLog($"找到最末按钮 {button.name}");
                MyUtils.ShowMonoToParent(button.transform);
                var buttonUI = button.GetComponentInParent<UIBase>();
                if (buttonUI != null)
                {
                    MyUtils.MyLog($"找到最末按钮所属 ui {buttonUI}");
                    MyUtils.ShowMonoOne(buttonUI.transform);
                    if(FindButtonFirst(buttonUI.transform, out var buttonOut, out var isUISelf, out var firstIndex))
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
        public static bool FindButtonFirst(Transform transform, out Button button, out bool isSelf, out int index, 
            bool checkSelf=true, int startIndex=0, int endIndex = -1)
        {
            button = default;
            isSelf = false;
            index = -1;
            if (!CheckObjShow(transform.gameObject)) return false;
            if(checkSelf)
            {
                var btn = transform.GetComponent<Button>();
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

            MyUtils.MyLog($"FindButtonFirst {transform.name} 有{transform.childCount}个， 查找范围[{startIndex}, {endIndex})");
            for (int i = startIndex; i < endIndex; i++)
            {
                if (FindButtonFirst(transform.GetChild(i), out button, out var child, out var childIndex, checkSelf=true))
                {
                    isSelf = false;
                    index = i;
                    return true;
                }
            }
            return false;
        }
        public static bool FindButtonLast(Transform transform, out Button button, out bool isSelf, out int index,
            bool checkSelf = true, int rStartIndex = -2, int rEndIndex = 0)
        {
            button = default;
            isSelf = false;
            index = -1;
            if (rStartIndex == -1) return false;
            if(!CheckObjShow(transform.gameObject)) return false;
            if(rStartIndex == -2) rStartIndex = transform.childCount - 1;
            MyUtils.MyLog($"FindButtonLast {transform.name} 有{transform.childCount}个， 查找范围[{rEndIndex}, {rStartIndex})");
            for (int i = rStartIndex; i >= rEndIndex; i--)
            {
                if(FindButtonLast(transform.GetChild(i), out button, out var child, out var childIndex, checkSelf=true))
                {
                    isSelf = false;
                    index = i;
                    return true;
                }
            }
            if(checkSelf)
            {
                var btn = transform.GetComponent<Button>();
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
        public static bool GetButtonSibiFirst(Button buttonIn, out Button buttonOut, out int index)
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
                var btn = child.GetComponent<Button>();
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
            return obj.activeInHierarchy && CheckTransformScale(obj.transform);
        }
        public static bool CheckTransformScale(Transform transform)
        {
            if (transform == topUI.transform) return true; // canvas 0.0069f 特殊处理
            if (transform.localScale.x > 0.1f && transform.localScale.y > 0.1f && transform.localScale.z > 0.1f)
                return true;
            return false;
        }
        public static bool CheckButton(Button btn)
        {
            if(btn != null && btn.gameObject.activeInHierarchy && btn.interactable)
            {
                return CheckTransformScale(btn.transform);
            }
            return false;
        }
        /// <summary>
        /// 将 tipstext 移动到该按钮
        /// </summary>
        /// <param name="button"></param>
        public static void MoveTo(Button button)
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
        }
        public static Image GetButtonBg(Button button)
        {
            if (button.GetComponent<Image>())
            {
                return button.GetComponent<Image>();
            }
            else
            {
                var bg = button.transform.Find("Normal");
                if(bg)
                {
                    if(bg.GetComponent<Image>())
                    {
                        return bg.GetComponent<Image>();
                    }
                    else
                    {
                        return bg.GetComponentInChildren<Image>();
                    }
                }
            }
            return null;
        }

        public static void CheckClickKey()
        {
            if (!isEnter) return;
            if(MyUtils.isAnyKey(clickKey, out var keyCode) && CheckButton(curButton))
            {
                MyUtils.MyLog($"按了 {keyCode}");
                ClickButton();
            }
        }
        public static void ClickButton()
        {
            var button = curButton;
            ToExit();
            button.onClick?.Invoke();
        }
        public static void CheckMoveKey()
        {
            if (!isEnter) return;
            if(Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
            {
                if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.RightArrow))
                {
                    MoveJumpNext();
                }
                else if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.LeftArrow))
                {
                    MoveJumpLast();
                }
            }
            else if(Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))
            {
                if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.RightArrow))
                {
                    MoveDown();
                }
                else if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.LeftArrow))
                {
                    MoveUp();
                }
            }
            else
            {
                if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.RightArrow))
                {
                    MoveNext();
                }
                else if(Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.LeftArrow))
                {
                    MoveLast();
                }
            }
            //MyUtils.MyLog($"按了 move");
        }

        public static Button IsObjFindBtn(Transform transform)
        {
            if (!CheckObjShow(transform.gameObject))
                return null;
            var btn = transform.GetComponent<Button>();
            if (CheckButton(btn))
            {
                return btn;
            }
            return null;
        }
        /// <summary>
        /// 只在父级下查找按钮
        /// </summary>
        public static Button FindBtnNext(Button button)
        {
            var index = button.transform.GetSiblingIndex();
            var parent = button.transform.parent;
            if (FindButtonFirst(parent, out var button1, out var _, out var _, checkSelf: false, startIndex: index + 1))
            {
                return button1;
            }
            return null;
        }
        public static Button FindBtnLast(Button button)
        {
            var index = button.transform.GetSiblingIndex();
            var parent = button.transform.parent;
            if (FindButtonLast(parent, out var button1, out var isSelf, out var _, checkSelf: false, rStartIndex: index - 1))
            {
                return button1;
            }
            return null;
        }
        public static Button FindBtnUp(Button button)
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
        public static Button FindBtnDown(Button button)
        {
            if (FindButtonFirst(button.transform, out var button1, out var isSelf, out var index1, checkSelf: false))
            {
                return button1;
            }
            return null;
        }

        public static Button FindBtnJumpNext(Button button)
        {
            var parent = button.transform.parent; // 不判断父节点
            MyUtils.MyLog($"FindBtnJumpNext 跳过{parent.name}  从 {parent.parent.name}查找");
            if (parent == null) return null;
            return JumpNextUp(parent);
        }
        public static Button JumpNextUp(Transform transform)
        {
            if (!transform) return null;
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

        public static Button FindBtnJumpLast(Button button)
        {
            var parent = button.transform.parent; // 不判断父节点
            if (parent == null) return null;
            MyUtils.MyLog($"FindBtnJumpLast 跳过{parent.name} 从 {parent.parent.name}查找");
            return JumpLastUp(parent);
        }
        public static Button JumpLastUp(Transform transform)
        {
            if (!transform) return null;
            if (transform.GetComponent<UIBase>()) { MyUtils.MyLog("JumpLastUp 已到ui节点，结束"); return null; }
            var index = transform.GetSiblingIndex();
            var parent = transform.parent;
            MyUtils.MyLog($"JumpNextUp 开始查找{parent.name}下 {index} 前的节点");
            if (FindButtonLast(parent, out var button1, out var _, out var _, checkSelf: false, rStartIndex: index - 1))
            {
                MyUtils.MyLog($"JumpLastUp 找到{button1.name}");
                return button1;
            }
            return JumpLastUp(parent);
        }
        public static void MoveNext()
        {
            var btn = FindBtnNext(curButton);
            if (btn) { MoveTo(btn); } else { MyUtils.MyLog($"跳转 Next 无效"); }
        }
        public static void MoveLast()
        {
            var btn = FindBtnLast(curButton);
            if (btn) { MoveTo(btn); } else { MyUtils.MyLog($"跳转 Last 无效"); }
        }
        /// <summary>
        /// 跨级跳转
        /// </summary>
        public static void MoveJumpNext()
        {
            var btn = FindBtnJumpNext(curButton);
            if (btn) { MoveTo(btn); } else { MyUtils.MyLog($"跳转 JumpNext 无效"); }
        }
        public static void MoveJumpLast()
        {
            var btn = FindBtnJumpLast(curButton);
            if (btn) { MoveTo(btn); } else { MyUtils.MyLog($"跳转 JumpLast 无效"); }
        }

        public static void MoveUp()
        {
            var btn = FindBtnUp(curButton);
            if (btn) { MoveTo(btn); } else { MyUtils.MyLog($"跳转 Up 无效"); }
        }
        public static void MoveDown()
        {
            var btn = FindBtnDown(curButton);
            if (btn) { MoveTo(btn); } else { MyUtils.MyLog($"跳转 Down 无效"); }
        }
    }
}
