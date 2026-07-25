#pragma warning disable CS8618, CS8600, CS8603, CS8625, CS8601, CS8604, CS8602

using Config;
using Config.Common;
using GameData.Common;
using GameData.Domains;
using GameData.Domains.Mod;
using HarmonyLib;
using Newtonsoft.Json;
using NLog;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TaiwuModdingLib.Core.Plugin;

namespace ConfigHook
{
	[PluginConfig(pluginName: "ConfigHook", creatorId: "atakhalo", pluginVersion: "0.1.0.1")]
	public class ConfigHookBackendPlugin : TaiwuRemakePlugin
	{
		private Harmony? harmony;
		private static readonly Logger logger = LogManager.GetCurrentClassLogger();

		public static bool logScan = true;
		public static bool reloadConfigs = false;
		private static bool initScan = false;

		/// <summary>ConfigName → (TemplateId → (FieldName → RawStringValue))</summary>
		private static Dictionary<string, Dictionary<int, Dictionary<string, string>>> _configOverrides;
		/// <summary>ConfigName → (TemplateId → (FieldName → 原始值))，用于还原覆盖</summary>
		private static Dictionary<string, Dictionary<int, Dictionary<string, object>>> _originalValues
			= new Dictionary<string, Dictionary<int, Dictionary<string, object>>>();

		public static void MyLog(string log)
		{
			logger.Info($"[ConfigHook] {log}");
		}

		public override void Initialize()
		{
			MyLog("Backend Initialize");
			harmony = Harmony.CreateAndPatchAll(typeof(ConfigHookBackendPlugin));

			// 首次扫描已移到 OnModSettingUpdate 中执行
		}

		public override void Dispose()
		{
			harmony?.UnpatchSelf();
		}

		public override void OnModSettingUpdate()
		{
			DomainManager.Mod.GetSetting(ModIdStr, "logScan", ref logScan);
			DomainManager.Mod.GetSetting(ModIdStr, "reloadConfigs", ref reloadConfigs);

			// 首次扫描：延迟 100ms，等所有 mod 初始化完毕后再执行
			if (!initScan)
			{
				initScan = true;
				System.Threading.Timer? initTimer = null;
				initTimer = new System.Threading.Timer(_ =>
				{
					initTimer?.Dispose();
					ScanAndApplyConfigs();
				}, null, 100, System.Threading.Timeout.Infinite);
			}
			else if (reloadConfigs) // 非首次时才处理重新加载
			{
				reloadConfigs = false;
				System.Threading.Timer? t = null;
				t = new System.Threading.Timer(_ =>
				{
					t?.Dispose();
					ReloadConfigs();
				}, null, 100, System.Threading.Timeout.Infinite);
			}
		}

		// ============================================================
		//  扫描所有已加载 Mod，应用 configHook 配置
		// ============================================================

		private void ScanAndApplyConfigs()
		{
			_configOverrides = new Dictionary<string, Dictionary<int, Dictionary<string, string>>>();

			// 先扫描自身：插件 Initialize 时当前 ModId 尚未加入 LoadedMods，需独立处理
			TryLoadModConfigs(ModIdStr);

			// 再扫描其他已加载的 mod
			foreach (ModId modId in ModDomain.GetLoadedModIds())
			{
				TryLoadModConfigs(modId.ToString());
			}

			ApplyAllOverrides();
		}

		/// <summary>检查单个 mod 是否有 configHook 配置，有则加载</summary>
		private void TryLoadModConfigs(string modIdStr)
		{
			string dir = DomainManager.Mod.GetModDirectory(modIdStr);
			if (string.IsNullOrEmpty(dir)) return;

			string yamlPath = Path.Combine(dir, "configHook.yaml");
			if (!File.Exists(yamlPath)) return;

			string csvDir = Path.Combine(dir, "configHook");
			if (!Directory.Exists(csvDir)) return;

			string title = DomainManager.Mod.GetModTitle(modIdStr);
			MyLog($"发现 configHook 配置: {title}");

			foreach (string csvFile in Directory.GetFiles(csvDir, "*.csv"))
			{
				string className = Path.GetFileNameWithoutExtension(csvFile);
				LoadCsvOverrides(className, csvFile);
			}
		}

		private void ReloadConfigs()
		{
			MyLog("重新加载配置...");
			RestoreOverrides();
			_configOverrides.Clear();
			ScanAndApplyConfigs();
		}

		#region ConfigHook Core — 前后端逻辑一致，同步修改时请同步两端
		// ============================================================
		//  解析 CSV → _configOverrides
		// ============================================================

		private void LoadCsvOverrides(string className, string csvPath)
		{
			string[] lines = File.ReadAllLines(csvPath, Encoding.UTF8);
			if (lines.Length < 2) return;

			string headerLine = lines[0].Trim();
			if (string.IsNullOrEmpty(headerLine)) return;

			string[] headers = ParseCsvLine(headerLine);
			int idIdx = Array.IndexOf(headers, "TemplateId");
			if (idIdx < 0)
			{
				MyLog($"  [警告] {csvPath} 缺少 TemplateId 列，跳过");
				return;
			}

			if (!_configOverrides.ContainsKey(className))
				_configOverrides[className] = new Dictionary<int, Dictionary<string, string>>();

			var classDict = _configOverrides[className];

			for (int i = 1; i < lines.Length; i++)
			{
				string line = lines[i].Trim();
				if (string.IsNullOrEmpty(line)) continue;

				string[] fields = ParseCsvLine(line);
				if (fields.Length <= idIdx) continue;

				if (!int.TryParse(fields[idIdx], out int templateId)) continue;

				var fieldDict = new Dictionary<string, string>();
				for (int j = 0; j < headers.Length; j++)
				{
					if (j == idIdx) continue;
					if (j >= fields.Length) break;
					string val = fields[j]?.Trim();
					if (string.IsNullOrEmpty(val)) continue;
					fieldDict[headers[j]] = val;
				}

				if (fieldDict.Count > 0)
					classDict[templateId] = fieldDict;
			}
		}

		// ============================================================
		//  写入已初始化的 Config 实例
		// ============================================================

		private void ApplyAllOverrides()
		{
			foreach (var kvp in _configOverrides)
			{
				string className = kvp.Key;
				var overrides = kvp.Value;

				object configInstance = GetConfigInstance(className);
				if (configInstance == null) continue;

				Type configType = configInstance.GetType();

				// 检查 _dataArray 是否已初始化
				FieldInfo dataArrayField = FindDataArrayField(configType);
				if (dataArrayField == null)
				{
					MyLog($"  [警告] {className} 无法访问 _dataArray 字段");
					continue;
				}

				IList dataArray = dataArrayField.GetValue(configInstance) as IList;
				if (dataArray == null || dataArray.Count == 0)
				{
					MyLog($"  [跳过] {className} _dataArray 为空（可能未初始化）");
					continue;
				}

				// 获取 Item 类型（ConfigData<T,TKey> 的泛型参数 T）
				Type itemType = GetItemType(configType);
				if (itemType == null) continue;

				// 获取 this[int] 索引器
				PropertyInfo indexer = FindIndexer(configType);
				if (indexer == null) continue;

				int appliedCount = 0;
				foreach (var entry in overrides)
				{
					int templateId = entry.Key;
					var fieldOverrides = entry.Value;

					object item = indexer.GetValue(configInstance, new object[] { templateId });
					if (item == null)
					{
						if (logScan)
							MyLog($"  [跳过] {className}[{templateId}] 未找到");
						continue;
					}

					foreach (var fieldKvp in fieldOverrides)
					{
						string fieldName = fieldKvp.Key;
						string rawValue = fieldKvp.Value;

						FieldInfo field = itemType.GetField(fieldName);
						if (field == null)
						{
							MyLog($"  [警告] {className}.{fieldName} 字段不存在");
							continue;
						}

						try
						{
							object converted = ConvertValue(rawValue, field.FieldType);
							// 保存原始值（仅首次覆盖时记录）
							if (!_originalValues.ContainsKey(className))
								_originalValues[className] = new Dictionary<int, Dictionary<string, object>>();
							if (!_originalValues[className].ContainsKey(templateId))
								_originalValues[className][templateId] = new Dictionary<string, object>();
							if (!_originalValues[className][templateId].ContainsKey(fieldName))
								_originalValues[className][templateId][fieldName] = field.GetValue(item);
							field.SetValue(item, converted);
							appliedCount++;
						}
						catch (Exception ex)
						{
							MyLog($"  [错误] {className}[{templateId}].{fieldName} = {rawValue} 转换失败: {ex.Message}");
						}
					}
				}

				if (appliedCount > 0 || logScan)
					MyLog($"  [成功] {className}: 修改了 {appliedCount} 个字段");
			}
		}

		private void RestoreOverrides()
		{
			int count = 0;
			foreach (var classKvp in _originalValues)
			{
				string className = classKvp.Key;
				object configInstance = GetConfigInstance(className);
				if (configInstance == null) continue;
				Type configType = configInstance.GetType();
				PropertyInfo indexer = FindIndexer(configType);
				if (indexer == null) continue;
				Type itemType = GetItemType(configType);
				if (itemType == null) continue;
				foreach (var idKvp in classKvp.Value)
				{
					object item = indexer.GetValue(configInstance, new object[] { idKvp.Key });
					if (item == null) continue;
					foreach (var fieldKvp in idKvp.Value)
					{
						FieldInfo field = itemType.GetField(fieldKvp.Key);
						if (field != null)
						{
							field.SetValue(item, fieldKvp.Value);
							count++;
						}
					}
				}
			}
			_originalValues.Clear();
			MyLog($"  还原了 {count} 个字段");
		}

		// ============================================================
		//  类型转换：CSV 字符串 → 目标字段类型
		// ============================================================

		private object ConvertValue(string raw, Type targetType)
		{
			// 处理可空类型
			Type underlying = Nullable.GetUnderlyingType(targetType);
			if (underlying != null)
				targetType = underlying;

			if (targetType == typeof(string))
				return raw;

			if (targetType == typeof(bool))
			{
				if (raw.Equals("True", StringComparison.OrdinalIgnoreCase) || raw == "1") return true;
				if (raw.Equals("False", StringComparison.OrdinalIgnoreCase) || raw == "0") return false;
				return bool.Parse(raw);
			}

			if (targetType == typeof(int)) return int.Parse(raw);
			if (targetType == typeof(short)) return short.Parse(raw);
			if (targetType == typeof(byte)) return byte.Parse(raw);
			if (targetType == typeof(sbyte)) return sbyte.Parse(raw);
			if (targetType == typeof(long)) return long.Parse(raw);
			if (targetType == typeof(float)) return float.Parse(raw);
			if (targetType == typeof(double)) return double.Parse(raw);
			if (targetType == typeof(uint)) return uint.Parse(raw);
			if (targetType == typeof(ushort)) return ushort.Parse(raw);

			// 枚举
			if (targetType.IsEnum)
				return Enum.Parse(targetType, raw);

			// JSON 数组或对象
			if (raw.StartsWith("[") || raw.StartsWith("{"))
				return JsonConvert.DeserializeObject(raw, targetType);

			return Convert.ChangeType(raw, targetType);
		}

		// ============================================================
		//  反射辅助方法
		// ============================================================

		private object GetConfigInstance(string className)
		{
			Type configType = Type.GetType($"Config.{className}, GameData.Shared");
			if (configType == null)
			{
				foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
				{
					try
					{
						configType = asm.GetType($"Config.{className}");
						if (configType != null) break;
					}
					catch { }
				}
			}

			if (configType == null)
			{
				MyLog($"  [跳过] 未找到 Config.{className} 类型");
				return null;
			}

			PropertyInfo instanceProp = configType.GetProperty("Instance",
				BindingFlags.Public | BindingFlags.Static);
			if (instanceProp == null)
			{
				FieldInfo instanceField = configType.GetField("Instance",
					BindingFlags.Public | BindingFlags.Static);
				if (instanceField == null)
				{
					MyLog($"  [跳过] {className} 没有 Instance 属性/字段");
					return null;
				}
				return instanceField.GetValue(null);
			}

			return instanceProp.GetValue(null);
		}

		private Type GetItemType(Type configType)
		{
			Type baseType = configType.BaseType;
			while (baseType != null)
			{
				if (baseType.IsGenericType && baseType.GetGenericTypeDefinition() == typeof(ConfigData<,>))
					return baseType.GetGenericArguments()[0];
				baseType = baseType.BaseType;
			}
			return null;
		}

		private FieldInfo FindDataArrayField(Type type)
		{
			while (type != null)
			{
				FieldInfo field = type.GetField("_dataArray",
					BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
				if (field != null) return field;
				type = type.BaseType;
			}
			return null;
		}

		private PropertyInfo FindIndexer(Type type)
		{
			while (type != null)
			{
				PropertyInfo prop = type.GetProperty("Item",
					BindingFlags.Public | BindingFlags.Instance,
					null, null, new Type[] { typeof(int) }, null);
				if (prop != null) return prop;
				type = type.BaseType;
			}
			return null;
		}

		// ============================================================
		//  CSV 解析（支持引号转义）
		// ============================================================

		private string[] ParseCsvLine(string line)
		{
			var result = new List<string>();
			bool inQuotes = false;
			var current = new StringBuilder();

			for (int i = 0; i < line.Length; i++)
			{
				char c = line[i];
				if (c == '"')
				{
					if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
					{
						current.Append('"');
						i++;
					}
					else
					{
						inQuotes = !inQuotes;
					}
				}
				else if (c == ',' && !inQuotes)
				{
					result.Add(current.ToString());
					current.Clear();
				}
				else
				{
					current.Append(c);
				}
			}
			result.Add(current.ToString());
			return result.ToArray();
		}
		#endregion // ConfigHook Core

	}
}
