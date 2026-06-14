"""查询 Canvas 排序信息 - 用 property 访问 sortingOrder/renderMode"""
import json, time
from pathlib import Path

WD = Path(r"C:\Program Files (x86)\Steam\steamapps\common\The Scroll Of Taiwu\Mod\运行时助手\forMod")
Q = WD / "quest.json"
R = WD / "result.json"

def send(req):
    rid = req["requestId"]
    Q.write_text(json.dumps(req, ensure_ascii=False, indent=4), encoding="utf-8")
    deadline = time.time() + 15
    last = ""
    while time.time() < deadline:
        if R.exists():
            try:
                t = R.read_text(encoding="utf-8", errors="ignore").strip()
            except:
                t = last
            if t and t != last:
                try:
                    r = json.loads(t)
                    if r.get("requestId") == rid: return r
                except: pass
                last = t
        time.sleep(0.3)
    return {"success": False, "error": "超时"}

def get_long(r):
    if r.get("hasLongResult") and r.get("longResultFile"):
        fp = WD / r["longResultFile"]
        if fp.exists(): return json.loads(fp.read_text(encoding="utf-8"))
    return r.get("data")

# 1. 获取所有 Canvas
r = send({"requestId":"ui_h_001","type":"scene_hierarchy","params":{"rootType":"all_canvases"}})
if not r.get("success"):
    print("Canvas 列表获取失败:", r.get("error"))
    exit()
data = get_long(r)
if not isinstance(data, list):
    print("数据格式异常:", type(data))
    exit()

# 按路径主分组展示
grouped = {}
for c in data:
    p = c.get("path","")
    parts = p.split("/")
    group = "/".join(parts[:4]) if len(parts) >= 4 else p
    if group not in grouped: grouped[group] = []
    grouped[group].append(c)

print(f"共 {len(data)} 个 Canvas，分 {len(grouped)} 组\n")

# 2. 查排序信息
all_info = []
for g in sorted(grouped.keys()):
    items = grouped[g]
    active_any = any(it.get("active") for it in items)
    mark = "🟢" if active_any else "🔴"
    print(f"  {mark} {g}")
    
    for it in items:
        p = it.get("path","")
        name = it.get("name","")
        active = it.get("active")
        children = it.get("childCount",0)
        
        so = "-"
        rm = "-"
        if active:
            time.sleep(0.3)
            r2 = send({
                "requestId": f"ui_so_{abs(hash(p)) % 100000}",
                "type": "front_code",
                "params": {
                    "entry": {"name": "UnityEngine.GameObject"},
                    "chain": [
                        {"step":"method","name":"Find","argTypes":["System.String"],"args":[p]},
                        {"step":"method","name":"GetComponent<UnityEngine.Canvas>"},
                        {"step":"property","name":"sortingOrder"}
                    ]
                }
            })
            if r2.get("success"): so = r2["data"]
        
            time.sleep(0.3)
            r3 = send({
                "requestId": f"ui_rm_{abs(hash(p)) % 100000}",
                "type": "front_code",
                "params": {
                    "entry": {"name": "UnityEngine.GameObject"},
                    "chain": [
                        {"step":"method","name":"Find","argTypes":["System.String"],"args":[p]},
                        {"step":"method","name":"GetComponent<UnityEngine.Canvas>"},
                        {"step":"property","name":"renderMode"}
                    ]
                }
            })
            if r3.get("success"): rm = r3["data"]
        
        all_info.append((p, name, active, children, so, rm))
        print(f"    ├ {name}  active={active}  children={children}")
        print(f"    │  sortOrder={so}  renderMode={rm}")

# 3. 按 sortingOrder 排序展示（玩家视角）
print()
print("=" * 60)
print("玩家视角 UI 层级（按 sortingOrder 从小到大=从远到近）")
print("=" * 60)

def sort_key(x):
    try: return int(x[4])
    except: return 999

sorted_info = sorted([x for x in all_info if x[3]], key=sort_key)
for i, (p, name, active, children, so, rm) in enumerate(sorted_info):
    if so != "-":
        print(f"  {i+1:2d}. order={so:3d}  renderMode={rm}  {name}")
        print(f"       {p}")
    else:
        print(f"  {i+1:2d}. order=inactive  {name}")
        print(f"       {p}")
print()

# 4. 也列出 inactive 的
print("=" * 60)
print("隐藏的 Canvas（active=false）")
print("=" * 60)
for p, name, active, children, so, rm in all_info:
    if not active:
        print(f"  🔴 {name}")
        print(f"       {p}")
