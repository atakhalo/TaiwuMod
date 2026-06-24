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
using Game.Views.CharacterMenu;
using GameData.Domains.World;
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
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;


namespace LiaoRan
{
	

public class MyUtils
{
	public static string modName;
	public static void MyLog(string log)
	{
		Debug.Log($"[{modName}] {log}");
	}

	public static void DelayCall(Action action, float delay, bool real)
	{
		//Game.Instance.StartCoroutine(DelayCoroutine(TrySetNpcFace, 0, avatar, null, relatedData));
		GameApp.Instance.StartCoroutine(DelayCoroutine(action, delay, real));
	}

	private static IEnumerator DelayCoroutine(Action action, float delay, bool real)
	{
		if (real)
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
		if (canvas != null)
		{
			var depth = 0;
			var cur = transform;
			while (cur != canvas.transform)
			{
				ShowMonoOne(cur, depth, prefix: cur.GetSiblingIndex().ToString());
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

	public static void ShowMonoOne(Transform transform, int depth = 0, Transform sp = null, string prefix = "", string postfix = "")
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
}
