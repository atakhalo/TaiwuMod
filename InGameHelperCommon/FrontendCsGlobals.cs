using System.Collections.Generic;

namespace InGameHelper.Common
{
    /// <summary>
    /// 前端脚本全局参数。Roslyn 会将 public 成员暴露为脚本中的顶級变量。
    /// 放在独立程序集中，避免 Mono 加载时的类型冲突。
    /// </summary>
    public class FrontendCsGlobals
    {
        public Dictionary<string, object> Params { get; set; } = new Dictionary<string, object>();
    }
}
