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

**陆地点击链路（v0.2.1 实测确认，改输入前必读）**
```
Unity EventSystem（StandaloneInputModule）
  → PointerRationalizer（全屏手势接收器，实现 IPointer*Handler；状态机 None/Hover/ButtonDown/Dragging）
  → 发布事件 onClick(button, screenPos) / onButtonDown / onDrag …（类型是 System.Core 3.5 的 Action`2）
  → 订阅者：Navigator（选中/移动小队）、ConfirmButton（确认按钮）、CameraController（镜头拖动）
  → 换算地面点：NavSpot.NavSpotCast(screenPos, out RaycastHit hit) → hit.point
     （内部 = ViewportPointToRay(归一化屏幕坐标) + "Voxels" 层 + "Modules" 层各打一发）
```
三个由此得出的硬约束：
1. **不要用 `EventSystem.IsPointerOverGameObject()` 判断"点 UI"**：这游戏所有世界交互都走 EventSystem，指针几乎恒为"over 某对象"，该判断会把点击**静默吞掉**（这是 v0.2.0 "点了没反应且无任何日志"的真凶）。
2. **不要直接 `pointerRationalizer.onClick += 回调`**：该事件类型是 `System.Core 3.5` 的 `System.Action`2`，而 net472 编译时 C# 把它绑到 `mscorlib`，游戏运行时的 mscorlib 2.0 没有它 → `MissingMethodException`。必须用反射 `GetEvent("onClick")` + `Delegate.CreateDelegate(ev.EventHandlerType, this, mi)` + `ev.AddEventHandler(...)`。
3. **不要对 `MemberInfo`/`EventInfo`/`MethodInfo` 用 `== null`**：这些类型的 `op_Equality` 是 .NET 4.0 才加的，mscorlib 2.0 没有；用 `object.ReferenceEquals`。

**输入/生成的正确取点顺序（本 mod 采用）**：`island.navSpotter.NavSpotCast(screenPos, out hit)` → `hit.point`（原版路径）；失败再退 `ViewportPointToRay(归一化) × LayerMaster.voxelMask`（"Voxels" 层）；再失败不限层。

**下船（disembark）机制（v0.2.2 实测确认）**
```
Agent.Spawn()（由 Longship.SpawnRoutine 逐帧调用，随船行进而推进）
   └ 各 AgentComponent / Brain.Setup()
        Brain.Setup(): actions.AddRange(GetComponentsInChildren<IBrainAction>())
                       orderList.AddRange(GetComponentsInChildren<IAgentOrder>()) → PickNewOrder()
船到岸：Longship.UpdateIncoming 在 interpolator ≥ 1 时 animator.SetTrigger(landId)
   → 动画事件 Longship.LandAnimComplete() → landed = true（并且 agents 为空时 enabled=false、Invoke 1s 后关动画器）
Pirate.MaybeAct(brain)：条件 longship && longship.landed && agent.orderDist < 0.01
   → navPos = landing.navPos（岛屿网格）；navPos.wPos = agent.navPos.transform.TransformPoint(pos - border*0.3)
   → RemoveFromShip() → longship.RemoveAgent(agent) + brain.RemoveAction(pirate) + brain.PickNewOrder()
另有：Pirate.AddToLongship 里挂 body.hopping.OnUpdate → PirateUpdate()（条件 agent.navPos.island != null 就下船）
空船撤离：agents 清空且 landed → Longship.enabled=false；随后 Longship.Launch()（outgoing=true，倒放 interpolator）驶离
```
⚠️ **中途投放为什么不下船（v0.2.4 实证，之前的推测已纠正）**：日志实测 `brainOrder=KillAllEnemies(是Pirate=False)`、`orderDist=1000000`、`brainActions 含 Pirate=True`。
即 **Pirate 已被 Brain 收集（早先"Brain.Setup 早于挂载"的猜测不成立）**，真正的问题是 **order 被 `KillAllEnemies` 抢走**：
- `KillAllEnemies.WantsControl() = agent.faction.enemy.agents.Count > 0`（有英军就抢）；它在 `orderList` 里比运行期才加的 Pirate 更靠前 → `PickNewOrder()` 选中它 → `SampleOrder` 给出流场哨兵值 `orderDist=1000000` → `Pirate.MaybeAct` 的 `orderDist < 0.01` 永不成立 → 不下船。
- 原版之所以天然正确：船与敌人在**关卡生成期**就备好，那时我方尚未部署，`KillAllEnemies.WantsControl()` 为假 → `PickNewOrder` 选中 Pirate；一旦拿到，`PickNewOrder` 只在 `order` 不再 `WantsControl()` 时才换（`Pirate.WantsControl() = longship != null`，乘船期间恒真）→ 一直保持。
- **修法**（`LandingInjector.AttachPirateOrder`）：`landing.Spawn()` 之后立刻把每个敌人的 `brain.order`（与 `orderMono`）设为它的 `Pirate`，之后完全走原版节奏——航行中未 `landed` 不下船，靠岸后走到船头 `orderDist→0` 才跳下（与原版同速）；下船时 `longship` 置空 → `WantsControl` 变假 → `PickNewOrder` 自动切回 `KillAllEnemies` 冲锋。

✅ **"乘船途中照常射击"是原版自带的、且与本 mod 不冲突**：敌人的弓箭手/巨弓手（`Archery : Brain`，它本身就是 Brain）由自己的状态机驱动开火，**与 Pirate / order 系统无关**，所以船在航行中照样射箭；`DisembarkWatchdog` 只在"到岸 + 宽限期之后"才动手，航行阶段完全不碰任何东西。下船后 `RemoveFromShip()` 只从 `brain.actions` 摘掉 Pirate 并 `PickNewOrder()`，**Archery 作为 Brain 本体继续工作**，所以"边射边下船、下船后继续射"都成立。

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

**v0.2.4 变更（下船恢复原速 + 投放失败率）**：
1. **下船延迟根因修复**：见 §4 下船机制——order 被 `KillAllEnemies` 抢走使 `orderDist=1000000`，原版下船链根本没启动，之前是靠看门狗 3 秒兜底才下船（所以觉得"慢"）。现在 `landing.Spawn()` 后立刻 `AttachPirateOrder()` 把 order 交还 `Pirate` → **完全按原版节奏走到船头再跳下**，`DisembarkWatchdog` 退化为纯安全网（正常情况下不再触发）。
2. **投放失败率修复**：实测失败样例命中的是 `MeshColliders@Modules`（建筑/岩石模块），而原版 `Landing.TryPlace` 最后一步 `Physics.SphereCast(..., LayerMaster.moduleMask)` 会因**进近廊道被 Modules 挡住**返回 false；原版在 `Raid.IIslandFirstEnter` 里是**遍历候选滩头反复 TryPlace**。现在 `TryResolve` 收集"岸线余量足够 + 与海面齐平 + 距点击处 ≤ MaxShoreDistance"的候选并按距离排序，`CorridorClear()` 预检廊道（复刻 TryPlace 那一步，供悬停预览准确显示可/不可投放），`TrySpawn` 再逐个 `TryPlace` 直到成功；全失败才报"附近 N 个滩头都被地形/建筑挡住或被占用"。
3. 新增诊断：`VerboseLog=true` 时打印候选滩头数量、首选廊道是否通畅、最终采用第几个候选。

**v0.2.3 变更（幽灵船 + 自动清场 + 结算语义）**：
1. **修复"退出战局后船残留 / 出现在同一岛下一场战局"**：新增 `SpawnLedger` 登记每次投放的根节点，**两条收场路径都自动清场**：
   - **战局结束**（胜/败/撤离/放弃）：订阅原版 `EndOfLevel.postProcess`（`Action<Island>`，作用域 mscorlib 2.0 → 可直接订阅；`preProcess` 是 `Action`2`（System.Core）才需反射），四条路径都经 `ProcessEOL` 触发它 → 立刻销毁并打日志（带 `Reason`：None/Won/Wiped/Fled）。
   - **中途退出到地图/主菜单**：`island.state != Playing`（`LeaveIsland` 会置 Idle）、岛屿换实例、或 `island.raid == null` → 同样统一销毁。
   成因（源码证实）：`Faction.OnIslandWipe` 会销毁**所有 Agent**并清表，但**长船不是 Agent** —— 它只随 `Raid.IIslandWipe` 里对 `raid.waves` 各 landing 的 `Reset()`（销毁 `spawnedShip`）被清掉；我们的 Wave 有意不在 `raid.waves`，于是残留的是**空船**。
2. **新增一键清场热键** `General.CleanupHotkey`（默认 `F2`）：销毁本 mod 投放过的全部船/单位，用于"生成了大量敌人清不完"的调试收尾。安全依据：`Agent.OnDestroy` 会自行 `faction.agents.Remove(this)`。
3. **结算语义（实测确认，不是 bug）**：`IslandWinConditions.Update() → AllEnemiesDefeated() = raid.AllWavesSpawned() && island.vikings.agents.Count == 0`。我们投放的单位是真的维京人（`Squad.CreateAgent` 把 `faction` 设为 `island.vikings`，`Agent.spawned` 激活时加入 `faction.agents`），因此**只要还有未击杀的投放单位，关卡就不会结算**（这正是"自然生成"的应有代价）；要收尾就用 `F2` 清场或让玩家把它们清掉。

**v0.2.2 变更（修复"敌人不下船"）**：
1. 新增 `DisembarkWatchdog`（只盯本 mod 投放的船）：船到岸（`landed` 或 `interpolator ≥ 0.999`）超过 `DisembarkGrace` 秒仍有人在船上 → 判定卡住。
2. 卡住时先打**完整诊断**（`longship` 的 interpolator/landed/enabled/agents/haveAllSpawned/animator + 每个敌人的 `navPos.island`、`orderDist`、`spawned`、`brain.actions` 是否含 `Pirate`、`brain.order` 是不是 `Pirate`），用来确定到底卡在哪一环。
3. 然后**兜底下船**（`DisembarkFix`，默认开）：照抄 `Pirate.MaybeAct` 后半段——补登记 `brain.actions`/`brain.order`（等价于 `Brain.Setup` 时就有 Pirate）+ 把 `agent.navPos` 换成 `landing.navPos` 那套岛屿坐标 + 调公开的 `Pirate.PirateUpdate()`，由原版 `RemoveFromShip()` 完成下船。
4. 新增 cfg：`Landing.DisembarkGrace`（默认 3s）、`Landing.DisembarkFix`（默认 true；设 false 可只诊断不动手，做对照）。

**v0.2.1 变更（修复"点了没反应"）**：
1. **输入改为订阅原版世界点击事件**：反射订阅 `IslandGameplayManager.pointerRationalizer.onClick`（与 `Navigator`/`ConfirmButton` 同源），拿到游戏自己认定的点击与屏幕坐标；订阅失败自动退回 `Input.GetMouseButtonDown` 轮询。
2. **取点改为原版路径**：`island.navSpotter.NavSpotCast(screenPos, out hit)` → `hit.point`（内部 `ViewportPointToRay(归一化)` + "Voxels"/"Modules" 层），失败再退自制射线（归一化 viewport × `LayerMaster.voxelMask`），再失败不限层。
3. **移除 `EventSystem.IsPointerOverGameObject()` 闸门**：该判断在本游戏里恒为真（世界交互全走 EventSystem），是 v0.2.0 静默吞掉全部点击的**真凶**。
4. **点击全程日志**（前缀 `[NewMode][点击]`）：屏幕坐标与来源 → 地形命中（碰撞体名@层 + 世界点 + 海拔）→ 解析结果/原因 → 投放结果。以后"点了没反应"能直接从日志定位到卡在哪一步。

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


