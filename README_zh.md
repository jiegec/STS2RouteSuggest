# RouteSuggest - 杀戮尖塔 2 Mod

[English](README.md) | 简体中文

![](screenshot.png)

一个《杀戮尖塔 2》的 Mod，能够在地图上推荐最优路线，并以金色/红色高亮显示。

**支持的游戏版本：** v0.99.1 和 v0.102.0（公开测试版）

## 功能特性

- **双路线推荐**：同时显示两条最优路线（安全路线和激进路线）
- **视觉高亮**：
  - **金色**：安全路线（最小化风险）
  - **红色**：激进路线（优先战斗以获取奖励）
- **智能评分**：针对安全与激进两种玩法采用不同的权重
- **专家评分**：可选的相邻结点加成，奖励特定位置组合（例如精英战前休息）
- **图形界面配置**：通过 [ModConfig](https://github.com/xhyrzldf/ModConfig-STS2) 在游戏中进行配置（可选）
- **手动配置**：资深用户可直接编辑 JSON 配置文件

## 安装方法

1. 从 [GitHub releases](https://github.com/jiegec/STS2RouteSuggest/releases) 或 [Nexus mods](https://www.nexusmods.com/slaythespire2/mods/54) 下载最新版本
2. 将 Mod 文件解压到《杀戮尖塔 2》的 mods 文件夹中（`mods` 文件夹应与游戏可执行文件位于同一目录）：
   - **Windows**：`C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods\`
   - **macOS**：`~/Library/Application\ Support/Steam/steamapps/common/Slay\ the\ Spire\ 2/SlayTheSpire2.app/Contents/MacOS/mods/`
   - **Linux**：`~/.steam/steam/steamapps/common/Slay\ the\ Spire\ 2/mods`
3. 启动《杀戮尖塔 2》，Mod 将自动加载

## 从源码构建

### 前置要求

- .NET 9.0 SDK 或更高版本
- 支持 Mono 的 Godot 4.5.1
- 《杀戮尖塔 2》（用于引用 sts2.dll）

### 构建步骤

```bash
# 克隆仓库
git clone https://github.com/jiegec/STS2RouteSuggest
cd RouteSuggest

# 构建 Mod
./build.sh

# 安装 Mod
./install.sh
```

## 工作原理

该 Mod 使用不同的评分系统计算两条最优路径：

### 安全路线（金色）

最小化遭遇战并优先保证安全：

| 房间类型     | 分数 | 原因                |
|--------------|------|---------------------|
| **休息处**   | +1   | 恢复生命、升级卡牌   |
| **宝箱房**   | +1   | 免费获得遗物        |
| **商店**     | +1   | 购买卡牌、遗物和药水 |
| **普通战斗** | -1   | 避免战斗            |
| **精英战**   | -3   | 避免高难度遭遇战    |
| **Boss**     | 0    | 最终目的地          |

### 激进路线（红色）

优先选择战斗和未知房间：

| 房间类型     | 分数 | 原因                 |
|--------------|------|----------------------|
| **休息处**   | +1   | 恢复生命、升级卡牌    |
| **宝箱房**   | +1   | 免费获得遗物         |
| **商店**     | +1   | 购买卡牌、遗物和药水  |
| **普通战斗** | +2   | 获得金币和卡牌奖励   |
| **精英战**   | +3   | 获得遗物和更好的奖励 |
| **未知房间** | +2   | 可能是战斗           |
| **Boss**     | 0    | 最终目的地           |

当两条路线共享同一条边时，显示为金色。

### 专家评分（相邻结点加成）

每条路线都可以选择开启**专家模式**，在基础权重之上应用位置加成。专家模式是一个全局开关，影响所有路线。这些加成奖励特定的结点序列：

| 组合模式                 | 描述                                 | 默认值 |
|--------------------------|--------------------------------------|--------|
| **休息 -> 精英**         | 精英战前直接是休息处                 | 0      |
| **精英 -> 休息**         | 休息处前直接是精英战                 | 0      |
| **宝箱 -> 精英**         | 精英战前直接是宝箱房                 | 0      |
| **休息 -> 任意 -> 精英** | 精英战前两格是休息处（中间隔一个结点） | 0      |
| **精英 -> 任意 -> 休息** | 休息处前两格是精英战（中间隔一个结点） | 0      |

所有加成默认值均为 0（即关闭）。当专家模式关闭时，评分系统与之前完全一致。该开关和各项加成均可通过 ModConfig 图形界面或 JSON 配置。

## 配置说明

### 图形界面设置（推荐）

RouteSuggest 可选集成 [**ModConfig**](https://github.com/xhyrzldf/ModConfig-STS2)。安装 ModConfig 后，可在游戏的 **设置 > 模组配置** 菜单中找到 RouteSuggest 的配置选项。如果未安装 ModConfig，Mod 仍可正常运行，但需要手动编辑 JSON 配置文件（见下文）。

通过 ModConfig 图形界面，你可以：

- **通用设置**：
  - **高亮类型**：选择高亮一条最优路线，或高亮所有得分相同的最优路线
    - **一条**：从所有最优路线中选择一条高亮
    - **全部**：高亮所有得分并列最高的路线
  - **启用专家评分**：全局开关，开启后应用相邻结点加成（影响所有路线）
- **配置每条路线**：
  - **启用**：开启或关闭该路线（关闭的路线不会被计算或显示）
  - **名称**：路线的标识名称
  - **颜色**：输入十六进制颜色代码（例如 `#FFD700` 表示金色，`#FF0000` 表示红色）
  - **优先级**：滑动条设置渲染优先级（数值越高，路线重叠时越靠上层显示）
  - **评分权重**：每种房间类型的滑动条
    - 正值 = 偏好该房间类型
    - 负值 = 避开该房间类型
    - 零 = 中立
  - **专家评分**：5 个相邻结点加成滑动条（休息->精英 等）
- **添加新路线**：滑动条设置为 1 以添加新路线
- **移除路线**：每条路线有一个滑动条用于移除（0=保留，1=移除）
- **恢复默认**：滑动条恢复所有路线的默认配置
- **更改自动保存**到配置文件位置（详见下文）

### 手动 JSON 配置

另外，你也可以通过手动编辑 `RouteSuggestConfig.json` 来自定义路线类型：

- **老用户**：如果你已有 `mods/RouteSuggestConfig.json` 配置文件，将继续使用（无需迁移）
- **新用户**：配置将保存在 `RouteSuggest.dll` 旁边（在 mods 文件夹中递归查找）。如果找不到 DLL，则回退到 `mods/RouteSuggestConfig.json`
- **注意**：配置会保存到读取时的同一位置。Mod 不会自动将配置文件迁移到其他位置。

```json
{
  "schema_version": 4,
  "highlight_type": "One",
  "expert_mode": false,
  "path_configs": [
    {
      "name": "Safe",
      "color": "#FFD700",
      "priority": 100,
      "enabled": true,
      "rest_before_elite_bonus": 0,
      "elite_before_rest_bonus": 0,
      "treasure_before_elite_bonus": 0,
      "rest_two_before_elite_bonus": 0,
      "elite_two_before_rest_bonus": 0,
      "scoring_weights": {
        "RestSite": 1,
        "Treasure": 1,
        "Shop": 1,
        "Monster": -1,
        "Elite": -2
      }
    },
    {
      "name": "Aggressive",
      "color": "#FF0000",
      "priority": 50,
      "enabled": true,
      "rest_before_elite_bonus": 0,
      "elite_before_rest_bonus": 0,
      "treasure_before_elite_bonus": 0,
      "rest_two_before_elite_bonus": 0,
      "elite_two_before_rest_bonus": 0,
      "scoring_weights": {
        "RestSite": 1,
        "Treasure": 1,
        "Shop": 1,
        "Monster": 1,
        "Elite": 2,
        "Unknown": 1
      }
    }
  ]
}
```

- **schema_version**：配置文件格式版本（当前版本为 4）
- **highlight_type**：`"One"`（选择一条最优路线）或 `"All"`（高亮所有得分相同的最优路线）
- **expert_mode**：设为 `true` 以启用相邻结点加成（影响所有路线配置）
- **name**：路线的标识名称（例如 "Safe"、"Aggressive"）
- **enabled**：设为 `false` 以禁用某条路线（禁用的路线不会被计算或显示）
- **color**：十六进制颜色代码（例如 `#FFD700` 表示金色，`#FF0000` 表示红色）
- **priority**：数值越高，路线重叠时越靠上层显示
- **scoring_weights**：每种房间类型的整数值（正值 = 偏好，负值 = 避开）
- **rest_before_elite_bonus**：休息处后紧跟精英战的加成
- **elite_before_rest_bonus**：精英战后紧跟休息处的加成
- **treasure_before_elite_bonus**：宝箱房后紧跟精英战的加成
- **rest_two_before_elite_bonus**：休息处后隔一个结点是精英战的加成
- **elite_two_before_rest_bonus**：精英战后隔一个结点是休息处的加成

可用房间类型：`RestSite`、`Treasure`、`Shop`、`Monster`、`Elite`、`Unknown`、`Boss`

如果配置文件缺失或无效，将使用默认路线配置。

### 社区配置

玩家们在 [Nexus Mods 讨论区](https://www.nexusmods.com/slaythespire2/mods/54?tab=posts) 分享了他们的自定义配置。可以去看看获取灵感，或找到适合你玩法的配置。

## 更新日志

### v1.9.0

- 测试通过游戏测试版 v0.99.1 和 v0.102.0
- 新增专家评分模式，支持相邻结点加成（schema version 4）
  - 全局开关控制专家评分的启用（影响所有路线）
  - 5 个相邻结点加成滑动条：休息->精英、精英->休息、宝箱->精英、休息->任意->精英、精英->任意->休息
  - 专家模式关闭时隐藏相关滑动条
  - 在 ModConfig 图形界面和 JSON 配置中均可用
  - 所有加成默认为 0，关闭专家模式时不影响原有评分
- 现在 Mod 集成 ModConfig v0.2.1 新增的 Button/ColorPicker 控件，如果可用则使用，旧版 ModConfig 自动回退

### v1.8.0

- 改进 ModConfig 界面：将部分滑动条改为开关，配置更便捷
- 为每条路线配置添加启用/禁用选项
  - 禁用的路线不会在地图上计算或显示
  - 在 ModConfig 图形界面和 JSON 配置中均可用
- 测试通过游戏测试版 v0.100.0

### v1.7.0

- 将 `affects_gameplay` 设为 false，因为该 Mod 纯为视觉效果，不影响实际游戏玩法

### v1.6.0

- 改进配置文件路径解析
  - 根据用户反馈：当 mods 文件夹中 Mod 较多时，根目录放配置文件会显得杂乱
  - 如果 `mods/RouteSuggestConfig.json` 已存在，则继续使用（无需迁移）
  - 否则递归查找 `RouteSuggest.dll` 并将配置保存在其旁边
  - 如果找不到 DLL 则回退到 `mods/RouteSuggestConfig.json`
  - 配置保存到读取时的同一位置（不会自动迁移）

### v1.5.0

- 新增 HighlightType 选项，控制高亮路线的数量
  - **一条**：从所有最优路线中选择一条
  - **全部**：高亮所有得分并列最高的路线
  - 在 ModConfig 图形界面和 JSON 配置中均可用

### v1.4.0

- 新增 ModConfig 图形界面设置集成，可在游戏中配置路线
  - 通过图形界面配置名称、颜色（十六进制输入）、优先级和评分权重
  - 添加/移除自定义路线类型
  - 更改自动保存到 JSON
  - 暂时使用滑动条代替按钮
  - 支持英文和简体中文国际化
- 测试通过游戏测试版 v0.99.1

### v1.3.0

- 调整安全/激进路线的默认权重
- 新增通过 `mods/RouteSuggestConfig.json` 配置自定义路线
- 路线配置支持自定义颜色、优先级和评分权重
- 配置文件缺失或无效时回退到默认值

### v1.2.0

- 修复进入新章节时的路线高亮问题

### v1.1.0

- 新增双路线模式：安全路线（金色）和激进路线（红色）
- 安全路线最小化战斗遭遇
- 激进路线优先选择精英战（+2）、普通战斗（+1）和未知房间（+1）
- 重叠的边显示为金色

### v1.0.0

- 初始发布
- 支持游戏版本 v0.98.3 和 v0.99
