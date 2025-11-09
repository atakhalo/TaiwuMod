using CharacterDataMonitor;
using Config;
using FrameWork;
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
using static GameData.Domains.Item.ItemOperationType;


namespace QuickKey
{
    public static class MyKey
    {
        //public static KeyCode TalkWindowNext = KeyCode.C;
        //public static KeyCode TalkWindowPrev = KeyCode.Z;
        //public static KeyCode CharWindowNext = KeyCode.C;
        //public static KeyCode CharWindowPrev = KeyCode.Z;

        //public static KeyCode SkipCollect = KeyCode.P;

        //public static KeyCode CharMenuNextPeople = KeyCode.S;
        //public static KeyCode CharMenuPrevPeople = KeyCode.W;
        //public static KeyCode CharMenuNextPage = KeyCode.D;
        //public static KeyCode CharMenuPrevPage = KeyCode.A;
        //public static KeyCode CharMenuNextSubPage = KeyCode.E;
        //public static KeyCode CharMenuPrevSubPage = KeyCode.Q;

        public static KeyCode TaiwuCharMenu = KeyCode.C;
        public static KeyCode TaiwuEquip = KeyCode.V;
        public static KeyCode TaiwuBag = KeyCode.B;
        public static KeyCode TaiwuLifeSkill = KeyCode.Q;
        public static KeyCode TaiwuCombotSkill = KeyCode.E;
        public static KeyCode Read = KeyCode.Z;
        public static KeyCode LoopNeigong = KeyCode.X;

        public static KeyCode Warehouse = KeyCode.Tab;
        public static KeyCode TaiwuVillage = KeyCode.T;
        public static KeyCode Map = KeyCode.M;
        //public static KeyCode SettleInfo = KeyCode.N;

        //public static KeyCode DialogYes = KeyCode.Space;
        //public static KeyCode EventWindowSpace = KeyCode.Space;

        //public static KeyCode MainMenu = KeyCode.F10;
        //public static KeyCode SystemOption = KeyCode.F11;
    }

    public static class MyKeyList
    {
        public static List<KeyCode> EventWindowConfirm = new List<KeyCode> {
            KeyCode.Space, KeyCode.Return, KeyCode.KeypadEnter
        };

        public static List<KeyCode> EventWindowKeys = new List<KeyCode> {
            KeyCode.Keypad1,KeyCode.Keypad2, KeyCode.Keypad3,
            KeyCode.Keypad4,KeyCode.Keypad5,KeyCode.Keypad6,
            KeyCode.Keypad7,KeyCode.Keypad8,KeyCode.Keypad9,
        };
    }


    public static class MyToggle
    {
        public static bool ToggleBlock = false;
        public static bool ToggleChar = false;
        public static bool ToggleTaiwuChar = false;
        public static bool ToggleTaiwuUtils = false;
        public static bool ToggleOther = false;
    }

    [PluginConfig(pluginName: "QuickKey", creatorId: "atakhalo", pluginVersion: "0.1.0.1")]
    public class QuickKeyFrontendPlugin : TaiwuRemakePlugin
    {
        private Harmony harmony;

        public static bool quickKeyEnable = true; // 开关

        public static bool quickConfirm = true; // 确认开关
        public static bool quickPad = true; // 小键盘开关


        public static void MyLog(string log)
        {
            Debug.Log($"[QuickKey] {log}");
        }

        public override void Initialize()
        {
            MyLog($"Initialize");
            harmony = Harmony.CreateAndPatchAll(typeof(QuickKeyFrontendPlugin));
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
            ModManager.GetSetting(ModIdStr, "quickKeyEnable", ref quickKeyEnable);

            ModManager.GetSetting(ModIdStr, "quickConfirm", ref quickConfirm);
            ModManager.GetSetting(ModIdStr, "quickPad", ref quickPad);

            //ModManager.GetSetting(ModIdStr, "quickKeyEnable", ref quickKeyEnable);
            //ModManager.GetSetting(ModIdStr, "quickKeyEnable", ref quickKeyEnable);
            //ModManager.GetSetting(ModIdStr, "quickKeyEnable", ref quickKeyEnable);

            MyKey.TaiwuCharMenu = TryGetKeyCode("TaiwuCharMenu");
            MyKey.TaiwuEquip = TryGetKeyCode("TaiwuEquip");
            MyKey.TaiwuBag = TryGetKeyCode("TaiwuBag");
            MyKey.Read = TryGetKeyCode("Read");
            MyKey.LoopNeigong = TryGetKeyCode("LoopNeigong");
            MyKey.TaiwuLifeSkill = TryGetKeyCode("TaiwuLifeSkill");
            MyKey.TaiwuCombotSkill = TryGetKeyCode("TaiwuCombotSkill");
            MyKey.Map = TryGetKeyCode("Map");
            MyKey.Warehouse = TryGetKeyCode("Warehouse");
            MyKey.TaiwuVillage = TryGetKeyCode("TaiwuVillage");

            MyLog($"setting {quickKeyEnable}");
        }

        public KeyCode TryGetKeyCode(string key)
        {
            string temp = "";
            KeyCode r = KeyCode.None;
            ModManager.GetSetting(ModIdStr, key, ref temp);
            if(Enum.TryParse<KeyCode>(temp, true, out r))
            {
                MyLog($"TryGetKeyCode {key} -> {r}");
                return r;
            }
            //if(KeyCode.IsDefined(typeof(KeyCode), temp))
            //{
            //    return (KeyCode)temp;
            //}
            MyLog($"TryGetKeyCode {key} -> {KeyCode.None}");
            return KeyCode.None;
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
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);

                // 构建缩进字符串
                var indent = new string('\t', depth);
                var specialMark = (sp == child) ? ">>" : "";

                // 构建组件信息
                var monos = child.GetComponents<MonoBehaviour>();
                var monoNames = string.Join(",", monos.Select(m => m.GetType().Name));

                // 构建完整日志信息
                var str = $"{indent}{specialMark}{child.gameObject.name} ({monoNames})";

                // 先打印当前节点，再递归子节点
                MyLog(str);
                ShowMonoHelper(child, depth + 1, sp);
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(Game), "Update")]
        public static void GameUpdate(Game __instance)
        {
            if (Game.Instance.GetCurrentGameStateName() != EGameState.InGame)
                return;
            if (!quickKeyEnable) return;
            if (!Input.anyKeyDown) return;

            CheckEventWindow();
            CheckTaiwu();
            CheckUtil();
        }

        public static void CheckEventWindow()
        {
            if (UIElement.EventWindow.Exist)
            {
                var ui = UIElement.EventWindow.UiBase as UI_EventWindow;
                var Data = Traverse.Create(ui).Property("Data").GetValue<TaiwuEventDisplayData>();
                if(quickConfirm)
                {
                    for (int i = 0; i < MyKeyList.EventWindowConfirm.Count; i++)
                    {
                        if (Input.GetKeyDown(MyKeyList.EventWindowConfirm[i]))
                        {
                            if (Data.EscOptionIndex >= 0) { }// 空格被 退出 占用了
                            else
                            {
                                EventWindowClickKey(ui, Data, 0);
                            }
                        }
                    }
                }
                if(quickPad)
                {
                    for (int i = 0; i < MyKeyList.EventWindowKeys.Count; i++)
                    {
                        KeyCode key = MyKeyList.EventWindowKeys[i];
                        if (Input.GetKeyDown(key))
                        {
                            EventWindowClickKey(ui, Data, i);
                        }
                    }
                }
            }
        }

        public static void EventWindowClickKey(UI_EventWindow ui, TaiwuEventDisplayData Data, int index)
        {
            if (Data.EventOptionInfos.Count > index)
            {
                var key = Data.EventOptionInfos[index].OptionKey;
                Traverse.Create(ui).Method("SelectOptionByOptionKey", key).GetValue<CToggleGroup>();
            }
        }
        #region 人物界面
        public static void CheckTaiwu()
        {
            if (Input.GetKeyDown(MyKey.TaiwuCharMenu)) TaiwuCharacter();
            if (Input.GetKeyDown(MyKey.TaiwuBag)) Bag();
            if (Input.GetKeyDown(MyKey.TaiwuCombotSkill)) CombatSkill();
            if (Input.GetKeyDown(MyKey.TaiwuLifeSkill)) LifeSkill();
            if (Input.GetKeyDown(MyKey.TaiwuEquip)) Equip();
        }

        public static void OpenTaiwuCharacter()
        {
            if (UIElement.CharacterMenu.Exist) return;

            if(UIElement.EventWindow.Exist)
            {
                var ui = (UIElement.EventWindow.UiBase as UI_EventWindow);
                Traverse.Create(ui).Method("OnViewLostFocus", UIElement.CharacterMenu).GetValue();
            }

            var taiwuCharId = SingletonObject.getInstance<BasicGameData>().TaiwuCharId;
            if (UIElement.CombatBegin.Exist)    // 战斗时的界面
            {
                var ui = (UIElement.CombatBegin.UiBase as UI_CombatBegin);
                Traverse.Create(ui).Method("ShowCharMenu", true, taiwuCharId).GetValue();
            }
            else if (UIElement.Combat.Exist)    // 战斗时的界面
            {
                var ui = (UIElement.Combat.UiBase as UI_Combat); // 触发下暂停
                //if(SingletonObject.getInstance<CombatModel>().TimeScale != 0f) // 不是暂停；又感觉还是判断ui稳点
                var t = Traverse.Create(ui).Field("_pauseToggle").GetValue<CToggle>();
                if (!t.isOn) { t.isOn = true; }
                // 可看 CombatAvatar ShowCharMenu
                CombatUtils.ShowCharMenu(taiwuCharId);
            }
            else
            {
                // 可看 ui_bottom OpenTeammateCharacterMenu
                ArgumentBox argBox = EasyPool.Get<ArgumentBox>();
                argBox.Set("CharacterId", taiwuCharId);
                argBox.Set("IsTaiwuTeam", true);
                argBox.Set("CanOperate", true);
                UIElement.CharacterMenu.SetOnInitArgs(argBox);
                UIManager.Instance.ShowUI(UIElement.CharacterMenu);
            }
        }

        public static int GetIndexOfCharMenu(CharacterMenuSubPageElement element)
        {
            return element.UiBaseAs<UI_CharacterMenuSubPageBase>().Key;
        }
        public static void TaiwuCharacter()
        {
            var index = GetIndexOfCharMenu(UIElement.CharacterMenuInfo);
            OpenTaiwuCharacter();
            OpenToTaiwuPage(index); // 第一页
        }
        public static void Bag()
        {
            var index = GetIndexOfCharMenu(UIElement.CharacterMenuItems);
            OpenTaiwuCharacter();
            OpenToTaiwuPage(index);
        }
        public static void CombatSkill()
        {
            var index = GetIndexOfCharMenu(UIElement.CharacterMenuCombatSkill);
            OpenTaiwuCharacter();
            OpenToTaiwuPage(index);
        }
        public static void LifeSkill()
        {
            var index = GetIndexOfCharMenu(UIElement.CharacterMenuLifeSkill);
            OpenTaiwuCharacter();
            OpenToTaiwuPage(index);
        }
        public static void Equip()
        {
            var index = GetIndexOfCharMenu(UIElement.CharacterMenuEquip);
            OpenTaiwuCharacter();
            OpenToTaiwuPage(index);
        }
        public static void OpenToTaiwuPage(int index, int subIndex=-1)
        {
            // 看 OpenTargetPage
            UI_CharacterMenu charMenu = (UI_CharacterMenu)UIElement.CharacterMenu.UiBase;
            var _tabTogGroup = Traverse.Create(charMenu).Field("_tabTogGroup").GetValue<CToggleGroup>();
            _tabTogGroup.Set(index, true, false);
            if(subIndex != -1)
            {
                charMenu.SetCurPageSubpage(subIndex);
            }
        }
        #endregion
        #region 下方功能界面
        public static void CheckUtil()
        {
            if (!UIElement.Bottom.Exist) return;
            if (Input.GetKeyDown(MyKey.Read)) Read();
            if (Input.GetKeyDown(MyKey.LoopNeigong)) LoopNeigong();
            if (Input.GetKeyDown(MyKey.Map)) Map();
            if (Input.GetKeyDown(MyKey.Warehouse)) Warehouse();
            if (Input.GetKeyDown(MyKey.TaiwuVillage)) TaiwuVillage();
        }
        public static void LoopNeigong()
        {
            if (UIElement.Looping.Exist)
            {
                UIElement.Looping.UiBase.QuickHide();
                return;
            }
            UIManager.Instance.ShowUI(UIElement.Looping);
        }
        public static void Read()
        {
            if (UIElement.Reading.Exist)
            {
                UIElement.Reading.UiBase.QuickHide();
                return;
            }
            // 看 ui_bottom OnReadingClicked
            if (UIElement.WorldMap.Exist)
            {
                UI_Worldmap worldMap = UIElement.WorldMap.UiBaseAs<UI_Worldmap>();
                if (worldMap != null && (worldMap.IsMoving || worldMap.IsDoingMove))
                {
                    return;
                }
            }
            UIElement.Reading.SetOnInitArgs(EasyPool.Get<ArgumentBox>().Set("SlotIndex", 0));
            UIManager.Instance.ShowUI(UIElement.Reading);
        }

        public static void Map()
        {
            if (UIElement.StatePartWorldMap.Exist)
            {
                UIElement.StatePartWorldMap.UiBase.QuickHide();
                return;
            }
            WorldMapModel mapModel = SingletonObject.getInstance<WorldMapModel>();
            if (mapModel.TaiwuMoveState == WorldMapModel.MoveState.Idle)
            {
                UIManager.Instance.ShowUI(UIElement.StatePartWorldMap);
            }
        }
        public static void Warehouse()
        {
            if (UIElement.Warehouse.Exist)
            {
                UIElement.Warehouse.UiBase.QuickHide();
                return;
            }
            UIManager.Instance.ShowUI(UIElement.Warehouse);
        }
        public static void TaiwuVillage()
        {
            if(UIElement.ResourceBar.Exist)
            {
                // 具体逻辑可看 UI_ResourceBar OnClick Building
                var ui = (UIElement.ResourceBar.UiBase as UI_ResourceBar);
                var b = ui.CGet<Refers>("TaiwuVillageInfo").CGet<CButton>("Building");
                if(b.interactable) b.onClick?.Invoke();
            }
        }

        #endregion
    }
}
