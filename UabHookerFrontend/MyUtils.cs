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
