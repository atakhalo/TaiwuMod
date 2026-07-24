#pragma warning disable CS8618, CS8600, CS8603, CS8625, CS8601, CS8604

using UnityEngine;

namespace UabHooker
{
	public class MyUtils
	{
		public static string modName;
		public static void MyLog(string log)
		{
			Debug.Log($"[{modName}] {log}");
		}
	}
}
