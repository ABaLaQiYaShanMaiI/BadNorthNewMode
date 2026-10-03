# BadNorthNewMode — 项目约束与说明

本文件是本项目唯一的需求/约束来源。新增说明只在确有需要时追加到 `开发日志.md`，不再新建第三个文档。

## 1. 目标

研究 Bad North 的**游戏新机制探索**：在**正常战局内**（非编辑器、非自定义关卡），通过**按键 + 点击水面方格**，让敌方单位以**原版方式**自然出现——即走完"敌舰从海面驶来 → 靠岸停靠 → 舱内敌人下船 → 转入常规 AI 战斗"的**完整船只靠岸生成过程**。

## 2. 硬性约束

1. **以 DLL 类模组为主**：只交付 BepInEx 插件 DLL；不新增 AssetBundle / 贴图 / 音效 / 预制件资产（必要时仅内嵌极小资源）。
2. **不改游戏本体文件**：`Assembly-CSharp.dll` 保持原样，只用运行时 Hook（Harmony / MonoMod `On.`）。
3. **复用原版逻辑**：不自行实现航行、靠岸、下船、AI；必须由原版 `Longship` / `Pirate` / `Squad` / `Brain` 承担，mod 只负责"构造 + 触发"。
4. **文档从简**：全项目只保留本文件（+ 可选极简 `开发日志.md`）；代码内只写"为什么"的必要注释，不写逐行解释。
5. **场景隔离**：仅在战局（island gameplay）中生效；战役地图/主菜单不响应。
6. **互不干扰**：不与既有 mod（BadNorthBlackSpearman 1.3、BadNorthMixedSquad 1.0）冲突；不干扰原版波次计时与关卡结算。
7. **技术基线**：`net472` + `LangVersion 7.3`（与参考工程一致，兼容 Unity Mono）。

## 3. 环境与参考（已实测）

| 项 | 值 |
|---|---|
| 游戏目录 | `D:\Steam\steamapps\common\BadNorth` |
| 运行时 | Unity **Mono** + BepInEx 5（`winhttp.dll` / doorstop 已装） |
| 必引 DLL | `BadNorth_Data\Managed\Assembly-CSharp.dll`、`UnityEngine*.dll`、`BepInEx\core\BepInEx.dll`、`0Harmony.dll`、`BepInEx\plugins\MMHOOK-Assembly-CSharp.dll` |
| 构建 | `dotnet build -p:BadNorthDir=<游戏根目录>`，产物复制到 `BepInEx\plugins\` |
| 反编译源码（只读参考） | `C:\Users\ABaLaQiYaShanMaiI\Desktop\BadNorthDatabase-main\src\Assembly-CSharp`（下文路径均以此为根） |
| 同类范例 | `C:\Users\ABaLaQiYaShanMaiI\Desktop\BadNorthEnemy-main`（MonoMod `On.` 写法、csproj/目录规范） |

## 4. 原版事实（已核对源码，可直接依赖）

**生成链（战局开启时一次性预生成）**

```
Island.raid (Voxels.TowerDefense.Raid)
 ├ IIslandFirstEnter : 建 Wave → ShipGroup → Landing → ShipLoad；填 vikingRef/count
 │   选滩头 island.beaches.GetBeachPositions(0.1f) → Landing.TryPlace(navPos, dir, speedMult, placedLandings)
 ├ IIslandPlay       : 对每个 Landing 调 Spawn()  ← 预生成 Longship（船体 SetActive(false)）+ 舱内全部 Agent
 └ MaybeLaunchWaves()（每帧）: timer ≥ wave.waveStartTime → BeginNextWave() → wave.BeginWave()（协程）→ Landing.Launch()
Landing.Spawn()      : Instantiate(shipPrefab) → Longship.Setup(this)
                       → 每个 ShipLoad.count 次：船内 navMesh.bounds 随机 navPos
                       → squad.CreateAgent(vikingRef.agent, navPos)
                       → agent.GetOrAddComponent<Pirate>().AddToLongship(ship)
                       → agent.GetOrAddComponent<VikingAgent>().vikingReference = vikingRef
Landing.TryPlace()   : ShipTravel(起航点 = 滩头 + dir*50, 终点 = 滩头) + 立方体占位互斥 + SphereCast(moduleMask)
Pirate.Update        : 船上待命 → agent.navPos.island 成立 → RemoveFromShip() → brain.PickNewOrder() 转常规战斗
```

**关键成员**（均为 public）：`Landing.dir/pos/navPos/placed/shipTravel/shipPrefab/shipLoads`、`ShipLoad.vikingRef/count`、`ShipGroup.wave/landings/squad`、`Wave.raid/waveStartTime/triggered`、`Raid.waves/landingContainer`、`Island.raid/beaches/navMesh/levelNode.possibleShips/levelNode.enemies`、`IslandGameplayManager.instance.island`、扩展方法 `GameObject.AddEmptyChild(name)`。

**强制依赖链**：`Landing.Spawn()` 内部读 `shipGroup.squad`，而 `ShipGroup.squad` 走 `wave.raid.island.vikings`。因此投放用的对象树**必须是 `Wave → ShipGroup → Landing → ShipLoad`，且 `wave.raid = island.raid`**，否则必然空引用。

**点击链**：`ClickPasser.OnPointerClick(PointerEventData)` → `ClickPasser.GetHitAtMouse(eventData, clickMask0)`（内部 `Singleton<LevelCamera>.instance.cameraRef.ViewportPointToRay`）→ 命中体上的 `IPassedClick.OnPassedClick`。
**注意**：输入走 Rewired（无 `UnityEngine.Input.GetMouseButtonDown`）；水面没有 navmesh，命中不到 `IPassedClick`，取点需自行计算。

## 5. 机制设计

- **触发**：战局内按热键（默认 `F9`，cfg 可改）进入投放模式 → 点击水面方格投放 1 艘敌舰；`Esc`/右键取消。
- **水面取点**：`LevelCamera.instance.cameraRef` 射线与海平面求交（备选：对水面层 Raycast）得 `worldPos`。
- **找滩头**：`island.beaches.GetBeachPositions(0.1f)` 中取离点击点最近、`distToEdge` 足够且未被占用的 `Pos`。
- **方向**：`dir = (点击点 - 滩头).GetZeroY().normalized`（滩头指向海面，即原版 `Landing.dir` 语义）。
- **生成（必须沿用原版路径，不得自建私有航行/生成实现）**：

```csharp
var raid = island.raid;                       // 原版 Raid 组件
var wave = raid.landingContainer.gameObject.AddEmptyChild("ModWave").AddComponent<Wave>();
wave.raid = raid; wave.waveStartTime = 0f;    // 与计时波次解耦，见 T1
var group = wave.gameObject.AddEmptyChild("Group").AddComponent<ShipGroup>();
wave.AddShipGroup(group);
var landing = group.gameObject.AddEmptyChild("Landing").AddComponent<Landing>();
group.AddLanding(landing);
landing.Init(island);
landing.shipPrefab = <island.levelNode.possibleShips 中 area ≥ load.area 的第一艘>;
var load = landing.gameObject.AddEmptyChild("Load").AddComponent<ShipLoad>();
load.vikingRef = <island.levelNode.enemies 中指定/随机>;
load.count = cfg.squadSize;
landing.AddShipLoad(load);
if (!landing.TryPlace(beachPos.navPos, dir, 1f, existingLandings)) { 提示失败; return; }  // existingLandings 见 T2
landing.Spawn();    // 预生成船 + 舱内敌人
landing.Launch();   // 激活 → 原版航行/靠岸/下船/战斗
```

- **结算一致性**：舱内 `VikingAgent` 计入 `island.vikings.agents`，故 `Raid.AllEnemiesDefeated()` 会等玩家清完这批敌人才通关（符合"自然"要求）。

## 6. 待验证风险（实施时逐条实测，禁止凭猜写码）

| 编号 | 风险 | 候选做法 |
|---|---|---|
| T1 | 投放船挂在自己新建的 `Wave` 上后，`Raid.waves` 列表不含它 → `AllWavesLaunched()`/结算与 UI 进度是否受影响；直接 `Launch()` 会跳过 `Wave.BeginWave()` 内的 approach/arrive 音频与 `OnShipArrival` 回调 | 优先挂到 `raid.waves` 末波（若语义允许）；或新建 wave 但不入 `raid.waves`，仅用于满足 `squad` 链，`waveStartTime = 0`；音效需完整则改调 `wave.BeginWave()` 协程 |
| T2 | `TryPlace` 的 `existingLandings` 在原版只是生成期局部 list | 运行时自建集合，装入全部已存在 `Landing.placed` 者 |
| T3 | 水面精确取点方式 | 优先相机射线 × `y = 0` 平面；否则对水面层 Raycast |
| T4 | `Spawn()` 中 `BatchedSprite.Awake()`/`CorpseManager.Precache()` 在运行中重复调用的副作用 | 单独实测；必要时改为延迟到帧末 |
| T5 | 非战局误触发 | 判 `Singleton<IslandGameplayManager>.instance` 与 `island.state` 后再响应输入 |

## 7. 里程碑与验收

- **M1** 热键 + 水面点击取点（只打日志，不生成）
- **M2** 最近滩头求解 + `TryPlace` 占位（Gizmo 可视化）
- **M3** `Spawn()` + `Launch()`：肉眼可见完整靠岸流程
- **M4** cfg（热键/船型/兵种/人数/冷却）+ 失败提示 + 与既有 mod 共存回归

验收标准：
1. 战局内 `F9` + 点水面 → 立刻有船自海面驶来；
2. 船沿 `TryPlace` 计算的航向靠岸停靠，敌人下船并转入常规 AI；
3. 原版波次计时不受影响，清完投放敌人后关卡可正常结算；
4. 开关关闭时与原版无差异；与黑矛兵 / 混合编队同时加载不崩；
5. 无 Unity 报错，日志只留 1~2 行摘要。

## 8. 命名与提交约定

- DLL / 程序集：`BadNorthNewMode.dll`；命名空间 `BadNorthNewMode`；插件 GUID `badnorth.newmode`。
- 部署路径：`<BadNorthDir>\BepInEx\plugins\BadNorthNewMode.dll`。
- 文档纪律：只本文件（+ 可选 `开发日志.md`）；不写长注释、不做文档膨胀。

## 9. 实现状态（v0.1.0，已编译并部署）

`BadNorthNewMode.dll` → `<BadNorthDir>\BepInEx\plugins\`（0 警告 0 错误，SHA256 校验 MATCH）。

**阶段范围（按作者要求收敛）**：热键 **F1**；敌人**只做一种**——最基础的普通小兵（剑兵 `Viking_Sword`，`EnemyName` 默认值）。想试别的兵种改 cfg 即可，代码不分兵种特化。

已实现：
- **触发**：热键 `KeyboardShortcut`（默认 `F1`）开关投放模式 → 左键点水面投放；右键 / `Esc` 取消；仅 `Island.State.Playing` 且 `island.raid != null` 时生效；指针在 UI 上时不响应（`EventSystem.IsPointerOverGameObject`）。
- **水面取点**：`Singleton<LevelCamera>.instance.cameraRef` 的鼠标射线 × `y = WaterLevelY`（默认 0）平面。
- **滩头选择**：`island.beaches.GetBeachPositions(0.1f)` 中取满足「`distToEdge > 船半径`（同原版）+ 方向与滩头朝海法线点积 `≥ MinOutwardDot`（判掉点在岛上/海湾内侧）+ 离点击点最近」，且距离 `≤ MaxShoreDistance`。
- **敌人选取**：先查 `island.levelNode.enemies`；不在本关池里则退回 `LevelStateObjectReferences.dict` 取**同名**单位（保证"始终同一种小兵"不被随机化）；都取不到才随机并打警告。
- **投放**：原版对象树 `Wave → ShipGroup → Landing → ShipLoad`（**不进 `raid.waves`**，故不影响原版波次计时）→ `TryPlace(navPos, dir, speedMul, 全岛已放置 Landing 集合)` → `RefreshLandings()` → `Spawn()` → `raid.StartCoroutine(wave.BeginWave())`（原版协程：`Launch()` + 靠岸到达回调 + 取自 `VikingReference` 的 approach/arrive 音乐）。失败自动销毁已建对象并回报原因。
- **cfg**：`General`（Hotkey/ShowHud/EnemyName/SquadSize）、`Landing`（ShipSpeedMultiplier/MaxShoreDistance/MinOutwardDot/WaterLevelY）、`Diag`（VerboseLog）。
- **HUD**：纯 `GUI` 文本（零资源），显示模式状态与上一次结果。
- **`.vscode/settings.json`**：把 .NET Install Tool 指向本机已装 `dotnet`（`existingDotnetPath`）+ 加大 `installTimeoutValue`，规避国内 CDN 导致的语言服务运行时下载超时。

目标框架说明：模组编译为 **`.NETFramework,Version=v4.7.2`**（mscorlib 4.0.0.0），与 Unity 2018 Mono 同代，**与 .NET 10 无关**；本机的 .NET 10 SDK 只是编译器/构建工具。

两处与原计划的有意偏差：
1. **不使用 Harmony / MonoMod 补丁**：本机制只需"构造原版对象 + 触发原版协程"，零补丁即零侵入，也不与既有 mod 抢补丁点。
2. **M2 的 Gizmo 可视化改为 HUD 文本 + 日志**：发行版没有 Unity 编辑器，Gizmo 只在编辑器可见，对实机验证无用。

仍须游戏内实测（源码静态分析覆盖不到）：T1（投放波次对 `AllWavesLaunched()`/结算与 UI 进度的影响）、T3/T4（水面点精度、运行期 `Spawn()` 的副作用）、以及 §7 的五条验收。


