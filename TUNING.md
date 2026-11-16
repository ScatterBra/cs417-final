# Buliding_Game 调参与素材导入手册

## 一、Inspector 可调参数

### BuildSystem 对象 → GridSystem 组件
| 参数 | 默认值 | 作用 |
|------|--------|------|
| Width / Height | 20 / 15 | 地皮尺寸（格子数） |
| Cell Size | 1 | 每格边长（米），家具子网格 = 它的 1/4 |
| Max Levels | 4 | 楼层上限 |
| Wall Height | 2.5 | 层高/墙高（米），改后所有构件按新值重建 |
| Wall Thickness | 0.12 | 墙厚 |
| Door Height | 2.0 | 门洞高度 |
| Window Sill / Head Height | 0.9 / 2.1 | 窗台/窗楣高度 |
| Roof Pitch | 0.6 | 屋顶坡度（离屋檐每进一格升高多少米），越大越陡 |
| Level Fly Height | 6 | 切楼层动画的飞行高度 |
| 各 Material 槽位 | Assets/Materials/*.mat | 换材质引用即换全局外观 |

### BuildSystem 对象 → BuildManager 组件
| 参数 | 默认值 | 作用 |
|------|--------|------|
| Fly Duration | 0.3 | 切楼层动画时长（秒） |
| Save Slot | building | 手动存档文件名（autosave 为自动档） |

### BuildSystem 对象 → WalkController 组件（第一人称）
| 参数 | 默认值 | 作用 |
|------|--------|------|
| Move Speed | 3 | 行走速度（米/秒） |
| Eye Height | 1.55 | 视线高度 |
| Look Speed | 0.2 | 转视角灵敏度（鼠标/触屏共用） |

### Main Camera → IsoCameraController 组件
| 参数 | 默认值 | 作用 |
|------|--------|------|
| Pitch | 30 | 等距视角俯仰角 |
| Min / Max Ortho Size | 2.5 / 16 | 缩放范围（越小越近） |
| Start Ortho Size | 9 | 初始缩放 |
| Pan Speed | 8 | WASD 平移速度 |

## 二、颜色与材质（Assets/Materials/）

7 个材质球，点开改 **Base Map** 颜色即全局生效（含已放置构件）；
也可以把 Base Map 换成贴图：
Wall（墙）、Floor（地板）、Ground（草地）、GridLine（网格线）、
Frame（门板/窗框）、Glass（玻璃）、Roof（屋顶坡面）。

## 三、导入自己的素材

### 家具模型（最常用入口）
1. FBX 放到任意目录（建议 `Assets/Models/Furniture/`）
2. 打开 `Assets/Resources/Furniture/` 里对应的家具资源（如 Bed.asset），
   把模型拖进 **Prefab** 槽位 → 占位方块立即替换为模型
3. 新增家具：Project 右键 → Create → Building → Furniture Item，
   **必须放在 `Assets/Resources/Furniture/` 目录**（运行时按目录加载），
   填好 Display Name / Size / Color / Prefers Wall

**模型规范**（务必遵守，否则吸附/碰撞错位）：
- 轴心在**底面中心**
- **+Z 为家具正面**
- 真实米制尺寸
- Size 字段照实填（决定占地/防穿模/幽灵框），x/z 取 **0.25 的倍数**
- 模型上保留（或加一个）Collider——第一人称碰撞和"指哪打哪"拾取都靠它

### 地板样式（同一关卡多种地板）
样式目录在 `Assets/Resources/FloorStyles/`（Wood/Tile/Stone/Carpet/Terracotta）。
- 改颜色：点开样式资源改 Color；贴图：把材质拖进 Material 槽位
- 新样式：Project 右键 → Create → Building → Surface Style，放进该目录即出现在游戏里
- 游戏内切换：选 Floor 工具弹出样式条 → 点色块 → 拖矩形，空格子铺新砖、已有砖直接重刷
- 默认样式 = 名为 "Wood" 的资源（改名会退回目录第一个）

### 墙 / 门墙 / 窗墙样式（按构件分目录，每个样式=一个独立模型）
三个目录**互相独立**，选中对应工具时弹出各自的样式条：
- `Assets/Resources/WallStyles/`：实墙样式（White白墙/Brick红砖/WoodPanel木板/Concrete水泥）
- `Assets/Resources/DoorStyles/`：门墙样式（Wood木门/White白门）
- `Assets/Resources/WindowStyles/`：窗墙样式（Standard标准窗/Black黑框窗）

每个样式资源（Surface Style，和地板同一个类）：Display Name / Color /
可选 Material 槽 / **Prefab 模型槽**（一个样式一个模型）。
- 无模型时程序化配色：实墙样式刷整面墙；门样式刷门板；窗样式刷窗框
- 游戏内切换：选 Wall/Door/Window 工具 → 各自样式条 → 点空边建该样式构件、
  点已有**同类**构件直接重刷；门/窗工具点实墙 = 开洞+套样式一步完成
- 新样式：Create → Building → Surface Style，放进对应目录即出现在游戏里
- 默认样式按名字优先：墙 "White" / 门 "Wood" / 窗 "Standard"，缺失则目录第一个
- 优先级：样式 Prefab 槽 > GridSystem 全局模型槽 > 程序方块（样式颜色着色）

### 墙体模型（实墙/门墙/窗墙）
Hierarchy 选 `BuildSystem` → GridSystem → **Wall Prefabs** 三个槽位，
把模型 prefab 拖进去即全局生效；槽位留空 = 程序方块兜底。
已放置的墙在下次进 Play / 读档时自动换成新模型。

**建模规范**（默认参数下）：
- 轴心在**底面中心**（地面线、1m 墙段的中点），墙体沿 **+X 轴**延伸
- 尺寸：长 1.12m（两端各含 0.06m 搭角）× 高 2.5m × 厚 0.12m
- 门洞参考 0.85m 宽 × 2.0m 高；窗洞参考 0.7m 宽、窗台 0.9m、窗楣 2.1m
  ——洞口造型随模型自由发挥，这些只是和程序版对齐的参考
- 模型上保留 Collider 最佳；没有时系统自动补
  （门墙自动补"门柱+门楣"三块，门洞保证能走人）

### 楼梯模型（暂为程序化生成）
生成代码在 `GridSystem.BuildStairChildren`，换 prefab 需小改代码（找 Claude）。

## 四、程序化生成的构件清单

以下内容**没有模型文件**，全部由代码在运行时拼装或生成网格。
想换成自己的模型时，替换点在"代码位置"一栏（家具已支持 prefab 槽位无需改码）。

| 构件 | 生成方式 | 代码位置（GridSystem.cs 内） | 换模型 |
|------|---------|------------------------------|--------|
| 实墙 | 1 个立方体（兜底） | `BuildWallChildren` (WallOpening.None) | **免改码**：GridSystem→Wall Prefab 槽位 |
| 带门墙 | 门柱×2+门楣+门板（兜底） | `BuildWallChildren` (Door) | **免改码**：Door Wall Prefab 槽位 |
| 带窗墙 | 墙体+窗框+玻璃 6 块（兜底） | `BuildWallChildren` (Window) | **免改码**：Window Wall Prefab 槽位 |
| 地板砖 | 1 个立方体 + 样式材质 | `PlaceFloor` / `RebuildIndexFromChildren` | 改码替换 |
| 楼梯 | 8 级实心踏步立方体 | `BuildStairChildren` | 改码替换 |
| 坡屋顶 | **纯程序网格**：洪水填充识别轮廓 + 棋盘距离场决定高度，逐格三角扇+底面，硬边法线 | `BuildHipRoofMesh` | 不适合换模型（形状自适应轮廓） |
| 屋顶预览绿色平面 | 平面网格 | `BuildFootprintPlane` | — |
| 家具占位盒 | 立方体按目录尺寸缩放 | `RebuildFurnitureVisual` | **免改码**：拖 prefab 进目录资源 |
| 草地地皮 | 1 个大立方体 | `BuildGround` | 改码替换 |
| 网格参考线 | 线段拓扑网格（MeshTopology.Lines） | `BuildGridLines` | — |
| 幽灵预览（绿/红） | 立方体/楼梯形/平面 + 运行时透明材质 | BuildManager | — |
| 兜底材质 | Inspector 材质槽为空时代码生成纯色 URP Lit；家具色块/地板样式的纯色材质同理 | `CreateLitMaterial` 等 | 填材质槽即覆盖 |
| 全部 UI | 运行时代码构建 Canvas（无 prefab） | GameUI.cs | — |
| 任务书 Brief | 程序化模板随机组合（非几何） | Brief.cs `BriefGenerator` | — |

**非程序化**（普通资源文件，直接在 Unity 里编辑）：材质（Assets/Materials）、
家具目录与地板样式（Assets/Resources/Furniture、FloorStyles）、字体（Resources/Fonts）。

## 五、只能改代码的常量（需要时报给 Claude）
- 门洞宽比例 0.85、窗宽比例 0.7（GridSystem.BuildWallChildren）
- 楼梯级数 8（GridSystem.BuildStairChildren）
- 家具贴墙磁吸距离 0.35m（GridSystem.SnapFurnitureToWall）
- 幽灵预览红/绿颜色（BuildManager.CreateGhostMaterial 调用处）
- UI 配色/基准分辨率 1600×900（GameUI 顶部常量）
- 第一人称 FOV 70（WalkController.EnterWalk）

## 六、操作速查
1-8 选工具（8=家具）· R 旋转楼梯/家具 · [ ] 切层 · Tab 总览 · V 鸟瞰
F 第一人称 · Q/E 转视角 · WASD 平移/行走 · 滚轮或 -/= 缩放
F5 存档 · F9 读档 · 退出 Play 自动存档
存档位置：~/Library/Application Support/DefaultCompany/Buliding_Game/
