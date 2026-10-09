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
| 构建 | `.\build.ps1`（`-BadNorthDir/-Configuration/-SkipDeploy`）：编译 → API 闸门 → **本地化闸门** → 备份 → 部署 → 哈希校验 |
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
| `EventInfo/MethodInfo == null` | `MemberInfo`/`Type` 的 `op_Equality`/`op_Inequality` 是 **.NET 4.0 才有的运算符**，游戏 mscorlib 2.0 没有（闸门实测会拦：`Type::op_Equality`、`MethodInfo::op_Equality`、`Type::op_Inequality`） | 一律 `object.ReferenceEquals(x, null)`；`Plugin`(onClick 订阅)、`GameInput`(反射读 Rewired) 都按这条写 |
| 中途投放不下船 | `brain.order` 被 `KillAllEnemies` 抢走 → `orderDist = 1e6` → `MaybeAct` 永不成立 | 生成后 `AttachAgentBehaviours()` 把 order 交还 `Pirate`（原版靠"生成期我方未部署"天然正确） |
| 幽灵船（波次残留） | 我们的 Wave 不在 `raid.waves` → `Raid.IIslandWipe` 不清它 | `SpawnLedger` 在战局结束 / 离开战局 / 换岛时销毁（订阅 `EndOfLevel.postProcess`） |
| 滩头看似可用却投放失败 | 点击命中 `Modules`，或进近廊道被挡 | 收集候选滩头逐个 `TryPlace`；`CorridorClear()` 预检廊道（悬停预览共用同一判定） |
| 连续投放"叠船"（看着像一船混编） | `CollectPlaced` 只查 `raid.waves`，而我们的 Wave 不在其中 → **自家的船不参与占位互斥**，可叠在同一滩头 | 改为收集 `raid.landingContainer` 下**所有** Landing（含我们自己的）；另加船员自检日志 |
| `EndOfLevel` 的两个事件 | `preProcess` 是 `Action<T1,T2>` 形态（同 `onClick`，直接 `+=` 会炸） | 只用 `postProcess`（`Action<Island>`，可直接订阅）做清场；要 hook `preProcess` 须照 `onClick` 的反射写法 |
| 弓手在船上照样射击 | 与 `Pirate` / order 无关（`Archery : Brain` 自驱） | 无需处理；下船时只摘掉 Pirate action，Archery 不受影响 |
| 船上敌人"打不到" | 原版 `Longship` 每帧以 `Data(…, dangerous:false, **hittable:false**)` 上报流场 → 我方只能"感到要来"，不会交战（原版设计：打船要用火箭技能） | `ShipboardThreat` 在靠岸前 4m 起（同原版 amount 门控）追加一条 `hittable=true` 的存在；下船后自动停用、交回 Brain 上报 |
| 连投多艘 = 多段接近音乐同时响 | 每次投放各自一个 Wave，各跑一次 `Wave.BeginWave()`（内含 `PostEvent(approachAudioId)`） | `FlotillaLauncher`：窗口（`FlotillaDelay` 默认 1s）内合并进**同一个 Wave**（原版一波本就多船）→ 一条音乐、一次 `BeginWave`；`timeSpreadGroup/Ship` 覆写为 `FlotillaSpread`（默认 2s）避免拖到十几秒 |
| 遥控单位仍是敌人 | `agent.faction` 未变（viking）→ 双方 presence 依旧互为敌方（`Agent` 每帧 `faction.enemy.presence`） | 遥控版**故意如此**：只换 `brain.order`；招安要动两层 faction + 三份列表 + `AllHeroesDead` 对 `english.allSquads` 的硬转，风险高，**不做** |
| 框选拖动与相机平移冲突 | `CameraController.OnDrag` 不看按钮、任何键拖拽都平移；`CursorManager` 只把拖拽发给栈顶 `Last()` | 框选用**左键**且**起点必须在非原生单位附近**（`RemoteGrabRadius`，默认 48px）→ 别处拖动仍是原版相机平移；框选期间 `cursorManager.Remove((IDragListener)cam)`、结束 `Add` 回去（`Add/Remove/Contains` 均 public），且**按下即接管**避免"先平移一点再被接管" |
| 清场只删了船，**登岛单位还在** | `ShipGroup.squad` 是**懒加载**：`SpawnGetFromPrefab(..., island.runContainer)` → 单位是 `squad.CreateAgent(...)` 的子物体，挂在 `runContainer` 下、**不在我们登记的 Wave 树里**（所以只销毁 Wave 会漏掉它们） | `SpawnLedger` 现在同时登记 **squad**（`TrackSquad` → 销毁 squad 对象）并在 `DestroyAll` 里兜底 `ForeignUnit.DestroyAll()`（直接销毁已登记单位） |
| 小岛后期"投不出来" | ① 原版占位其实是**朝向盒不相交**（`Landing.TryPlace` 的 `ColCube.CheckBox`，等效间距 ≈0.7~1.4m），而我们曾经"最近候选被占就拒投"、门槛还是 2.5m + 船长；② 敌舰卸完人**长驻滩头**（不会开走：`Longship.Launch()`/`outgoing` 只属于**玩家撤离登船**，`SquadEvacuationLocation.Launch()` → `EvacuateAbility`；`Landing.Launch()` 才是敌舰进近，`Wave.cs:172`） | 候选**逐个试**（占用 + 廊道一起判）→ **两级间距**（首选"基础+船长"，自动放宽到"只要不重叠"）→ **全岛兜底**改用最近空滩头（`LandingFallbackAnywhere`），HUD 写明实际距离 |
| 后加的 order 组件不入 `orderList` | `Brain.orderList` 只在 `Setup()` 收集一次 → 后加组件不会被 `PickNewOrder` 选中 | 不必进列表：直接 `brain.order = 组件`（`WantsControl()=true` 即不会被换掉）；释放时置 `null`，由原列表（`KillAllEnemies`）接管 |
| 左键点地块"没反应"（**首个小队永远建不起来**） | `HandleRemoteMode` 的早退条件曾写成 `if (!RemoteGroup.Any) return;` → 一个组都没建立时，连"成队并前进"那条分支都进不去（日志：点了很多次地块，却从没有"遥控小队前进"） | 改成 `if (!MarqueeSelect.HasPending) return;`——**只有"这一次有选中"才接管**（v1.6.0：未选中不得移动，见 §5） |
| 空 `NavPos` 取值抛异常 | `NavPos` 是结构体，`default(NavPos)` 调 `wPos` 属性会抛 `NullReferenceException`（日志第一行 `NavPos pos is null`）；栈：`RemoteGroup.Reslot`（此刻还没目标格）→ `GroupOrder.SetSlot` → 整个过程被打断 | `SetSlot(NavPos slot, bool has)` **只在 `has` 为真时读 `slot.wPos`**，否则用 `Vector3.zero`；空 `NavPos` 只做结构体拷贝（不触属性）→ 可以安全存着备用 |
| `MoveTo` 的"没选中就命令全部队"回退 | `RemoteGroup.MoveTo` 在没有队被标记 `selected` 时回退 `targets.AddRange(_groups)` → **未选中也会移动**（点一下地块，册上所有单位立刻出发） | 去掉这条回退：没有选中就什么都不做（`message = 没有选中单位…`）；`ExecuteMove` 同样要求"本次选中"非空（见 §5） |
| 用自家取点给原版的否定结论"翻案" | `NavSpot.NavSpotCast` 返回 `null`（原版判定"不是有效格"）时，我曾继续用 `TryGetLandPoint`（**全场景射线**，会打到海面/背后地形）→ 再由 `GetNavSpot` 找到 1m 内的陆地格 → **点海面被翻成"有效"、于是不取消选中**（作者现象："点海面，存在大陆地块判定就有可能不取消"） | 一律**采信原版结论**：`NavSpotCast` 返回 `null` 就是无效格；只有它**抛异常**才走兜底，而兜底也照抄原版（Voxels/Modules 两层 + 同一道 1m 闸门） |
| `LevelCamera.cameraRef` 会指向 CampaignCamera | 战局里实测 `WorldToScreenPoint` 把单位投到 **x≈3700 / z≈101**（屏幕 1920×1080，鼠标在中心）→ 框选与标记全部落空；点选改世界距离后才正常 | 新增**自验证相机** `CamFor`：拿 NavSpotCast 的"已知地面点 ↔ 点击屏幕点"给 `LevelCamera.cameraRef` / `Camera.main` / 全场景相机打分取误差最小者；框选再加**世界四边形兜底**（四角 NavSpotCast 成世界点做包含判定），彻底不依赖投影 |
| 时间减速（`TimeManager`） | 原版**没有"空格减速"**（全工程无 `KeyCode.Space`）；减速只来自三处：选中我方小队（`SquadSelector` → 0.1）、过场（`CinematicCameraController`）、暂停（`LevelPauser` → 0）。`TimeManager.RequestTimeScale(requester, scale)` / `RemoveTimeScale` 是 **public static**，`UpdateTimeScale` **取所有请求里的最小值**（`LateUpdate` 里写 `Time.timeScale`） | 我们以独立 requester 挂同一套：**框选中 / 已有选中时申请减速**（默认 0.1，可 cfg），菜单打开 / 离开战局 / 清场 / 插件卸载时 `RemoveTimeScale`，避免残账把全局时间卡住 |
| 受控单位**隔岛扔火炬烧房** | `Arsonist.GetNewTarget` 用 `agent.orderDist > 0.2f → 不烧` 当"我到家了没"的判据；而我们的 order 里 `orderDist` 是"**离我指定的目标格**还有多远"——我们的人一站定（≈0），原版就以为它站在房子前 → `House.TryThrow` 放行，隔着地形与距离点房子 | **受控期间把 `Arsonist` 从 `Brain.actions` 摘掉**（`GroupOrder.SuppressHouseBurning`），释放 / 清场时 `RestoreHouseBurning` 还原；`Arsonist` 是 `IBrainAction`，摘掉后 `MaybeAct` 不再被调用 |
| 原版"选中我队"的生命周期 | `SquadMover.MoveTo` 末尾 `SquadSelector.SelectSquad(null,false)` → **下达移动的那一刻就取消选择**（不是等到达）；`Navigator.SelectPC/SelectTouch` 在"已选中 + 点到不可移动处（空地/不可互动环境）"→ `DeselectUnit()` | 我们照抄这两条：MoveTo 后 `MarqueeSelect.ClearPending()`；并与我方选择**互斥**（选中遥控单位时调用 `SelectSquad(null,false)`，我方被选中时自动清空遥控选择）→ 任何时刻只服务一方，不会双控 |

**遥控已投放单位（v1.4.0 落地依据）**
```
输入：PointerRationalizer.State = None/Hover/ButtonDown/Dragging，onClick 只在 state==ButtonDown 时发布 → 拖拽天然抑制点击
      CursorManager.dragListeners 是栈、分发只给 Last()；Add/Remove/Contains(IDragListener) 均 public
      原版 PC 映射（Navigator.SelectPC）：左 = 选中/取消小队，右 = 移动到悬停 NavSpot；只有 TwoButton 模式有右键
指挥：Brain.order / PickNewOrder（order==null || !order.WantsControl() 才换；orderList 只在 Setup() 收集一次）
      移动由兵种脑驱动：Swordsman / Spear / Archery / TankBrain 每拍调 this.order.ApplyOrder()/ApplyWalk()
      （Agent.FixedUpdateAgent 每帧先清零 walkDir/movability/enemyMovability，再由 stateRoot.Update() 里各状态写回）
      Pirate.WantsControl = longship（下船后 false，且自身被移出 Brain.actions）；KillAllEnemies.WantsControl = faction.enemy.agents.Count > 0
      ⇒ brain.order = null 即自动回到原 AI
寻路：NavSpot : IPathTarget（DistanceField.SampleDistanceDir / GetDistanceFrom）；NavSpot.GetNavSpot(pos, true)
      阵型：SquadFormation(count, bounds, dir).Get(i) 是 public struct（槽距 = agent.radius*2.01）；NavPos.Move(offset) 定位槽
归属：Landing.Spawn 用 shipGroup.squad（一波一个 Squad）⇒ 跨船同类在引擎里本就属于不同 Squad，无法引擎级合并
      敌人不占 NavSpot（occupant 只给玩家小队）；NavSpot.neighbours[8] 是八向相邻格
```

## 5. 机制设计

- **按键**：`F1` 开关投放菜单（菜单即投放模式）；`F2` 强制清场；右键 / `Esc` 关闭菜单。
- **菜单**（IMGUI，零资源）：① 兵种（本关 `levelNode.enemies` ∪ 全局字典 `Viking_*`，按 **bounty 升序** = 难度递增；显示 `简中名（内部名）+ 默认数`）；② 数量预设（默认 / 1 / 2 / 3 / 4 / 6 / 8 / 10 / 12）。左键点选即写回 cfg（`EnemyName` / `SquadSize`）；信息行显示"该人数会自动配哪艘船"。**不选船型**——人数定了船就定了。菜单区域内的点击被屏蔽（`IngameMenu.Contains`，注意 IMGUI 的 y 轴翻转）。
- **兵种显示名**（`UnitNames`）：原版 I2 本地化**没有**敌方兵种显示名（维京兵种只出现在 hint 里）→ 采取"官方用词优先 + 项目既有叫法"，与 cfg 内部名一一对应。
- **默认装载数**：`UnitNames.DefaultCounts` 梯度表（剑兵 12 / 盾兵 10 / 弓手 8 / 掷斧手 7 / 双手剑士 5 / 狂战士 5 / **巨人级各 1**）；未收录兵种回退原版公式（最小船容量 ÷ 单体面积），最终由最大船容量裁剪。
- **船随人数自动匹配**（v1.2.4）：`PickShipForCount` = **装得下该人数的最小船**（都装不下则用最大的船并把人数裁到容量）——人数少就小船、人数多就大船，不提供船型选择。
- **投放链**：`TryResolve`（点击处与落点都须与海面齐平 + 距离 ≤ `MaxShoreDistance` → **候选滩头逐个试**：占用 + 进近廊道两项都过才选它）→ `TrySpawn` 建原版对象树 → 逐个候选 `TryPlace` → `Spawn()` → `AttachAgentBehaviours()` → `raid.StartCoroutine(wave.BeginWave())`（原版 Launch / 音频 / 到达回调）。
- **滩头占用规则**（v1.4.0 放宽，原 v1.2.4 过于苛刻）：
  - 候选逐个试，**不是**"最近那个被占就拒绝"；间距**两级**：首选 = `MinLandingSpacing`（默认 **1.0m**，基础值）**+ 本船船长**；附近找不到就自动放宽到**只要不重叠**（`max(0.35, 2×船半径)` ≈ 0.7m）。
  - 附近（`MaxShoreDistance`）全都满/被挡时，`LandingFallbackAnywhere`（默认开）→ **全岛最近的可投放滩头**兜底，HUD 写明实际距离与"已改用最近空滩头"。
  - 依据：原版自己的占位是 `Landing.TryPlace` 里**朝向盒不相交**（`ColCube.CheckBox` 比 moveCube/standCube），等效中心间距只有 ≈ 0.7~1.4m；且敌舰卸完人**长驻滩头**（不会开走），所以小岛后期必须靠"放宽间距 + 换候选 + 全岛兜底"才投得出去。
  - 为什么不会抢原版的位置：原版所有登陆点在 `Raid.IIslandFirstEnter`（**开战前**）就一次性放置完毕，战斗中途不再新增；且它们都在 `landingContainer` 下 → 一直在我们的占用名单里。
- **跨岛借用兵种**（船型不借用）：兵种取自本关 `enemies` ∪ 全局字典（`PickEnemy` 回退），只写进**我们自己**的 `ShipLoad.vikingRef`；**不参与关卡生成与存档**（原版 RaidDef 在我们投放前已生成完，我们的 Wave 也不在 `raid.waves`）。
- **落点 UI**：`PlacementMarker` 运行时生成环形 / 内芯贴图（优先加法混合），悬停实时预览（亮青 = 可投放，暗红 = 不可投放）。
- **清理**：`SpawnLedger` 自动（战局结束 / 离开战局 / 换岛）+ `F2` 手动；销毁范围 = **我们的 Wave（船/Landing）+ 懒加载 squad + 已登记的非原生单位**（后两者在 `runContainer` 下，不在 Wave 树里，见 §4 坑表）。
- **编队发射**（v1.3.0）：`FlotillaLauncher` 把 `FlotillaDelay`（默认 1s）窗口内的投放合并进**同一个 Wave** → 只播一条接近音乐、一次 `BeginWave`；`FlotillaSpread`（默认 2s）覆盖 Wave 出厂的时间散布；`FlotillaMaxShips`（默认 6）超出即开新编队；`FlotillaDelay = 0` 退回"各自立即出发"。
- **船上敌人算威胁**（v1.3.0）：`ShipboardThreat`（原理见 §4 坑表）。
- **暂停与时间基准**（v1.3.0）：`InBattle` 增加 `levelPauser.isPaused` 判定（暂停中不投放）；投放相关计时统一 `Time.time`（暂停冻结，与玩法一致）。
- **船速跟随难度**（v1.3.0）：`FollowDifficultyShipSpeed`（默认开）→ `speedMult = ShipSpeedMultiplier × levelNode.diffiucltySettings.shipSpeedMultiplier`（原版语义；VeryHard 更快）。
- **跨岛兵种开关**（v1.3.0）：`AllowCrossIslandUnits`（默认开）；关掉后只从本关 `enemies` 取。
- **占用表缓存**（v1.3.0）：已放置船位缓存，投放成功 / 清场 / 换岛时失效 → 悬停预览不再每帧遍历全岛 Landing。
- **遥控非原生单位**（v1.4.0；**不改阵营**，只接管行军 —— 见 §4 相关事实与坑）：
  - **身份**：投放时给每个敌人挂 `ForeignUnit`（队键 = `VikingReference.name`）。默认**不接管**，走原 AI（下船后 `PickNewOrder` → `KillAllEnemies` 追敌）。
  - **点选（主路径，完全绕开相机）**：**按住 `Shift` + 左键或右键点一个非原生单位**（v1.5.3：左右键对称）= 选中它所在的**整队**（再点同一队 = 取消）；**`RemoteSelectAllKey`（默认 R）= 一键全选**所有"可选"单位（单位跑远看不清时最省事）。命中判定用**世界距离**（`RemoteClickRadius` 默认 1.2m，走与投放同一套 `NavSpotCast`，**不依赖屏幕投影**），屏幕半径（`RemoteGrabRadius` 默认 **64px**）仅作兜底。选中的单位用亮青十字标记，HUD 显示"非原生单位 N（可选 M, 已选中 K）"。候选来源 = **标记注册表 ∪ 我们在册 squad 的成员**（缺标记时自动补上，见 §4）。
  - **框选（次路径）**：**左键从非原生单位附近按住拖动**（移动 > `RemoteMarqueePixels`（默认 8px）才算框选；"附近" = `RemoteGrabRadius`（默认 **64px**）内）；或**按住 `RemoteFreeMarqueeKey`（默认左 Alt，左右 Alt 都认）从任意位置拖动**（按下左键**前后**按住都行，中途补按会就地转成框选、起点 = 补按处）。默认替换"已选中"，按住 `Shift` 则并入。**所有输入模式统一**（双键 / 单键 / 触摸）。
  - **成队（下令时发生）**：框里/已选中混了几种兵种就**按兵种各成一个小队**（同类并入已有队）；每队上限 `RemoteSoftCap`（默认 **40**，0 = 不限），超出的单位**不组队、保持原逻辑**；同队内按离框中心 / 点击处由近到远入选。
  - **下令（v1.5.4：普通点击即可，无需修饰键）**：有选中时**左键或右键点地块** → 先把"已选中"按兵种分队，再让**最近一次命中的小队**一起前往该 `NavSpot`（没有待成队时直接命令已有小队）；各队独立排布（`SquadFormation` 槽位，槽距 = `radius*2.01`）。普通点击会**挂起 2 帧**再执行：若原版把这次点击当成"选/移我方小队"（`VanillaSelected`）或正在框选，就**放弃**这次下令（双控根治见 T22）；按住 `Shift` / `R` 点地块则**立即**下令、跳过仲裁。
  - **落位（T8 已落实）**：到位（`orderDist < 0.12` 且离槽位 < 0.3m）即停稳；连续 1.5s 几乎无位移判定为被堵 → 之后 0.8s 放弃槽位、走距离场并加横向绕行解卡。
  - **不提供"释放回原 AI"（T9 不落实）**：小队一旦成立就持续受遥控，只在成员阵亡 / `F2` 清场 / 换岛 / 战局结束时清理（清理时会把接管前的 order 还原回去）。
  - **减速（缓解"框不住移动中的敌人"）**：**框选中 / 已有选中**时向原版 `TimeManager` 申请减速（`RemoteSlowMo` 默认开、`RemoteSlowMoScale` 默认 0.1，与"选中我方小队"同款；`UpdateTimeScale` 取最小值合并，所以与暂停/过场互不干扰）。菜单打开 / 离开战局 / 清场 / 卸载都会立即释放，不留残账。
  - **交互（v1.5.4 定稿：选队要修饰键，下令不强制）**：
    - **选择**：`Shift` + **左键或右键**点一个非原生单位 = 选中它所在的**整队**（同一次投送的 `Squad`，一般一船 4 个；再点同一队 = 取消）；`R` = **全选**所有"可选"单位；`Alt`+拖动（或从单位上起拖）= 框选（可选手段）。
    - **下令**：**左键或右键点一个地块**（有选中即可，无需修饰键）= 把"已选中"的单位集结过去——按兵种各成一个小队，**多兵种/人多时就地分到相邻格**（`NavSpot.neighbours[8]`，占满就都挤点击格）；**移动后自动取消选择**（与原版 `SquadMover.MoveTo` 一致，也立刻恢复时间流速）。按住 `Shift`/`R` 点 = **立即下令**（给"确定要动"的场景，且不受仲裁影响）。
    - **不干扰原版（v1.5.4：延迟仲裁）**：普通点击先**挂起 2 帧**，等原版表态——原版选中了我方小队（`VanillaSelected`）或正在框选（`Dragging`）就**放弃**这次遥控下令（verbose 模式会留一行证据）→ "一次点击同时指挥我方与遥控单位"从构造上不会发生，也不需要为每次移动按住 Shift。取消选择 / 拖动超过 `RemoteMarqueePixels`(8px) / 落在 F1 菜单区域内的按下，都不算"点击下令"。不再有 `Z/X` 等额外键（v1.4.6 的换队键已移除）。
    - **点无效目标 = 取消选中（v1.5.4，照抄原版）**：点海面 / 非可站立地块时，原版 `Navigator.SelectPC` 走 `DeselectUnit()`（并有 `UI/InGame/UnitDeselect` 音）；我们同样**清掉"已选中"**（`ClearPending`，时间流速一并恢复）并播同一音效；若当时没有"已选中"则算无效点击，播原版 `UI/InGame/Error` 音（见 T23）。
    - **与我方选择互斥（v1.4.8）**：选中遥控单位（Shift 点 / R / 框选）时顺手调用原版 `SquadSelector.SelectSquad(null, false)` **取消我方小队的选择**；反过来，你若选了我方小队（或点到空地触发原版取消），我们的"已选中"会自动清空、左右键完全归原版 → **任何时刻只有一方"被选中"，点击永远不双控**（覆盖双键与单键两种光标模式）。
    - **减速**：`RemoteSlowMo`（默认开、0.1×，与原版"选中我方小队"同款）在**框选中 / 已有选中**时生效；原版 `ManualSlomo`（按住）与本自动减速由 `TimeManager` 取最小值合并。

  - **表现（对齐原版手感）**：受控/选中单位画**亮青十字**（选中的更大更亮）、目标格画**空心方框**、**鼠标落点光标**（指针处地面点吸附最近 `NavSpot` 再画框——原版选中我队时也是这个提示）；框选矩形自绘（复用 `PlacementMarker` 的运行时贴图技术）；**不复用**原版选中环 / 小队 banner / 相机聚焦。
  - **UI 内置简要说明**（免得玩家不知道）：**按键说明只出现在 F1 菜单**（v1.5.3 起）——菜单底部 3 行「Shift + 点左右键点单位 = 选整队」「有选中时点左右键点地块 = 前进｜R = 全选」「Alt + 拖动 = 框选｜F1 关闭菜单 · F2 强制清场」；**HUD 只报状态**（"非原生单位 N（可选 M[, 已选中 K]）"、"遥控小队：…"、框选中提示、一次性 toast），不再重复按键串。
  - **相机**：只在"左键从非原生单位附近按下"的那一次拖动里临时接管相机（**按下即接管、松开即交还**，避免"先平移一点再被接管"的偏移）；其余任何拖动都完全交给原版相机。
  - **与原版输入的边界（T12）**：新增的只有两件事 —— ① 左键**从非原生单位附近**按下并拖动（或按住 `RemoteFreeMarqueeKey` 拖动）= 框选；② 有"已选中 / 已有小队"时，左键或右键点地块 = 成队并前进 / 前进（**普通点击经 2 帧仲裁**，`Shift`/`R` 为立即）。其余与原版一致：

    | 操作 | 本 mod 下的行为 |
    |---|---|
    | `Shift`+左/右键单击**非原生单位** | 本 mod：选中它所在的**整队**（再点同一队 = 取消）；原版那边视作"点了空地"（它们不在我方可选列表 `english.livingSquads` 里），不会误选英雄小队 |
    | 左键单击我方小队 / 空地 | 原版：选中 / 取消（单键模式下还会移动已选的小队） |
    | 单(右)击地块 | 原版照旧；本 mod 额外：有"已选中"或已有小队时 → 成队并前进（**挂起 2 帧**、原版接管则放弃；`Shift`/`R` 立即） |
    | 左键拖动（起点不在非原生单位附近） | 原版：平移相机 |
    | 左键拖动（起点在非原生单位附近） | 框选；这一次拖动不平移相机（按下接管、松开交还） |
    | 按住 `RemoteFreeMarqueeKey`（默认左 Alt，左右 Alt 都认）+ 左键拖动 | 从任意位置起拖都算框选；这一次拖动不平移相机（按下左键前后按住都行；按住该键时这一次按下**不会**被当成下令） |
    | 右键单击 / 右键拖动 | 完全交还原版（移动已选小队 / 取消 / 拖动相机） |
    | 滚轮缩放 / 触摸手势 | 原版 |
    | F1 菜单打开时 | 本 mod 的投放模式（左键投放、右键或 `Esc` 关闭）——既有功能，不属"原版一致"范围 |

- **文件日志默认关闭**（v1.5.0；**v1.5.3 加固**，`[Diag] LogToFile = false`）：BepInEx 在插件加载前就建好了写文件的 `DiskLogListener`，`LogFileSwitch` 在 `Awake` 里把它从 `Logger.Listeners` 摘掉，然后**亲自 `Dispose` 它的 `LogWriter`**——BepInEx 自带的 `DiskLogListener.Dispose()` 对 writer 用的是**非虚 `call`**，走不到 `StreamWriter.Dispose`，**文件句柄根本不关**，于是 `LogOutput.log` 被占着、删不掉（v1.5.3 实测复现：文件停在第 14 行 "Loading [Bad North - New Mode]"，旧代码的 `catch { }` 把 IOException 静默吞了）。现在：残留**一律删除**（不再按 64KB 跳过）、删不掉就**每 0.25s 重试 ≤30s**（`Plugin.Update` → `LogFileSwitch.Tick`）、失败打**控制台警告**并给出解法（BepInEx.cfg 的 `[Logging.Disk] Enabled=false`）。**控制台日志不受影响**，设 `true` 则完全不干预 BepInEx 原行为。README/玩家装完不会再生成 `BepInEx\LogOutput.log`。

- **菜单点击穿透（v1.5.6）**：菜单打开时在菜单矩形上盖一层**运行时生成的不可见 UI**（`ClickShield`：`Canvas(ScreenSpaceOverlay, sortingOrder=32767)` + `GraphicRaycaster` + `RawImage(alpha=0, raycastTarget=true)`，尺寸每帧随 `IngameMenu.MenuRect` 同步）——原版点击走 **EventSystem 射线**，会先命中我们这层 → 全屏手势接收器 `PointerRationalizer` 收 `OnPointerExit`（`_state=None`）→ `OnPointerClick` 的 `state==ButtonDown` 前置不成立 → **原版 `onClick` 不再发布**（IMGUI 不属于 EventSystem，所以此前拦不住，见 §6/T2）。只在菜单矩形内生效，菜单外的地图点击照旧（投放不受影响）；`[General] MenuBlocksWorldClicks=false` 回到旧行为；创建失败 / 菜单关闭 / 插件卸载都会撤掉或销毁这层。

- **点击路由 v3（v1.5.6，根治"鸡肋"）**：默认 `[Remote] RemoteOrderButton = Auto` → **自动跟随游戏的单/双键设置**（读 `Profile.userSettings.cursorBehaviour`，与原版 `Navigator.OnButtonUp` 同一来源）：**双键模式** = 左键点单位即选中**该单位本身**（**精确**，不再默认整队）、**双击**选整队、右键点地块即前进；**单键 / 触摸模式** = 照抄原版映射，**一个键按"当前有没有选中"决定**（没选中时点我们的单位 = 选整队；有选中时点地块 = 前进；点海面等无效位置 = 取消选中）。`Shift` + 点在任何模式下都是"并入 / 移出选择"。实现 = `RemoteCursor`：在**按下那一帧**用 `Input.GetMouseButtonDown` 自己判定这次点击归谁、是"选"还是"走"（按下时记下意图，松开时照它执行——单键模式靠按钮分不出来）。⚠️ 原计划反射订阅 `pointerRationalizer.onButtonDown`，但**实机取不到该事件**（`GetEvent("onButtonDown", Instance|NonPublic)` 返回 null，与 `onClick` 不同，见 §4 坑表 / T29）→ 改成轮询按键状态：归我们才把自己**压到 `CursorManager` 栈顶**（`Add(IPointerCursor)` 会把原版 `Navigator` 临时 `SetActive(false)`）→ 原版 `OnButtonUp` 拿不到这次点击 → **不会误选我方、不播 `FailedClick` 错误音、也不需要 2 帧仲裁**；松开立即 `Remove` 还回去（变拖动 / 丢事件由 `Update` 兜底）。原版 `Navigator` 的 `OverrideCursorTexture` / `UpdateHoverTarget` 都是空实现，所以我们的空实现**等价于原版**（光标贴图与悬停效果不变）。`RemoteOrderButton = Left` = 回到 v1.5.4 手感（左键下令、2 帧仲裁、`Shift` 选队，见 T22，且**不随单/双键适配**），此时不启用接管订阅。
- **船上选中 + 登陆集结点（v1.5.6）**：`ForeignUnit.Selectable`（可选，**含仍在船上**）与 `Commandable`（可下令，须 `navPos.island`）分开——**船上不抢 `brain.order`**（那是 `Pirate` 的地盘，抢了下不了船），只把"已选中"记成集结点（`RemoteGroup.RequestRally`）；落地后 `RemoteGroup.TickRallies` 自动认领成组并前往集结点。HUD 常显"待登陆集结 N 人 / M 处"，船上单位画**琥珀色**十字（落地单位仍是亮青）。
- **点海面/无效处 = 取消选中，双方一视同仁（v1.6.0）**：原版 `Navigator.SelectPC` 在"已选中 + 没 hover 到有效 `NavSpot`"时走 `DeselectUnit()`（我方小队会被取消）。我们补上同样效果：**双键模式的左键**在按下时若落点**既不是我们的单位、也不是我方小队**（`PickAtTight` 未命中 且 `!OverVanillaSquad`）就调 `CancelSelectionLikeVanilla()` —— 有选中播 `UnitDeselect`、没选中播 `Error`，与原版同音（**v1.6.0 第 4 轮起不看"落点是否有效地块"**：左键本来就不负责移动、只负责取消，否则"贴着海岸、最近格恰好在 1m 内"的海面点击会被判成有效而漏取消）。**坑**：原先这句"空点清空"带了 `!_grab` 条件，而 `_grab` 只要指针 **64px 内有我们的单位**就为真（守海岸线时几乎必然）→ 点海面时我们的选中**没被清**（v1.6.0 起改为按落点判定）；右键 / 单键模式的这条路径本来就走 `RemoteOrderAt → FailedTarget`，行为一致。**第二层坑（T34 复测才发现）**：就算我们补了"清自己"，**那一次点击仍会被 `PickAtTight` 抢走**——守海岸线时"落点 1.2m 内的世界距离命中"必然成立 → `Push()` 吃掉点击 → **原版收不到、我方小队不取消**。修法 = `PickAtTight` **只在落点可站立时才做世界距离匹配**（点海面时跳过），只保留 24px 屏幕兜底；**第三层坑（作者复测"部分不可点地块仍不取消"）**：我原来的"可站立"判据是 `TryGetLandPoint`（只看射线**有没有打到碰撞体**）+ `GetNavSpot(land, true)` → **只要有 NavSpot 就恒为真**，漏掉了原版真正的闸门。原版 `NavSpotter.NavSpotCast` 是：**只打 `Voxels` / `Modules` 两层**取最近格，且 **`SqrMagnitude(navSpot.navPos.pos - hit.point) > 1f` 即判无效**（点悬崖立面 / 建筑侧面 / 海面都会命中这条）→ 修法 = 新增 `MarqueeSelect.NavSpotAt(screenPos, island)`：**先直接调原版 `NavSpot.NavSpotCast`**（同源），失败再用我们自己的取点 + **同一道 1m 闸门**兜底；`ValidStand` 与 `RemoteOrderAt` 都用它 → 无效落点必然走"取消选中 / `FailedTarget`"，且不再把单位派到崖顶去。另外：无效落点若**紧贴某个单位 24px 内**（如点船上的单位），交给"选中"路径、不重复播取消音。**第四层坑（作者 v1.6.0 实测"点海面，存在大陆地块判定就有可能不取消"）**：`NavSpotAt` 第一步调原版 `NavSpot.NavSpotCast`，**它在"无效"时返回 `null`** —— 而我当时把这当成"没取到、继续兜底"，于是用 `TryGetLandPoint`（**全场景射线**，能打到海面碰撞体或海面**背后**的地形）再 `GetNavSpot(land, true)` + 1m 闸门 → **把原版的"无效"翻成"有效"** → 点海面就不取消了。修法 = **原版返回 `null` 一律采信**（`try { return NavSpot.NavSpotCast(screenPos); }`，只有它**抛异常**才走兜底，兜底也照抄原版两层 + 1m 闸门）；同时删掉 `ExecuteMove` 里那句"宽松预检"（`TryGetLandPoint` 只看"射线有没有打到东西"，与真判据不一致），**全程只留 `NavSpotAt` 一个真判据**。
- **寻路：直线捷径必须过 `TriCast`（v1.6.0）**：`GroupOrder` 在"离目标格 <1.5m"时会**直奔自己的槽位**（直线），这省了绕行但**没做可达性检查** → 从崖顶往崖下的槽位直冲，会**卡在崖边地块、"仿佛只会走直线"**。原版 `EnglishFormationAgent.ModifyPath` 是同一步的参照：它写的是 `if (dist < 2f && navPos.TriCast(this.orderPos.navPos))` —— **先验证两点直线可达**才覆盖距离场。修法 = 照抄这道闸：`_slotNavPos` 存槽位所在格，`else if (d > 0.05f && (dist < 1.5f || d < 1.5f) && navPos.TriCast(_slotNavPos))` → 不可直线到达时**保持距离场结果绕行**（下坡/绕崖都交给流场）。
- **未选中不得移动 / 未选中不接管（v1.6.0，作者明确要求）**：**"选中"只指"这一次点击之前刚刚选好的单位"**（`MarqueeSelect.HasPending`）——册上已经有小队 ≠ 有选中。三条同时生效：① `Plugin.ExecuteMove` 先取"本次选中"，为空则**直接返回**（提示"没有选中单位：先点一个单位选中它"），**不再**顺带命令已有小队；② `RemoteGroup.MoveTo` 删掉"没有选中就 `AddRange(_groups)` 命令全部队"的回退；③ 点击路由（`RemoteCursor` 双键右键 / 单键"有选中=走"）与旧路径（`HandleRemoteMode`）都以 `HasPending` 为门槛，**没选中时这次点击 100% 归原版**（我们连栈都不压）→ 不会出现"点一下地块，我们自己的人先跑起来"或"自动跳转到非原生单位操控上"。**判据**：清空选择后点任何地块/海面，我们的单位**一动不动**、也不出提示；要先点单位（或 `R`）选中，再点地块才会前进；下令后选择照旧自动清空（同原版 `SquadMover`），想再指挥要重新选。
- **点选不抢我方（v1.6.0）**：v1.5.7 的接管判定用 `PickAt`（屏幕兜底半径 = `RemoteGrabRadius` 64px）→ 指针附近只要有一个我们的单位，这次左键就被吃掉，**选不中自己的小队**（原生单位默认登记后更容易撞上）。修法 = ① 接管判定改用 `PickAtTight`（世界距离主路径，屏幕兜底只认 **24px**）；② 按下时若 `OverVanillaSquad(指针)` 命中我方小队 → **一律归原版**（左键/单键都算）。
- **控制敌我（原生单位可遥控：v1.6.0 恢复 + 改名）**：术语 —— **原生单位** = 原版自己生成 / 上岛的敌人（`ForeignUnit.native = true`，我们**想控制**的方向）；**非原生单位** = 本 mod 投放的单位（`native = false`）。`[Native] ControlNativeUnits`（默认 **true**；v1.6.0 **从 `RemoteNativeUnits` 改名**，因为 BepInEx 不覆盖已存在的键，旧键 `false` 会一直卡住默认值）→ 扫描 `island.vikings.agents` 里**没有我们标记**的单位并补挂 `ForeignUnit(native = true)`；原生单位与非原生单位共用同一套选中 / 成组 / 下令，被接管期间不再自行作战；`F2` 与离开战局**不销毁**它们（只撤登记，`ForgetNative`）。HUD 单列"原生单位 N（可遥控）"，**F1 菜单两个开关按钮**（写回 cfg 立即生效）：`原版波次：拦下 / 正常`、`原生单位：可遥控 / 不可`，下方一行说明作用；候选由 `Plugin.Update` **每 0.25s 定时刷新**，且**首次登记会打一条日志**便于核对。
- **重开战局要清场（v1.6.0 修）**：原版"重玩关卡"走 `ReplayIslandRoutine()` = `WipeIslandRoutine()`（**只销毁 `runContainer`**）→ `ResetIslandRoutine()` → `PlayIslandRoutine()`；而我们的船挂在 `raid.landingContainer` 下，**vanilla 不会打扫**（它只 `Reset()` 自己的 Wave）。修法 = 把"**`island.runContainer` 换了一个新对象**"当作"这局是重开的"信号 → `DestroyAll` + `ForgetNative`。⚠️ **必须用 `object.ReferenceEquals` 比较**：被 `Destroy` 的 `Transform` 用 Unity 重载的 `==` 判定也"等于 null"，会把"换了新容器"误判成"还没初始化"而**静默失效**（v1.5.7 就是这么没生效的，见 §4 坑表）。
- **烧房：按原版口径（房子距离场）判定（v1.6.0 起，后续修全）**：`Arsonist.GetNewTarget` 拿 `agent.orderDist ≤ 0.2` 当"已到房前"，而我们的 `orderDist` 是"到我指定格的距离" —— **站定即 ≈0**；`_target == null`（刚接管还没下令）时 `SampleOrder` 早退更会写死 `dist = 0` → 它在任何地方都以为贴着房子、隔岛扔火炬。**关键**：原版 `VikingPatherSquad.SampleOrder` 判"到房子了没"用的是 **`house.distanceField.SampleDistance(navPos)` 再减高差补偿**（`ExtraMath.RemapValue(|Δy|, 0.2f, 0f)`）——**不是欧氏距离**；距离场沿**可行走面**算，所以"崖上的房"从山脚过去天然很远。修法 = ① `SampleOrder` 收敛为**单一出口**，退出前 `if (dist < 0.2f) dist = HouseDist(navPos)`（照抄上面那条公式，房子取 `arsonist.pather.house`）；② 据此**逐帧开关 `Arsonist` 是否留在 `brain.actions`**（只在房距 < 0.2 时放行），释放时 `RestoreArson` 还原；③ `[Diag] VerboseLog` 下每 2s 打一条 `[NewMode][烧房] 房距 …｜orderDist …｜放行 …｜房子 …` 便于现场判读。
- **集结后不再抽动（v1.5.7）**：根因两条 ① 原版 `EnglishPatherAgent` 到位后靠 `formation.movability` 收敛，我们照抄 `Lerp(2,0.2,orderDist)` 却没有这一项 → 到位仍是 2f 的最高"行走权限"，几个单位互相推挤；② `TrackStuck` 在**站着不动**时也会累积到 1.5s → 判定被堵 → 触发 0.8s 绕行 → 走开又回来，形成周期性抽动。修法：`GroupOrder` 增加 `_settled`（到位即 `walkDir=0`、`movability=0.05`），到位判定给足阈值（`dist<0.12 && d<0.35`）并加**滞回**（被挤开 <0.8m 不重新起步，>0.8m 才归位），且**就位后不再做卡住检测**。
- **拦下原版波次 = 无尽自定义模式（v1.5.7 修）**：v1.5.6 只拦一次，结果被 `Raid.IIslandFirstEnter` **覆盖回去**——它是协程，先创建 Wave 再 `yield`，在**最后一刻**才按排序赋 `waveStartTime`（第 0 波 = **0**，进岛即发），所以"原生单位照常生成"。修法 = **每帧持续重写**（幂等）：`waveStartTime = float.MaxValue` + `LevelTools.IsBlocked` 让投放占位表放行这些永不发射的 Landing。**不标记 `haveAllLaunched/Spawned`** → 本关**不会自然结束**（清光自己的单位也不判胜，留出思考布局的时间），**退出只能按 F3 强制胜利**，HUD 常显该提示。关掉开关会把原始 `waveStartTime` 还回去（原版波次立刻补发）。
- **F3 强制胜利（v1.5.6）**：`EndOfLevel.AllVikingsKilled()` 是 **public**，直接调用即走**完整原版胜利流程**（结算屏 / `postProcess` / 成就 / checkpoint / 道具判定），不伪造状态；已结束（`reason != None`）时拒绝重复触发。无尽自定义模式下它是唯一的出口。

- **站立抽动（v1.6.0 第 2 轮，作者反馈"数量一多就抽动"）**：v1.5.7 的 `_settled` 只解决了一部分，人多时仍然抖。四个原因一起修：① **movability 会变成负值**——贴房子时 `SampleOrder` 把 `dist` 换成房距（可能十几米），而 `ApplyOrder` 用 `Mathf.Lerp(2f, 0.2f, orderDist)`（**不夹 t**）→ 外推成负数，单位被推挤得乱动 → 改 `Mathf.Clamp01`；② **就位判定太严**——原判据是 `dist < 0.12 && d < 0.35`（d = 到自己槽位的距离），可**人数一多外层槽位被同伴占住，d 永远降不下来** → 全队长期"未就位"互相推挤；现在改为"**离目标格足够近就算到位**"（`dist < 0.25` 或 `dist < 0.6 && d < 0.45`），且**退出就位**要求"真被挤走"（`d ≥ 0.8 && dist ≥ 0.5`）；③ **目标格附近不再做卡住检测**（`TrackStuck(dist)`：`dist < 0.8` 直接清空计时）——那里"没位移"多半是同伴挤在一起，触发 0.8s 横向绕行会让整队周期性挪动 = 玩家看到的抽动，同时也给解卡分支加了 `dist >= 0.8` 前置；④ **站位改用原版我方小队那套**（见下面"站位照抄原版"一条）：紧凑方阵 + 只在挤不下时互相让位，从根上减少推挤。实测若仍抖，先把 `[Remote] RemoteSoftCap` 调小（一队人少，站位自然更稳）。
- **框选入口改为 Alt（v1.6.0，作者反馈"按鼠标键就触发框选"）**：v1.6.0 之前 `_grab = FreeMarqueeKeyHeld() || (_pressUnit != null)` —— 只要**在自己的单位附近按下**（世界距离 `RemoteClickRadius` 1.2m 内），这一次拖动就变成框选、相机也让给了框选 → 想拖着看地图时频繁误触发。修法 = `_grab = FreeMarqueeKeyHeld() || (RemoteMarqueeFromUnit && _pressUnit != null)`，新增 `[Remote] RemoteMarqueeFromUnit`（默认 **false**）：**不按 Alt 的拖动 = 原版相机平移**，框选请按 Alt（按下前后按住都认、左右 Alt 都认、从任意位置起拖都行）；想要旧手感把它设 `true` 即可。`RemoteGrabRadius` 仍然同时是 Shift 点选的命中半径。
- **站位照抄原版我方小队（v1.6.0，作者"间距太开、影响到原生单位"后修正）**：未提交的第一稿是我自己发明的"每格铺 1 人、BFS 两层最多 25 格" → 一队人被**散到十几个格子**，原生单位（也归我们管辖）同样被散开 ✗。**参考游戏文件**（`src/Assembly-CSharp/.../NavSpotFormationSquad.cs`，即玩家 `EnglishSquad` 的站位系统）后照抄：① `UpdateFormation` —— **全队都在同一个目标格上排阵**，`SquadFormation(count, navSpot)`（= `meshBounds` + `lookDir`），槽距 = **各自** `agent.radius * 1.01 * 2`，每格槽位用 `NavPos.Move(offset)` 落位，**Move 失败**才需要处理；② `SlotPusher()` —— 重叠的两人沿连线**各挪"重叠量的 20%"**（`push = dir * (|a| - (r1+r2)*1.01) * 0.2f`）迭代到不再重叠；原版是逐帧协程（先热身 4 步再后台跑），我们在 `Reslot` 里一次算完（带收敛判定 + 步数上限）。结果 = **紧凑的方阵**，只在真挤不下时互相让位，**不会散到隔壁格**。跨兵种仍然各占一格：点击格 1、其余队各取一个相邻格（= 上一轮那半句"多支部队赶往指定和相邻地块"的正确形态）。
- **多支部队一起走：点击格 + 相邻格（v1.6.0）**：`RemoteGroup.MoveTo` 让**每支部队各占一格**（第一队 = 点击格，其余队 = `NeighbourSpot(target, i)` 的相邻格），队内再按上一条的**原版排阵**站好；点一次地块，框选到的多支部队**分头**赶往该格与周围格子（HUD 会列出每队人数）。
- **原生单位上船前也能选（v1.6.0）**：`EnsureCandidates` 登记原生单位时原先要求 `ForeignUnit.Commandable`（= 已下船）→ 船上的原生单位登记不上、也选不了。改为 `ForeignUnit.Selectable`（**含船上**）：现在它们和非原生单位一样，**靠岸前就能选中并定集结点**，落地后由 `TickRallies` 自动认领成组前往（`Commandable` 仍然只在下令时才要求，绝不在船上抢 `Pirate` 的 order）。
- **一键释放遥控（v1.6.0）**：F1 菜单新增按钮 `一键释放遥控（全部交还原版 AI）` → `Plugin.ReleaseRemoteControl()` = 清空选中 + `RemoteGroup.Clear()`（**还原接管前的 order、销毁我们挂的组件**）→ 所有受控小队立刻回原版 AI（原生单位的登记保留，随时可再选）。提示会写清"几支小队 / 几个单位"。
- **菜单与 HUD 的字号/宽度（v1.6.0，作者反馈"字体显示不完整"）**：菜单宽度改为**按语言自适应**（简中 600 / 英文 760；原 420 会把长句裁掉），HUD 宽度自适应屏幕（≤920）；`IngameMenu.Draw` 里临时把 `GUI.skin` 的 label/button/box 字号改成 **12**（中英都能完整显示），`finally` 立刻还原，**不动游戏自己的皮肤**；菜单高度同步加上"一键释放"行与第 4 行"拖动/框选"说明行。

## 6. 已知限制 / 待实测

- **T1（v1.5.6 待实测）**：投放波次不在 `raid.waves`，对 UI 进度 / 结算展示的影响（历次实测无异常，未深究）。**判据**：击杀进度条不卡不跳、结算弹窗与金币正常；`[Diag] VerboseLog` 下无异常行。展示性限制，若复现再单独定。
- **T2（已修，v1.5.6）**：菜单是 IMGUI 覆盖层，而 IMGUI **不属于 EventSystem/Canvas** → 原版 `PointerRationalizer.OnPointerClick` 照常发布 `onClick`（点菜单会顺带选中压在菜单下的小队）。修法 = 菜单打开时在菜单矩形上盖一层**运行时生成的 UI 拦截面**（见 §5）：EventSystem 射线先命中它 → 手势接收器收 `OnPointerExit`（`state=None`）→ `state==ButtonDown` 前置不成立 → 原版收不到这次点击；菜单外点击不受影响。`[General] MenuBlocksWorldClicks=false` 回到旧行为；创建失败（如缺 `UnityEngine.UI`）自动退化为旧行为并打警告。**判据**：菜单开着连点兵种/数量/语言按钮，原版不选中压在菜单下的小队、不产生移动指令；菜单外点击仍能正常投放。
- **T3（v1.5.6 待实测）**：`MaxLandHeight`（0.5m）/ `MaxShoreDistance`（3m）的通用性。**判据**：在高地边缘 / 陡坡 / 浅滩 / 深水边各点几次，HUD 落差读数与"放行/拒绝"是否与预期一致；不一致就按读数回调这两个值。
- **T4**：`DisembarkWatchdog` 兜底路径正常不触发；若某兵种仍卡住，日志会给完整诊断。
- **T5（已结论，v1.3.0）**：船体 Collider **不会堵路**——本作单位通行用自研三角形导航网格 + presence 流场，物理 Collider 不参与寻路；船的 Collider 只出现在射线层（`longshipModulesMask` / `SquadSelection`）里，而 `moduleMask` 只含 `"Modules"`。故不做改动。
- **T6（原版口径，不打算改）**：投放单位的击杀会计入英雄 `bountiesCollected` 与图鉴（`OnShipArrival → VikingReference.Saw()`）——属原版统计口径；要屏蔽需补丁。
- **T7（v1.5.6 待实测）**：`ShipboardThreat` 让船上敌人可被索敌后，我方弓手是否真会射击（含"航行中 / 靠岸未下船"两段），以及是否会因此暴露我方意图（朝着空滩射箭）。**判据**：未靠岸时用弓手/火箭打船能命中、下船后恢复 Brain 上报；要调就改 `ShipboardThreat` 的 4m / amount 门控。
- **T8（已落实，v1.4.0）**：遥控行军落位——到位即停稳 + 卡住检测（1.5s 几乎无位移 → 0.8s 走距离场 + 横向绕行解卡）；实机若仍挤住/抖动，调 `GroupOrder.TrackStuck` 的阈值。
- **T9（不落实，v1.4.0）**：**不做"战斗中释放回原 AI"**——小队一旦成立就持续受遥控；仅在成员阵亡 / `F2` 清场 / 换岛 / 结算时清理。
- **T10（已定，v1.4.0）**：每队上限 `RemoteSoftCap` 默认 **40**（0 = 不限）；超出的同类单位**不进队、保持原逻辑**。
- **T11（已澄清，v1.4.0）**：这里的"相机抖动"**不是**受击/攻击震动，而是**拖动框选时把相机一起拖走**（画面滑动 → 框选范围与世界错位）。已用两条规则解决：① 只有"从非原生单位附近起拖"才接管拖动；② 框选期间把相机拖拽监听临时摘掉。
- **T12（v1.5.6 待实测）**：**点选**手感——`Shift` + 左/右键单击非原生单位（选中整队 / 再点取消）；以及"拖动框选"与"点自己单位选中英雄小队"的共存。**调参项**：`RemoteGrabRadius`（现 64px）/ `RemoteClickRadius`（1.2m）/ `RemoteFreeMarqueeKey`（左 Alt）。
- **T13（v1.4.0 观察）**：日志里 `Cannot set the parent of the GameObject 'Torch(Clone)' while activating or deactivating the parent GameObject 'Weapon'` 出现在"离开战局 → 我方清场"同一时段，属 Unity 侧**销毁期重挂父级**告警。需确认：普通（没有本 mod 投放对象）的撤离 / 换岛是否也会出现——若只在有我们的对象时出现，再查销毁顺序（`SpawnLedger` 销毁的是 Wave 根节点）。
- **T14（v1.4.0 观察）**：放宽后三次投放都走了"已放宽间距"（说明该滩头很密、船间距 ≈0.7m）。若观感太挤，把 `MinLandingSpacing` 调回 2.0~2.5——现在它只是"首选偏好"，**调大不会再导致投不出来**（会自动放宽 / 换滩头）。
- **T15（v1.4.2 排错）**：点空/框空都会打诊断——HUD 常显"非原生单位 登记 N（可选 M[, 已选中 K]）"；**点空**日志含"最近单位 屏幕 Xpx（屏 x,y z=…｜鼠标 x,y｜屏幕 W×H｜相机名）+ 地面点 + 两个阈值"；**框空**日志含"登记/可用/命中 + 最近单位屏幕信息"。判读：**世界距离**才是点选依据（`RemoteClickRadius` 1.2m）——若日志里最近单位的**屏幕** Xpx 很大而它明明在你鼠标旁，就是投影问题；若它不在画面里，说明单位已经跑远（用 `R` 全选即可）。另：**原版自己的敌人没有标记、也绝不能被遥控**（设计如此）。
- **T16（v1.5.6，已被 T24 取代；仅当 `RemoteOrderButton=Left` 的旧手感适用）**：① `Shift`+左/右键点单位是否选中**整队**、再点同一队是否取消；② 左/右键点地块是否**集结并前进**、多兵种是否**分到相邻格**、移动后选择是否自动清空；③ 不按 Shift 时左右键是否**完全归原版**（选我方 / 移动我方，不误动遥控单位）；④ `R` 全选与 `Alt` 框选仍可用。**判据**：四条全可复现，且 `VerboseLog` 有对应证据行；手感调参 `RemoteMarqueePixels`（8px）/ `RemoteSlowMoScale`（0.1）/ `RemoteSoftCap`（40）。
- **T17（v1.5.0，取舍已定）**：关掉文件日志后**没有 `LogOutput.log` 可查**——要排查就先在 `BepInEx\config\badnorth.newmode.cfg` 把 `[Diag] LogToFile` 设成 `true` 再启动（我们的开关只是"摘掉磁盘监听"，属 **BepInEx 全局行为**：同一份日志文件里其他 mod 的内容同样不再落盘；`true` 即恢复原样）。
- **T18（v1.5.1）**：**中英切换只覆盖界面 + 日志**（HUD/F1 菜单/提示 + `Util.Log/Warn/Error` 与 `Log.LogInfo`），实现 = `Loc` 文案对照表（key **就是简中原文**，缺表回退中文，绝不抛异常）；`[General] Language = auto / zh / en`，`auto` 只读 `I2.Loc.LocalizationManager.mCurrentLanguage` 私有字段（**不调用** LocalizationManager → 不会把游戏语言提前写进 PlayerPrefs），读不到再按系统语言；**cfg 说明文案保持简中**、不随语言切换（只有 `[General] Language` 一项自带中英双语说明），`UnitNames` 增英文显示名；`README.md` 改双语（内容与 v1.5.0 完全一致，仍 ≤250 汉字）。
- **T19（v1.5.1，作者实测反馈后瘦身）**：**F1 菜单只留关键信息**——删掉"数量：…（本兵种默认 X，上限 Y）　船：Z"整行、每一行的"默认 N 个"、以及 5 行遥控操作说明，改为「语言行 + 标题/当前 + 兵种列表 + 数量按钮 + 3 行按键」；**数量按钮去掉"默认"**，预设为 `1/2/3/4/6/8/10/12`，**点多少装多少**（仍受"最大长船容量"这一物理上限裁剪，船型按人数自动匹配）；`SquadSize = 0`（按兵种梯度表）只保留在 cfg 层、UI 不再暴露，**投放算法与 cfg 默认行为不变**；顺带删掉因此失去调用者的 `IngameMenu.SelectedUnit` 与 `UnitCatalog.MaxSquadSize`。**菜单宽度 560 → 420（-25%，少挡横向地图）**，内容改纵向展开：按键拆成 3 行，并在顶部加 **中文 / English 语言按钮**（按钮各自用本语言书写、不依赖当前语言；点击 `Loc.SetLanguage` 写回 cfg，下一帧即生效，玩家不必去手改 cfg）。
- **T20（v1.5.3，作者实测反馈）**：① **按键说明只留一处**——HUD 只报状态（删掉整行遥控按键串、`[关菜单后/遥控·仅调控非原生单位]` 两处前缀、以及"遥控小队：…"里的"左键点地块 = 全队前进"），按键说明只保留在 F1 菜单；② **遥控下令改为必须按住 Shift**（`Shift + 左键/右键点地块` = 前进；不按 Shift 的点击 100% 归原版），根治"一次左键同时把我方与遥控单位指挥到同一格"：原设计靠 `VanillaSelected` 互斥，但本 Mod 与游戏的 `Update` 帧序不确定（游戏那侧可能在本帧之后才写 `selectedSquad`）→ 存在窗口期；③ 新增 `MarqueeSelect.ClickUsedForSelect`（每帧重算）：`Shift + 点单位` 已判给"选整队"的那一次按下，不再被同帧的前进逻辑消费（顺带修掉 v1.4.7 遗留的"已有选择时 Shift 点单位会误当前进并清空选择"）；④ **选队与前进都认左右键**（适配双键设置）：`Shift + 左键/右键点单位` = 选整队（左键走"松开且未拖动"路径、右键在按下帧直接判定），`Shift + 左键/右键点地块` = 前进；README 英文改用 "Hold `Shift` and …" 表达"按住"，中文同步为"按住 `Shift` + …"；⑤ **`R` 兼作下令修饰键**（作者实测反馈"按住 R 再点左右键失效"）：`Shift` = 精确指挥、**按住 R = 全选 + 批量下令**，两者都不按时点击仍 100% 归原版（双控防线不破）；`SelectAll` 顺带补上 `_pendingCenter = 鼠标位置`（此前用陈旧中心给分桶排序）；⑥ **Alt 审计（作者提问）**：`Alt` 只与"框选起手"绑定、不参与下令 —— 修两处：(a) 按下瞬间若按着 `RemoteFreeMarqueeKey`，这一次按下不再被当成前进命令（此前 `Alt+Shift` / `Alt+R` 拖动会一边框选一边下令）；(b) 允许**先按左键、再补按 Alt** 就地转成框选（起点 = 补按处），不再"Alt 按晚一点就框不上"。原版代码层无 Alt 动作绑定（仅 `KeyCodeDisplayNames` 提到）；Rewired 的按键映射存在玩家 PlayerPrefs 里，静态无法判定，若原版把 Alt 当修饰键则二者会叠加，可用 `RemoteFreeMarqueeKey` 换键规避。
- **T23（v1.5.4，作者观察"原版点海面会打破选中"）**：**照抄原版"点无效目标 = 取消选中"**——原版 `Navigator.SelectPC` 里，右键（及 OneButton 下有选中时的任意键）点在**没有有效 NavSpot** 的地方（海面 / 被建筑挡住的格）走 `DeselectUnit()` = `squadSelector.SelectSquad(null,false)` + `FabricWrapper.PostEvent("UI/InGame/UnitDeselect")`；完全没有选中时走 `FailedClick()` = `UI/InGame/Error` 音。我们的对应物 = `MarqueeSelect.ClearPending()`（清"已选中" + 恢复时间流速）+ 同一套音效；`[NewMode]` 只多一条 toast（原因 + "已取消选择"）。**注**：原版左键点**有效**地块也是"取消选中"（TwoButton 下左键不移动），这一条我们**不抄**——我们的普通点击在有效地块上是"前进"（v1.5.4 的设计意图）。
- **T22（v1.5.4，作者反馈"长按 Shift 操作依旧存在问题"）**：**选队仍要 `Shift`，但下令不再强制修饰键**——有选中时普通点击地块即可前进（恢复 v1.5.1 手感），代价是**挂起 2 帧**再执行：`FlushPendingMove` 先看原版有没有把这次点击当成"选/移我方小队"（`VanillaSelected`）或正在框选（`Dragging`），有则**放弃**（verbose 留一行证据）。这样既避开 v1.4.x 协作式互斥的帧序窗口，也不用为每次移动按住 Shift；`Shift`/`R` 点地块保留为**立即下令**通道。另加 `[Remote] RemoteMoveRequiresModifier`（默认 false）可一键回到 v1.5.3 的保守模式。拖动判定：按下→松手位移 > `RemoteMarqueePixels`(8px) 视为平移/框选，不算点击；`MarqueeSelect` 新增 `PressWasDrag`、`ClickUsedForSelect` 改为"按下置位、保持到松开"。**源码审查补丁**：读 `Navigator.SelectPC` 发现 **OneButton** 模式下原版点地块是「移动我方小队 + 自动取消选择」→ 事后 2 帧再查 `VanillaSelected` 已查不出来（会双控）；改为**按下那一帧**记 `_pressVanillaSelected` 并随挂起带到执行时（`_pendingVanillaBusy`），松开分支与 Flush 都认它。
- **T21（v1.5.3，作者实测反馈"运行后仍在 mod 目录生成日志"）**：根因 = BepInEx `DiskLogListener.Dispose()` 的 IL 对 writer 是 `call`（**非虚**）→ `StreamWriter` 未被 Dispose → 文件被占 → `File.Delete` 抛 IOException，被旧代码的 `catch { }` **静默吞掉**，于是每次启动都留下 `BepInEx\LogOutput.log`（实测残留 751 B，末尾正好是 "Loading [Bad North - New Mode]"）。修法：① 摘监听器后**亲自 `LogWriter.Dispose()`**，并从 `TextWriter.BaseStream as FileStream` 的 `.Name` 取**真实路径**（比拼 `Paths.BepInExRootPath` 更准，另备 `GameRootPath\BepInEx` 兜底）；② 残留**无条件删除**、失败**每 0.25s 重试 ≤30s**（`Plugin.Update` 驱动），首次失败与超时都打**控制台警告**（含路径 + 原因 + "把 BepInEx.cfg 的 `[Logging.Disk] Enabled` 设为 false"的建议）；③ `Dispose` 与 `Remove` 各自兜异常，避免"一处抛异常连累清残留"。**分发版默认零日志文件**（`[Diag] LogToFile=true` 仍可恢复 BepInEx 原行为）。

- **T24（v1.5.6 待实测，取代 T16）**：点击路由 v3——① **双键模式**：左键点遥控单位是否**直接选中整队**（不用 Shift）、再点同一队取消；右键点地块是否**立即前进且无错误音**；原版选中我方小队后右键是否仍归原版；② **单键 / 触摸模式**（把游戏设置改成单键）：没选中时点我们的单位 = 选整队；**有选中时点地块 = 前进**；有选中时点海面 = 取消选中；`Shift` + 点 = 并入/移出；③ 点空地是否清空遥控选择并播 `UnitDeselect`；④ **按住不放再松开**是否正常（看门狗只在"真松开 / 变拖动"时交还）；⑤ 拖动（平移相机 / `Alt` / 从单位上起拖的框选）是否不被当成点击。异常时把 `[Remote] RemoteOrderButton` 改 `Left` 一键回到 v1.5.4 手感（T16/T22 那套）。注意：T23 里"左键点有效地块 = 前进"的说法**已被本版取代**。
- **T25（v1.5.6 待实测）**：船上选中——船上点单位能选中（琥珀色十字）、点地块记成集结点（琥珀色方框）、**下船流程不受影响**、落地后自动前往（HUD"待登陆集结"归零）。
- **T26（v1.6.0 待实测）**：**控制敌我**——`[Native] ControlNativeUnits`（默认开；改名后旧键 `RemoteNativeUnits=false` 不再卡住）时**原生单位**能被选中 / 框选 / 点地块前进，落地后马上就能选；启动日志应有"接管开关：…原生单位可遥控 = True"与"已登记原生单位 N 个"；`F2` 后它们**没被销毁**；菜单点"原生单位：不可"后立刻不可选。
- **T27（v1.6.0 待实测，修 v1.5.6"拦不住"）**：`BlockVanillaWaves`（F1 菜单"原版波次：拦下"或 cfg）→ 本关**一波原版敌人都不出现（含第 0 波，它是 `waveStartTime = 0`、进岛即发的那一波）**、滩头未被"看不见的船"占满、投放正常；**清光自己投放的单位不应判胜**、HUD 常显"无尽自定义模式…F3 退出"、**F3 能正常走原版结算**；点回"正常"后原版波次立刻补发。
- **T28（v1.6.0 待实测）**：集结后**不再抽动**（站定即静止 5~10s）；被挤开 >0.8m 能自动归位。
- **T29（v1.6.0 待实测）**：点击路由生效（日志"点击路由已启用"、无 `找不到 onButtonDown`）；双键（左选右走）与单键（没选中=选、有选中=走）两套手感。
- **T30（v1.6.0 待实测，修 v1.5.7"仍无视距离/地形"）**：投放单位**能烧房**（把一队人带到完好房子旁站定 → 约 1~2s 内应扔火炬），且**离房远、或房子在崖上而单位在山脚 → 一定不烧**（含刚接管还没下令的站桩单位）。**排查**：`[Diag] VerboseLog = true` 后看 `[NewMode][烧房] 房距 x.xx｜orderDist x.xx｜放行 True/False｜房子 xxx`——若"房子 无（没有分到房子）"说明该单位所在 squad 没被 `VikingPatherSquad` 分到房（原版 AI 也没打算烧它）。
- **T31（v1.6.0 待实测）**：**精确点选**——单击只选 1 个、双击选整队、`Shift` 并入/移出；并且**永远不会**与我方单位共选或一起被派往同一格。
- **T32（v1.6.0 待实测，修 v1.5.7"重开后船还在"）**：打完一局用"重玩关卡"重开 → 上一局的船/单位**全部消失**（日志有"重开战局：已清空上一局的 N 组投放对象"）。
- **T33（v1.6.0 待实测，修 v1.5.7"选不中我方"）**：`R` 全选 → 定集结点 → **左键点我方小队能正常选中**（含我方单位与我们的单位挨在一起时）；左键点我们的单位仍能选中它。
- **T34（v1.6.0 第 3~4 轮，作者两轮复测）**：**无效落点必取消**——判据改用原版 `NavSpotter.NavSpotCast`（Voxels/Modules 两层 + **最近格离落点 >1m 即无效**），覆盖点**海面 / 悬崖立面 / 建筑侧面 / 离格子太远的坡面**；**第 4 轮补的两条**：(a) 原版 `NavSpotCast` 返回 `null` **一律采信**，不再被自家全场景射线"翻案"（否则贴岸海面会判成有效）；(b) 双键**左键**不再看落点有效性——非"我们的单位/我方小队"处一律取消。**判据**：① 选中我方小队 → 点这些地方 → **原版取消我方选中**；② 选中我们的单位 → 点这些地方 → **我们的也清**（同音）；③ 空手点 → `Error` 音；④ 点**船上的单位**（24px 内）仍能选中、不误取消；⑤ 右击无效落点 → 也走"取消选中"而不是把单位派到崖顶；⑥ **贴着海岸线**点海面（离岸边 <1m 的那些像素）**同样取消**。
- **T35（v1.6.0 待实测，新增）**：**绕崖/下坡寻路**——让一队人在**崖顶**站定，再把集结点订到**崖下的沿海**（或反之），观察是否绕坡走下去、不再卡在崖边地块"只会走直线"；被堵时应有短促绕行（`TrackStuck`）而不是长期卡住。
- **T36（v1.6.0 待实测，作者复测反馈后新增）**：**未选中不得移动 / 不自动接管**——在"没有选中任何单位"的状态下：① 点地块（左/右键都试）→ 我们的单位**一动不动**、没有任何提示、也不接管这次点击（原版该取消我方就取消我方）；② 点海面 → 只按原版规则取消我方小队 + 清空我们的选择，**不会**因为我们册上有小队就跑起来；③ 选中我们的单位后再点地块 → 正常前进（这一步必须仍然可用）；④ 下令后（选择被自动清空）**再点一次地块** → 仍然什么都不发生（要重新选中才能再指挥）。**判据**：全程 HUD 不出现"遥控小队前进"，`VerboseLog` 下也没有对应的移动日志。
- **T37（v1.6.0 待实测，作者反馈"数量一多就抽动"）**：**站立不抽动**——投放/框选**一大群人**（例如 12~20 个同兵种）到同一片空地，到位后观察 5~10s：① 整队**完全静止**（允许微小的互相让位，但不应有周期性来回挪动）；② 站定后 HUD 不再变；③ 被别的东西挤开 >0.8m 后能自己归位。若仍抖，`[Diag] VerboseLog=true` 取日志 + 告诉我是"原地高频抖"还是"每 1.5s 挪一下"（后者 = 卡住检测仍在触发，我再调阈值）。
- **T38（v1.6.0 待实测，作者反馈"按鼠标键就触发框选"）**：**拖动 = 平移相机，Alt + 拖动 = 框选**——① 不按 Alt 从**自己单位身上**起拖 → 相机平移、不出现框选矩形、不改变选中；② 按住 `Alt` 从**空地上**起拖 → 出现框选矩形、松手后框住的人都进选中；③ 先按下左键再补按 `Alt` → 这一次拖动就地转成框选；④ 想回旧手感：`[Remote] RemoteMarqueeFromUnit = true`。
- **T39（v1.6.0 待实测，作者复测后修正为"照抄原版我方小队"）**：**多支部队一起走 + 紧凑站位**——`Alt` 框选 2~3 个兵种（或一大群同类），点一块开阔地：① **每支部队各占一格**（第一队 = 点击格，其余队 = 相邻格），**队内全部站在这一个格上排成方阵**（同我方小队，`SquadFormation` 槽距 = 各自 radius*2*1.01），**不再散到十几个格子**；② 挤不下时只做原版那种"互相让一点"（`SlotPusher` 20% 重叠量），看起来紧凑、不叠人；③ 走的是距离场（绕开房子/崖壁），不会直穿障碍；④ 原生单位（归我们管辖的那批）也不该被散开；⑤ 落地后不抽动（与 T37 一起看）。
- **T40（v1.6.0 待实测，新增功能）**：**原生单位在船上就能选**——`[Native] BlockVanillaWaves = false` 且 `ControlNativeUnits = true` 时，原版敌舰**还没靠岸**就点船上的单位：① 能选中（琥珀色十字）、能框选；② 点地块能定集结点；③ 靠岸下船后自动成组前往集结点；④ 期间不会卡住下船（日志无 `Pirate` 相关异常）。
- **T41（v1.6.0 待实测，新增功能）**：**一键释放遥控**——F1 菜单点"一键释放遥控（全部交还原版 AI）"：① 所有受控小队**立刻**回原版 AI（该烧房烧房、该找路找路）；② 提示显示"已释放遥控：N 支小队 / M 个单位"；③ 释放后还能重新选中/接管；④ 菜单里没有受控小队时点它 → 提示"没有受控的遥控小队"。
- **T42（v1.6.0 待实测）**：**菜单字体与宽度**——F1 菜单在**简中与 English 两种语言**下：① 每一行文字都**完整显示**（不被右边缘裁掉、中文不出现方框/缺字）；② 兵种行、数量按钮、两个接管开关、"一键释放遥控"按钮、4 行按键说明都看得全；③ HUD 的状态行（英文更长）同样不被裁；④ 点菜单按钮不会穿透到世界里（`ClickShield`）。

## 7. 命名与提交约定

- 产物：`BadNorthNewMode.dll`；命名空间 `BadNorthNewMode`；GUID `badnorth.newmode`；部署到 `BepInEx\plugins`。
- 仓库根另有 **`README.md`**（面向玩家的操作说明 + 链接 + 许可，≤250 汉字，不再写其他内容）。
- **版本号统一**：`Plugin.VERSION` = `csproj Version` = 仓库提交 `vX.Y.Z`（自 v1.2.0 起）。**本次提交 = `v1.6.0`**（两处都写 `1.6.0`；作者手动提交）。
- 提交：一个里程碑一个提交，标题由作者撰写。
- 文档纪律：只本文件（+ 可选 `开发日志.md`）；新改动只在 §5 追加一行、§8 表格追加一行，不写长叙事。
- **代码结构**（v1.3.1 按职责拆分）：`Plugin`(入口/输入/取点) · `IngameMenu`(HUD + 兵种菜单) · `DropPlanner`(滩头解析/落差/占用/廊道，长方法拆成 `CheckClickHeight`/`CollectCandidates`/`PreferClearCorridor`) · `UnitCatalog`(兵种·船·人数) · `LandingInjector`(建树/投放/装配) · `FlotillaLauncher` · `SpawnLedger` · `DisembarkWatchdog` · `ShipboardThreat` · `PlacementMarker` · `ModConfig` · `UnitNames` · `Util`(向量格式化 / cfg 守卫 / 日志守卫)。
- **遥控相关文件**（v1.4.0）：`ForeignUnit`(非原生标记 + 注册表) · `RemoteGroup`(控制组容器：成组/并入/释放/槽位) · `GroupOrder`(自研 `IAgentOrder`：距离场 + 槽位) · `MarqueeSelect`(右键框选 + 相机拖拽让位)。
- **日志文件开关**（v1.5.0）：`LogFileSwitch`(摘掉 BepInEx 磁盘日志监听 + 清启动残留；`[Diag] LogToFile` 默认 false)。
- **菜单点击拦截面**（v1.5.6）：`ClickShield`(菜单矩形上的不可见 UI，阻断原版 EventSystem 点击；`[General] MenuBlocksWorldClicks` 默认 true)。
- **点击路由 / 接管本关**（v1.5.6）：`RemoteCursor`(按下那一帧压 `CursorManager` 栈顶吃掉这一次点击：左键=选整队、右键=前进；`[Remote] RemoteOrderButton` 默认 Right) · `LevelTools`(拦下原版波次 + F3 强制胜利；`[Native] BlockVanillaWaves`) · `ClickShield`(菜单矩形上的不可见 UI)。
- **构建闸门**（v1.5.6）：`tools/check-api.ps1`（**全部被引用程序集**的成员引用都要在游戏侧存在——BCL + UnityEngine* + BepInEx* + Assembly-CSharp；**470 项**）+ `tools/check-loc.ps1`（Loc 词条：无缺失 key、无死词条、`{n}` 占位符一致、无重复 key；带"正则失效"假阳性自检）。
- **中英切换**（v1.5.1）：`Loc`(界面/日志文案对照表 + `[General] Language`) · `UnitNames`(增英文显示名) · `README.md`(双语)。

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
| **v1.4.0** | **1.4.0** | **遥控非原生单位首版：左键从单位上拖 = 框选、左键点地块 = 按兵种各成一个小队并前进（每队上限 40，超出的保持原逻辑）；持续受遥控、不释放回 AI；不改阵营；落位/解卡（T8）** |
| **v1.4.1** | **1.4.1** | **投放放宽：滩头占用改"候选逐个试 + 两级间距 + 全岛兜底"（`MinLandingSpacing` 1.0、新增 `LandingFallbackAnywhere`）；遥控改左键点选为主 + `Shift` 点同类合并 + `Alt` 自由框选，相机按下即接管；修清场漏删登岛单位（squad 懒加载在 `runContainer` 下）；框选加登记/可用/命中诊断** |
| **v1.4.2** | **1.4.2** | **选中链自愈：候选 = 标记注册表 ∪ 在册 squad 成员（缺标记自动补）、`RemoteGrabRadius` 48→64px、点空打"登记/可用/最近"诊断；UI 内置遥控说明（HUD 常显可选数量 + 操作串、F1 菜单 5 行说明）；版本号校正到 1.4.2** |
| **v1.4.3** | **1.4.3** | **点选改世界距离主路径（`RemoteClickRadius` 1.2m，走 NavSpotCast，不依赖屏幕投影，屏幕半径仅兜底）；新增 `RemoteSelectAllKey`（默认 R）一键全选可选单位；诊断升级为"最近单位的屏幕原始坐标/z/相机名 + 地面点 + 世界距离"** |
| **v1.4.4** | **1.4.4** | **修"选中后点地块没反应"（早退条件少算了待成队选择 → 首个小队永远建不起来）；修相机（`LevelCamera.cameraRef` 指向 CampaignCamera → 自验证相机 + 世界四边形兜底框选）；框选/选中时减速（原版 `TimeManager`，0.1×）；右键取消选择** |
| **v1.4.5** | **1.4.5** | **受控期间屏蔽 `Arsonist`**（它拿 `orderDist` 当"到家"判据 → 隔岛扔火炬烧房）；**表现对齐原版**：亮青十字标记（选中的更大更亮）+ 目标空心方框 + **鼠标落点光标**（地面点吸附最近 `NavSpot`）；`GameInput` 反射读原版 Rewired 动作（当时用于切队） |
| **v1.4.6** | **1.4.6** | **按键不再与原版共键**：换队改用自建 `Z`/`X`（`RemotePrevGroupKey`/`RemoteNextGroupKey`）；新增 `RemoteAlsoUseVanillaKeys`（默认 false）；**点选改为"连点累加"**（Shift 移除、右键清空）；"已接管/前进"提示只列本次涉及的小队；细长/退化拖动按"点一下"兜底 |
| **v1.4.7** | **1.4.7** | **交互定稿（少按键）**：选择 = `Shift` + 左/右键点单位（选中**整队**，再点取消）/ `R` 全选 / `Alt` 框选；下令 = **左键或右键点地块**集结前进（按兵种各成队，多兵种**就地分到相邻格**），**移动后自动取消选择**（同原版 `SquadMover`）；不按 Shift 时左右键完全归原版；移除 Z/X 换队键与 `GameInput`；**与我方选择互斥**（选中遥控单位时调用原版 `SelectSquad(null,false)` 取消我方选择，我方被选中时自动清空遥控选择 → 永不双控） |
| **v1.5.0** | **1.5.0** | **新增 `README.md`**（操作说明 + 项目链接 + 开源许可，≤250 汉字）；**文件日志默认关闭**（`[Diag] LogToFile = false` → `LogFileSwitch` 摘掉 BepInEx 的磁盘日志监听器并清掉启动残留，控制台不受影响，玩家装完不再生成 `LogOutput.log`）；版本号与仓库提交对齐（1.4.8 的内部跳号并入本版） |
| **v1.5.1** | **1.5.1** | **中英切换（界面 + 日志）**：新增 `Loc`（key = 简中原文，缺表回退；`[General] Language = auto / zh / en`，auto 只读游戏语言私有字段、不触发 I2 初始化），`UnitNames` 增英文显示名，`README.md` 改双语（内容同 v1.5.0），**cfg 说明保持简中**；**F1 菜单瘦身**：删掉默认数量/上限/船型整行 + 每行"默认 N 个" + 5 行遥控说明，只留「兵种 · 当前 · 数量按钮 · 2 行按键」，数量预设去掉"默认"改为 1/2/3/4/6/8/10/12（点多少装多少，仅受最大船容量裁剪），并删掉失去调用者的 `IngameMenu.SelectedUnit` / `UnitCatalog.MaxSquadSize`；菜单宽度 560 → 420（-25%）、按键拆 3 行，顶部加 **中文 / English 语言按钮**（写回 cfg 立即生效，与 `[General] Language` 同一份设置） |
| **v1.5.3** | **1.5.3** | **遥控交互 v2（显式修饰键）**：**按住 `Shift` = 精确指挥**（点单位=选整队、点地块=前进）、**按住 `R` = 全选 + 批量下令**，**两者都不按则点击 100% 归原版** → 根治"一次点击同时指挥我方与遥控单位"的双控（原靠 `VanillaSelected` 互斥，帧序上有窗口期）；选队/下令**左右键对称**（适配双键设置）；新增 `MarqueeSelect.ClickUsedForSelect`（每帧重算）阻止"选队那一下"被同帧当前进消费，修掉 v1.4.7 遗留的"已有选择时点单位会误当前进并清空选择"；**Alt 审计**：按住 `RemoteFreeMarqueeKey` 的那一次按下不再被当成下令、允许先按左键再补按 Alt 就地转框选（cfg 补注"左右 Alt 都认 / 前后按住都行"）；**按键说明只留 F1 菜单**（3 行），HUD 只报状态；`SelectAll` 补 `_pendingCenter`；同步 cfg 说明、README 与 §5 过时描述（`RemoteGrabRadius` 48→64px 等）；**版本号快进**（v1.5.2 的内部跳号并入本版） |
| **v1.5.4** | **1.5.4** | **下令不再强制修饰键**：有选中时**普通点击地块 = 前进**（选队仍要 `Shift`），执行前**挂起 2 帧**由原版状态仲裁（原版接管则放弃）→ 保留双控根治、去掉"每次移动都要按住 Shift"的手感问题；`Shift`/`R` 点地块 = 立即下令；新增 `[Remote] RemoteMoveRequiresModifier`（默认 false，可回到 v1.5.3 保守模式）；`MarqueeSelect` 新增 `PressWasDrag`、`ClickUsedForSelect` 改为按下置位到松开；F1 菜单按键行与 README 同步；**工程加固**：新增 `tools/check-loc.ps1`（i18n 一致性闸门，已并入 `build.ps1`）、`check-api.ps1` 检查面扩到全部被引用程序集（153→430 项）、HUD/菜单显示版本号；另按原版源码审查补上 OneButton 模式下的双控漏洞（按下瞬间留证"原版是否在管我方小队"）与**"点海面/非可站立地块 = 取消选中"**（照抄原版 `DeselectUnit` + 同名音效，T23） |
| **v1.5.5** | **1.5.4** | **仅 `README.md`**（英文条目改写 + 中文两处含混表述修正）；DLL 无变化，故版本号当时留在 1.5.4——本版起恢复"提交 = DLL 版本"口径 |
| **v1.6.0（第 2 批）** | **1.6.0** | **按作者实测反馈的 6 项（3 修 + 3 新）**：① **站立抽动（人多时）**——`movability` 被房距外推成**负值**（`Lerp` 未夹 t）、就位判定过严（外层槽位被同伴占住 → 全队长期"未就位"互相推挤）、目标格附近仍在做卡住检测（周期性横向绕行）→ 夹 t + 放宽就位（"离目标格够近即到位"，退出就位要求"真被挤走"）+ 目标格附近不做卡住检测（T37）；② **拖动误触发框选**——原先"在自己单位附近按下"这一次拖动就变框选、相机也让位 → 改为**只有按住 Alt 才框选**（新增 `[Remote] RemoteMarqueeFromUnit`，默认 false 可回旧手感），不按 Alt 拖动 = 原版平移相机（T38）；③ **原生单位上船前不能选**——登记时要求 `Commandable`（= 已下船）→ 改为 `Selectable`（含船上），可先选、先定集结点，落地自动前往（T40）；④ **站位照抄原版我方小队**（第一版自己发明的"每格铺 1 人到 25 格"太散、原生单位也被散开 ✗）——读游戏文件 `NavSpotFormationSquad`（玩家 `EnglishSquad` 的站位系统）后照抄 `UpdateFormation`（全队在同**一个目标格**上用 `SquadFormation(count, navSpot)` 排阵，槽距 = 各自 `radius*1.01*2`）+ `SlotPusher`（只在 `NavPos.Move` 失败时把重叠的两人各挪 20% 重叠量，迭代到不重叠）；跨兵种仍各占一格（点击格 + 相邻格）（T39）；⑤ **新功能：一键释放遥控**——F1 菜单按钮，清选中 + `RemoteGroup.Clear()`（还原接管前 order、销毁我们挂的组件），全部交还原版 AI（T41）；⑥ **菜单字体/宽度**——菜单宽度按语言自适应（简中 600 / 英文 760，原 420 会裁长句）、HUD ≤920 自适应，`Draw` 里临时把 `GUI.skin` 字号设 12 并在 `finally` 还原（不动游戏皮肤），菜单高度补"一键释放"行与第 4 行"拖动/框选"说明（T42）。版本号 1.6.0 |
| **v1.6.0（第 1 批）** | **1.6.0** | **按实机反馈修 7 项（v1.5.7 的收尾 + 两轮复测）**：① **选不中我方小队**——接管判定用 `PickAt`（屏幕兜底 64px）会被附近我们的单位抢走点击 → 改 `PickAtTight`（屏幕兜底 24px）且**指针下是我方小队时一律归原版**（T33）；② **烧房仍无视距离/地形**——`_target == null`（刚接管未下令）时 `SampleOrder` 早退写 `dist = 0`，`Arsonist` 便以为"已到房前"从任何地方扔火炬 → `SampleOrder` 改**单一出口**，`dist < 0.2` 一律换成 `NearestHouseDist()`，并**按邻近逐帧开关 `Arsonist`**（释放时 `RestoreArson` 还原）→ 贴同层房才烧（T30）；③ **重开战局残留船**——`_seenContainer` 用了 Unity 重载的 `==`，被 `Destroy` 的 `Transform` 也"== null"，把"换了新 `runContainer`（= 重开了一局）"误判成"未初始化"而静默失效 → 改 `object.ReferenceEquals`（T32）；④ **原生单位仍不可控**——`RemoteNativeUnits` 的旧 cfg 值 `false` 一直生效（BepInEx 不覆盖已有键）→ 键名改 `ControlNativeUnits`（默认 true）使其真正生效，并加**启动日志**与**首次登记日志**；F1 菜单新增**开关作用说明行**（"拦下 = 本关无原版敌人（无尽，F3 退出）；可遥控 = 原生敌人也能指挥"）；⑤ **未选中不得移动 / 未选中不接管**（作者明确要求）——`ExecuteMove` 要求"本次选中"非空，`RemoteGroup.MoveTo` 删掉"没选中就命令全部队"的回退，点击路由与旧路径都以 `HasPending` 为门槛（没选中时点击 100% 归原版、我们连栈都不压）（T36）；⑥ **点海面仍偶发不取消**——根因是我用自家全场景射线给原版 `NavSpotCast` 的"无效"结论**翻案**（打到海面/背后地形 → `GetNavSpot` 找到 1m 内的陆地格）→ 改成**采信原版 null**、只有抛异常才兜底；同时**双键左键不再看落点有效性**（非"我们的单位/我方小队"处一律取消选中）；⑦ **修一个实机 NRE**：`Reslot` 在还没有目标格时 `SetSlot(default(NavPos), false)`，而空 `NavPos` 的 `wPos` 会抛 `NavPos pos is null`（日志实测）→ `SetSlot` 只在 `has` 为真时读 `wPos`。版本号 1.6.0 |
| **v1.5.7** | **1.5.7** | **按实机反馈修 7 项**：① **点击路由在实机上从未生效**——反射订 `pointerRationalizer.onButtonDown` 取不到（与 `onClick` 不同）→ 改成**按下那一帧轮询 `Input.GetMouseButtonDown`** 自己判定（v1.5.6 的双键/单键手感这次才真正上线）；② **点选改为精确选 1 个单位**（`双击`=选整队、`Shift`=并入/移出），彻底避免与我方单位"共选 / 会合到同一格"；③ **修"集结后抽动"**：`GroupOrder` 增 `_settled`（到位 `walkDir=0`/`movability=0.05`）+ 到位阈值放宽 + 滞回（挤开 <0.8m 不重新起步）+ **就位后不再做卡住检测**；④ **恢复烧房**并修"到家"判据：改用房子真正的火炬落点 `House.worldTargetPos` 算**水平距离** + **高差门控**（`|Δy|>0.8m` 不算）→ 不再"山脚烧崖上的房"；⑤ **恢复"控制敌我"**：`[Native] RemoteNativeUnits` 回归且**默认开**（原版上岛敌人与投放单位共用同一套操作；`F2`/离开战局只撤登记不销毁）；⑥ **修"重开战局残留船"**：用 `island.runContainer` 换新判断这是重开的局 → `DestroyAll`（vanilla 只打扫 `runContainer`，不碰我们的 `landingContainer`）；⑦ **F1 菜单新增两个开关按钮**（`原版波次：拦下/正常`、`原生单位：可遥控/不可`，写回 cfg 立即生效）；另：`BlockVanillaWaves` 改每帧幂等重写（原先只拦一次会被 `Raid.IIslandFirstEnter` 协程最后赋的 `waveStartTime`（第 0 波 = 0）覆盖）并明确为**无尽自定义模式**（清光单位也不判胜，只能按 F3 退出）；README/§5/§6 同步 |
| **v1.5.6** | **1.5.6** | **版本号三处对齐 +（按玩家反馈）四项玩法/交互改动**：① **修 T2 菜单点击穿透** = `ClickShield`（菜单矩形上盖运行时不可见 UI，EventSystem 射线先命中它 → 原版 `PointerRationalizer.onClick` 不发布；`[General] MenuBlocksWorldClicks` 默认 true）；② **点击路由 v3** = `RemoteCursor`（默认 `[Remote] RemoteOrderButton=Right`：**左键点单位即选中整队（不用 Shift）、右键点地块即前进**；在按下那一帧把自己压到 `CursorManager` 栈顶吃掉这一次点击 → 原版不误选我方、不播 `FailedClick` 错误音、不需要 2 帧仲裁；失败自动回退 v1.5.4 手感）；③ **船上选中 + 登陆后集结点**（`Selectable`/`Commandable` 分离，船上不抢 `Pirate` 的 order，落地自动成组前往）；④ **接管本关** = `[Native] BlockVanillaWaves`（拦下原版波次 + 释放其滩头占位）、`[Native] RemoteNativeUnits`（原版上岛单位也可遥控，默认关）、`[General] ForceWinHotkey`（**F3 强制胜利**走 `EndOfLevel.AllVikingsKilled()` 原版胜利流程）；⑤ 工程：gates 470 项 / 151 词条、README 与 §5/§6 同步 |


> **版本号口径**：本次提交 = **`v1.6.0`**，`Plugin.VERSION` / `csproj Version` / HUD / 菜单 / 日志一律显示 **1.6.0**（上一批 v1.5.7 提交之后，作者两轮实测反馈的改动都在这一版里；表中"第 1 批 / 第 2 批"只是两轮反馈的区分，同一版号）。提交由作者手动执行，标题由作者撰写。

## 9. UI 打磨路线（v1.6.0 分析结论，待实施）

**结论：能借原版 UI，而且能借得很彻底。** 依据（游戏反编译源码 `BadNorthDatabase-main\src\Assembly-CSharp\`）：

| 原版设施 | 证据 | 我们能拿它做什么 |
|---|---|---|
| `RTM.UISystem.UIManager : Singleton<UIManager>` | 菜单栈 `menuStack` / `Add(menu)` / `activeMenu` / `blockUIInput`；Rewired 导航（`UISubmit`/`UICancel`/`UIHorizontal`…） | 把菜单注册进去 → 原版自动接管输入、焦点、手柄导航，**可以退休 `ClickShield` 那套"盖住菜单矩形"的 hack** |
| `UIMenu : MonoBehaviour` | `OpenMenu/CloseMenu`、`isOpen`/`isFocussed`/`blockingInput`、focus 事件、`ScrollRect`、navigables 池 | 菜单继承它即得到原版的开关 / 焦点 / 滚动行为 |
| **`GeneratedMenu : UIMenu`** | `AddButton(locTerm, action)` / `AddBoolWidget` / `AddIntWidget` / `AddMultiSelectWidget` / `AddOnOffWidget` / `AddYesNoWidget`；字段 `widgetParent`、`textButtonPrefab`、`textIconButtonPrefab`、`boolWidgetPrefab`、`intWidgetPrefab`、`multiSelectWidgetPrefab`（均 `[SerializeField] protected`） | **运行时生成整套原版风格控件**（按钮 = 兵种 / 数量 / 释放；开关 = BoolWidget；多选 = MultiSelectWidget） |
| `Widget` | **`SetNonLocalizedLabel(string)` 是 public**（内部 `localize.enabled = false; label.text = 文本`）；`label` 是 uGUI `Text`；`SetSuccessAudio/SetFailAudio/SetHoverAudio/SetVisibilityCallback/SetUpdateAction`；默认音效 `FabricID.uiButtonClick` / `uiError` | **不用碰 I2**：把 `Loc` 文案直接塞进原版控件；点一下自带原版按钮音 |
| `PauseMenu` / `UserSettingsMenu` / `ModalOverlay` / `DebugSettingsMenu`（都是 `GeneratedMenu`） | `PauseMenu` 挂在岛屿场景（`IslandUIManager.IAwake`）；`UserSettingsMenu.Open(ium)` 展示原版开菜单全流程（`islandViewfinder.Push` → `visibility.SetVisible` → `OpenMenu()` → 滑动动画），且自带**语言字体映射** `defaultLanguageFont` + `fontMap[]` | 运行时**克隆一份现成菜单**当"外观壳"（面板 / 羊皮纸 / 背景 / 布局 / Canvas 全套），再填我们自己的控件；中文用它的语言字体 |
| `IslandUINotificationManager.PostMessage(locTerm, priority, iconLeft, iconRight, distanceField, duration)` | public；`IslandUIManager.notificationManager` 是 public 字段；带 `UI/InGame/NotificationOn/Off` 音效与优先级 | **HUD 消息改走原版提示条**（现在是 IMGUI 的黑底文字） |
| `ModalOverlay : GeneratedMenu, IPoolable` | 池化模态 + `HandleClickOff` | "确定清场 / 释放遥控？"确认框用原版样式 |

**实施顺序（每步独立可验收、都能回退）**：

1. **提示条（最低风险，立刻"像原版"）**：`IngameMenu.Say` → `IslandUINotificationManager.PostMessage`。坑：它内部是 I2（`localizeTarget.Term = locTerm`）→ 要么传中文原文（I2 查不到显示 term 本身，英文玩家会看到中文 ✗），要么反射把 `localizeTarget.enabled = false` 后直写 `label.text`（= `SetNonLocalizedLabel` 的做法，中英都对 ✓）。HUD 常显块先留 IMGUI，但换**原版字体**（从任意原版 `Text` 取 `font`）与羊皮纸配色。
2. **F1 菜单换壳（核心）**：`class ModMenu : GeneratedMenu`；运行时**反射**把 vanilla 菜单实例的 5 个 prefab 字段与 `widgetParent` 拷进我们自己的实例，然后 `AddButton` / `AddOnOffWidget`（原版波次、原生单位）/ `AddMultiSelectWidget`（兵种、数量预设）/ 释放·清场·强制胜利按钮，逐条 `SetNonLocalizedLabel(Loc...)`；外观壳 = `Instantiate(vanillaMenu.gameObject)`（**先 `SetActive(false)`** 防其 `Awake/Initialize` 抢先跑）后清掉逻辑子物体、保留视觉层级。**取不到 prefab / 字段名不符 → 自动降级回现有 IMGUI 菜单**。
3. **确认框与图标**：`ModalOverlay` 做清场 / 释放确认；兵种按钮用 `AddButton(term, action, Sprite icon)`（`IconButtonWidget`）+ 运行时可取的原版图标（`SpriteBank` / 原版控件）。

**红线**：全程**不把任何游戏美术 / 音频资源打进 mod**（不做 AssetStudio 提取后分发）——只"运行时借用原版对象"，对公开仓库最安全，也天然跟随原版更新。

