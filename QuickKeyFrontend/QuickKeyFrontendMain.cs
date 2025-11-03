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
        public static KeyCode Read = KeyCode.Z;
        public static KeyCode LoopNeigong = KeyCode.X;
        public static KeyCode TaiwuLifeSkill = KeyCode.Q;
        public static KeyCode TaiwuCombotSkill = KeyCode.E;

        public static KeyCode Map = KeyCode.M;
        public static KeyCode Warehouse = KeyCode.Tab;
        //public static KeyCode TaiwuVillage = KeyCode.T;
        //public static KeyCode SettleInfo = KeyCode.N;

        //public static KeyCode DialogYes = KeyCode.Space;
        //public static KeyCode EventWindowSpace = KeyCode.Space;

        public static KeyCode MainMenu = KeyCode.F10;
        public static KeyCode SystemOption = KeyCode.F11;
    }

    public static class MyKeyList
    {
        public static List<KeyCode> EventWindowConfirm = new List<KeyCode> {
            KeyCode.Space, KeyCode.Return, KeyCode.KeypadEnter
        };

        public static List<KeyCode> EventWindowKeys = new List<KeyCode> {
            KeyCode.Keypad1,KeyCode.Keypad2, KeyCode.Keypad3,KeyCode.Keypad4,KeyCode.Keypad5,KeyCode.Keypad6,
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
            MyLog($"setting {quickKeyEnable}");
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
        public static void TaiwuCharacter()
        {
            UI_CharacterMenu charMenu = (UI_CharacterMenu)UIElement.CharacterMenu.UiBase;
            if (UIElement.CharacterMenu.Exist && charMenu.IsTaiwuTeam)  // 太吾的人物界面就关闭，否则打开太吾的
            {
                UIElement.CharacterMenu.UiBase.QuickHide();
                return;
            }
            if (!UIElement.CharacterMenu.Exist)
                OpenTaiwuCharacter();
            else if (!charMenu.IsTaiwuTeam)
                RefreshTaiwuCharacter();
            OpenToTaiwuPage(0); // 第一页
        }
        public static void OpenTaiwuCharacter()
        {
            if (UIElement.CharacterMenu.Exist) return;

            var taiwuCharId = SingletonObject.getInstance<BasicGameData>().TaiwuCharId;
            if (UIElement.Combat.Exist)    // 战斗时的界面
            {
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
        public static void RefreshTaiwuCharacter()
        {
            OpenTaiwuCharacter();
            //UI_CharacterMenu charMenu = (UI_CharacterMenu)UIElement.CharacterMenu.UiBase;
            //Traverse.Create(charMenu).Method("CheckTabPage").GetValue<bool>();
            //Traverse.Create(charMenu).Method("RefreshSubPage").GetValue();
        }

        public static int GetIndexOfCharMenu(CharacterMenuSubPageElement element)
        {
            return element.UiBaseAs<UI_CharacterMenuSubPageBase>().Key;
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
            if (Input.GetKeyDown(MyKey.Read)) Read();
            if (Input.GetKeyDown(MyKey.LoopNeigong)) LoopNeigong();
            if (Input.GetKeyDown(MyKey.Map)) Map();
            if (Input.GetKeyDown(MyKey.Warehouse)) Warehouse();
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
        #endregion
    }
}
