# UabHooker 配置文档

## 概述

`uabhook.xml` 放在 Mod 根目录，UabHooker 会自动扫描所有已启用 Mod 中的此文件，根据配置替换游戏加载的资源。

---

## 目录

- [UabHooker 配置文档](#uabhooker-配置文档)
	- [概述](#概述)
	- [目录](#目录)
	- [公共属性](#公共属性)
		- [enable 条件](#enable-条件)
		- [tofolder 自动扫描](#tofolder-自动扫描)
		- [torand 随机](#torand-随机)
	- [1️⃣ HookUab — 整包替换](#1️⃣-hookuab--整包替换)
	- [2️⃣ HookImg — 单图替换](#2️⃣-hookimg--单图替换)
	- [3️⃣ HookSpineImg — Spine 贴图替换](#3️⃣-hookspineimg--spine-贴图替换)
	- [4️⃣ HookAtlas — 图集精灵替换](#4️⃣-hookatlas--图集精灵替换)
	- [5️⃣ HookSpine — Spine 完整资源替换](#5️⃣-hookspine--spine-完整资源替换)
	- [6️⃣ HookAvatar — 角色 Spine 替换（含 cover）](#6️⃣-hookavatar--角色-spine-替换含-cover)
		- [cover 自动发现（tofolder/torand + type="cloth"）](#cover-自动发现tofoldertorand--typecloth)
	- [附录：enable 条件语法](#附录enable-条件语法)
		- [示例](#示例)

---

## 公共属性

### enable 条件

所有 hook 标签和条目都支持 `enable` 属性，格式见 [enable 条件语法](#附录-enable-条件语法)。

### tofolder 自动扫描

扫描指定文件夹下的所有文件，**自动生成条目**，无需逐个手写。

| Hook | 扫描内容 | 匹配 key 生成方式 |
|---|---|---|
| HookImg | 图片 `.png/.jpg/.jpeg` | `assetPath + 文件相对路径（无扩展名）` |
| HookAtlas | 图片 `.png/.jpg/.jpeg` | 文件名（无扩展名） |
| HookSpine | `.json/.skel` + 同目录同名 `.atlas` | 文件名（无扩展名） |
| HookAvatar | `.json/.skel` + 同目录同名 `.atlas` + `_cover` 文件 | 文件名（无扩展名） |

### torand 随机

运行时从文件夹中**随机选取**一个文件使用，每次加载都可能不同。

---

## 1️⃣ HookUab — 整包替换

替换 `AssetBundle.LoadFromFile` 加载的整包文件。

```xml
<HookUab>
  <uab name="assetbundle名称或路径" to="替换文件路径" />
</HookUab>
```

| 属性 | 必填 | 说明 |
|------|------|------|
| `name` | 是 | 匹配原始路径（全路径或文件名均可） |
| `to` | 是 | 替换文件路径（相对 XML 目录或绝对路径） |
| `enable` | 否 | 启用条件 |

---

## 2️⃣ HookImg — 单图替换

替换 AssetBundle 中单个资源的加载（Texture2D / Sprite / TextAsset）。

```xml
<HookImg>
  <uab name="AssetBundle名称">
    <!-- 单文件模式 -->
    <img assetPath="图片资源路径" to="替换图片路径" />

    <!-- tofolder 自动扫描模式 -->
    <img assetPath="Assets/UI/" tofolder="myImages/" />

    <!-- 随机模式 -->
    <img assetPath="xxx" torand="随机文件夹/" />
  </uab>
</HookImg>
```

| 属性 | 必填 | 说明 |
|------|------|------|
| `name` (uab) | 是 | AssetBundle 名称 |
| `assetPath` | 是* | 匹配的资源完整路径（也支持短文件名匹配） |
| `to` / `tofolder` / `torand` | 是* | 三选一 |
| `w` / `h` | 否 | 指定替换后的宽高（0=不缩放） |
| `enable` | 否 | 启用条件 |

---

## 3️⃣ HookSpineImg — Spine 贴图替换

替换 Spine 骨骼使用的贴图（纹理级，不替换骨骼/动画）。

```xml
<HookSpineImg>
  <skel name="骨骼名称">
    <img name="贴图名称" to="替换贴图路径" />
    <img name="贴图名称" torand="随机文件夹/" />
  </skel>
</HookSpineImg>
```

| 属性 | 必填 | 说明 |
|------|------|------|
| `name` (skel) | 是 | 匹配 `skeletonDataAsset.name`（自动去掉 `_SkeletonData` 后缀） |
| `name` (img) | 是 | 匹配 `mainTexture.name` |
| `to` / `torand` | 是* | 二选一 |

---

## 4️⃣ HookAtlas — 图集精灵替换

替换 `SpriteAtlas.GetSprite()` 和 `CImage.SetImageSpriteOnly()` 返回的精灵。

```xml
<HookAtlas>
  <atlas name="图集名称">
    <sprite name="精灵名称" to="图片路径" />
    <sprite name="精灵名称" torand="随机文件夹/" w="64" h="64" posx="10" posy="-5" />
  </atlas>

  <!-- tofolder 模式：文件名即精灵名 -->
  <atlas name="图集名称" tofolder="images/" />
</HookAtlas>
```

| 属性 | 必填 | 说明 |
|------|------|------|
| `name` (atlas) | 是 | `SpriteAtlas` 名称 |
| `name` (sprite) | 是 | 精灵名称 |
| `to` / `tofolder` / `torand` | 是* | 三选一 |
| `w` / `h` | 否 | 仅 `SetImageSpriteOnly` 生效，设置控件尺寸 |
| `posx` / `posy` | 否 | 仅 `SetImageSpriteOnly` 生效，设置控件位置 |

---

## 5️⃣ HookSpine — Spine 完整资源替换

替换 Spine 的 `.atlas` + `.skel` 完整资源（骨骼 + 动画 + 贴图都换）。

```xml
<HookSpine>
  <!-- 单组替换 -->
  <spine name="骨骼名称" atlas="xxx.atlas" skel="xxx.skel" />

  <!-- tofolder 自动扫描：配对同名文件 -->
  <spine name="xxx" tofolder="spineFiles/" />

  <!-- 随机池 -->
  <spine name="xxx" torand="spinePool/" objDir="NpcSpine" />

  <!-- 按 objDir 过滤 -->
  <spine name="xxx" atlas="a.atlas" skel="a.skel" objDir="Body/NpcSpine" />
</HookSpine>
```

| 属性 | 必填 | 说明 |
|------|------|------|
| `name` | 是 | 匹配 `skeletonDataAsset.name`（自动去掉 `_SkeletonData` 后缀） |
| `atlas` + `skel` / `tofolder` / `torand` | 是* | 三选一 |
| `objDir` | 否 | 按 GameObject 路径后缀过滤，支持如 `"NpcSpine"`、`"Body/NpcSpine"`。不填匹配所有 |
| `enable` | 否 | 启用条件 |

---

## 6️⃣ HookAvatar — 角色 Spine 替换（含 cover）

替换角色部位 Spine，支持 cloth 类型的 cover 遮盖层。

```xml
<HookAvatar>
  <!-- 基础替换 -->
  <spine name="部位名称" atlas="a.atlas" skel="a.skel" />

  <!-- cloth 类型：自动查找 cover 文件 -->
  <spine name="衣服名称" tofolder="cloth/" type="cloth" />

  <!-- 自定义 cover -->
  <spine name="xxx" atlas="a.atlas" skel="a.skel"
         coverAtlas="cover.atlas" coverSkel="cover.skel" />

  <!-- 保留原始 cover -->
  <spine name="xxx" atlas="a.atlas" skel="a.skel" coverkeep="true" />

  <!-- 随机池（自动发现 cover） -->
  <spine name="xxx" torand="clothPool/" type="cloth" />
</HookAvatar>
```

| 属性 | 必填 | 说明 |
|------|------|------|
| `name` | 是 | 匹配 `skeletonDataAsset.name` |
| `atlas` + `skel` / `tofolder` / `torand` | 是* | 三选一 |
| `type` | 否 | 设为 `"cloth"` 时启用 cover 自动发现 |
| `coverkeep` | 否 | `true`=保留原始 cover，`false`=隐藏（默认） |
| `coverAtlas` / `coverSkel` | 否 | 自定义 cover 资源路径 |
| `objDir` | 否 | 按 GameObject 路径后缀过滤 |
| `enable` | 否 | 启用条件 |

### cover 自动发现（tofolder/torand + type="cloth"）

扫描时自动识别以下文件：

| 文件 | 作用 |
|------|------|
| `xxx_cover.skel` / `xxx_cover.json` + `xxx_cover.atlas` | 替换 cover |
| `xxx_coverkeep.txt`（空文件标记） | 保留原始 cover |

---

## 附录：enable 条件语法

所有配置项（包括 Hook 标签和内部条目）都支持三种 `enable` 格式：

| 格式 | 说明 | 示例 |
|------|------|------|
| `true` / `false` | 静态开关，写死启用或禁用 | `enable="true"` |
| `Toggle:设置项名称` | 关联游戏 Mod 设置中的一个 **开关**（bool），由玩家控制 | `enable="Toggle:mySwitch"` |
| `Dropdown:设置项名称:索引` | 关联游戏 Mod 设置中的一个 **下拉框**（int），等于指定索引时启用 | `enable="Dropdown:myList:1"` |

Toggle 和 Dropdown 的设置在游戏 Mod 配置面板中调整后，需要点击 **reScan** 重新扫描生效。

### 示例

```xml
<HookImg enable="Toggle:enableCharRepl">   <!-- 整类开关 -->
  <uab name="char_icons">
    <img assetPath="avatar/head.png" to="new_head.png"
         enable="Toggle:enableHeadRepl" />   <!-- 条目单独开关 -->
  </uab>
</HookImg>
```
