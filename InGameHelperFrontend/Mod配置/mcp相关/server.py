"""
InGameHelper FastMCP Server
将 太吾绘卷 游戏内查询 Mod 封装为 MCP 工具，供 AI Agent 调用。
"""

import json
import time
from pathlib import Path

from mcp.server.fastmcp import FastMCP

# ─── 配置 ──────────────────────────────────────────────────────────

HERE = Path(__file__).parent.parent.resolve()
WORK_DIR = HERE / "forMod"
QUEST_FILE = WORK_DIR / "quest.json"
RESULT_FILE = WORK_DIR / "result.json"
TIMEOUT = 15

mcp = FastMCP("ingame-helper")

# ─── 底层通讯 ──────────────────────────────────────────────────────

def _call(request_id: str, req_type: str, **kwargs) -> dict:
    body = {"requestId": request_id, "type": req_type}
    if kwargs:
        body["params"] = {k: v for k, v in kwargs.items() if v is not None}
    QUEST_FILE.write_text(json.dumps(body, ensure_ascii=False, indent=4), encoding="utf-8")
    deadline = time.time() + TIMEOUT
    last = ""
    while time.time() < deadline:
        if RESULT_FILE.exists():
            text = RESULT_FILE.read_text(encoding="utf-8", errors="ignore").strip()
            if text and text != last:
                try:
                    r = json.loads(text)
                    if r.get("requestId") == request_id:
                        return r
                except json.JSONDecodeError:
                    pass
                last = text
        time.sleep(0.3)
    return {"requestId": request_id, "success": False, "error": "等待 mod 响应超时"}

def _resolve(resp: dict) -> str:
    if not resp.get("success"):
        return f"失败: {resp.get('error', '未知错误')}"
    if resp.get("hasLongResult") and resp.get("longResultFile"):
        fp = WORK_DIR / resp["longResultFile"]
        if fp.exists():
            return fp.read_text(encoding="utf-8")
    data = resp.get("data")
    if data is not None:
        return json.dumps(data, ensure_ascii=False, indent=2)
    return "成功（无数据）"

# ─── 参数映射 ──────────────────────────────────────────────────────

_PY2JSON = {
    "root_type": "rootType",
    "path": "path",
    "game_object_path": "gameObjectPath",
    "component_type": "componentType",
    "field": "field",
    "domain": "domain",
    "method": "method",
    "args": "args",
    "char_id": "charId",
    "data": "data",
}

def _go(req_type: str, **kw) -> str:
    js_kw = {}
    for k, v in kw.items():
        js_k = _PY2JSON.get(k, k)
        if v is not None:
            js_kw[js_k] = v
    rid = f"{req_type}_{int(time.time()*1000)}"
    return _resolve(_call(rid, req_type, **js_kw))

# ═══════════════════════════════════════════════════════════════════
#  工具定义
# ═══════════════════════════════════════════════════════════════════

@mcp.tool(description="查询场景 GameObject 层级。不传 root_type 列出所有 Canvas；传 root_type=path + path 查询指定物体。")
def query_scene_hierarchy(root_type: str = "all_canvases", path: str | None = None) -> str:
    return _go("scene_hierarchy", root_type=root_type, path=path)

@mcp.tool(description="查询 GameObject 上的组件列表及公开字段。")
def query_component_info(game_object_path: str, component_type: str | None = None) -> str:
    return _go("component_info", game_object_path=game_object_path, component_type=component_type)

@mcp.tool(description="读取组件某个字段的值。")
def query_component_field(game_object_path: str, component_type: str, field: str) -> str:
    return _go("component_field", game_object_path=game_object_path, component_type=component_type, field=field)

@mcp.tool(description="通用游戏数据查询。支持链式方法(object_methods+object_args_list)和读字段(field)。")
def query_game_data(
    domain: str,
    method: str | None = None,
    args: list[int] | None = None,
    object_methods: list[str] | None = None,
    object_args_list: list[list] | None = None,
    field: str | None = None,
) -> str:
    return _go("game_data", domain=domain, method=method, args=args,
               object_methods=object_methods, object_args_list=object_args_list, field=field)

@mcp.tool(description="快捷查询太吾信息：ID、姓名、年龄、性别、传承、遗产等。")
def query_taiwu_info() -> str:
    return _go("taiwu_info")

@mcp.tool(description="获取角色姓名（姓氏+名字）。")
def query_character_name(char_id: int) -> str:
    return _go("character_name", char_id=char_id)

@mcp.tool(description="获取游戏世界日期、年份、月份。")
def query_world_info() -> str:
    return _go("world_info")

@mcp.tool(description="获取角色全部字段（深度3层序列化）。")
def query_character_info(char_id: int) -> str:
    return _go("character_info", char_id=char_id)

@mcp.tool(description="通过前端 SingletonObject 查询数据。支持泛型(object_methods)和链式调用。")
def query_frontend_data(
    data: str,
    object_methods: list[str] | None = None,
    object_args_list: list[list] | None = None,
    field: str | None = None,
) -> str:
    return _go("frontend_data", data=data,
               object_methods=object_methods, object_args_list=object_args_list, field=field)
