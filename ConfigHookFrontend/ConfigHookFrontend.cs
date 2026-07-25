#pragma warning disable CS8618, CS8600, CS8603, CS8625, CS8601, CS8604, CS8602

using Config;
using Config.Common;
using FrameWork.ModSystem;
using GameData.Domains.Mod;
using HarmonyLib;
using Newtonsoft.Json;
using System;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TaiwuModdingLib.Core.Plugin;
using UnityEngine;

public partial class MyUtils
{
	public static string modName;
	public static void MyLog(string log)
	{
		Debug.Log($"[{modName}] {log}");
	}
}

namespace ConfigHook
{
	[PluginConfig(pluginName: "ConfigHook", creatorId: "atakhalo", pluginVersion: "0.1.0.1")]
	public class ConfigHookFrontendPlugin : TaiwuRemakePlugin
	{
		private Harmony harmony;

		public static bool logScan = true;
		public static bool reloadConfigs = false;
		private static bool initScan = false;

		/// <summary>ConfigName → (TemplateId → (FieldName → RawStringValue))</summary>
		private static Dictionary<string, Dictionary<int, Dictionary<string, string>>> _configOverrides;
		/// <summary>ConfigName → (TemplateId → (FieldName → 原始值))，用于还原覆盖</summary>
		private static Dictionary<string, Dictionary<int, Dictionary<string, object>>> _originalValues
			= new Dictionary<string, Dictionary<int, Dictionary<string, object>>>();

		public override void Initialize()
		{
			MyUtils.modName = nameof(ConfigHook);
			MyUtils.MyLog("Initialize");

			harmony = Harmony.CreateAndPatchAll(typeof(ConfigHookFrontendPlugin));

			// 首次扫描已移到 OnModSettingUpdate 中执行
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
			ModManager.GetSetting(ModIdStr, "logScan", ref logScan);
			ModManager.GetSetting(ModIdStr, "reloadConfigs", ref reloadConfigs);

			// 首次扫描：等所有 mod 设置更新完毕后再执行
			if (!initScan)
			{
				initScan = true;
				MyUtils.DelayCall(ScanAndApplyConfigs, 0, true);
			}
			else if (reloadConfigs) // 非首次时才处理重新加载
			{
				reloadConfigs = false;
				MyUtils.DelayCall(ReloadConfigs, 0, true);
			}
		}

		// ============================================================
		//  扫描所有已启用 Mod，加载 configHook 配置
		// ============================================================

		private void ScanAndApplyConfigs()
		{
			_configOverrides = new Dictionary<string, Dictionary<int, Dictionary<string, string>>>();

			foreach (ModId mod in ModManager.EnabledMods)
			{
				ModInfoWithDisplayData modInfo = ModManager.GetModInfo(mod);
				if (modInfo == null) continue;

				string yamlPath = Path.Combine(modInfo.DirectoryName, "configHook.yaml");
				if (!File.Exists(yamlPath)) continue;

				MyUtils.MyLog($"发现 configHook 配置: {modInfo.Title}");

				ProcessModConfigs(modInfo.DirectoryName, yamlPath, mod.ToString());
			}

			ApplyAllOverrides();
		}

		private void ReloadConfigs()
		{
			MyUtils.MyLog("重新加载配置...");
			RestoreOverrides();
			_configOverrides.Clear();
			ScanAndApplyConfigs();
		}

		/// <summary>解析 enabled 表达式</summary>
		private bool EvaluateEnabled(string expr, string modIdStr)
		{
			if (string.IsNullOrEmpty(expr) || expr == "true") return true;
			if (expr == "false") return false;

			if (expr.StartsWith("complex:"))
			{
				string[] parts = expr.Substring(8).Split('&');
				return parts.All(p => EvaluateEnabled(p.Trim(), modIdStr));
			}
			if (expr.StartsWith("Toggle:"))
			{
				string key = expr.Substring(7);
				bool val = true;
				ModManager.GetSetting(modIdStr, key, ref val);
				return val;
			}
			if (expr.StartsWith("Dropdown:"))
			{
				string[] parts = expr.Split(':');
				if (parts.Length >= 3 && int.TryParse(parts[2], out int expected))
				{
					int val = 0;
					ModManager.GetSetting(modIdStr, parts[1], ref val);
					return val == expected;
				}
			}
			return bool.TryParse(expr, out bool r) && r;
		}

		/// <summary>读取并应用单个 mod 的 configHook.yaml</summary>
		private void ProcessModConfigs(string modDir, string yamlPath, string modIdStr)
		{
			string yamlText = File.ReadAllText(yamlPath, Encoding.UTF8);
			if (string.IsNullOrWhiteSpace(yamlText)) // 空 yaml → legacy
			{
				LegacyScanCsvDir(modDir);
				return;
			}

			try
			{
				var deserializer = new DeserializerBuilder()
					.WithNamingConvention(UnderscoredNamingConvention.Instance)
					.Build();
				var cfg = deserializer.Deserialize<HookConfig>(yamlText);
				if (cfg != null)
					ProcessYamlConfig(modDir, cfg, modIdStr);
				else
					LegacyScanCsvDir(modDir);
			}
			catch (Exception ex)
			{
				MyUtils.MyLog($"  [错误] YAML 解析失败: {ex.Message}");
				LegacyScanCsvDir(modDir);
			}
		}

		/// <summary>向后兼容：扫描 configHook/*.csv</summary>
		private void LegacyScanCsvDir(string modDir)
		{
			string csvDir = Path.Combine(modDir, "configHook");
			if (!Directory.Exists(csvDir)) return;
			foreach (string csvFile in Directory.GetFiles(csvDir, "*.csv"))
			{
				string className = Path.GetFileNameWithoutExtension(csvFile);
				LoadCsvOverrides(className, csvFile, null);
			}
		}

		#region ConfigHook Core — 前后端一致，同步修改时请同步两端
		// ============================================================
		//  解析 CSV → _configOverrides
		// ============================================================

		private void LoadCsvOverrides(string className, string csvPath, HashSet<int> skipIds = null)
		{
			string[] lines = File.ReadAllLines(csvPath, Encoding.UTF8);
			if (lines.Length < 2) return;

			string headerLine = lines[0].Trim();
			if (string.IsNullOrEmpty(headerLine)) return;

			string[] headers = ParseCsvLine(headerLine);
			int idIdx = Array.IndexOf(headers, "TemplateId");
			if (idIdx < 0)
			{
				MyUtils.MyLog($"  [警告] {csvPath} 缺少 TemplateId 列，跳过");
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
				if (skipIds != null && skipIds.Contains(templateId)) continue;

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
				{
					if (!classDict.ContainsKey(templateId))
						classDict[templateId] = new Dictionary<string, string>();
					foreach (var kv in fieldDict)
						if (!classDict[templateId].ContainsKey(kv.Key))
							classDict[templateId][kv.Key] = kv.Value;
				}
			}
		}

		// ============================================================
		//  将 _configOverrides 写入已初始化的 Config 实例
		// ============================================================

		private void ApplyAllOverrides()
		{
			foreach (var kvp in _configOverrides)
			{
				string className = kvp.Key;
				var overrides = kvp.Value;

				object configInstance = GetConfigInstance(className);
				if (configInstance == null) continue;

				// 检查 _dataArray 是否已初始化
				Type configType = configInstance.GetType();
				FieldInfo dataArrayField = FindDataArrayField(configType);
				if (dataArrayField == null)
				{
					MyUtils.MyLog($"  [警告] {className} 无法访问 _dataArray 字段");
					continue;
				}

				IList dataArray = dataArrayField.GetValue(configInstance) as IList;
				if (dataArray == null || dataArray.Count == 0)
				{
					MyUtils.MyLog($"  [跳过] {className} _dataArray 为空（前端可能未初始化）");
					continue;
				}

				// 获取 Item 类型（ConfigData<T,TKey> 的泛型参数 T）
				Type itemType = GetItemType(configType);
				if (itemType == null) continue;

				// 获取 this[int] 索引器
				PropertyInfo indexer = configType.GetProperty("Item", new Type[] { typeof(int) });
				if (indexer == null)
				{
					// 尝试从基类获取
					indexer = FindIndexer(configType);
				}
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
							MyUtils.MyLog($"  [跳过] {className}[{templateId}] 未找到");
						continue;
					}

					foreach (var fieldKvp in fieldOverrides)
					{
						string fieldName = fieldKvp.Key;
						string rawValue = fieldKvp.Value;

						FieldInfo field = itemType.GetField(fieldName);
						if (field == null)
						{
							MyUtils.MyLog($"  [警告] {className}.{fieldName} 字段不存在");
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
							MyUtils.MyLog($"  [错误] {className}[{templateId}].{fieldName} = {rawValue} 转换失败: {ex.Message}");
						}
					}
				}

				MyUtils.MyLog($"  [成功] {className}: 修改了 {appliedCount} 个字段");
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
			MyUtils.MyLog($"  还原了 {count} 个字段");
		}

		// ============================================================
		//  YAML 配置模型
		// ============================================================

		public class HookConfig
		{
			public string Enabled { get; set; } = "true";
			/// <summary>旧版兼容：单目录路径</summary>
			public string CsvDir { get; set; }
			/// <summary>新版：多目录列表，每个可独立控制</summary>
			public List<CsvDirEntry> CsvDirs { get; set; }
			public List<CsvFileEntry> CsvFiles { get; set; }
		}

		public class CsvDirEntry
		{
			public string Dir { get; set; }
			public string Enabled { get; set; } = "true";
		}

		public class CsvFileEntry
		{
			/// <summary>限定作用的目录（不填则对所有目录生效）</summary>
			public string Dir { get; set; }
			public string Name { get; set; }
			public string File { get; set; }
			public string Enabled { get; set; } = "true";
			public List<ItemEntry> Items { get; set; }
			/// <summary>子条目：有此项时自身上级字段（Dir）作为作用域</summary>
			public List<CsvFileEntry> Files { get; set; }
		}

		public class ItemEntry
		{
			public int Id { get; set; }
			public string Enabled { get; set; } = "true";
		}

		/// <summary>按 YAML 配置处理 csv_files</summary>
		private void ProcessYamlConfig(string modDir, HookConfig cfg, string modIdStr)
		{
			if (!EvaluateEnabled(cfg.Enabled, modIdStr)) return;

			// 1. 构建活跃目录列表
			var activeDirs = new List<string>();
			if (cfg.CsvDirs != null && cfg.CsvDirs.Count > 0)
			{
				foreach (var de in cfg.CsvDirs)
				{
					if (!EvaluateEnabled(de.Enabled, modIdStr)) continue;
					activeDirs.Add(Path.Combine(modDir, de.Dir ?? "configHook"));
				}
			}
			else if (!string.IsNullOrEmpty(cfg.CsvDir))
			{
				activeDirs.Add(Path.Combine(modDir, cfg.CsvDir));
			}
			else
			{
				activeDirs.Add(Path.Combine(modDir, "configHook"));
			}
			if (activeDirs.Count == 0) return;

			// 2. 无 csv_files → 扫描每个活跃目录（向后兼容）
			if (cfg.CsvFiles == null || cfg.CsvFiles.Count == 0)
			{
				foreach (var dir in activeDirs)
				{
					if (!Directory.Exists(dir)) continue;
					foreach (string f in Directory.GetFiles(dir, "*.csv"))
						LoadCsvOverrides(Path.GetFileNameWithoutExtension(f), f, null);
				}
				return;
			}

			// 3. 逐条处理 csv_files（含分组 Files 子条目）
			foreach (var entry in ExpandGroupEntries(cfg.CsvFiles))
			{
				if (string.IsNullOrEmpty(entry.Name)) continue;
				if (!EvaluateEnabled(entry.Enabled, modIdStr)) continue;

				string csvName = string.IsNullOrEmpty(entry.File) ? entry.Name + ".csv" : entry.File;

				// 确定作用于哪些目录
				var targetDirs = new List<string>();
				if (!string.IsNullOrEmpty(entry.Dir))
				{
					string ed = Path.Combine(modDir, entry.Dir);
					if (activeDirs.Any(d => string.Equals(d, ed, StringComparison.OrdinalIgnoreCase)))
						targetDirs.Add(ed);
				}
				else
				{
					targetDirs.AddRange(activeDirs);
				}

				foreach (var dir in targetDirs)
				{
					string fullPath = Path.Combine(dir, csvName);
					if (!File.Exists(fullPath))
					{
						if (logScan)
							MyUtils.MyLog($"  [跳过] CSV 不存在: {fullPath}");
						continue;
					}

					// 构建跳过 ID 集合
					HashSet<int> skipIds = null;
					if (entry.Items != null && entry.Items.Count > 0)
					{
						skipIds = new HashSet<int>();
						foreach (var item in entry.Items)
							if (!EvaluateEnabled(item.Enabled, modIdStr))
								skipIds.Add(item.Id);
					}

					LoadCsvOverrides(entry.Name, fullPath, skipIds);
				}
			}
		}

		/// <summary>展开分组条目（带 Files 的拆成多个普通条目，继承父级 Dir）</summary>
		private IEnumerable<CsvFileEntry> ExpandGroupEntries(List<CsvFileEntry> entries)
		{
			foreach (var entry in entries)
			{
				if (entry.Files != null && entry.Files.Count > 0)
				{
					foreach (var sub in entry.Files)
					{
						sub.Dir = entry.Dir; // 继承父级作用域
						yield return sub;
					}
				}
				else
				{
					yield return entry;
				}
			}
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

			// string
			if (targetType == typeof(string))
				return raw;

			// bool
			if (targetType == typeof(bool))
			{
				if (raw.Equals("True", StringComparison.OrdinalIgnoreCase) || raw == "1") return true;
				if (raw.Equals("False", StringComparison.OrdinalIgnoreCase) || raw == "0") return false;
				return bool.Parse(raw);
			}

			// 数值类型
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

			// JSON 数组或对象（List<T>, T[], Dictionary, 自定义类等）
			if (raw.StartsWith("[") || raw.StartsWith("{"))
				return JsonConvert.DeserializeObject(raw, targetType);

			// 回退：尝试 ChangeType
			return Convert.ChangeType(raw, targetType);
		}

		// ============================================================
		//  反射辅助方法
		// ============================================================

		/// <summary>通过反射获取 Config.{className}.Instance</summary>
		private object GetConfigInstance(string className)
		{
			// 尝试从已加载的程序集中查找 Config.{className}
			Type configType = Type.GetType($"Config.{className}, GameData.Shared");
			if (configType == null)
			{
				// 回退：扫描所有程序集
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
				MyUtils.MyLog($"  [跳过] 未找到 Config.{className} 类型");
				return null;
			}

			// 获取 Instance 静态属性
			PropertyInfo instanceProp = configType.GetProperty("Instance",
				BindingFlags.Public | BindingFlags.Static);
			if (instanceProp == null)
			{
				// 尝试 Instance 字段
				FieldInfo instanceField = configType.GetField("Instance",
					BindingFlags.Public | BindingFlags.Static);
				if (instanceField == null)
				{
					MyUtils.MyLog($"  [跳过] {className} 没有 Instance 属性/字段");
					return null;
				}
				return instanceField.GetValue(null);
			}

			return instanceProp.GetValue(null);
		}

		/// <summary>获取 ConfigData<T,TKey> 基类的泛型参数 T（Item 类型）</summary>
		private Type GetItemType(Type configType)
		{
			Type baseType = configType.BaseType;
			while (baseType != null)
			{
				if (baseType.IsGenericType && baseType.GetGenericTypeDefinition() == typeof(ConfigData<,>))
				{
					return baseType.GetGenericArguments()[0];
				}
				baseType = baseType.BaseType;
			}
			return null;
		}

		/// <summary>递归查找 _dataArray 字段（可能在基类中）</summary>
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

		/// <summary>查找 this[int] 索引器</summary>
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
