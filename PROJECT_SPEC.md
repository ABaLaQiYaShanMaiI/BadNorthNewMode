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
7. **技术基线**：`net472` + `LangVersion 7.3`。**不能用 net35**（游戏那批 DLL（Assembly-CSharp/UnityEngine/BepInEx）元数据依赖 `mscorlib 4.0.0.0`，net35 会让 MSBuild 丢弃这些引用、编不过）；而**运行时 Mono 是 mscorlib 2.0.0.0（.NET 2.0/3.5 级别，没有 `Array.Empty`）**，所以 net472 下**严禁"params 空数组"调用**——`new KeyboardShortcut(KeyCode.F1)` 会被 Roslyn 优化成 `Array.Empty<T>()`，必须写 `new KeyboardShortcut(KeyCode.F1, new KeyCode[0])`，否则运行期 `MissingMethodException`。`build.ps1` 调用 `tools/check-api.ps1` 作为构建闸门自动拦截此类 API。

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

- **触发**：战局内按热键（默认 `F1`，cfg 可改）进入投放模式 → **点击滩头陆地**投放 1 艘敌舰；`Esc`/右键取消。
- **输入必须是陆地**（v0.2.0 起）：鼠标射线 × `LayerMaster.voxelMask`（原版 "Voxels" 层，和 `NavSpotter` 打地面同一个掩码）→ 命中点的**真实世界坐标（含海拔）**；水面/天空取不到 → 不响应（对齐原版"只有陆地可交互"）。
- **落差校验**（v0.2.0 起）：点击处海拔、以及最终落点滩头的海拔，与 `WaterLevelY`（默认 0 = 海平面）之差都必须 ≤ `MaxLandHeight`（默认 0.5m）→ 挡掉悬崖顶/高台地。
- **找滩头**：`island.beaches.GetBeachPositions(0.1f)`（按岛屿实例缓存，供鼠标预览每帧复用）中取 `distToEdge > 船半径`（同原版）、自身与海面齐平、离点击点最近的 `Pos`。
- **方向**：照抄原版 Raid 的公式 `dir = beachPos.dir + beachPos.navPos.pos.normalized * 0.3f`。
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
| T3 | ~~水面精确取点~~ → v0.2.0 已改为**点陆地地形**（`LayerMaster.voxelMask`），原问题消失 | 已完成；若某些岛地形没有 "Voxels" 层碰撞，代码有 `~0` 兜底并在 VerboseLog 打印 mask |
| T4 | `Spawn()` 中 `BatchedSprite.Awake()`/`CorpseManager.Precache()` 在运行中重复调用的副作用 | 单独实测；必要时改为延迟到帧末 |
| T5 | 非战局误触发 | 判 `Singleton<IslandGameplayManager>.instance` 与 `island.state` 后再响应输入 |

## 7. 里程碑与验收

- **M1** 热键 + ~~水面点击取点~~ → 已完成（v0.2.0 改为点击滩头陆地 + 高亮预览）
- **M2** 最近滩头求解 + 落差校验 + `TryPlace` 占位 → 已完成（可视化用 HUD 文本 + 光亮标记，不用发行版不可见的 Gizmo）
- **M3** `Spawn()` + `BeginWave()`：肉眼可见完整靠岸流程 → 代码就位，待实机确认
- **M4** cfg（热键/兵种/人数/速度/落差/标记）+ 失败提示 + 与既有 mod 共存回归 → 部分完成

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

## 9. 实现状态（v0.2.0，已编译并部署）

`BadNorthNewMode.dll` → `<BadNorthDir>\BepInEx\plugins\`（0 警告 0 错误，SHA256 校验 MATCH）。

**阶段范围（按作者要求收敛）**：热键 **F1**；敌人**只做一种**——最基础的普通小兵（剑兵 `Viking_Sword`，`EnemyName` 默认值）。想试别的兵种改 cfg 即可，代码不分兵种特化。

**v0.2.0 变更（本轮微调）**：
1. **输入从"点水面"改为"点滩头陆地"**：射线打 `LayerMaster.voxelMask`（原版 "Voxels" 层，`NavSpotter` 打地面同款），水面/天空一律不响应——对齐原版"只有陆地可交互"。
2. **落差/悬崖校验**：点击处与落点滩头的海拔都必须与 `WaterLevelY`（海平面）之差 ≤ `MaxLandHeight`（默认 0.5m），杜绝把船生成到悬崖或高台地上。
3. **光亮落点 UI**：新增 `PlacementMarker`——运行时生成环形+内芯贴图（零资源）、优先加法混合着色器、呼吸缩放闪烁、平铺在地面；**投放模式下鼠标扫过即实时预览**（亮青=可投放，暗红=不可投放），投放后加长显示。观感对齐技能落点高亮。

已实现：
- **触发**：热键 `KeyboardShortcut`（默认 `F1`）开关投放模式 → 左键点滩头陆地投放；右键 / `Esc` 取消；仅 `Island.State.Playing` 且 `island.raid != null` 时生效；指针在 UI 上时不响应（`EventSystem.IsPointerOverGameObject`）。
- **地形输入**：`Singleton<LevelCamera>.instance.cameraRef` 鼠标射线 × `LayerMaster.voxelMask` → 命中点真实坐标（含海拔）；拿不到 mask 时退 `~0` 兜底。
- **滩头选择**：`island.beaches.GetBeachPositions(0.1f)`（按岛缓存）中取满足「`distToEdge > 船半径`（同原版）+ 自身与海面齐平 + 离点击点最近」，且水平距离 `≤ MaxShoreDistance`。
- **敌人选取**：先查 `island.levelNode.enemies`；不在本关池里则退回 `LevelStateObjectReferences.dict` 取**同名**单位（保证"始终同一种小兵"不被随机化）；都取不到才随机并打警告。
- **投放**：原版对象树 `Wave → ShipGroup → Landing → ShipLoad`（**不进 `raid.waves`**，故不影响原版波次计时）→ `TryPlace(navPos, dir, speedMul, 全岛已放置 Landing 集合)` → `RefreshLandings()` → `Spawn()` → `raid.StartCoroutine(wave.BeginWave())`（原版协程：`Launch()` + 靠岸到达回调 + 取自 `VikingReference` 的 approach/arrive 音乐）。失败自动销毁已建对象并回报原因。
- **cfg**：`General`（Hotkey/ShowHud/EnemyName/SquadSize）、`Landing`（ShipSpeedMultiplier/MaxShoreDistance/MaxLandHeight/WaterLevelY/ShowHoverPreview/MarkerSeconds）、`Diag`（VerboseLog）。
- **HUD**：纯 `GUI` 文本（零资源），显示模式状态 + 悬停地形判定 + 上一次结果。
- **`.vscode/settings.json`**：把 .NET Install Tool 指向本机已装 `dotnet`（`existingDotnetPath`）+ 加大 `installTimeoutValue`，规避国内 CDN 导致的语言服务运行时下载超时。

目标框架（实测踩坑，必须记住）：游戏 `BadNorth_Data\Managed` 里是 **mscorlib 2.0.0.0 / System 2.0.0.0 / System.Core 3.5.0.0**，BepInEx 自报 `CLR runtime version: 2.0.50727.1433` → 运行期是 Unity 2018.4 的 **.NET 2.0/3.5 级别**。
- 只能编 **`net472`**：改 net35 后 MSBuild 对 Assembly-CSharp/UnityEngine/BepInEx/UnityEngine.UI 全部报 MSB3258（它们元数据里依赖 mscorlib 4.0.0.0，高于 2.0.0.0）并丢弃引用 → 40 个编译错误。
- 后果：net472 能编过、但**运行时缺 .NET 4.x 的 API**。已实测踩到的第一个：`new KeyboardShortcut(KeyCode.F1)`（params 空数组）被 Roslyn 优化成 `Array.Empty<T>()` → `MissingMethodException: Method not found: 'System.Array.Empty'`，表现为"BepInEx 说插件已加载，但游戏内毫无反馈"（Awake 抛异常后组件不再被驱动）。修法：显式传 `new KeyCode[0]`。
- 现已加**构建闸门** `tools/check-api.ps1`（`build.ps1` 第 3 步调用）：用 Mono.Cecil 逐一核对模组 DLL 对 mscorlib/System/System.Core 的全部成员引用是否存在于**游戏自带**的同名程序集，缺一个就让构建失败。当前 33 个引用全通过。
- 另：`<GenerateTargetFrameworkAttribute>false</GenerateTargetFrameworkAttribute>` 关掉了 `[assembly: TargetFramework]`（该特性类型在 mscorlib 2.0 里没有；虽属无害（CLR 仅反射时解析特性），关掉让闸门完全干净）。

与 mod 无关的原版报错（勿误判）：`[Unity Log] Not enough beach!` 是原版岛屿生成器的正常拒绝信息；随后的 `NullReferenceException`（`Island.get_meshPool` → `MeshMerger2.OnIslandDestroy` → `CampaignManager.ClearCampaign`，以及 `Fake3dTex.GetIndex` → `Painter.Paint` → `IslandGenerator`）是**原版"岛屿生成中途退出/清场"的竞态**，链路里没有任何本 mod 的类型。旧版插件 Awake 失败时插件完全未运行，因此那两次 F1 与这些报错无因果关系。

两处与原计划的有意偏差：
1. **不使用 Harmony / MonoMod 补丁**：本机制只需"构造原版对象 + 触发原版协程"，零补丁即零侵入，也不与既有 mod 抢补丁点。
2. **M2 的 Gizmo 可视化改为 HUD 文本 + 日志**：发行版没有 Unity 编辑器，Gizmo 只在编辑器可见，对实机验证无用。

仍须游戏内实测（源码静态分析覆盖不到）：T1（投放波次对 `AllWavesLaunched()`/结算与 UI 进度的影响）、T4（运行期 `Spawn()` 的副作用）、以及 §7 的验收；另外需实机确认 v0.2.0 的三项手感——陆地判定是否顺手、`MaxLandHeight` 默认 0.5m 是否过严/过松、光亮标记的可见度与时长。


