# BadNorthNewMode — 项目约束与说明

本文件是**唯一**的需求 / 约束 / 知识来源。原版机制的因果链与踩过的坑只写在这里，代码里最多留一行索引。

## 1. 目标

在**正常战局内**（非编辑器、非自定义关卡），按键唤出菜单选择兵种、点击滩头陆地，让敌方单位以**原版方式**自然出现——走完"敌舰自海面驶来 → 靠岸 → 舱内敌人下船 → 转入战斗"的完整流程。

## 2. 硬性约束

1. **以 DLL 类模组为主**：只交付 BepInEx 插件 DLL，不新增资产（必要图形运行时生成）。
2. **不改游戏文件**：只用运行时接口 / 反射；当前**零 Harmony 补丁**。
3. **复用原版逻辑**：航行、靠岸、下船、AI 全由原版承担；mod 只做"构造 + 触发 + 兜底"。
4. **注释与文档从简**（v1.2.3 起明确）：
   - 代码注释**只写"为什么 / 坑 / 反直觉点"**，不复述代码；类头 `<summary>` 一行，不写多行 XML 文档。
   - 长解释一律写进本文件，代码里最多留一行 `见 PROJECT_SPEC §x` 索引；**单文件注释行占比 ≤ 5%**（当前约 5%）。
   - `cfg.Bind(..., "说明")` 里的文字是面向用户的配置文档，可以写清楚，不计入注释。
5. **场景隔离**：仅在战局（`Island.State.Playing`）生效；结算 / 退出战局时自动清场。
6. **互不干扰**：与既有 mod 共存（黑矛兵 1.3、混合编队 1.0、Full Unlock）；不动原版波次计时与结算判定。
7. **技术基线**：`net472` + `LangVersion 7.3`；**禁用"params 空数组"写法**（会被编成 `Array.Empty`，游戏 mscorlib 2.0 没有）；构建闸门 `tools/check-api.ps1` 会拦。

## 3. 环境（已实测）

| 项 | 值 |
|---|---|
| 游戏 | `D:\Steam\steamapps\common\BadNorth`（Unity 2018.4 + Mono + BepInEx 5.4.23.4） |
| 运行时 BCL | `mscorlib 2.0.0.0` / `System.Core 3.5`（`CLR 2.0.50727`）——**不是** .NET 4.x |
| 引用 | `BadNorth_Data\Managed\{Assembly-CSharp, UnityEngine.CoreModule, PhysicsModule, AnimationModule, UI, IMGUIModule}.dll`、`BepInEx\core\BepInEx.dll` |
| 构建 | `.\build.ps1`（`-BadNorthDir/-Configuration/-SkipDeploy`）：编译 → API 闸门 → 备份 → 部署 → 哈希校验 |
| 反编译源码 | `C:\Users\ABaLaQiYaShanMaiI\Desktop\BadNorthDatabase-main\src\Assembly-CSharp`（下称**源码**） |
| 同类范例 | `C:\Users\ABaLaQiYaShanMaiI\Desktop\BadNorthEnemy-main` |
| 本地化取数 | UnityPy 载 `BadNorth_Data\data.unity3d` → 取"含 CJK 最多的 MonoBehaviour"（本作 `path_id=1150`，11047 串）→ 词条结构 `term + 12 语言`，简中恒为第 9 项 |

## 4. 原版事实（已核对源码）

**生成 → 靠岸 → 下船**
```
Raid.IIslandFirstEnter : 建 Wave→ShipGroup→Landing→ShipLoad；Beaches.GetBeachPositions() 选滩头 → Landing.TryPlace()
Raid.IIslandPlay       : 对每个 Landing 调 Spawn()（预生成 Longship，船体 inactive）
Raid.MaybeLaunchWaves  : timer ≥ wave.waveStartTime → BeginNextWave() → Wave.BeginWave() → Landing.Launch()
Landing.Spawn()        : Instantiate(shipPrefab) → Longship.Setup → 逐个 squad.CreateAgent(vikingRef.agent, navPos)
                         + GetOrAddComponent<Pirate>().AddToLongship(ship) + VikingAgent.vikingReference = vikingRef
Landing.TryPlace()     : ShipTravel(起点 = 滩头 + dir*50，终点 = 滩头) + 占位互斥 + SphereCast(moduleMask)
                         失败主因：进近廊道被 Modules（建筑/岩石）挡住
Longship.UpdateIncoming: interpolator → 1 时 animator.SetTrigger(landId) → 动画事件 LandAnimComplete() → landed = true
Pirate.MaybeAct(brain) : 需 longship && landed && agent.orderDist < 0.01 → navPos 换成岛屿网格 → RemoveFromShip() 下船
```
**陆地点击链路**
```
EventSystem → PointerRationalizer（全屏手势接收器）→ 发布 onClick(button, screenPos)
  → 订阅者：Navigator / ConfirmButton / CameraController
  → 换算地面点：NavSpot.NavSpotCast(screenPos, out hit)（内部 ViewportPointToRay(归一化) + "Voxels"/"Modules" 层）→ hit.point
```
**结算判定**：`IslandWinConditions.AllEnemiesDefeated() = raid.AllWavesSpawned() && island.vikings.agents.Count == 0`。
我们投放的是真维京人（`faction = island.vikings`）→ **只要还有未击杀的投放单位，关卡就不会结算**（`F2` 清场可收尾）。

**控制"原版上岛的敌方单位"（v1.3.0 只记录，未实现）**
```
LevelNode.Setup(levelState) → levelState.GetReferencedObjects(this.enemies)     // 关卡敌人池，每关生成期填一次
Raid.IIslandFirstEnter:
    this.possibleAgents = island.levelNode.enemies;                              // 仅取"同一个 List 对象"的引用
    for k in 0..wavesCount-1:
        if (k < possibleAgents.Count)  shipLoad.vikingRef = possibleAgents[k];   // ★ 第 k 波固定用 enemies[k]
        else                           shipLoad.vikingRef = possibleAgents[Random];
```
- **三种手段**：① 改池内容 → 限定原版波次能用哪些兵种；② **改池顺序** → 直接决定"第几波出什么"（原版自带的难度前置编排）；③ 改 `VikingReference` 数值 → ⚠️ 会污染全局共享资产，必须克隆后注入。
- **时机与实现成本**：`LevelNode.Setup` 只在岛屿生成期填一次；我们在"已生成但未 `Playing`"阶段**幂等覆写**该 List 即可，Raid 拿的是同一引用（即使落在 first-enter 的 yield 之间也生效）→ **零补丁**可达。
- **存档无影响**：敌人池是运行时从 `LevelState` 派生的，存档不含该 List；只在内存改，重启即恢复。
- 若将来实现，计划形态：cfg `NativeEnemies`（逗号分隔，留空 = 不干预）+ 菜单一排"原版波次"按钮，只改 List 内容/顺序、绝不改共享资产。

**必须绕开的坑（全部实测过）**

| 坑 | 现象 | 正确做法 |
|---|---|---|
| net472 编 params 空数组 | 运行期 `MissingMethodException: System.Array.Empty`，插件静默失效 | 写 `new KeyCode[0]`；API 闸门会拦 |
| `EventSystem.IsPointerOverGameObject()` | 本游戏恒为真 → 点击被静默吞掉、无任何日志 | 不用它做 UI 判断，直接订阅原版 `onClick` |
| `pointerRationalizer.onClick +=` | 事件类型是 System.Core 3.5 的 `Action`2，运行期解析不到 | 反射 `GetEvent` + `Delegate.CreateDelegate` + `AddEventHandler` |
| `EventInfo/MethodInfo == null` | `op_Equality` 是 .NET 4.0 才加的 | 用 `object.ReferenceEquals` |
| 中途投放不下船 | `brain.order` 被 `KillAllEnemies` 抢走 → `orderDist = 1e6` → `MaybeAct` 永不成立 | 生成后 `AttachAgentBehaviours()` 把 order 交还 `Pirate`（原版靠"生成期我方未部署"天然正确） |
| 幽灵船（波次残留） | 我们的 Wave 不在 `raid.waves` → `Raid.IIslandWipe` 不清它 | `SpawnLedger` 在战局结束 / 离开战局 / 换岛时销毁（订阅 `EndOfLevel.postProcess`） |
| 滩头看似可用却投放失败 | 点击命中 `Modules`，或进近廊道被挡 | 收集候选滩头逐个 `TryPlace`；`CorridorClear()` 预检廊道（悬停预览共用同一判定） |
| 连续投放"叠船"（看着像一船混编） | `CollectPlaced` 只查 `raid.waves`，而我们的 Wave 不在其中 → **自家的船不参与占位互斥**，可叠在同一滩头 | 改为收集 `raid.landingContainer` 下**所有** Landing（含我们自己的）；另加船员自检日志 |
| `EndOfLevel` 的两个事件 | `preProcess` 是 `Action<T1,T2>` 形态（同 `onClick`，直接 `+=` 会炸） | 只用 `postProcess`（`Action<Island>`，可直接订阅）做清场；要 hook `preProcess` 须照 `onClick` 的反射写法 |
| 弓手在船上照样射击 | 与 `Pirate` / order 无关（`Archery : Brain` 自驱） | 无需处理；下船时只摘掉 Pirate action，Archery 不受影响 |
| 船上敌人"打不到" | 原版 `Longship` 每帧以 `Data(…, dangerous:false, **hittable:false**)` 上报流场 → 我方只能"感到要来"，不会交战（原版设计：打船要用火箭技能） | `ShipboardThreat` 在靠岸前 4m 起（同原版 amount 门控）追加一条 `hittable=true` 的存在；下船后自动停用、交回 Brain 上报 |
| 连投多艘 = 多段接近音乐同时响 | 每次投放各自一个 Wave，各跑一次 `Wave.BeginWave()`（内含 `PostEvent(approachAudioId)`） | `FlotillaLauncher`：窗口（`FlotillaDelay` 默认 1s）内合并进**同一个 Wave**（原版一波本就多船）→ 一条音乐、一次 `BeginWave`；`timeSpreadGroup/Ship` 覆写为 `FlotillaSpread`（默认 2s）避免拖到十几秒 |

## 5. 机制设计

- **按键**：`F1` 开关投放菜单（菜单即投放模式）；`F2` 强制清场；右键 / `Esc` 关闭菜单。
- **菜单**（IMGUI，零资源）：① 兵种（本关 `levelNode.enemies` ∪ 全局字典 `Viking_*`，按 **bounty 升序** = 难度递增；显示 `简中名（内部名）+ 默认数`）；② 数量预设（默认 / 1 / 2 / 3 / 4 / 6 / 8 / 10 / 12）。左键点选即写回 cfg（`EnemyName` / `SquadSize`）；信息行显示"该人数会自动配哪艘船"。**不选船型**——人数定了船就定了。菜单区域内的点击被屏蔽（`IngameMenu.Contains`，注意 IMGUI 的 y 轴翻转）。
- **兵种显示名**（`UnitNames`）：原版 I2 本地化**没有**敌方兵种显示名（维京兵种只出现在 hint 里）→ 采取"官方用词优先 + 项目既有叫法"，与 cfg 内部名一一对应。
- **默认装载数**：`UnitNames.DefaultCounts` 梯度表（剑兵 12 / 盾兵 10 / 弓手 8 / 掷斧手 7 / 双手剑士 5 / 狂战士 5 / **巨人级各 1**）；未收录兵种回退原版公式（最小船容量 ÷ 单体面积），最终由最大船容量裁剪。
- **船随人数自动匹配**（v1.2.4）：`PickShipForCount` = **装得下该人数的最小船**（都装不下则用最大的船并把人数裁到容量）——人数少就小船、人数多就大船，不提供船型选择。
- **投放链**：`TryResolve`（点击处与落点都须与海面齐平 + 距离 ≤ `MaxShoreDistance`）→ `TrySpawn` 建原版对象树 → 逐个候选 `TryPlace` → `Spawn()` → `AttachAgentBehaviours()` → `raid.StartCoroutine(wave.BeginWave())`（原版 Launch / 音频 / 到达回调）。
- **滩头占用规则**（v1.2.4）：离点击处**最近**的滩头若已有船（原版或本 mod）→ **直接拒绝**并提示距离；要求间距 = `MinLandingSpacing`（默认 2.5m，基础值）**+ 本船船长**，所以**大船会自动留出更大空档，不会挤占原版停靠点**；只有"地形/进近廊道被挡"才自动换候选滩头（候选逐个 `TryPlace`）。
  - 为什么不会抢到原版的位置：原版所有登陆点在 `Raid.IIslandFirstEnter`（**开战前**）就一次性放置完毕，战斗中途不再新增；且它们都在 `landingContainer` 下 → 一直在我们的互斥/占用名单里。
- **跨岛借用兵种**（船型不借用）：兵种取自本关 `enemies` ∪ 全局字典（`PickEnemy` 回退），只写进**我们自己**的 `ShipLoad.vikingRef`；**不参与关卡生成与存档**（原版 RaidDef 在我们投放前已生成完，我们的 Wave 也不在 `raid.waves`）。
- **落点 UI**：`PlacementMarker` 运行时生成环形 / 内芯贴图（优先加法混合），悬停实时预览（亮青 = 可投放，暗红 = 不可投放）。
- **清理**：`SpawnLedger` 自动（战局结束 / 离开战局 / 换岛）+ `F2` 手动。
- **编队发射**（v1.3.0）：`FlotillaLauncher` 把 `FlotillaDelay`（默认 1s）窗口内的投放合并进**同一个 Wave** → 只播一条接近音乐、一次 `BeginWave`；`FlotillaSpread`（默认 2s）覆盖 Wave 出厂的时间散布；`FlotillaMaxShips`（默认 6）超出即开新编队；`FlotillaDelay = 0` 退回"各自立即出发"。
- **船上敌人算威胁**（v1.3.0）：`ShipboardThreat`（原理见 §4 坑表）。
- **暂停与时间基准**（v1.3.0）：`InBattle` 增加 `levelPauser.isPaused` 判定（暂停中不投放）；投放相关计时统一 `Time.time`（暂停冻结，与玩法一致）。
- **船速跟随难度**（v1.3.0）：`FollowDifficultyShipSpeed`（默认开）→ `speedMult = ShipSpeedMultiplier × levelNode.diffiucltySettings.shipSpeedMultiplier`（原版语义；VeryHard 更快）。
- **跨岛兵种开关**（v1.3.0）：`AllowCrossIslandUnits`（默认开）；关掉后只从本关 `enemies` 取。
- **占用表缓存**（v1.3.0）：已放置船位缓存，投放成功 / 清场 / 换岛时失效 → 悬停预览不再每帧遍历全岛 Landing。

## 6. 已知限制 / 待实测

- **T1**：投放波次不在 `raid.waves`，对 UI 进度 / 结算展示的影响（实测无异常，未深究）。
- **T2**：菜单是 IMGUI 覆盖层，点菜单时**游戏自己的世界点击仍会收到**（可能顺带选中压在菜单下的小队）；彻底屏蔽需补游戏侧点击入口，暂未做。
- **T3**：`MaxLandHeight`（0.5m）/ `MaxShoreDistance`（3m）的通用性——按 HUD 显示的实测落差微调。
- **T4**：`DisembarkWatchdog` 兜底路径正常不触发；若某兵种仍卡住，日志会给完整诊断。
- **T5（已结论，v1.3.0）**：船体 Collider **不会堵路**——本作单位通行用自研三角形导航网格 + presence 流场，物理 Collider 不参与寻路；船的 Collider 只出现在射线层（`longshipModulesMask` / `SquadSelection`）里，而 `moduleMask` 只含 `"Modules"`。故不做改动。
- **T6（原版口径，不打算改）**：投放单位的击杀会计入英雄 `bountiesCollected` 与图鉴（`OnShipArrival → VikingReference.Saw()`）——属原版统计口径；要屏蔽需补丁。
- **T7（待实测，v1.3.0 新增）**：`ShipboardThreat` 让船上敌人可被索敌后，我方弓手是否真会射击（含"航行中 / 靠岸未下船"两段），以及是否会因此暴露我方意图（朝着空滩射箭）。

## 7. 命名与提交约定

- 产物：`BadNorthNewMode.dll`；命名空间 `BadNorthNewMode`；GUID `badnorth.newmode`；部署到 `BepInEx\plugins`。
- **版本号统一**：`Plugin.VERSION` = `csproj Version` = 仓库提交 `vX.Y.Z`（自 v1.2.0 起）。
- 提交：一个里程碑一个提交，标题由作者撰写。
- 文档纪律：只本文件（+ 可选 `开发日志.md`）；新改动只在 §5 追加一行、§8 表格追加一行，不写长叙事。
- **代码结构**（v1.3.1 按职责拆分）：`Plugin`(入口/输入/取点) · `IngameMenu`(HUD + 兵种菜单) · `DropPlanner`(滩头解析/落差/占用/廊道，长方法拆成 `CheckClickHeight`/`CollectCandidates`/`PreferClearCorridor`) · `UnitCatalog`(兵种·船·人数) · `LandingInjector`(建树/投放/装配) · `FlotillaLauncher` · `SpawnLedger` · `DisembarkWatchdog` · `ShipboardThreat` · `PlacementMarker` · `ModConfig` · `UnitNames` · `Util`(向量格式化 / cfg 守卫 / 日志守卫)。

## 8. 版本对照（仓库提交 ↔ DLL）

| 提交 | DLL | 关键改动 |
|---|---|---|
| v1.0.0 | — | 立项 + 约束文件 |
| v1.0.1 | 0.1.0 | 主体落实：F1 生成单兵种（net472，首次可运行） |
| v1.0.2 | 0.1.0 | 修 `Array.Empty`（禁用 params 空数组） |
| v1.0.3 | 0.2.0 | 改点滩头陆地 + 海平面落差校验 + 光亮落点预览 |
| v1.0.4 | 0.2.1 | 订阅原版 `onClick` + 删 `IsPointerOverGameObject` 闸门 + 点击日志 |
| v1.0.5 | 0.2.2 / 0.2.3 | 下船诊断 + 兜底；幽灵船修复 + 自动清场 + `F2` |
| v1.1.0 | 0.2.4 | 下船恢复原速（order 交还 Pirate）+ 候选滩头修复投放失败（实机验证通过） |
| v1.2.0 | 1.2.0 | 版本号统一；`F1` 兵种选择菜单 + `F2` 说明 |
| v1.2.1 | 1.2.1 | 日志去噪（解析缓存 + `LogOnce`）；数量阶梯化 + 手动数量；删"赏金"显示；按难度排序 + 简中名 |
| v1.2.2 | 1.2.2 | 默认数量按兵种梯度（巨人 1 只）；注释精简（154 → 75 行） |
| v1.2.3 | 1.2.3 | 本文件精简（249 行/19.8KB → 109 行/6.0KB）+ 注释规范写入 §2 |
| v1.2.4 | 1.2.4 | 滩头占用规则（有船即拒投，间距随船长放大以预留原版坑位）+ 船随人数自动匹配 + 修"叠船" + 船员混编自检 |
| **v1.3.0** | **1.3.0** | **船上敌人算威胁；编队发射（一条音乐）；暂停不投放 + 时间基准统一；船速跟随难度；跨岛兵种开关；占用表缓存；文档记录"控制原版上岛单位"方案** |
| **v1.3.1** | **1.3.1** | **纯重构（行为不变）：`Plugin` 拆出 `IngameMenu`、`LandingInjector` 拆出 `DropPlanner`+`UnitCatalog`+`Util`；日志/cfg 守卫收拢（逐个 `Plugin.Log != null` → `Util.Log/Warn/Error`、`ModConfig.X.Value` → `Util.V`）；`TryResolve`(108 行) 按步骤拆三个子方法** |
