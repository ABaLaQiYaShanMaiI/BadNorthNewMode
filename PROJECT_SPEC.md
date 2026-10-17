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
| net472 的 `System.Func<>` / `System.Action<>` | 编译把 `Func`/`Action` 解析到 **mscorlib**（.NET 4 起它们就在 mscorlib 里），而本游戏是 Mono 2.0 + **System.Core 3.5** → 运行期找不到 `[mscorlib]System.Func` ⇒ `MissingMethodException`（v1.7.5 挂原版设置项时被 `check-api` **当场拦下**） | 需要**游戏侧的委托类型**时**别写 lambda / 方法组**：反射取参数的 `ParameterType`，用 `Delegate.CreateDelegate(游戏侧类型, MethodInfo)` 现造委托，再 `MethodInfo.Invoke`（见 `VanillaSettings.PrepareWidgetApi`） |
| `EventSystem.IsPointerOverGameObject()` | 本游戏恒为真（世界点击走一个**全屏**手势接收器 `PointerRationalizer`）→ 点击被静默吞掉、无任何日志。**v1.7.4 又栽了一次**（拿它做 UI 门控 → 所有世界点击全被丢弃、报障"鼠标不能控制"；见 §11.10 修订 3） | 不用它做 UI 判断：世界点击直接订阅原版 `onClick`；要判"指针在不在 UI 上"用 `Util.PointerOverUi()`（`RaycastAll` + 跳过那个全屏接收器）。已做成**闸门**：规则⑦ 禁用清单 `tools\banned-api.txt`（加一行即永久拦截，见下表「坑 → 闸门 覆盖对照」） |
| `pointerRationalizer.onClick +=` | 事件类型是 System.Core 3.5 的 `Action`2，运行期解析不到 | 反射 `GetEvent` + `Delegate.CreateDelegate` + `AddEventHandler` |
| `EventInfo/MethodInfo == null` | `MemberInfo`/`Type` 的 `op_Equality`/`op_Inequality` 是 **.NET 4.0 才有的运算符**，游戏 mscorlib 2.0 没有（闸门实测会拦：`Type::op_Equality`、`MethodInfo::op_Equality`、`Type::op_Inequality`） | 一律 `object.ReferenceEquals(x, null)`；`Plugin`(onClick 订阅)、`GameInput`(反射读 Rewired) 都按这条写 |
| 中途投放不下船 | `brain.order` 被 `KillAllEnemies` 抢走 → `orderDist = 1e6` → `MaybeAct` 永不成立 | 生成后 `AttachAgentBehaviours()` 把 order 交还 `Pirate`（原版靠"生成期我方未部署"天然正确） |
| 幽灵船（波次残留） | 我们的 Wave 不在 `raid.waves` → `Raid.IIslandWipe` 不清它 | `SpawnLedger` 在战局结束 / 离开战局 / 换岛时销毁（订阅 `EndOfLevel.postProcess`） |
| 滩头看似可用却投放失败 | 点击命中 `Modules`，或进近廊道被挡 | 收集候选滩头逐个 `TryPlace`；`CorridorClear()` 预检廊道（悬停预览共用同一判定） |
| 连续投放"叠船"（看着像一船混编） | `CollectPlaced` 只查 `raid.waves`，而我们的 Wave 不在其中 → **自家的船不参与占位互斥**，可叠在同一滩头 | 改为收集 `raid.landingContainer` 下**所有** Landing（含我们自己的）；另加船员自检日志。⚠️ v1.7.1-b4：该互斥在"拦下原版波次"模式下会被我们自己的 `_blockedLandings` 抹掉（见 §11.3 B10），已修 |
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

**坑 → 闸门 覆盖对照（v1.7.4 修订 3 新增 —— 直接回答"为什么踩过的坑会再犯"）**

> 先说结论：**只写在文档里守不住**。`IsPointerOverGameObject` 从 v1.0.4 就在上面那张表里躺着，v1.7.4 还是被重踩了一次。
> 原因是结构性的：① 坑表是**给人读的散文**，而每一轮新任务都会先从"最标准的写法"下手（`EventSystem.IsPointerOverGameObject()` 正是 Unity 的教科书做法）；
> ② 上面那行的"正确做法"当初写的是**当时这条路的绕法**（"直接订阅原版 onClick"），读起来不像"这个 API 在本游戏任何地方都不许用"；
> ③ 当时没有实机复现手段，改完就按"应该修好了"写进文档（还真写了句"与原版一致"的错背书）——而它的症状恰恰是**静默吞点击、连日志都没有**，只有玩家真去用鼠标才会暴露。
> 所以每踩一坑都要问一句：**构建期拦得住吗？** 拦得住就做成闸门，拦不住才允许留在"靠人记"，并在下表写明。

| 坑 | 谁在拦 | 备注 |
|---|---|---|
| `EventSystem.IsPointerOverGameObject()` | **闸门**：规则⑦ 禁用清单（`tools\banned-api.txt`） | **加一行即永久拦截**；替代物 = `Util.PointerOverUi()` |
| `Array.Empty`（params 空数组被 Roslyn 优化出来） | **闸门**：① `check-api` **＋** 规则⑦ 清单 | 双保险：编译期清单 + 运行期成员引用校验 |
| net472 把 `Func`/`Action` 解析到 mscorlib（游戏侧却在 System.Core） | **闸门**：① `check-api` | v1.7.5 挂原版设置项时被它当场拦下；解法 = 按游戏侧类型现造委托 |
| 原版 `ValueWidget` 的 setter 必须 `return true` | `AddBoolWidget(词条, get, set, …)` 的 `set` 是 `Func<T,bool>`，**返回 false 会被当成"这次设置被拒绝"** → 控件不刷新（v1.7.5 实机现象：关掉了但"开"的高亮还亮着，退出设置再进才显示为关） | setter 写完就 `return true`（原版所有 setter 都这样）；外部改了值再靠 `ForceUpdate()` 跟上（设置界面开着时每 15 帧刷一次，同原版 `UpdateOccasionally` 节奏） |
| 反射成员用 `== null` 判空 | **闸门**：规则② | 作用域精确（只认反射类型的字段/方法内局部） |
| 空 `catch { }` 没有说明 | **闸门**：规则① | 有意静默必须写 `// silent-ok` |
| 用 `Debug.Log` / 硬编码盘符路径 | **闸门**：规则③ / 规则④ | 走 `Util.Log/Warn`、路径从 `BadNorthDir` 来 |
| 热路径（`Update`/`OnGUI`/`Draw*`/`Tick*`）里每帧 new 容器 | **闸门**：规则⑤ | 确有必要时写 `// alloc-ok` |
| 纵向布局链丢返回值（`DrawXxx(x, inner, y);` 少了 `y =`） | **闸门**：规则⑥ | v1.7.4 事故：4 格档位条被按钮行整片盖住 |
| 引用了整合包版被删/改的成员 | **闸门**：③ `check-pack-compat` | 需 `-PackAssemblyCSharp` 或环境变量 |
| 注释里引用的 `§N` 其实不存在 | **闸门**：⑤ `check-docrefs` | 文档引用不许"指空" |
| 翻译 key 缺失 / 占位符不一致 | **闸门**：② `check-loc` | 漏 key 会静默回退中文、占位符错会在 OnGUI 抛异常 |
| "采信原版结论"（`NavSpotCast` 返回 `null` 就是无效格） | ⚠️ **只能靠人** | 语义约束，静态扫不出来 → 改这块按 §5 判定表逐条对照 |
| `order` 契约、共享资产只读（`shipPrefab` / `VikingReference` 不许写） | ⚠️ **只能靠人** | 见 §10.2 的三条铁律 |
| 叠加行为类（"点原版 UI 不该给世界下令"、"档位条不该被谁盖住"） | ⚠️ **只能靠实机** | 流程要求：**先拿到截图/复现步骤再改**；改完在提交说明里留可复验步骤（本文件 §11 的"验证"列） |
| **"可能的 bug"（静态推断、实机没复现）** | ⚠️ **只能靠流程**（闸门拦不住） | §11.10 修订 4 的三条硬规矩：**① 不标"真 bug"（最多"推断，未复现"）；② 不为此改默认行为 —— 要改就默认关 + cfg；③ 不替未验证的写法背书** |

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
- **船速跟随难度**（v1.3.0）：`FollowDifficultyShipSpeed`（默认开）→ `speedMult = ShipSpeedMultiplier × levelNode.diffiucltySettings.shipSpeedMultiplier`（原版语义；VeryHard 更快）。**运行时调速（菜单里开关 + 拖动滑条，含在航船）设计定稿见 §10（待落实）** —— 船速在 `TryPlace` 时被烘焙进 `ShipTravel.duration`，故"在航船"必须改 `duration`（不是 `ship.speed`，更不是共享 `shipPrefab`）。
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
- **释放遥控（v1.6.0）**：F1 菜单按钮 → 清空选中 + `RemoteGroup.Clear()`（还原接管前的 order、销毁我们挂的组件）→ 所有受控小队立刻回原版 AI；原生单位的登记保留，随时可再选。
- **菜单与 HUD 的字号/宽度（v1.6.0，作者反馈"字体显示不完整"）**：菜单宽度改为**按语言自适应**（简中 600 / 英文 760；原 420 会把长句裁掉），HUD 宽度自适应屏幕（≤920）；`IngameMenu.Draw` 里临时把 `GUI.skin` 的 label/button/box 字号改成 **12**（中英都能完整显示），`finally` 立刻还原，**不动游戏自己的皮肤**；菜单高度同步加上"一键释放"行与第 4 行"拖动/框选"说明行。

- **UI 借用与外观（v1.6.1）**：提示走原版通知条（`IslandGameplayManager.notificationManager.PostMessage`，并直写 uGUI `Text` 绕开 I2 词条）、破坏性操作走 `ModalOverlay` 确认框、按钮音 `FabricID.uiButtonClick`、字体借原版 `Font`；面板与按钮的圆角九宫格贴图由 `MenuSkin` 运行时生成（`GUIStyle.border` 切片）。确认框在前台时 IMGUI 不画、点击路由全部早退。

- **UI 第二轮（v1.6.1 第 2 批）**：字体链 = `[UI] FontName` → 原版 `fontMap` → 系统中文黑体（`CreateDynamicFontFromOSFont`）→ 原版 Text 字体，逐级用 `Font.HasCharacter` 验真；贴图用 `VanillaSprites`（按名 → 原版 `ButtonWidget` 兜底 → 九宫格 → 自绘）；头像条 = `SquadBar`（点头像选中整队；与 `ClickShield` / `MarqueeSelect` / `RemoteCursor` 的让位关系见 §6）；头像真源 = `VikingAgent.vikingReference.sprite2`（`UnitPortraits`）；配色照作者给的官网 PDF（量出沙底 / 蓝灰 / 黄键 / 黑字）。**已核对的原版资源名**（UnityPy 读 `data.unity3d`）：字体 `Body` / `Body_Chinese` / `Body_Chinese_Simp` / `Buttons` 等 18 支；贴图 `UISprite`(32², border 10)、`UI_Buttons_Filled_NoBorder/Border`(border 14)、`UI_Buttons_Dashed_White`(border 16，原版焦点框)、`UI_Flair`(128² 底纹)、`Hero_Class_*`(256² 兵种徽记)；配色取自 `Palette.png`。

- **代码体检（v1.6.3）**：热路径不再每帧分配（`MenuSkin.Measure` 复用 `GUIContent`、九宫格坐标用静态数组、HUD 行列表复用、帮助行缓存）；原版技能条探测与头像条的受控标记 / `×N` 文本改为 0.2~0.3s 扫描一次；删 `ForeignUnit.displayName`（组名改为现算）、`VanillaSprites` 的 3 个未用常量、3 个重复词条；`Contains || HudContains` 收敛到 `IngameMenu.AnyPanelContains`。gates 581 项 / 174 词条。

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
- **T41（v1.6.0）**：释放遥控 —— 菜单点按钮后所有受控小队立刻回原版 AI；提示「已释放遥控：N 支小队 / M 个单位」；之后还能重新选中；没有受控小队时提示「没有受控的遥控小队」。
- **T42（v1.6.0）**：菜单字体与宽度 —— 中英两种语言下每行都完整显示（不裁字、无方框）；HUD 状态行在英文下同样不裁；点菜单按钮不穿透到世界（`ClickShield`）。

- **T43（v1.6.1）**：原版提示条 / 确认框 —— 提示是否出现在游戏通知条、切英文后是否仍是英文；`F2` 清场与释放遥控是否弹原版确认框、点确定才执行、点取消无动作；确认框开着时点它不会误选单位；`UseVanillaUI=false` 时回退到 HUD 文本。
- **T44（v1.6.1）**：菜单外观与自适应 —— 圆角面板 + 亮青当前兵种、中英不裁字、改 `UiFontSize` 立即生效；拖标题栏可移动且不出屏；HUD 排在面板下方；提示框也能拖。

- **T45（已实测 ✓，v1.6.1）**：字体 = 原版艺术字，日志 `UI 字体 = game:Body_Chinese_Simp`；`[UI] FontName` 填系统字体名时日志应为 `cfg:`；`UiFontSize` 调大后不裁字。
- **T46（已实测：头像偏小 → v1.6.2 修，见 T48）**：头像条 —— 投放后底部出现一行头像、人多的在前；点头像选中整队（亮青）、`Shift` 并入；`SquadBar=false` 整条消失；点头像不会在世界里误选 / 误移。
- **T47（已实测：观感不符 → v1.6.2 改官网配色，见 T48）**：外观 —— 面板 / 行 / 按钮是否原版形状；日志是「已借用原版按钮底」还是「没借到」（后者回退自绘圆角）；悬停 / 选中高亮是否看得出。

- **T48（v1.6.2 待实测）**：官网配色 / 紧凑菜单 / TNR 数字 / 原版头像 —— 沙底黑字且无描边（当前兵种 = 蓝灰 + 原版虚线框）；菜单字号 13、兵种 >4 种两列；数量值、预设、序号、头像条 `×N` 都是 Times New Roman；头像条用原版兵种头像（`sprite2`）。微调只改 `[UI] PanelColor / ButtonColor / UiFontSize`。

- **T49（v1.6.2 第二轮待实测）**：头像条整改 + 菜单放大 —— 兵种名与 `×人数` 不被裁；选中我方小队时条自动抬到原版技能条上方（或 `SquadBarBottom` 固定）；**同兵种两船兵只占一格**、`×N` 是总数、点一下整类一起选中。

- **T50（v1.7 计划，未落实，落实依据 = §10）**：**船速运行时控制**（F1 菜单里「跟随难度」开关 + **可拖动滑条** + 「含在航船」开关 + 重置）——滑条对后续投放立即生效；「含在航船」为开时在航船立刻变速且**不跳位**。**判据**：见 §10.6 的 T-S1~T-S8；只影响本模组投放的船，原版波次与难度语义零改动；零 Harmony 补丁（仅在 `duration` 这一处做反射写，取不到字段自动降级为"只影响后续投放"）。

## 7. 命名与提交约定

- 产物：`BadNorthNewMode.dll`；命名空间 `BadNorthNewMode`；GUID `badnorth.newmode`；部署到 `BepInEx\plugins`。
- 仓库根另有 **`README.md`**（面向玩家的操作说明 + 链接 + 许可，≤250 汉字，不再写其他内容）。
- **版本号统一**：`Plugin.VERSION` = `csproj Version` = 仓库提交 `vX.Y.Z`（自 v1.2.0 起）。**本次提交 = `v1.7.4`**（两处都写 `1.7.4`；本版含两批改动，见 §8；作者手动提交）。
- 提交：一个里程碑一个提交，标题由作者撰写。
- 文档纪律：只本文件（+ 可选 `开发日志.md`）；新改动只在 §5 追加一行、§8 表格追加一行，不写长叙事。
- **代码结构**（v1.3.1 按职责拆分）：`Plugin`(入口/输入/取点) · `IngameMenu`(HUD + 兵种菜单) · `DropPlanner`(滩头解析/落差/占用/廊道，长方法拆成 `CheckClickHeight`/`CollectCandidates`/`PreferClearCorridor`) · `UnitCatalog`(兵种·船·人数) · `LandingInjector`(建树/投放/装配) · `FlotillaLauncher` · `SpawnLedger` · `DisembarkWatchdog` · `ShipboardThreat` · `PlacementMarker` · `ModConfig` · `UnitNames` · `Util`(向量格式化 / cfg 守卫 / 日志守卫)。
- **遥控相关文件**（v1.4.0）：`ForeignUnit`(非原生标记 + 注册表) · `RemoteGroup`(控制组容器：成组/并入/释放/槽位) · `GroupOrder`(自研 `IAgentOrder`：距离场 + 槽位) · `MarqueeSelect`(右键框选 + 相机拖拽让位)。
- **日志文件开关**（v1.5.0）：`LogFileSwitch`(摘掉 BepInEx 磁盘日志监听 + 清启动残留；`[Diag] LogToFile` 默认 false)。
- **菜单点击拦截面**（v1.5.6）：`ClickShield`(菜单矩形上的不可见 UI，阻断原版 EventSystem 点击；`[General] MenuBlocksWorldClicks` 默认 true)。
- **点击路由 / 接管本关**（v1.5.6）：`RemoteCursor`(按下那一帧压 `CursorManager` 栈顶吃掉这一次点击：左键=选整队、右键=前进；`[Remote] RemoteOrderButton` 默认 Right) · `LevelTools`(拦下原版波次 + F3 强制胜利；`[Native] BlockVanillaWaves`) · `ClickShield`(菜单矩形上的不可见 UI)。
- **构建闸门**（v1.7.3 起**五道**，全部并进 `build.ps1`）：① `tools/check-api.ps1`（全部被引用程序集的成员引用都要在游戏侧存在：BCL + UnityEngine* + BepInEx* + Assembly-CSharp，当前 **650 项**）② `tools/check-loc.ps1`（Loc 词条：无缺失 key、无死词条、`{n}` 占位符一致、无重复 key，当前 **227 条**）③ `tools/check-pack-compat.ps1`（对整合包替换的那份 `Assembly-CSharp.dll` 做同样的成员引用校验，当前 **165 项**；需传 `-PackAssemblyCSharp` 或环境变量 `BadNorthPackDll`）④ `tools/check-rules.ps1`（静态规则：空 catch 必须写 `silent-ok`、反射成员不许用 `== null` 判空、禁 `Debug.Log` 与盘符路径、热路径方法体不许每帧分配、**纵向布局链 `DrawXxx(x, inner, y)` 必须接返回值**（规则⑥）、**规则⑦ 禁用清单**（`tools\banned-api.txt`，逐行扫全部 `.cs`；当前 2 条：`IsPointerOverGameObject` —— 本游戏恒为真、会静默吞掉世界点击，`Array.Empty` —— 游戏 mscorlib 2.0 没有）；**不需要游戏 DLL**）⑤ `tools/check-docrefs.ps1`（`.cs` 注释里的 `§N`/`§N.M` 必须在 md 里真有对应标题；**不需要游戏 DLL**）。④⑤ 可在 CI 跑（`.github/workflows/gates.yml`）。
- **中英切换**（v1.5.1）：`Loc`(界面/日志文案对照表 + `[General] Language`) · `UnitNames`(增英文显示名) · `README.md`(双语)。

- **借用原版 UI / 自绘外观**（v1.6.1）：`VanillaUI`（原版通知条 · `ModalOverlay` 确认框 · `FabricID` 按钮音 · 借原版 `Font`；`ModalShowing` 供各处让位） · `MenuSkin`（运行时生成圆角 9 宫格贴图与 `GUIStyle`，卸载时释放） · 新增依赖 `UnityEngine.TextRenderingModule`（只为 `UnityEngine.Font`） · cfg 段 `[UI] UseVanillaUI / UseVanillaFont / UiFontSize / ConfirmDestructive`。

- **借原版贴图 / 底部头像条**（v1.6.1）：`VanillaSprites`（按名字或原版 `ButtonWidget` 借 sprite + color，九宫格绘制 + 图标等比绘制） · `SquadBar`（底部小队头像条：分组 / 绘制 / 命中 / 点击选中） · `ClickShield` 支持**两块**矩形（菜单 + 头像条） · cfg `[UI] FontName / PanelColor / ButtonColor / TextOutline / SquadBar / SquadBarMax`（配色 = 灰蓝底 + 黄键 + 键上黑字，作者可按喜好微调）。

## 8. 版本对照（仓库提交 ↔ DLL）

| 提交 | DLL | 关键改动 |
|---|---|---|
| v1.0.0 | — | 立项 + 约束文件 |
| v1.0.1 | 0.1.0 | 主体落实：`F1` 生成单兵种（net472，首次可运行） |
| v1.0.2 | 0.1.0 | 修 `Array.Empty`（禁用 params 空数组） |
| v1.0.3 | 0.2.0 | 改点滩头陆地 + 海平面落差校验 + 落点预览 |
| v1.0.4 | 0.2.1 | 订阅原版 `onClick` + 去掉 `IsPointerOverGameObject` 闸门 + 点击日志 |
| v1.0.5 | 0.2.2 / 0.2.3 | 下船诊断与兜底；幽灵船修复 + 自动清场 + `F2` |
| v1.1.0 | 0.2.4 | 下船恢复原速（order 交还 `Pirate`）+ 候选滩头修投放失败 |
| v1.2.0 | 1.2.0 | 版本号统一；`F1` 兵种菜单 + `F2` 说明 |
| v1.2.1 | 1.2.1 | 日志去噪（解析缓存 + `LogOnce`）；数量阶梯 + 手动数量 |
| v1.2.2 | 1.2.2 | 默认数量按兵种梯度（巨人 1 个）；注释精简 154 → 75 行 |
| v1.2.3 | 1.2.3 | 本文件精简；注释规范写入 §2 |
| v1.2.4 | 1.2.4 | 滩头占用规则 + 船随人数匹配 + 修叠船 + 船员混编自检 |
| **v1.3.0** | **1.3.0** | **船上敌人算威胁；编队发射（一条音乐）；暂停不投放；船速随难度；跨岛兵种开关；占用表缓存** |
| **v1.3.1** | **1.3.1** | **纯重构（行为不变）：`Plugin` 拆 `IngameMenu`、`LandingInjector` 拆 `DropPlanner`/`UnitCatalog`/`Util`；日志与 cfg 守卫收拢** |
| **v1.4.0** | **1.4.0** | **遥控首版：拖或 `Alt` 框选、点地块按兵种成队前进（每队上限 40）；持续受控、不释放回 AI** |
| **v1.4.1** | **1.4.1** | **滩头占用改候选逐个试 + 全岛兜底；点选为主 + `Shift` 合并；修清场漏删登岛单位** |
| **v1.4.2** | **1.4.2** | **选中链自愈（注册表 ∪ 在册 squad）；拾取半径 48→64px；点空打诊断** |
| **v1.4.3** | **1.4.3** | **点选改世界距离（`RemoteClickRadius` 1.2m）；`R` 全选；诊断加屏幕坐标 / 相机名** |
| **v1.4.4** | **1.4.4** | **修「选中后点地块没反应」与相机取值；框选 / 选中时减速 0.1×；右键取消选择** |
| **v1.4.5** | **1.4.5** | **受控期间屏蔽 `Arsonist`（防隔岛烧房）；标记与落点光标对齐原版** |
| **v1.4.6** | **1.4.6** | **换队改自建 `Z`/`X`；点选改连点累加；细长拖动按单击兜底** |
| **v1.4.7** | **1.4.7** | **交互定稿：`Shift` 点选 / `R` 全选 / `Alt` 框选；点地块前进、移动后清选择；与我方选择互斥** |
| **v1.5.0** | **1.5.0** | **新增 `README.md`；文件日志默认关闭（`LogFileSwitch`）；版本号与提交对齐** |
| **v1.5.1** | **1.5.1** | **中英切换（`Loc` + `[General] Language`）；F1 菜单瘦身、数量预设 1/2/3/4/6/8/10/12 + 语言按钮** |
| **v1.5.3** | **1.5.3** | **遥控 v2：`Shift` 精确指挥 / `R` 全选下令，不按则点击全归原版；左右键对称；`ClickUsedForSelect`** |
| **v1.5.4** | **1.5.4** | **有选中时普通点击也能下令（挂起 2 帧仲裁）；新增 `RemoteMoveRequiresModifier`；加 `check-api` / `check-loc` 闸门** |
| **v1.5.5** | **1.5.4** | **仅 `README.md`（英文改写 + 中文修正），DLL 无变化** |
| **v1.6.1（第 2 批）** | **1.6.1** | **字体走原版 `fontMap` → 系统中文黑体（`HasCharacter` 验真）；`VanillaSprites` 借原版贴图做九宫格；底部小队头像条；提示框可拖动** |
| **v1.6.2** | **1.6.2** | **官网配色（沙底 `D5D0C8` / 蓝灰 `89A1AD` / 淡黄键 `E5C98E` / 黑字，去描边）；菜单字号 13 + 两列；数字 Times New Roman；头像读原版 `sprite2`；头像条按兵种合并** |
| **v1.6.3** | **1.6.3** | **代码体检（零行为变更）：热路径去分配、场景扫描改 0.2s 缓存、删死代码与重复判断、注释回到 ≤5%** |
| **v1.6.1（第 1 批）** | **1.6.1** | **UI 打磨：提示走原版通知条、破坏性操作走原版确认框、按钮音；`MenuSkin` 自绘圆角面板；标题栏可拖动并记忆；面板尺寸自适应** |
| **v1.6.0（第 2 批）** | **1.6.0** | **站位照抄原版我方小队（`SquadFormation` + `SlotPusher`）；新增「释放遥控」；菜单字号与宽度自适应** |
| **v1.6.0（第 1 批）** | **1.6.0** | **按实机反馈修 7 项：拾取改 `PickAtTight`；烧房判定改单一出口；重开局残留船（`ReferenceEquals`）；`RemoteNativeUnits` → `ControlNativeUnits`；未选中不移动；采信原版 `NavSpotCast` 的 null；修 `Reslot` NRE** |
| **v1.5.7** | **1.5.7** | **点击路由改按下帧轮询；点选精确 1 人（双击整队）；`GroupOrder` 防抽动；烧房改按 `worldTargetPos` + 高差门控；原生单位可遥控并默认开** |
| **v1.5.6** | **1.5.6** | **`ClickShield` 阻止菜单点击穿透；点击路由 v3（`RemoteCursor`）；船上可选 + 登陆集结点；接管本关（拦波次 / 原生单位 / `F3` 强制胜利）** |
| **v1.7.0** | **1.7.0** | **与《旧灰复燃的战旗》3.0 整合包 / Together 联机共存立项（含 2026-10-14 重做）：`Compat` 检测与让路（投放 / 遥控 / 终岛 / 拦波次 / `F3`）、`tools/check-pack-compat.ps1` 闸门、外生兵种 UI 兼容** |
| **v1.7.1** | **1.7.1** | **修 B1~B9（含"联机会话类名写错使让路静默全失效"、"拦波次在单机被误禁"、"盾兵挡不住我们投放的巨弓"）+ 局内「靠岸速度」4 档（`ShipSpeedRuntime` + F1 拖动条）** |
| **v1.7.2** | **1.7.2** | **修 B10：`BlockVanillaWaves` 误伤自家 `ModWave`（→ 叠船 / 编队出发错峰失效）；自建波显式写 `waveStartTime = 0f`；联机拒绝拦波次的文案只针对 Together（见 §11.3）** |
| **v1.7.3** | **1.7.3** | **性能与结构（零行为变更）：热路径去分配（HUD 文本 0.2s 节流、状态行 0.5s 缓存、候选扫描同帧去重、拦波次扫描按需重扫）；`MarqueeSelect`/`Plugin` 按职责拆 `partial` 文件、删死代码 `PickAt`、`MenuHeight` 自检；新增两道闸门 `check-rules` / `check-docrefs` + CI（见 §11.7）** |
| **v1.7.4（第 1 批）** | **1.7.4** | **UI 可读性与挡岛：面板高度改"实测自纠"（修 77px 空高）；删冗余说明、说明块可关（`[UI] ShowHelp`）、船速 ×1 标"（原版）"；数字字体默认跟随原版（`[UI] NumberFont`）；修"切语言后字体不重挑"；菜单开着时 HUD 并入面板 + 标题栏可收起（见 §11.8）** |
| **v1.7.4（第 2 批）** | **1.7.4** | **UI 间距与排版（作者实机反馈）：行距 10→13、说明行与按钮拉开 6px、说明文字垂直居中、数量行预设铺满到右边界、靠岸速度改成"轨道在上/说明在下"；环境自检挪到第一次 Update（修"整合包=0 个插件"的误导数字）；`[Diag] CleanLogOnStart`（默认不再删共享日志）；点击路由 / 框选加 UI 门控（点原版 UI 不再对世界下令，见 §11.10）** |
| **v1.7.5** | **1.7.5** | **在游戏原生设置界面里开关本模组：设置 → 控制 分类末尾两行（`BadNorthNewMode：启用模组` = 新 cfg `[General] Enabled` 总开关；`原生单位：可遥控` = 与 F1 菜单同键）。总开关关掉 = 完全静默（不接管点击 / 不画菜单·HUD / 快捷键失效 / 不拦波次 / 在航船速还原原版）；实现**零 Harmony·零 MMHOOK**（轮询 `UserSettingsMenu` + 反射 `AddBoolWidget` + 运行期注入 I2 词条，见 §11.12）** |
| **v1.7.6** | **1.7.6** | **修"×2 靠岸速度退出岛屿再重进 → 船速叠乘"（真 bug，见 §11.13A：改存基准用时、登记表不再在离开战局时清空、每秒自愈比对）+ 修 F1 档位条"拖动状态卡死 → 平移相机时误换档"；新增 `[Landing] AllowShipStack`（船只可堆叠，默认关）并挂进原版设置 → 控制 分类第三行（与前两行同一套机制，见 §11.13B）；**生命周期与状态清理专题**（见 §11.14：关总开关还原被拦波次、离开战局/换岛清每局状态、卸载销毁我们创建的常驻对象与设置行）；顺带修"投放失败后半成品 Landing 留在编队里"** |
| **v1.7.7** | **1.7.7** | **原版设置里给本模组一个独立分类标题（克隆原版分类容器、换 I2 词条写我们自己的标题，按层级顺序插到第 2 个标题位置；新 cfg `[UI] SettingsCategoryIndex`，克隆失败自动退回"塞进控制列"，见 §11.15A）+ Together 联机对接研究：新增 `TogetherBridge` **只读探测层**与 `[Compat] TogetherProbe`（版本/协议/账本序号/波签名/预检；**不写任何 Together 状态**、联机让路不变），并给出"只调一个私有 `AfterAccept` 即可登记"的实读结论与发射权冲突；**路线 A（全部在本 DLL 内、纯反射）实现说明书见 §11.16（未落地代码）**；实机两轮修：① 插入点改为"锚点标题块最前面"（标题与容器之间还夹着装饰节点，按"紧邻前一个兄弟"会插到标题后面）；② 标题改为克隆原版标题节点、关掉其美术层再放我们的 `Text`（字号照抄行标签、颜色照抄原版染色），观感与左边距对齐；③ 靠岸速度档位改为整数 **×1 / ×2 / ×5 / ×10**（档距 1/2/5/10，越高档变化越夸张；旧 cfg 值自动吸附到最近档） |
> **版本号口径**：按"新增功能"递增版本号 ⇒ 当前 = **`v1.7.7`**，`Plugin.VERSION` / `csproj Version` / HUD / 菜单 / 日志一律显示 **1.7.7**（**v1.7.7 = 设置界面独立分类 + Together 只读探测**，见 §11.15；v1.7.6 = 修船速叠乘 + 船只堆叠开关 + 生命周期清理，见 §11.13/§11.14；v1.7.5 = 原版设置里可开关本模组 + 模组总开关，见 §11.12；v1.7.4 的两批：第 1 批 = UI 可读性与挡岛（§11.8）、第 2 批 = UI 间距与排版 + 三项代码问题（§11.10）；v1.7.3 = 性能与结构 + 两道新闸门（零行为变更，§11.7）；**v1.7.2 = 修 B10**（拦下原版波次误伤自家 Wave，§11.3）；v1.7.1 = B1~B9 + 局内「靠岸速度」4 档；v1.7.0 = 整合包 / Together 共存立项与重做。更早：v1.6.1 两批 = 借用原版 UI + 原版艺术字 + 底部小队头像条 + 提示框可拖动；v1.6.2 = 官网配色、菜单两列紧凑、兵种头像读原版 `sprite2`，见 §5「UI 第二轮」与 §9；**v1.6.3 = 代码体检**（纯优化、零行为变更，见 §5）。）。提交由作者手动执行，标题由作者撰写。

## 9. UI 打磨路线

> **状态**：第 1 步（提示条）与第 2 步（借原版控件外观：字体走 `fontMap` / 系统中文黑体，面板与按钮借原版 sprite 做九宫格）都已做。菜单**不进** `UIManager.menuStack` —— 我们要「边点地图边投放」，而 `UIMenu` 会抢焦点并挡输入，属设计冲突，所以 F1 菜单保持 IMGUI（借原版贴图与字体）；底部头像条同样自绘。第 3 步（`GeneratedMenu` 控件壳 / 兵种图标 / 更多 `ModalOverlay` 用法）待做。

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

---

## 10. 船速运行时控制（设计定稿，**v1.7.1 已以"4 档"形式落地**）

> ⚠️ **状态更新（2026-10-14）**：作者最终选择 **4 档**（`1 / 1.25 / 1.5 / 2`）+ **F1 菜单 4 档拖动条**，而非本节设想的连续滑条。
> **实现与验收以 §11.6 为准**；本节的连续滑条细节（拖动中节流、`ApplySpeedToInFlight` 开关、T-S1/T-S3/T-S5 等滑条专属项）**不再实施**，
> 仅作为设计背景保留。核心结论仍然有效并被 §11.6 沿用：反射写 `shipTravel.duration/fleeDuration`、只写每条船自己那份、
> **绝不碰共享 `shipPrefab`**、反射失败即降级。



> **状态**：**设计已定稿，本文档即为落实依据；代码未写**（需求原话：作者问"本项目能否设计开关拖动以控制船只速度"）。
> 计划版本：v1.7（落实后并入 §6 的 T50 与 §8 版本对照）。**本节所有"原版事实"均已用 BepInEx 自带 `Mono.Cecil` 反查 `BadNorth_Data\Managed\Assembly-CSharp.dll` 验证**（与 `tools/check-api.ps1` 同一套工具、同一份口径）。

### 10.1 目标（玩家看到的样子）

F1 菜单「接管开关」那一行**下方**新增一行 **船速**：

```
船速   〔跟随难度 ●〕   ────────●──────   1.50×   〔含在航船 ●〕   [重置]
```

- **拖动滑条**（0.25× ~ 4×，步进 0.05；按住 `Shift` 精调 0.01；在滑条上滚轮 ±0.05）→ ① **对后续投放立即生效**（不必重启、不必手改 cfg）；② 「含在航船」为**开**时，**已经在航行的船立刻变速**（且**不跳位**）。
- **「跟随难度」开关** = 直接绑已有的 `[Landing] FollowDifficultyShipSpeed`（复用菜单现成的 `DrawToggle`）。
- **「含在航船」开关** = 新增 `[Landing] ApplySpeedToInFlight`（默认**开**）。
- **「重置」** = 回 1.00×（不动难度倍率）。
- 作用域：**只影响本模组投放的船**（登记册口径）；原版波次、难度语义、原版船一律不动。

### 10.2 原版事实（IL 级依据 —— 决定了"改哪个字段才有效"）

| 事实 | 依据（`Assembly-CSharp.dll`） |
|---|---|
| 有效航速 = `shipPrefab.speed × speedMultiplier × 随机档位` | `Landing.TryPlace`：`r = Random.value` → `r<0.1: 0.85 ｜ r≤0.7: 1.0 ｜ r≤0.9: 1.2 ｜ 否则 1.4`；`eff = ldarg.3(speedMultiplier) × r`；随后 `new ShipTravel(endPos, -dir, Landing.get_speed() * eff, Landing.get_speed(), island.fog.capsuleCollider)` |
| **`duration = distance / speed`、`fleeDuration = distance / fleeSpeed`** | `Landing/ShipTravel::.ctor`：`distance = Vector3.Distance(startPos, endPos)`（`startPos` 先取 `endPos − dir×50`，raycast `fog.capsuleCollider` 命中就用命中点，未命中退 `endPos − dir×6`） |
| **进港吃倍率、离岸不吃** | ctor 的第 3 个 speed 实参 = `get_speed() × eff`（→ `duration`）；第 4 个 = `get_speed()`（→ `fleeDuration`），**不含 speedMultiplier** |
| 在航速度**每帧只由 `duration` 决定** | `Longship.UpdateIncoming`：`interpolator += Time.deltaTime / duration`（`IL_0059~IL_0066`），位置由 `UpdatePosition()` 按 `interpolator` 在 `startPos→endPos` 间插值 → **改 `duration` 就是改实时航速，且 `interpolator` 连续、不会跳位**（`timeRemaining`/`distanceRemaining` 等派生量自动跟着变） |
| **投放后改 `ship.speed` 无效** | 全程序集扫描 `Longship::speed` 的引用点：只有 `Longship..ctor` 赋值、`Landing.get_speed()` 读取 → 而 `duration` 已在 `TryPlace` 时算好；`get_speed` 本身无其他消费者 |
| **绝不能改 `shipPrefab.speed`** | `landing.shipPrefab` 是关卡**共享预制体**（`island.levelNode.possibleShips` 里的资源）→ 改了会连原版船 / 其它投放一起变 |
| 改 `duration` 的**爆炸半径极小** | 全程序集扫描：`ShipTravel::duration` 只被 `ShipTravel..ctor` / `Landing.TryPlace` / `Longship.UpdateIncoming` / `Longship.get_timeRemaining` 引用；`fleeDuration` 只被 `ShipTravel..ctor` / `Longship.UpdateOutGoing` 引用 |
| 字段可访问性（决定实现手段） | 类型：`Voxels.TowerDefense.RaidGeneration.Landing`（public `MonoBehaviour`）、其嵌套类 `Landing/ShipTravel`（**NestedPublic, Sealed**）；字段：`Landing.shipTravel` = **public**（编译期可访问）、`ShipTravel.duration` / `fleeDuration` / `distance` / `startPos` / `endPos` = **public + InitOnly（readonly）** → **只读还能编译期读，赋值必须走反射** |
| 音乐 / 上岸节奏不受影响 | `Wave.get_launchDuration` 与 `Landing.startTime`（= `timeOffset`）在**波次开始时**就算好；调速只改船体位移 → 接近音乐与"波次排程"不会被打乱（这正是想要的） |
| 与时间减速不冲突 | `RemoteSlowMo` 走 `TimeManager.RequestTimeScale`（`UpdateTimeScale` 取最小值写 `Time.timeScale`），与 `duration` 无关；框选慢放时船同比变慢，相对速度仍是滑条倍率 |

### 10.3 实现设计

**A. UI —— `IngameMenu` 新增一行（不新增面板）**

- 新方法 `DrawSpeedRow(float x, float inner, float y)`，插在 `DrawTakeoverRow` 之后；`MenuHeight(n)` / `MenuWidth(n)` **必须同步**（本类首行注释已写明"改布局必须同步高度"）。
- 滑条**自绘**（零资源、与 `MenuSkin` 同风格）：轨道 = `MenuSkin.Bg(Rect,false,false)` 拉一条窄矩形 + 已填充段用 `RowOn`；滑块 = 12px 方块（`Texture2D.whiteTexture` + `BtnOnColor`/`Gold`）。命中用 `GUI.Button(knobRect, GUIContent.none, MenuSkin.HitCenter)` + `Event.current.type == MouseDrag`，或直接 `GUI.HorizontalSlider` 后覆写 `GUI.skin.horizontalSlider*`（二者都可，自绘更贴现有皮肤）。
- 拖动实现要点：**滑条不放标题栏** → 与 `HandleDrag()`（只在 `_titleRect.Contains(m)` 时接管面板拖动）天然不冲突；拖动期间**不要**调用 `IngameMenu.Say`（避免每帧刷原版通知条），只在**松手那一帧**播一次提示 + 日志。
- 输入安全：菜单内点击已被 `HandleDropMode` / `OnWorldClick` / `RemoteOrderAt` 用 `AnyPanelContains()` 排除，且 `ClickShield` 用不可见 UI 盖住 `MenuRect` → **拖滑条不会误投放、也不会漏给原版**（无需新增拦截面）。

**B. 数据通路 —— 新增 `ShipSpeedRuntime.cs`（约 120 行）**

```csharp
// 1) 后续投放：零改动。LandingInjector.TrySpawn 本来就每次现读 cfg（Util.V(ModConfig.ShipSpeedMultiplier,1f)
//    × FollowDifficultyShipSpeed 时的难度倍率）→ 只改 ConfigEntry.Value 即可立刻对"下一艘"生效。

// 2) 在航船：登记"本次投放用的基准倍率"，之后按比值缩放 duration
internal static void Track(Landing landing, float baseMult0);      // 投放成功后调一次（紧挨 SpawnLedger.Track）
internal static void ApplyToInFlight(float newBaseMult);            // 滑条变化 / 开关切换时调
//   duration_new     = duration0  * (baseMult0 / newBaseMult)   ← 用比值 → TryPlace 内部那个随机档位(0.85/1.0/1.2/1.4)
//                                                                 在比值里自动约掉，每艘船各自的抖动被保留
//   fleeDuration_new = flee0      / newBaseMult                 ← 撤离本就不吃倍率 → 用绝对值，语义 = "滑条就是全船速"
internal static void Forget(Island island);                          // 清场/换岛/结算时清登记
```

> 命名口径（防混）：`baseMult0` = 我们传给 `TryPlace` 的 `speedMultiplier`（= `ShipSpeedMultiplier × 难度倍率`），**不含** §10.2 里 `TryPlace` 内部的随机档位 `r`；`ApplyToInFlight` 里对 `newBaseMult` 取下限（≥0.05）防除零。

- **反射防御**（风格照 `Plugin.TrySubscribeGameClick` 那套"取不到就降级 + 只 Warn 一次"，但更简单——字段是 public，只有赋值要反射）：
  缓存 `FieldInfo durationFi = typeof(Landing.ShipTravel).GetField("duration", Instance|Public)`（`fleeDuration` 同理）；**取不到就 Warn 一次并降级为"只影响后续投放"**；每次写都 `try/catch`，失败只报一次、不崩、不静默错。
- **写频率**：只在"滑条数值变化 / 开关切换"时写，**不是每帧** → 反射开销可忽略；拖动过程中可节流（≥50ms 或数值真的变了才写）。

**C. 落盘策略（临时 vs 持久）**

`ConfigEntry.Value` 赋值在 BepInEx 默认 `SaveOnConfigSet=true` 下会**写回 cfg 文件**。拖动中每帧写盘会抖 → 设计为：**拖动中只改内存 + `MarkDirty`**，松手后写一次；另给一个「保存到配置」按钮（或滑条下的"记住"开关）把"临时调速"和"持久化"分开，与作者"cfg 手动可控"的习惯一致。

**D. 联机降级（Together /《旧灰复燃的战旗》）**

客机侧世界由主机权威（`WorldSnapshot` + 客机侧 `Longship.Update` Patch）→ 改 `duration` 会被覆盖/无意义。
✅ **联机检测已在仓库里**（v1.7.0 的 `Compat.TogetherOnline` = 只读反射 `Together.Session.Active`（订正：早先文档写的 `Together.Net.Session` 是错的），取不到按"未联机"处理并只 Warn 一次）→ 本节实现**直接复用它**；另外 `Compat.RemoteBlocked` 已把整条遥控链路（含框选与减速）在联机中整体让路，落地时保持一致口径（细节见 `BADNORTHMOD_COMPAT_PLAN.md` §10.3）。
落实时：**客机自动禁用「含在航船」**（开关置灰，判定用 `Compat.RemoteBlocked` / `Compat.TogetherOnline`），HUD 提示"联机客机：只影响后续投放"；主机侧照常。

**E. 生命周期清理**

登记点与 `SpawnLedger.Track/TrackSquad` 同一处（`LandingInjector.TrySpawn`）；`SpawnLedger.DestroyAll()`（`F2` 清场 / 离开战局 / 换岛 / `EndOfLevel.postProcess`）里顺手 `Forget`，避免持有已销毁 `Landing` 的引用。

**F. 文案与 cfg**

- `Loc` 增中英词条 6~8 条（"船速"、"跟随难度"、"含在航船"、"重置"、"拖滑条调速"、提示与日志格式串）——**`tools/check-loc.ps1` 是构建闸门**，key（简中原文）与英文占位符 `{n}` 必须齐平。
- `ModConfig` 新增 `[Landing] ApplySpeedToInFlight`（`bool`，默认 `true`，说明"调速是否立刻套用到已经在航行的船；关 = 只影响之后投放的船"）。

### 10.4 改动清单（预计半天，含两次实机验证）

| 文件 | 改动 | 规模 |
|---|---|---|
| `ShipSpeedRuntime.cs` | **新增**：登记 / 反射写 `duration`·`fleeDuration` / 降级 / 清理 | ~120 行 |
| `IngameMenu.cs` | `DrawSpeedRow` + 滑条绘制与命中；`MenuHeight` / `MenuWidth` 同步；HUD 加一行"船速 ×1.50（在航 3 艘）"（可选） | ~70 行 |
| `LandingInjector.cs` | 投放成功后一行 `ShipSpeedRuntime.Track(landing, baseMult0)`（把已算出的 `speedMult` 一起传进去） | 1~2 行 |
| `SpawnLedger.cs` | `DestroyAll()` 里 `ShipSpeedRuntime.Forget(...)` | 1 行 |
| `ModConfig.cs` | `BindLanding` 加 `ApplySpeedToInFlight` | ~3 行 |
| `Loc.cs` | 6~8 条中英词条 | ~10 行 |
| `PROJECT_SPEC.md` / `README.md` | 本节落实后改状态；`README` 的"Controls"补滑条一行 | — |

### 10.5 边界与风险（落实时必须守住）

1. **零 Harmony 补丁的原则不破**：全部改动 = 投放时多传一个倍率（本来就是我们的代码）+ 对 `ShipTravel.duration` 的一次反射写，**不 Patch 任何原版方法**（与 §2.2 一致）。
2. **作用域只限本模组投放的船**：登记册口径（`SpawnLedger` 同源）；原版波次的船不受影响 → 不会变成"改难度"。
3. **`shipPrefab` 绝对只读**：任何"改船速"的实现都不得写 `landing.shipPrefab.speed`（共享预制体，见 §10.2）。
4. **反射属"非公开契约"**：`duration` 是 public 但 `InitOnly` → 写失败/字段改名时必须**降级为"只影响后续投放"并 Warn 一次**，绝不静默错、绝不每帧重试。
5. **极端倍率与下船看门狗**：4× 时靠岸动画窗口变短、0.25× 时更慢；`DisembarkWatchdog`（`DisembarkGrace` 默认 3s，到岸后才计时）行为不变，但若实测出现"快船到岸抖动 / 慢船像卡住"，按 §10.6 的 S8 记录读数再决定是否让滑条联动手表阈值。
6. **落盘语义要写清**：`ConfigEntry.Value` 会写回 cfg（BepInEx `SaveOnConfigSet`）→ 拖动中节流、松手落一次，并明确告知"这是持久设置"。
7. **联机**：客机禁用"含在航船"（§10.3 D）。**联机检测已在仓库里**（v1.7.0 的 `Compat.TogetherOnline()` = 只读反射 `Together.Session.Active`）→ 直接复用，别再写第二套；检测失败一律按"未联机"处理（绝不误禁）。
8. **未落实前不得对外宣称**：本节属计划，`README` / HUD / cfg 里**不应**出现尚未实现的滑条键（避免"文档先行"的假承诺）。

### 10.6 验证清单（落实后并入 §6，编号 T-S1 ~ T-S8）

| 项 | 判据 |
|---|---|
| **T-S1** | 拖滑条：数值实时变；日志恰一行 `[NewMode][船速] 1.00× → 1.50×（跟随难度 1.0×，在航 3 艘已套用）`；拖动过程中**不刷**通知条（只在松手播一次） |
| **T-S2** | 拖完松手**不投放**：`VerboseLog` 下无"菜单内点击 → 忽略投放"以外的投放日志、场上不多船 |
| **T-S3** | 拖滑条时面板**不移位**（`IngameMenu._pos` 不变）→ 证明没误触标题栏拖动 |
| **T-S4** | 0.25× / 1.0× / 4× 各投一艘（`VerboseLog` 打印实际 `speedMult`），实测"点击 → 靠岸"时间比约为倍率的倒数 |
| **T-S5** | 「含在航船」= 开：在航时改速度 → 船**立刻**加速/减速、**无位置跳变**（`interpolator` 连续）；= 关：在航船不受影响、只影响之后投放 |
| **T-S6** | `F2` 清场 / 离开战局 / 换岛 / 重开一局后登记清空（无 `MissingReferenceException`），新关从滑条当前值起算 |
| **T-S7** | 联机客机：「含在航船」自动禁用 + HUD 提示"只影响后续投放"；主机侧正常 |
| **T-S8** | 极端倍率（0.25× / 4×）下观察 `DisembarkWatchdog`：无"到岸后不下船"误判；若 4× 出现靠岸抖动，记录 `DisembarkGrace` 读数后再决定是否联动 |

### 10.7 备选方案（若反射不可接受）

| 方案 | 做法 | 评价 |
|---|---|---|
| **α（推荐，见 10.3）** | 反射写 `ShipTravel.duration` / `fleeDuration` | 唯一能"在航船实时变速且不跳位"的做法；改动面小、可降级 |
| **β（零反射 / 最低风险）** | 滑条**只影响后续投放**：`TrySpawn` 现读 cfg 已天然支持，UI 只需加一个滑条；「含在航船」开关**不提供** | 半天内可交付、零非公开契约；缺点是"已经在海上的船改不了" |
| **γ（油门式实时操纵，即"鼠标拖动操纵杆连续控速"）** | 给在航 `Longship` 挂组件，每帧按滑块值改写 `interpolator` 推进（`interpolator += dt/duration × k`），实现"拖动即控速" | 能做出"拖到哪船多快"的手感，但**每帧改游戏状态**、与 Together 的 `Longship.Update` Patch 交互，必须额外做联机降级与冲突处理 → 工作量翻倍，**不推荐**（除非作者要的就是"操纵杆"而非"滑条"） |



---

## 11. 兼容层落地记录（v1.7.1：重做 → b2 → b3 → b4，2026-10-14）

> 按作者要求**续写**在文末，不改上文既有结论。姊妹文档：`BADNORTHMOD_COMPAT_PLAN.md`（工程侧分析 + §10.3 重做记录）、
> `TOGETHER_MP_SPEC.md`（联机专项）、`兼容旧灰复燃的战旗.md`（玩家说明书，仅本地）。

**背景**：v1.7.0 曾在 `backup/remote-v1.7.0`（提交 `41175b1`）立项，因"当时不需要联机适配"从主线撤回（HEAD 回到 1.6.3）。
需求回归后按兼容性笔记**重做**，并修掉 7 个已确认缺陷（B1~B7，见 §11.3）。

### 11.1 兼容层结构

| 文件 | 角色 |
|---|---|
| `Compat.cs`（**新**） | 唯一的兼容层：`Chainloader.PluginInfos` 查 20 个整合包 GUID + 本体改造类型探测 + 只读反射会话类 `Together.Session.Active`（**不是** `Together.Net.Session`；另注意要用"AppDomain 找程序集 → `asm.GetType`"，带程序集限定的 `Type.GetType` 在 LoadFrom 上下文会返回 null）。**零 Harmony 补丁**，检测失败一律按"没有"处理（绝不误锁单机玩家） |
| `ModConfig.cs` | `[Compat]` 6 键：`AutoLetGo`(总开关) / `DropOnTerminal` / `BlockWavesWithOthers`(联机中也强行允许拦波次) / `ForceWinConfirm` / `Report` / `ForceOffline`(排障)；另有 `[UI] UnitNameOverrides`（外生兵种显示名，见 §11.5） |
| `LevelTools.cs` / `IngameMenu.cs` | 拦波次：**单机一律允许**（叠加只提示一次）+ 联机中拒绝（回写 cfg + 提示）、F3 二次确认、F1 让路、HUD 兼容状态行 |
| `MarqueeSelect.cs` / `RemoteCursor.cs` / `Plugin.cs` | 联机中遥控整体让路（含框选与选中减速） |
| `LandingInjector.cs` | 投放后"被整合包增强"自检日志（整合包从 `Squad.CreateAgent` 挂载，拦不住 → 只如实告知） |
| `build.ps1` + `tools/check-pack-compat.ps1` | **第三个构建闸门**：把成员引用校验基准换成**包版** `Assembly-CSharp.dll` |

### 11.2 让路面（判定口径）

| 功能 | 单机 + 整合包 | Together 联机中 |
|---|---|---|
| 投放（F1） | 普通关可用；**终岛**（`LevelNode.isEnd`）让路 | **让路**（否则主机判"待开波缺少稳定逻辑身份"→ 整局冻结） |
| 遥控（点击 / 框选 / 减速） | 可用 | **让路**（客机被主机姿态流覆盖 = 无效；主机侧污染对方账本与存活统计） |
| 拦下原版波次 | **照常可用**（"无限试炼场"的核心设计，**不让路**）；与接管波次的插件叠加只**提示一次** | **拒绝**（每帧改写波次时间会让主机永远发布不出去 → 整局冻结）；`BlockWavesWithOthers=true` 可强行开 |
| F3 强制胜利 | 二次确认 | 二次确认 |
| F2 清场 | 可用 | 可用（清掉"联机前投放的船"反而减少对方账本污染） |

### 11.3 修掉的缺陷

| # | 症状 | 修法 |
|---|---|---|
| B1 | 提示让人去 cfg 找 `CompatBlockWavesWithOthers`（该键不存在） | 词条/日志改回真实键名 `BlockWavesWithOthers` |
| B2 | F1 菜单显示"原版波次：拦下"但实际每帧被拒（UI 撒谎） | `Compat.RefuseWaveBlock`：写回 `false` + 通知条 + 日志一次 |
| B3 | 菜单点"拦下"只有日志、没有可见反馈 | `IngameMenu.DrawToggle` 增 `(allowOn, refuseWhy)` 重载，菜单侧拒绝并说明 |
| B4 | 联机中遥控仍生效（v1.7.0 漏做） | `Compat.RemoteBlocked` 接入 `MarqueeSelect` / `RemoteCursor` / `Plugin.HandleRemoteMode` |
| **B5** | **联机让路会完全失效**（最要命）：会话探测永远返回"未联机" → 联机里投放/遥控照真执行 | 笔记把会话类写成 `Together.Net.Session`（实测是 `Together.Session`）+ 带程序集限定的 `Type.GetType` 在 LoadFrom 上下文返回 null → 改为 `FindAssembly("Together")` + `asm.GetType` 试候选名 + 要求真有 `Active`(bool)；`PackType` 同改 |
| **B6** | **"拦下原版波次"被误禁**：作者口径 = 它是"无限试炼场"的核心设计，1.7.0 不得让它失效 | `WaveBlockForbidden` 改为**只在 Together 联机中拒绝**；单机（含整合包）一律可用，叠加风险改由 `WaveBlockRiskNote()` **只提示一次**（并进 F3 提示条） |
| **B7** | 外生兵种（别的 mod 加/换的）只能看内部名，UI/程序对"兵种 mod 一多"不够兼容（见 §11.5） | `[UI] UnitNameOverrides` 自定义显示名 + 每岛一行 `[NewMode][兵种]` 日志 + 菜单标注（外生）+ 内置表补 `Viking_Spearman=长矛兵` |
| **B8** | 单机：**我方"手持盾牌的长矛兵"挡不住我们投放的巨弓**（原版巨弓正常）——`ShipboardThreat` 在船上上报 `dangerous=true`，命中 `Shield.ShieldCheck` 第②条 `enemyDist < 1 && enemyData.dangerous → noShield`（**放盾**）；原版 `Brain.<Setup>m__0` 带 `navPos.island == null` 门控（**船上不上报**），船上只有 `Longship` 报且 `dangerous=false` | `ShipboardThreat.Update`：`dangerous` → **`false`**（只留 `hittable=true`＝"可被索敌"），并加"生成+存活"门控 |
| **B9** | 联机：主机投放后**客机看不到**非原生船/敌人（逻辑与伤害仍在） | Together `AgentAuthority.CanPublishActor` 只广播"自己清单里的波次"（`RaidIdentity.IsCommitted` + `HostRaidManifest.IsWaveReady`）→ 我们自建 Wave 的 `ActorSpawn` **永不发送**。属架构级（笔记 §8 L2），本轮不做协议对接；改为在"强行投放"时由 `Compat.NoteDropInSession()` **提示一次**"对方看不到" |
| **B10** | 单机：`BlockVanillaWaves`（拦下原版波次）打开时**我们自己的 Wave 也被当成原版波拦掉**（它同样挂在 `raid.landingContainer` 下，被 `GetComponentsInChildren<Wave>(true)` 一并命中）→ ① `_blockedLandings` 把自家已放置的船标成"被拦的原版 Landing" → `DropPlanner.CollectPlaced` 的 `!IsBlocked` 过滤把自家船从**占位表与 `TryPlace` 的互斥列表**里一起剔除（§4 坑表那条"含我们自己的"防叠船设计被抹掉 → 反复投放会叠船、预览/HUD 一直说"滩头可用"）；② `ModWave.waveStartTime` 被写成 `float.MaxValue` → 经 `ShipGroup.startTime`/`Landing.startTime` 同时毁掉 `Landing.TryPlace` 的重叠盒 Y（`startTime*0.1f` → 盒被搬到 Y≈3.4e37，`Intersections` 的 SAT 必然判"不相交"）与 `Wave.BeginWave` 的出发错峰（各自 `startTime` 差值饱和成 0 → 同一编队一帧全出发） | `SpawnLedger.Owns(GameObject)`（根节点或其子代是否属于我们的投放）+ `LevelTools.TryBlockWaves` 两处循环 `continue` 跳过自家 Wave；提示/日志里的波数改用只统计原版波的 `blocked`；`FlotillaLauncher` 给自建波显式写 `waveStartTime = 0f`（= 原版第 0 波语义；`MaxValue` 只留给"永不发射"的占位波）。另：`Compat.RefuseWaveBlock` 文案/注释订正为"只针对 Together 联机"（此前写成"整合包 / 联机"，纯联机时会误导排查） |

### 11.4 验证状态

- **构建闸门（本机实测通过）**：`api-check 623` / `loc-check 213（占位符 98）` / `pack-compat 165 → 全部存在 OK`（v1.7.1 / b4 轮）。
- **v1.7.1-b4 体检轮（2026-10-16，本机实测）**：修 B10 后三闸门 `api-check 624` / `loc-check 213（占位符 98）` / `pack-compat 165` 全绿；
  另用 Cecil 对整合包 `MOD\BepInEx\plugins` 的 21 个 DLL 反读 `[BepInPlugin]` → 与 `Compat.PackGuids` **逐条一致**，`Together.dll` 里确认 `Together.Session`（静态类）的 `Active`/`IsHost` 均为 public static bool；
  包版特征类型 `SpearShield` / `ShieldChargeAbility` / `ShieldJumpEnforcer` / `CanGroundPound` 与 `EnhancementTypes` 全 10 项都能在包版 `Assembly-CSharp.dll` 里匹配到；
  游戏目录里已部署的 `BadNorthNewMode.dll`（v1.7.1）与 HEAD 构建 **IL 级一致**（42 类型 / 469 方法 / 461 个有 IL 的方法逐一比对，**0 差异**；755 条字符串字面量全同）。
  原版语义复核（数据库反编译源码）：`Longship.shipTravel` 是**属性**（每次读 `landing.shipTravel`）→ 靠岸速度"在航即时"成立；`Wave.BeginWave` 只吃各 landing `startTime` 的**差值**，`Raid.MaybeLaunchWaves` 只吃 `timer >= wave.waveStartTime` —— 与 §4/§5 的既有结论一致。
- **参考数据**：包版 `Assembly-CSharp.dll` 与原版 **MVID 相同**（`38edd33c-…`，就地编辑版）→ 同一份产物单构建即可两边跑。
- **反射实证**：32 位 PowerShell 对真实 `Together.dll` 实跑 —— `asm.GetType("Together.Net.Session")` = null（旧写法必失效），`asm.GetType("Together.Session")` 命中且 `Active/IsHost/IsClient` 均为 public static bool。
- **待实机**：单机共存冒烟（8 项）、联机让路（可用 `tools\mp-fake-peer.ps1` 假客机/假主机验证）、§10 的"含在航船"滑条（尚未实现，见 §10）。

### 11.5 外生兵种兼容（v1.7.0-b2，为"加兵种的 mod"做的）

**问题**：整合包的长矛兵（`Wei_Jing_Chang_Mao` = 克隆投斧手引用注册出 `Viking_Spearman`，并 Patch `GameSetup.Awake` / `Squad.CreateAgent`）会与**原版兵种槽位**互相影响；我们的兵种列表虽然是**动态枚举**（`island.levelNode.enemies` ∪ 全局 `Viking_*` 字典），但**显示名**原先只认 8 个硬编码原版名。

**已落地的四条**（都不涉及 Harmony 补丁）：

1. **列表始终动态**：任何新单位只要进了"本关生成池"或 `LevelStateObjectReferences` 字典就会被列出 —— 不靠硬编码清单。
   - 字典侧默认**不限 `Viking_` 前缀**（`[UI] ListAllVikingRefs`，默认 `true`）：合法判据 = "值是 `VikingReference` 且 `agent != null`"。别的 mod 用别的命名也能列出来；若菜单出现奇怪条目，关掉这个键即回到"只认 `Viking_` 前缀"。
2. **显示名三级回退**：`cfg [UI] UnitNameOverrides`（用户可改）→ 内置表 → 去掉 `Viking_` 前缀并自动分字（`WeiJingSpearman` → `Wei Jing Spearman`）。**永远不会没名字**。
3. **可自查**：每座岛打一行 `[NewMode][兵种] 本关可选 N 种（*=外生兵种…）：Viking_Sword=剑兵，Viking_Spearman=长矛兵*…`；菜单信息行也在内部名后标注**（外生）**。报障时这一行就能确认"列表里到底有什么、内部名是什么"。
4. **头像/图标天然兼容**：`UnitPortraits` 直接读该单位自己的 `VikingReference.sprite2`（包里的长矛兵会 SetSprite2，所以图标正常），取不到才回退；`VanillaSprites.DrawIcon/DrawPortrait` 对 null 安全（什么都不画，不报错）。

**做不到的（要说清）**：若某个原版兵种被别的 mod **真正替换/移除**（引用表里不再有它），我们**无法凭空生成**它 —— 能投的只有"当前引用表里真实存在"的单位。这类"某兵种消失"的报障，请附日志里的 `[NewMode][兵种]` 那一行（它列出的是**实际可用**的完整清单）。

### 11.6 局内"靠岸速度"4 档（v1.7.1 / b4，取代 §10 的连续滑条）

**需求**：局内可调**原生波次 + 本模组投放**的船只"靠岸/撤离"速度；档位 4 档：`1`（初始）/ `2` / `5` / `10`（v1.7.7 起为整数档、档距逐步拉大 ⇒ 高档差别非常明显；旧 cfg 值自动吸附到最近档）。

**机制（已实测核对 IL）**

- 原版 `Longship.UpdateIncoming` 与 `get_timeRemaining` **每帧读** `landing.shipTravel.duration`；`UpdateOutGoing` 读 `fleeDuration`
  ⇒ 改这两个字段就能"**在航的船立刻变速**、不改航线、不跳位"。
- `Landing.shipTravel` 是 **public 值类型字段**（嵌套 `Landing/ShipTravel`），`ShipTravel` 里还有 `startPos/endPos/distance`
  ⇒ 写法 = 反射取值 → 装箱改 → 整块写回（Together 自己做同样的事，见其 `HostRaidManifest.Build`）。
- **只写每条 Landing 自己那份**，**绝不碰共享的 `shipPrefab`**（那是所有船共用的预制体）。
- 缩放用**比值**：`duration' = duration × 旧档 / 新档` ⇒ 每条船在 `TryPlace` 里的随机档位自动约掉，抖动保留；
  无需为每条船记录基准值（新船出生即"初始速度"，登记表默认 1）。
- 覆盖范围：原生波次的船与我们投放的船**都挂在 `raid.landingContainer` 下** ⇒ 一次遍历全覆盖（每 1s 补扫新出现的船；换档时立即全量套用）。

**门控与降级**

| 情况 | 行为 |
|---|---|
| 反射拿不到字段 / 写入抛异常 | 整体禁用（`Disabled`）+ **Warn 一次**；菜单控件置灰 |
| `duration` 被运行时忽略（initonly 只读） | 写入后**读回校验**，不一致即按"不可用"降级（绝不静默无效） |
| 联机**客机**（`Compat.SessionClient`） | 控件置灰并提示"由主机决定"（船由主机权威，本地改无意义）；主机/单机正常 |
| 运行期异常 | 全部 `try/catch`；本条 Landing 跳过，不影响其他船 |

**UI**：F1 菜单新增一行 —— 一句提示 + **四等分拖动条**（每格写 `×1 / ×2 / ×5 / ×10`）：按下即选、按住横扫即换档、松手播一次点击音 + 打一行日志；换档时立即写 cfg（`[Landing] ShipSpeedGear`）并套用到全船。面板高度/宽度已同步（`MenuHeight`/`MenuWidth`）。

**顺带修掉的一个真 bug（闸门抓到）**：`FieldInfo == null` 会编译成 `MemberInfo.op_Equality`，那是 .NET 4.0 才有的成员、**游戏 mscorlib 2.0 里不存在** → 运行期 `MissingMethodException`。
`tools/check-api.ps1` 当场拦下（`方法不存在: System.Reflection.FieldInfo::op_Equality(2 参)`），已全部改为 `object.ReferenceEquals`。**反射代码必须走这个约定。**

**验收要点**：① 单机开档 → 在航的船立刻改变到岸时间（不改航线/不跳位）；② 原生波次与投放的船同时生效；
③ 切岛/重开一局后登记表作废、新船按当前档生效；④ 调回 `×1` 应还原；⑤ 联机客机置灰、主机正常。




### 11.7 v1.7.3：性能与结构（P1–P3，零行为变更，2026-10-16）

> 触发方式：一次完整的代码质量体检（客观指标 + 全文件通读 + 产物 IL 分析），按性价比落地三批改动。
> **总原则：本批不改变任何可见行为** —— 并已用 IL 级比对证明（见下"验证"）。

**P1 热路径去分配**（玩家侧只表现为更少的 GC 抖动）

| 位置 | 原来 | 现在 |
|---|---|---|
| `IngameMenu.DrawHud` | **每帧**做 2 次全表计数扫描 + `Compat.StatusLine()` 拼串 + 最多 8 次 `Loc.F` 拼串 | 文本按 **0.2s 节流重建**（`BuildHudLines`）；`Say()` / 开关菜单 / 悬停行变化会立即标脏，手感不变 |
| `Compat.StatusLine()` | 每次调用都拼字符串（装了整合包后 HUD 每帧都会问） | 结果 **0.5s 缓存**，`Tick` 里终岛状态变化时主动失效 |
| `ForeignUnit` | HUD 调 `SelectableCount(false)` + `SelectableCount(true)` 扫两遍 | 新增 `SelectableCounts(out foreign, out native)` —— 一次遍历数两类 |
| `MarqueeSelect.EnsureCandidates` | 9 处调用、无帧级去重，每次 `new List<Agent>` + 逐单位 `GetComponent` | **同帧只扫一次**（`_candFrame`）+ 复用静态表 `_candOurs` |
| `LevelTools.TryBlockWaves` | **每帧** `GetComponentsInChildren<Wave>(true)`（分配数组）+ 每帧重建 `_blockedLandings` | 拆出 `EnsureScanned`：只在"换 Raid / 容器子节点数变化 / 每 2s 保险丝"时重扫；每帧只做"重写 `waveStartTime`"（无分配） |

**P2 结构拆分**（纯搬动 + 删死代码 + 一处自检）

| 文件 | 现在 |
|---|---|
| `MarqueeSelect.cs` | 946 行 → **536 行**（状态机半边：输入时序 / 选择应用 / 相机 / 减速） |
| `MarqueeSelect.Hits.cs`（新） | 查询半边：候选登记、矩形/点选命中（屏幕 + 世界两套）、最近单位查询、修饰键与光标行为 |
| `MarqueeSelect.Overlay.cs`（新） | 绘制半边：框选矩形、十字/目标环、落点光标、IMGUI 坐标换算 |
| `Plugin.cs` | 533 行 → **113 行**（只留入口 / `Update` / `OnGUI` / `OnDestroy` 与单例） |
| `Plugin.Drop.cs` / `Plugin.Remote.cs`（新） | 投放半边（悬停预览 / `DoDrop` / 地形取点）与遥控半边（旧路径兜底 / 下令 / 全选释放 / 世界点击订阅） |
| 死代码 | 删除 `MarqueeSelect.PickAt`（Cecil 反读确认全程序集零引用） |
| `IngameMenu` | `MenuHeight`（手写累加式）与 `DrawMenu`（实际推进）新增**自检**：两者偏差 > 1.5px 时打一次日志（`DrawActions` 改为返回实际底边）—— 这是唯一会"新加一行忘改高度"导致 `ClickShield` 挡错区域的地方 |

**P3 把教训固化成闸门**（详见 §7 的闸门清单）

- `tools/check-rules.ps1`：空 `catch { }` 必须写 `// silent-ok`（现有 15 处已补标记，把"有意静默"变成显式约定）；反射成员禁止用 `== null` 判空（**作用域精确**：只认类级反射字段 + 同一方法内的反射局部，避免同名误判）；禁 `Debug.Log` / 盘符路径；热路径方法体（`Update/OnGUI/Draw*/Tick*`）禁 `new List/Dictionary/HashSet`（确有必要写 `// alloc-ok`，现有 1 处已标注）。
- `tools/check-docrefs.ps1`：`.cs` 注释里的 `§N`/`§N.M`（当前 **89 处**）必须在 md 里真有对应标题 —— 把"文档引用腐烂"变成构建期红灯。
- `.github/workflows/gates.yml`：CI 跑"不需要游戏 DLL"的三道（loc / rules / docrefs）。

**验证（本机实测）**

```
[api-check]    631 个成员引用 → 全部存在 OK
[loc-check]    词条 214（占位符 99）/ 调用点 214 → 全部一致 OK
[pack-compat]  对包版 Assembly-CSharp 检查 165 个引用 → 全部存在 OK
[rules]        扫描 33 个 .cs / 8107 行 / 热路径方法 46 个 → 全部通过 OK
[docrefs]      .cs 里的 § 引用 89 处；md 标题编号 81 个 → 全部找得到 OK
```

- **IL 级零差异证明（P2 的关键证据）**：把 P2 之前的构建（`pre-p2.dll`，含 P1）与 P2 之后的构建逐方法比对（指令流 + 局部变量类型哈希，466 vs 466 个方法）：
  差异**仅** `IngameMenu.DrawMenu` / `DrawActions`（新增自检）、新增 `IngameMenu.CheckMenuHeight`、`Loc..cctor`（新词条），以及**消失的 `PickAt`** ——
  其余 460+ 个方法 IL 完全一致 ⇒ 跨 6 个文件的 40 个代码块搬移是**可证明的纯搬动**，没有语义漂移。
- 顺带修掉一个**工具自身**的陷阱：早期比对脚本用了 Cecil 该版本不存在的 `MethodBody.GetILAsByteArray()`，被 `try/catch` 静默降级成 `'noil'` → 会给出"零差异"的**假阳性结论**。现在改为"指令流文本哈希"，并且先用一对已知有差异的 DLL 验证过工具确实能报差异。


### 11.8 v1.7.4（第 1 批）：UI 可读性与"挡岛"（2026-10-16，实机反馈驱动）

> 来源：v1.7.3 首次实机的日志 + 作者四条反馈（说明文字太多 / ×1 档没标"原版" / 字体不像原版且拥挤 / 面板挡岛）。
> **v1.7.3 加的面板高度自检当场抓到一条真缺陷**：`panel height self-check failed: content bottom 395.0 != MenuHeight budget 472.0 (diff -77.0px)`
> —— 菜单面板比内容**高 77px**（底部一大块空白），这正是"面板显得臃肿、挡岛"的第一大来源。

**根因与修法（结构性，而不是再手算一次）**

| 项 | 根因 | 修法 |
|---|---|---|
| 77px 空高 | `MenuHeight` 是**手写累加式**，与 `DrawMenu` 的实际推进脱钩（它多算了"数量行前的 Pad"与"底部 Pad+2"，且实际绘制比两者之和还少一块 —— 依赖 `HelpTexts()` 行数、状态行数等**运行时变量**，手写式本来就不可能恒等） | 面板高度改为**按上一帧实测内容高**绘制（首帧才用 `MenuHeight` 粗算兜底）。`CheckMenuHeight` 保留为"自纠日志"：两者不一致时打一行（含实测值），下一帧即自洽 ⇒ **这类脱钩永久消失** |

**四条反馈的对应改动**

1. **说明文字太多 → 删冗余 + 可一键关**：删掉"拦下 = 无原版敌人；可遥控 = 原生敌人也能指挥"（按钮文字已自解释）；"靠岸速度"的长句压成 `靠岸速度（×1 = 原版速度；按住拖动切档）`；按键说明 3 行并成 2 行；新增 `[UI] ShowHelp`（false = 整块不画，也参与宽度/高度计算）。
2. **×1 档没注明原版 → 直接标在按钮上**：第 1 格标签改成 **`×1（原版）`**，并把 4 个标签的实测宽度纳入 `MenuWidth`（格子绝不裁字）。
3. **字体不像原版 / 拥挤**：
   - **真 bug**：`VanillaUI.GameFont()` 的缓存**没有按语言记账** —— 启动时若游戏是英文，它按 `BodyEn` 选中原版英文艺术字 `Body`；之后你在菜单里切到中文，字体不会重挑（`Body_Chinese_Simp` 与之不是同一份资产）⇒ 中文用英文艺术字渲染，观感/字形自然不对。现在缓存带语言标记，切语言即重挑并重建 `GUIStyle`。
   - **数字字体**：旧版硬编码 Times New Roman（与正文的原版艺术字不同源）。新增 `[UI] NumberFont`，**留空 = 跟正文同字体（默认，观感统一）**；填 `Times New Roman` 才回到旧行为。
   - **拥挤**：文字行高 +2px（`LineH/InfoH 19→21`、`HelpH 18→20`），配合上面"面板高度按实测"⇒ 内容更舒展而面板**整体更矮**。
4. **挡岛**（见下节的分层建议）：本轮已落地三项结构性减负 —— 面板不再多 77px、删两处文字、**菜单开着时状态行并入面板底部**（不再叠一个浮窗），并给标题栏加了**收起/展开**（一键只剩 28px 标题栏）。

**实测数字（本机，构建期）**：五道闸门 `api-check 631` / `loc-check 215（占位符 99）` / `rules 33 文件` / `docrefs 89 处` / `pack-compat 165` 全绿。

**顺带确认（同一份日志）**：B10 的修复在实机上是生效的 —— 连续两次投放分别报"滩头离点击处 0.0m"与"0.3m，**已放宽间距**"（而不是叠在同一格），拦波次也照常只拦原版（`4 wave(s)`）。



### 11.9 UI 挡岛的分层减负（回答作者"你认为该如何调整"）

按"效果/成本"排序。**前四项 v1.7.4 已落地**，后面是可选升级：

| # | 措施 | 状态 | 效果 |
|---|---|---|---|
| 1 | 面板高度按实测（不再多算 77px） | ✅ 已做 | 立刻少占 77px |
| 2 | 菜单开着时状态行并入面板（不再叠浮窗） | ✅ 已做 | 少一整块浮窗（约 60~100px 高） |
| 3 | 删两处冗余说明 + 说明块可关（`[UI] ShowHelp=false` 再省 2 行） | ✅ 已做 | 少 2~4 行文字 |
| 4 | 标题栏「收起」按钮 → 只剩 28px 标题栏 | ✅ 已做 | 想看岛时一键清空 |
| 5 | 面板拖动后**记忆位置**（已有） | ✅ 既有 | 挪到自己顺眼的位置 |
| 6 | `[UI] UiFontSize` 调小（13 → 12） | 可自助 | 整块等比缩小 |
| 7 | 兵种网格 >4 种时改 **3 列**（9 个兵种从 5 行 → 3 行） | 待定 | 竖向再省 ~52px，但格子变窄（英文长名可能被裁） |
| 8 | 面板**半透明**（`[UI] MenuOpacity`） | 待定 | 不减少覆盖面积，但"透着能看见岛" |
| 9 | 菜单**自动贴边**（左/右手习惯） | 待定 | 中间留给岛（默认已贴左上） |
| 10 | **迷你模式**：默认只留标题栏 + 一行状态，悬停/点击才展开 | 待定 | 覆盖面积最小，代价是多一次点击 |

> **我的建议**：先试 **6 / 7 二选一**（只动数字，风险最低、可回退）；若还嫌挡，就上 **10（迷你模式）**——

### 11.10 v1.7.4（第 2 批）：UI 间距与排版 + 三项代码问题（2026-10-16，作者实机反馈驱动）

> 来源：作者第二批实机反馈（"UI 还有些挤（纵向）"、"部分文本和按键离边框较远"、"解释文本距离按键太近"）+
> "检查我们的代码还有没有问题"→ 顺带查出并修掉三处（其中一处是真 bug）。

**A. UI 间距与排版（全部是布局常量与摆放，不改交互）**

| 症状 | 根因 | 修法 |
|---|---|---|
| 纵向挤 | 行与行之间只有 `Pad = 10`；数量行还**没有**前导间距（贴着兵种网格） | 新增 `VPad = 13`（专用纵向行距，横向外边距 `Pad` 保持 10 不变）、数量行补前导 VPad、兵种网格 `RowGap 3→4`、`RowH 26→27` |
| 说明文本贴着按键 | 说明行紧跟在按钮行后面（0 间距），且 `Hint` 样式是 `UpperLeft`（文字贴在行盒顶部） | 新增 `HintGap = 6`（每处说明前留白）+ `Hint` 改 `MiddleLeft`（单行说明垂直居中） |
| 部分按键离边框较远 | 数量行的 8 个预设是**固定步长 38px**，右侧因此空出 ~80px | 预设按剩余宽度**等分铺满**到右边界（`step = avail / Presets.Length`，宽 = step − 4） |
| 靠岸速度那行看着"说明被夹在中间" | 说明在轨道上方、且与轨道零间距 | 改为**轨道在上、说明在下**（`BtnH + HintGap` 后画说明） |

> 面板高度自 v1.7.4 起按"上一帧实测"绘制，所以以上间距调整**不需要**同步任何公式（`MenuHeight` 只是首帧兜底）。

**B. 三项代码问题（"检查我们的代码还有没有问题"的产出）**

| # | 问题 | 性质 | 修法（最优） |
|---|---|---|---|
| 1 | `Compat.ReportEnvironment()` 在 `Plugin.Awake()` 里跑 → BepInEx 那时**还在按顺序加载插件**（我们排在最前），所以实机日志打出 `整合包=有（0 个插件）` | 诊断文案错（运行期让路判定不受影响） | 挪到**第一次 `Update`**（那时 Chainloader 已加载完全部 23 个插件） |
| 2 | 原始输入路径（`RemoteCursor` 点击路由 / `MarqueeSelect` 框选）只检查"我们自己的矩形"，**看上去**点原版 UI（技能条、按钮）时可能对世界下指令 | **静态推断、从未复现**（作者实测结论：**"本批没有这个问题"**）。复核：技能条只在"原版正选着我方小队"时出现，那一刻既有规则已双保险 —— 单键模式第一道 `VanillaSelected` 直接 return；双键模式右键 `VanillaSelected` return ＋ `!HasSelection()` return（我方选中早被互斥清空）⇒ 下令分支**不可达** | 实现改为 `Util.PointerOverUi()` 并**默认关闭**（`[Remote] UiClickGate = false`，与 v1.7.3 及以前完全一致）；要用额外保险的人自行打开。**踩坑记录见"修订 3/4"** |
| 3 | `LogFileSwitch` 默认（`LogToFile=false`）会**删掉所有插件共用的 `LogOutput.log`** —— 整合包 / Together 的报错会一起消失 | 设计风险（跨模组影响） | 默认改为"只停止写入、不删"；要删旧日志用新键 `[Diag] CleanLogOnStart = true`（旧行为可一键恢复） |

**验证**：五道闸门全绿（`api-check 633` / `loc-check 217（占位符 100）` / `rules 33 文件 8283 行` / `docrefs 89 处` / `pack-compat 165`），产物已部署。

**v1.7.4 修订（同日，作者实机反馈"靠岸速度档位条被遮挡"）**

| 现象 | 根因 | 修法 |
|---|---|---|
| 菜单里"靠岸速度"4 档按钮被别的东西压住 / 点不到 | ① `Plugin.OnGUI` 里 `IngameMenu.Draw()` **先画**、`SquadBar.Draw()` **后画** → 头像条（在菜单开着且两者重叠时）盖住菜单最后几行，而"靠岸速度"正是第 5 行；② 包把技能条加宽加高（`SkillIcons` 注册 3 个技能）＋ 矮屏时，底部原版 UI 区更容易顶到菜单；③ 矮屏时菜单本身偏高，"说明块 + 状态行"又把最后几行推向底部 | ① **菜单改为最后绘制**（覆盖层 → 头像条 → 菜单），IMGUI 里后画即在上层；② `SquadBar.BarY()` 在菜单开着时**主动让开菜单矩形**（优先挪到菜单下方，放不下就挪到上方）；③ 新增 `IngameMenu.TightScreen()` **矮屏自适应**：菜单若会压到底部原版 UI 区（用 `SquadBar.VanillaUiTop()` 读技能条上沿，读不到则按屏高 −150 预留），本轮就省掉"按键说明块 + 状态行"，优先保住档位条与操作按钮。判定只用屏幕高度与预估高度（与收缩结果无关）→ 不会来回抖 |

> 顺带把 `ShowHelp()` 拆成 `ShowHelpCfg()`（只读 cfg，供高度/宽度估算）与"实际是否绘制"（= cfg 且非矮屏），避免与 `TightScreen()` 递归。

> 它比半透明更能解决"看不清岛"，因为 IMGUI 的半透明面板仍然会吃掉点击（`ClickShield` 也得跟着改）。

**v1.7.4 修订 2（同日，作者实机截图："第 5 行靠岸速度 4 格都没看见"）**

| 现象 | 根因（真） | 修法 |
|---|---|---|
| 菜单里"靠岸速度"**4 格整排不可见**，可它那行说明却正常显示 | **画布上一个真 bug（回归）**：`DrawMenu` 里 `DrawShipSpeedRow(x, inner, y);` **没接返回值** → 档位条与紧随其后的"释放遥控 / 清场 / 强制胜利"按钮行落在**同一个 y**，而按钮行后画 → 整片盖住 4 格；档位条下方 `BtnH + HintGap = 32px` 的说明行正好露在按钮行下面 ⇒ 看起来"只有说明、没有轨道"。**副效应**：点 4 格的位置实际会命中"清场（F2）/ 强制胜利（F3）" | `y = DrawShipSpeedRow(x, inner, y);`（**一处赋值**）。面板高度随后把这一行算进去（`MenuHeight` 本来就含它，`_measuredH` 自纠也会跟着对上） |

> **上一轮"遮挡修复"的判定订正**：v1.7.4 第一次收到"靠岸速度档位条被压住 / 点不到"时（当时只有口述、没截图），判定为"底部头像条盖住菜单最后几行"，
> 因此落了三件事（菜单改为最后绘制 / `SquadBar.BarY()` 让位 / `TightScreen()` 矮屏自适应）。实机截图证明**元凶是上面这处丢返回值的赋值遗漏**，
> 那三件事并不是本症状的原因；但它们本身仍是正确的加固（IMGUI 后画即在上层、头像条不压菜单、矮屏优先保控件），**予以保留**，以后遇到"菜单被别的东西压住"也仍然吃得开。

> **防回归（新闸门规则）**：`tools\check-rules.ps1` 新增**规则 ⑥** —— 纵向布局链里 `DrawXxx(x, inner, y);` **必须**写成 `y = DrawXxx(...)`
> （确有不接的理由，在该行写 `// layout-ok`）。已用探针文件验证：命中即 `exit 1`，`build.ps1` 会拦在构建期。

**验证**：五道闸门全绿（`api-check 633` / `loc-check 217（占位符 100）` / `rules 33 文件 8286 行` / `docrefs 90 处` / `pack-compat 165`）；
产物已部署，与仓库构建**逐方法 IL 零差异**（32 类型 / 467 方法 / MVID 一致）；与修复前那版相比，**只有 `IngameMenu.DrawMenu()` 一个方法的 IL 不同** ⇒ 改动面确认只有这一处。

**v1.7.4 修订 3（同日，作者实机反馈"鼠标不能控制"）**

| 现象 | 根因（真） | 修法 |
|---|---|---|
| 进战局后**鼠标控制全失效**：点单位选不中、点地块不前进、框选也不动；菜单内的按钮与键盘快捷键（F1 / F2 / R …）却都正常 | **本批"点原版 UI 仍会下令"那一项，修法用回了一个本游戏已知恒真的 API**：`EventSystem.IsPointerOverGameObject()`。本游戏的世界点击走一个**全屏手势接收器** `PointerRationalizer`（§4 坑表在 v1.0.4 就记过：拿它当闸门 ⇒ 点击被静默吞掉且**无任何日志**）⇒ 门控恒为真 ⇒ `RemoteCursor.TryCaptureOnPress` 与 `MarqueeSelect` 两处按下路径**每次都在第一道闸就 return**，世界点击（选中 / 前进 / 框选）全部丢失；菜单 IMGUI 与键盘路径不受影响 ⇒ 症状正是"菜单能用、鼠标不能控制" | `Util.PointerOverUi()` 改为**真实射线判定**：`EventSystem.RaycastAll` 取命中列表（已按"从上到下"排序），**跳过原版那个全屏接收器**（它代表"点在世界上"，不算 UI），剩下的命中项才算 UI 控件；被挡时按**每个 UI 物体名各打一行**日志（`… ← 物体名`）留证据 |

> **防回归（这次的答案：让构建来守）**：`tools\check-rules.ps1` 新增**规则 ⑦ = 禁用清单**（`tools\banned-api.txt`，一行一个正则 + 原因）：代码里再出现 `IsPointerOverGameObject` 直接判违规（该行写 `// ok:` 可豁免），清单里同时收了 `Array.Empty`。
> 意义不在"这一条"，而在**机制**：以后任何踩过的坑，只要往那个文本文件**加一行**，就永久变成构建期拦截 —— 不再依赖"谁记得"。同批的规则 ⑥（布局链丢返回值）见上面"修订 2"；§4 还补了一张「坑 → 闸门 覆盖对照」，把"拦得住 / 只能靠人 / 只能靠实机"三类讲清楚。

**验证**：五道闸门全绿（`api-check 640` / `loc-check 217（占位符 100）` / `rules 33 文件 8317 行` / `docrefs 91 处` / `pack-compat 165`）；产物与仓库构建**逐方法 IL 零差异**。

**v1.7.4 修订 4（同日，作者指出："本批没有这个问题：点原版 UI 时我们的部队还会动"）**

| 事实 | 处置 |
|---|---|
| **那个"真 bug"根本不可达** —— 作者实测"本批没有这个问题"。复核代码：技能条只在"原版正选着我方小队"时出现（`ActiveAbilityButtonContainer`），而那一刻两处按下路径都已让路：单键模式第一道 `VanillaSelected` 就 return；双键模式右键 `VanillaSelected` return ＋ `!HasSelection()` return（我方选中早被 `MarqueeSelect` 的互斥清空）⇒ "点技能条让我方部队动"永远走不到下令分支 | ① 结论订正（B 表第 2 项的"性质"从 **真 bug** 改为 **静态推断、从未复现**）；② `Util.PointerOverUi()` 改为**默认关闭** —— 新增 `[Remote] UiClickGate = false` ⇒ **默认行为与 v1.7.3 及以前完全一致**，想要额外保险的人自行打开（实现是修好的 `RaycastAll` 版）；③ 文档不再替未验证的写法背书 |

> **流程新规（也写进了 §4「坑 → 闸门 覆盖对照」）**：这次连环失误（用错 API → 替它背书 → 鼠标全死）的共同根因是"**拿推断当事实**"。定三条硬规矩：
> **① 未经实机复现的问题不得标"真 bug"**（最多写"静态推断，未复现"）；**② 不得为未复现的推断改默认行为** —— 要改就**默认关 + cfg**，让作者自己决定要不要打开；
> **③ 不得在文档里给未验证的写法背书**（本次那句"与原版一致的门控"就是错背书）。这三条闸门拦不住，只能靠流程，所以照样写进表里接受审查。

### 11.11 v1.7.4 兼容层状态小结（2026-10-16，作者口径）

> 作者结论：**《旧灰复燃的战旗》3.0 的"单机兼容"已完成；联机（Together）本轮不解决**，只保留"整体让路、不破坏整局"。

**单机（原版 / 装了整合包）—— 已适配并实机验证**

| 项 | 状态 | 依据 |
|---|---|---|
| 投放 / 遥控 / 清场 / F3 | ✅ 全部可用（终岛除外） | 兼容层 8 个判定点全在**入口处**（不碰热路径）：`DropBlocked`（F1 开关 + `DoDrop`）、`RemoteBlocked`（`MarqueeSelect` / `RemoteCursor` / `Plugin.Remote`）、`WaveBlockForbidden`（`LevelTools` + 菜单）、`ForceWinConfirmNeeded`（`LevelTools`），另有 `StatusLine`（HUD）、`ReportEnvironment`（启动自检）、`AuditEnhancements`（投放后自检）、`SessionClient`（客机置灰） |
| 引用层 | ✅ 我们引用的 Assembly-CSharp 成员在**包版**里全部存在 | `tools/check-pack-compat.ps1`：**165 项**（每次构建都跑） |
| 行为层 | ✅ 三处已知交互都已成文（投放的兵被包增强 / `deadisnotenemy` 影响遥控交战 / 终岛让路 + F3 二次确认） | 玩家说明 §4 第 1~5 条；实机日志确认 |
| 终岛 | ⚠️ 默认**让路**（`EndlessFinale` 接管该岛波次）；`[Compat] DropOnTerminal=true` 可强行开放 | §11.2 让路面矩阵 |
| 拦下原版波次 | ✅ 单机**照常可用**（"无限试炼场"入口）；只在联机中拒绝 | §11.3 B6 + §11.3 B10 |
| UI 层 | ✅ 底栏自动避让原版技能条；"点原版 UI 会误下令"这条**实机不复现**（技能条出现时原版正选着我方小队 → 既有互斥已覆盖），故可选加固 **`[Remote] UiClickGate` 默认关**（实现见 §11.10 修订 3，订正见修订 4） | `SquadBar.VanillaBottomTop` / `Util.PointerOverUi` |

**联机（Together）—— 明确未适配**

- 我们的做法只有"**整体让路**"：联机中不投放、不遥控、拦波次被拒（`[Compat]` 三键可强行覆盖，后果自负）。
- **不参与** Together 的清单 / 序号 / 签名 / 姿态流协议（投放会让主机 `Fail("待开波缺少稳定逻辑身份")` 冻结整局 —— 见 §11.3 B9 与 `TOGETHER_MP_SPEC.md` §8）。
- 要做真正的联机投放需要**上游**把"外部波次接入"泛化成公开接口；研究结论与提案在本地笔记 `TOGETHER_L2_FEASIBILITY.md` / `TOGETHER_UPSTREAM_PROPOSAL.md`（不入库）。
- README 与玩家说明已按此口径写明"**适用范围：单机**"。


### 11.12 v1.7.5：把开关挂进**原版设置界面**（2026-10-16，作者需求）

> 作者需求原文：**"在游戏原生设置里能否增加按钮，开关我们的 BadNorthNewMode —— 避免正常游戏控制了原生敌方单位。"**

**做了什么**

| 项 | 内容 |
|---|---|
| 原版设置里**两行布尔项** | ESC → 设置（`UserSettingsMenu`）→ **控制** 分类**末尾**追加：`BadNorthNewMode：启用模组`（= 新 cfg `[General] Enabled`，**模组总开关**）与 `原生单位：可遥控`（= cfg `[Native] ControlNativeUnits`，**与 F1 菜单同一键**）。标签中英各一份 |
| **总开关**（默认开） | `[General] Enabled = false` ⇒ **完全静默**：`Plugin.Update` / `OnGUI` 全部早退 → 不接管点击（点单位选中 / 点地块前进 / 框选 / 点海面取消全归原版）、不画菜单·HUD·头像条、快捷键（F1/F2/F3/R）失效、不拦原版波次。从"开"变"关"的**那一帧**做一次收尾：关菜单与 HUD、撤拦截面、`RemoteCursor.ForceRelease()`（把原版 `Navigator` 从停用态还回去）、`RemoteGroup.Clear()` 归还 order、清时间减速账、`ShipSpeedRuntime.RestoreVanilla()` 把**在航船速还原原版**（不动 cfg 里的档位） |
| **逃生门**（关键设计） | `VanillaSettings.Tick()` 放在**总开关判断之前** ⇒ 关掉模组后，原版设置里那两行**照样在**（否则关掉就再也没法从游戏里打开它） |
| 三处同步 | 两行的 getter/setter **直接读写 cfg** ⇒ 原版设置 ↔ F1 菜单 ↔ cfg 文件天然一致，不引入第二份状态；关掉后重启游戏仍是关的 |

**怎么挂上去的（反编译 `BadNorthDatabase` 核实，非猜测）**

```
UserSettingsMenu : GeneratedMenu : UIMenu
  GeneratedMenu.MaybeInitialize()        ← Awake / IGameSetup.OnGameAwake 里只跑一次
     Initialize() → ClearWidgets()       ← 会 DestroyChildren(分类容器) ⇒ 必须等它跑完再加
  GeneratedMenu.AddBoolWidget(string 词条, Func<bool> get, Func<bool,bool> set, Transform 分类)
  UserSettingsMenu.Initialize()          ← 具体各项；分类容器挂 VerticalLayoutGroup（后加的子项会自动排版）
  private static UserSettingsMenu _instance;   private bool initialized;
  private Transform categoryControls;          ← 「控制」列
```

| 设计决定 | 原因（都在这一步踩过或差点踩到） |
|---|---|
| **不引入 Harmony / MMHOOK** | 本模组一贯"零补丁"。改为**每秒轮询**（私有 `_instance` + `initialized`），初始化完成后再追加；结构不符就**一次性放弃 + Warn**（不静默、不每帧重试），F1 菜单与 cfg 照旧可用 |
| 词条**运行期注入 I2** | 原版控件只认"词条"（`I2.Loc`），**缺词条 = 标签空白**。注入 `LocalizationManager.Sources[i].AddTerm(term)` → 按语言名写 `TermData.Languages[zh/en]` → `UpdateDictionary(true)`；中英都没认出来就整表填英文兜底 |
| 控件创建走**反射 + 现造委托** | `AddBoolWidget` 第 2/3 个参数是游戏侧的 `System.Func<bool>` / `System.Func<bool,bool>`；net472 会把 `Func` 解析到 **mscorlib**（游戏 mscorlib 2.0 没有它，本游戏在 **System.Core**）⇒ 直接传 lambda / 方法组必 `MissingMethodException`。**`check-api` 当场拦下**（"类型不存在: System.Func`1<System.Boolean> [mscorlib]"）→ 改为反射取参数 `ParameterType` 后 `Delegate.CreateDelegate(游戏侧类型, MethodInfo)` 现造委托再 `Invoke` |
| 标签文本**不写死**在控件上 | 语言切换时 I2 会重刷所有 `Localize` 组件；写死文本会在换语言后变空白。走词条 ⇒ 跟着游戏语言走 |

**验证**：五道闸门全绿（`api-check 650` / `loc-check 227（占位符 103）` / `rules 34 文件 8667 行 / 禁用清单 2 条` / `docrefs 96 处` / `pack-compat 165`）；
产物与仓库构建**逐方法 IL 零差异**。**实机验收（待作者）**：① 设置 → 控制 末尾能看到两行、开关能点；② 关掉总开关 → 鼠标完全交还原版（点单位 / 点地块 / 框选都不再被接管）、菜单与 HUD 消失、`F1`/`F2`/`F3` 失效、在航船速还原；③ 再从原版设置打开 → 一切恢复；④ 只用 `原生单位：可遥控` 关掉那一项 → 原版上岛的敌人不再被点选/遥控（本次需求的直接目的）。

**v1.7.5 修订（同日，作者实机反馈两点）**

| 反馈 | 根因 | 修法 |
|---|---|---|
| 切到"关"时那个"开"的**高亮不及时消失**，退出设置再进去才显示为关 | `ValueWidget<T>.SetValueHandler` 只在 **setAction 返回 true** 时才认这次改动：`if (!newValue.Equals(value) && setAction != null && this.setAction(newValue)) { …; onValueChanged(newValue); }`。我们的 setter 当初返回的是**新值**（关掉时 = `false`）⇒ 被当成"设置被拒绝" → **控件内部状态不更新**（副作用说明：cfg 其实已经写下去，所以模组行为立刻变了，只有控件显示没跟上） | 两个 setter 改为 `return true`（= 设置成功，与原版所有 setter 一致）；并新增 `RefreshRows()`：设置界面**开着**时按原版节奏（每 15 帧，同 `UserSettingsMenu.UpdateOccasionally`）`ForceUpdate()` 两行 ⇒ 在 F1 菜单 / cfg 里改了值，设置界面也会自动跟上 |
| 位置希望更显眼（"在所有选项的第一位"） | `AddBoolWidget` 走 `InstantiateChild` → `SetParent(parent, false)`：**只追加、不动 siblingIndex**，位置取决于容器现状（此前是排在末尾） | 显式 `SetSiblingIndex(0/1)`：两行**固定排在「控制」分类最前面**，顺序 = `启用模组` → `原生单位` |

**验证**：五道闸门全绿（`api-check 651` / `loc-check 227（占位符 103）` / `rules 34 文件 8699 行 / 禁用清单 2 条` / `docrefs 96 处` / `pack-compat 172`）；产物与仓库构建**逐方法 IL 零差异**。

### 11.13 v1.7.6：船只快速靠岸排查 + 船只堆叠开关（2026-10-17，玩家反馈驱动）

#### A. 报障 1「船只快速靠岸」—— **是我们的锅**（档位被重复缩放 / 叠乘），已根治

**玩家的原话（决定性线索）**：*"只要把船的二倍速开着，然后退出岛屿再重进，不要点重新开始，就会一直船速叠加。"*

**旧实现为什么会叠乘（逐步推演）**

| 步 | 旧代码（v1.7.1 ~ v1.7.5） | 发生了什么 |
|---|---|---|
| 1 | `Adjust`：`k = 旧档 / 新档`，`duration' = duration × k`，并把档位记进 `_applied[Landing]` | 首次 ×2：`k = 1/2` → 用时 ÷2 |
| 2 | `Tick`：`island == null \|\| !generated \|\| state != Playing` → **`_applied.Clear()`**（当时注释："离开战局 / 换岛：登记表作废"） | 退出岛屿的瞬间，记账全没了；但 **Landing 与它们被改过的 `shipTravel` 还在**（不点"重新开始"= 同一批对象） |
| 3 | 再进岛 → `ApplyAll` → `seen` 取不到 ⇒ 默认 `1`（"原版速度的新船"） | 对**已经 ÷2 的值**再乘 `1/2` → **÷4** |
| 4 | 再进出一次 | **÷8** …… 表现就是"每次进出岛屿船都更快一截" |

**结论**：**是**本模组的问题，触发条件 = `[Landing] ShipSpeedGear ≠ 1`（默认 ×1 时 `Adjust` 在第一步就早退，**一次读写都不做**，
所以没碰过滑条的玩家完全不受影响；这也解释了为什么只有部分玩家遇到）。

**根治（三道，缺一不可）**

| # | 改法 | 为什么这样就不会再叠 |
|---|---|---|
| ① | 每条船改记**基准用时** `baseDur / baseFlee`（= 第一次见到时的原版值，已含该船 `TryPlace` 的随机档位与难度倍率），目标值恒为 `基准 / 档位` | 同一艘船无论被套用多少次，结果都是同一个数 —— **结构上不可能叠乘**（旧实现是"对当前值再乘一次"） |
| ② | 登记表**不再在"离开战局 / 换岛"时清空**，只 `Prune()` 掉已销毁的 Landing | 回到同一座岛时 `Entry.gear` 还在 ⇒ `Adjust` 直接跳过；真正换了岛，旧条目随对象销毁被摘掉，新 Landing 重新捕捉基准（原版速度） |
| ③ | 档位 ≠ ×1 时每秒做一次**自愈比对**（`MatchesApplied`） | 原版若重新 `TryPlace` 把某条船的 `shipTravel` 写回原版速度 → 检测到"当前值 ≠ 我们写出的值" ⇒ 重新捕捉基准并再套一次（否则会变成"显示 ×2、实际原版"） |

配套：
- `RestoreVanilla`（总开关关掉）改为**直接写回基准用时**；**不在战局里就保留记账**，等回到战局再还原（旧实现会清表 → 再启用时又叠一次）。
- 新增**每岛一行**诊断：`[NewMode][船速] 本关按 ×N 缩放靠岸用时（含原版波次；档位来自 F1 菜单 / cfg）`；档位 = ×1 时不打（不刷屏）。
  以后玩家再报"船快"，**这一行就能一眼定案**（是档位在起作用，还是别的东西）。

**同症状的第二个真 bug（独立存在，也已修）：F1 档位条的拖动状态会卡死**

| 项 | 内容 |
|---|---|
| 根因 | `HandleGearDrag` 只在"轨道内 `MouseDown` + `MouseUp`"结束 `_gearDrag`，且 `MouseDrag` / `MouseUp` **不校验按键、不校验指针位置**。松手落在面板外 / 游戏窗口外（或那一帧 OnGUI 没跑到）→ 标志位一直为 true → 之后**任何左键拖动**（例如平移相机）都按鼠标 x 调 `ApplyGear` ⇒ 档位被静默改到 ×2、**原版波次的船真的变快**，玩家却完全不知道是自己"拖"出来的 |
| 修法 | ① 只认左键（`e.button != 0` 直接忽略）；② `_gearDrag && !Input.GetMouseButton(0)` 自动清（鼠标早就松了）；③ `MouseDrag` 时指针离开轨道上下各一格就结束本次拖动；④ 菜单关闭 / 折叠 / 总开关关闭时 `IngameMenu.CancelGearDrag()` |

**不改的原版语义**：档位仍然"原生波次 + 本模组投放的船都生效"（v1.7.1 作者口径），只是不再叠乘；×1 时完全不读不写。

#### B. 需求 2：**船只堆叠开关**（挂进原版设置界面，与"启用模组"同一套机制）

| 项 | 内容 |
|---|---|
| 新 cfg | `[Landing] AllowShipStack`（默认 `false` = 一直以来的"滩头占位互斥"） |
| 原版设置 | ESC → 设置 → **控制** 分类最前面**第三行**：`船只可堆叠（影响观感）` / `Ships may stack (visual only)`。与 v1.7.5 那两行**完全同一套机制**：运行期注入 I2 词条 → 反射 `AddBoolWidget` → `SetSiblingIndex(2)` → 每 15 帧 `ForceUpdate()` → setter `return true` → getter/setter 直读 cfg（原版设置 / cfg 文件天然同步） |
| 打开后（投放侧） | `DropPlanner.TryResolve`：首选间距与最低间距都设 **0**；`IsOccupied` 见到间距 0 直接返回"未占用"（也不重建占用缓存）⇒ 永远选**最近的、廊道通**的滩头 |
| 打开后（原版侧） | `LandingInjector` **不再把已有船交给 `Landing.TryPlace`**（否则原版 `ColCube.CheckBox` 的朝向盒重叠判定仍会拒绝，开关就成了空话）⇒ 同一滩头可叠任意多条船 |
| **不变** | 仍做**进近廊道预检**（`CorridorClear`：挡的是地形 / 建筑，不是船）；船照常装人、下船、编队出发、播接近音乐；「船员自检」照常 |
| 作用范围 | 只影响**之后**的投放（已在场的船不会被挪动）；只影响**本模组投放**（原版波次仍按原版规则占位）；纯观感，不参与胜负/数值 |

#### C. 顺带修掉的两处（代码体检产出）

| 位置 | 问题 | 修法 |
|---|---|---|
| `LandingInjector` 失败路径 | 候选全失败时只 `Destroy(landingGo)`，但这条 Landing 早已 `group.AddLanding()` 进了 `ShipGroup.landings` → 编队 `Launch()` 会对着一艘已销毁的 Landing 调 `Launch()` | 失败前把它从 `landings` 摘掉、`shipGroup = null`；**不动 `SpawnLedger`** —— 那个 Wave 仍归 `FlotillaLauncher` 复用，且 `SpawnLedger.Owns` 必须继续认得它（否则"拦下原版波次"会误伤自家波，见 §11.3 B10） |
| `ShipSpeedRuntime.Reset()` | 旧实现顺带清 `_applied`（同一类叠乘风险） | 只重置节流时间，**不清基准 / 记账** |

#### D. 验证

**构建闸门**：`api-check 654` / `loc-check 234（占位符 108）` / `rules 35 文件 9172 行 / 禁用清单 2 条` / `docrefs 119 处` 全绿（`pack-compat` 需提供整合包 DLL 时才有意义）。

**实机验收（待作者）**：
1. 档位 ×2 → **退出岛屿再进（不点重新开始）×3**：船的靠岸用时应始终是"原版 ÷2"，**不再逐次变快**（日志里 `本关按 ×2 …` 每岛一行）；
2. 档位拨回 `×1（原版）` 或关掉总开关 → 在航船的用时**立刻回到原版**；
3. 原版设置 → 控制 最前面应看到三行，第三行能点、能保存；
4. 打开「船只可堆叠」→ 往同一滩头连投 3 艘：船叠在一起、都能正常下船与编队（HUD 不再提示"滩头都被船占着"）；关掉后恢复避让。


### 11.14 v1.7.6 专题：生命周期与状态清理（2026-10-17，作者要求"退出地图 / 重启后我们的添加要及时消除"）

> 来源：作者要求 —— **"用户退出地图或重启游戏，我们的一些添加也要及时消除；之前我们生成的非原生单位也遇到过类似情况，建议做专题排查，避免泄漏后再泄漏。"**

这次把"我们加到游戏里 / 我们自己缓存的东西"**全部列出来**逐项核对，发现的问题都属同一类：
**状态的生命周期和它依赖的游戏对象不同步** —— 有的只清一半、有的根本没人清、有的清了但没还原游戏侧的值。
v1.6.0 的"重开战局后我们的单位还在"与 v1.7.6 的"退出岛屿再进 → 船速叠乘"都是这类。

#### A. 清单：我们到底加了什么、谁负责清

| # | 类别 | 我们加的东西 | 离开战局 / 换岛 | 关总开关 | 插件卸载 |
|---|---|---|---|---|---|
| 1 | 世界对象 | `Wave` / `Landing` / `ShipLoad` / `Longship` / `ShipGroup`（挂在 `raid.landingContainer`） | `SpawnLedger.DestroyAll()`（离开 / 重开 / 结算） | **保留**（玩家摆出来的） | `SpawnLedger.Destroy()` |
| 2 | 世界对象 | 维京 `Squad` 与其中的 `Agent`（挂在 `island.runContainer`，**不在** Wave 树里） | `SpawnLedger.TrackSquad` → `DestroyAll` | 保留 | 同 1 |
| 3 | 组件 | `ForeignUnit`（非原生标记）、`ShipboardThreat`（船上可被索敌） | 随 1 / 2 销毁；登记表 `ForeignUnit.Prune()` | 保留 | 随 1 / 2 |
| 4 | 游戏状态 | `brain.order` / `orderMono`（`GroupOrder`） | `RemoteGroup.Clear()` | `RemoteGroup.Clear()` | `RemoteGroup.Clear()` |
| 5 | 游戏状态 | `Landing.shipTravel.duration/fleeDuration`（靠岸速度） | **保留**（要跨"退出再进"记基准，见 §11.13A） | `RestoreVanilla` 写回基准 | `RestoreVanilla` |
| 6 | 游戏状态 | `Wave.waveStartTime = float.MaxValue`（拦下原版波次） | **保留**（同一岛重进要继续拦） | **`ReleaseWaves()`（本轮补）** | `ReleaseWaves()` |
| 7 | 游戏状态 | `TimeManager.RequestTimeScale`（框选减速） | `MarqueeSelect.ClearSession()` | `ClearSession()` | `ClearSession()` |
| 8 | 游戏状态 | `CursorManager` 栈（点击路由）/ 相机 `IDragListener` 让位 | `RemoteCursor.ForceRelease()` / `AttachCamera` | 同左 | 同左 |
| 9 | 游戏 UI | 原版设置里**三行**词条控件 + I2 词条 | 不适用（在原版设置菜单里，随菜单活） | **保留**（逃生门） | **`DestroyWidgets()`（本轮补，热重载不会两份）** |
| 10 | 常驻对象 | `BadNorthNewMode.Ledger / Flotilla / Watchdog / Marker / RemoteCursor / ClickShield`（均 `DontDestroyOnLoad`） | 只清数据，对象留着复用 | 同左 | **`DestroyHelpers()`（本轮补，旧实现只销毁了 ClickShield 与菜单贴图）** |
| 11 | 静态缓存 | `IngameMenu` 兵种列表 / HUD 行；`SquadBar` slots；`DropPlanner` 岸线采样 + 占位表；`UnitCatalog` 查找缓存；`LevelTools` 扫描缓存；`DisembarkWatchdog._entries`；`FlotillaLauncher._wave/_group` | **`SessionCleanup.PurgeBattleState()`（本轮新增）** | 同左 | 同左 |
| 12 | 跨局账 | `ShipSpeedRuntime` 每船基准用时；`LevelTools._origStart`（waveStartTime 原值） | **必须保留**（丢了船速会叠乘 / 波次永远不来） | 保留（还原时不删账） | 随进程结束 |

#### B. 本轮修掉的真实残留（每条都对应一次"泄漏后再泄漏"）

| # | 问题 | 后果 | 修法 |
|---|---|---|---|
| **L1** | **关总开关不还原被拦的波次**：`ApplyDisabled` 从没调过 `LevelTools.ReleaseWaves()`，而 `Plugin.Update` 之后全早退 | "拦下原版波次"开着时关掉模组 ⇒ 本局 `waveStartTime` 永远是 `float.MaxValue`，**再也不会出波**，且没有任何代码会去救它 | `SessionCleanup.RestoreGameState()` → `ReleaseWaves()`；与 `ShipSpeedRuntime.RestoreVanilla` 同一处收尾 |
| **L2** | **编队窗口关不掉**：`FlotillaLauncher` 只在 `Update` 里发现"wave 被销毁"才清；关总开关后 Update 仍在跑 | 关掉模组后 1 秒内仍会 `BeginWave()` —— "完全静默"只断了画面与点击，船照样出发 | 新增 `FlotillaLauncher.Cancel()`：销毁挂起的 Wave + 清静态引用；由 `PurgeBattleState` / 关总开关调用 |
| **L3** | **下船看门狗的静态表不清**：`DisembarkWatchdog._entries` 只在 Update 里删"船没了"的条目，而关总开关后 Update 早退 | 旧局的 `Landing` 被静态表长期引用；重进战局后还可能拿旧船做诊断 | 新增 `ForgetAll()`；`PurgeBattleState` 调用；Update 里也加了 `Plugin.Enabled` 门控（上一轮） |
| **L4** | **相机让位可能永久丢失**：`MarqueeSelect._detached` 只有 `AttachCamera` 会还；离开时若 `cursorManager` 已空就再也没机会还 | 同岛重开（`CursorManager` 复用）时相机拖拽彻底失效，玩家只能重启游戏 | 新增 `MarqueeSelect.ClearSession()`：**先尝试 `AttachCamera` 再丢引用**，并复位拖动/选择/减速 |
| **L5** | **大量"每局一份"的静态缓存不清**：兵种列表、HUD 行、头像条 slots、岸线采样、占位表、兵种查找、波扫描、编队引用 | 旧局对象被静态引用（内存）；极少数还会被画出来 / 点到（换岛那一帧的头像条） | 新增 `SessionCleanup.PurgeBattleState()` 一处调齐（各子系统新增 `ClearSession/ClearCache/ClearScanCache`） |
| **L6** | **常驻对象从不销毁**：Ledger / Flotilla / Watchdog / Marker / RemoteCursor 只有 `ClickShield` 与菜单贴图被销毁 | 热重载 / 卸载后残留 5 个 `DontDestroyOnLoad` 对象 + 2 张 128×128 贴图；`Marker` 的贴图还永远不释放 | 各加 `Destroy()`；`SessionCleanup.DestroyHelpers()` 统一调；`SpawnLedger.Destroy()` 先 `DestroyAll()` |
| **L7** | **原版设置三行在卸载后残留** | 热重载会出现两份同名开关（一份还指向旧程序集的委托） | `VanillaSettings.DestroyWidgets()`（**只在卸载**；关总开关必须保留，那是逃生门） |
| **L8** | `IngameMenu` 文件尾有 13 行空行、`ShipSpeedRuntime.Reset()` 死代码 | 无行为影响，属代码卫生 | 一并清掉 |

> **有意"不清理"的两处**（写在这里，避免以后有人当 bug 又"修"回去）：
> ① `ShipSpeedRuntime` 的每船基准（§11.13A）—— 清了就是"×2 叠乘"；
> ② `LevelTools._origStart` —— 它是把 `waveStartTime` 还回原版的唯一凭据，清了波次就再也回不来。
> 两者都按"对象被销毁时自动摘除"收敛规模（`Prune` / Unity null 判定），不会无限长。

#### C. 设计：三条时机，一个入口

```
SessionCleanup.PurgeBattleState()      每局一份的运行时状态（幂等）
        ├─ 离开战局 / 换岛：Plugin.Update 里"岛屿引用变了"→ 调一次
        ├─ 关总开关：ApplyDisabled
        └─ 卸载：DestroyHelpers 之前
SessionCleanup.RestoreGameState(gm)    把改到游戏里的东西还回去
        ├─ 关总开关
        └─ 插件卸载
SessionCleanup.DestroyHelpers()        销毁我们 new 出来的常驻对象 / 贴图 / 设置行
        └─ 插件卸载
```

**为什么用"岛屿引用变了"判定离开**：试过 `InBattle()`，但它在**暂停时也为 false** —— 按 ESC 暂停会误清遥控组。
改为 `!ReferenceEquals(gm.island, _lastIsland)`：离开地图时 `island` 变 `null`、换岛时变成另一个实例，暂停时不变。
首次进岛（`_lastIsland == null`）不清 —— 那会把刚建好的东西清掉。

**为什么离开战局时"不"调 `RestoreGameState`**：那一帧正在拆场景，把 `waveStartTime` 提前放回原值会让原版波次
在拆场过程中抢跑 `BeginWave()`；而 `waveStartTime` 的原值账（`_origStart`）保留着，真正需要还原的时机是
"关总开关"（人在局内）或"插件卸载"，两处都能准确还原。

#### D. 验证

**新增闸门（永久防线）**：`tools/check-rules.ps1` **规则⑧** —— 任何 `.cs` 只要用了 `DontDestroyOnLoad`，
就必须定义 `static void Destroy()`，否则**构建直接失败**。这样"以后再加一个常驻辅助对象、忘了写清理"会在编译期被拦下，
而不是等玩家报"退出地图后还剩东西"。

**构建闸门**：`api-check 654` / `loc-check 234（占位符 108）` / `rules 35 文件 9172 行 / 禁用清单 2 条 / 规则⑧` / `docrefs 119 处` 全绿。

**实机验收（待作者）**
1. **拦下原版波次**开着 → **关掉总开关** → 波次应立刻恢复（原来会永远不出波）；
2. 投放后**立刻**关总开关（1 秒编队窗口内）→ 不应再有船出发；
3. 框选拖到一半按 ESC / 退出岛屿 → 回来之后**相机仍能拖动**（L4）；
4. 从岛屿 A 退出回地图再进岛屿 B → 底部头像条、HUD 计数、菜单兵种列表都应立刻是 B 的（不残留 A）；
5. 热重载插件（或换 DLL 重启）→ 原版设置里**始终三行**，不重复；世界里没有 `BadNorthNewMode.*` 残留对象。

### 11.15 v1.7.7：设置界面独立分类 + Together 联机对接研究（2026-10-17，作者需求）

#### A. 原版设置里给本模组一个**独立标题**（不再塞进「控制键」）

**需求原文**：*"游戏设置菜单是有标题区分的，我们关于模组的按钮也单独设置一个标题，排在标题 2 的位置，不要放在'控制键'的标题分布内容。"*

**原版结构（反编译 + 反射核实）**

```
UserSettingsMenu : GeneratedMenu
  四个分类 = 四个"容器（VerticalLayoutGroup）+ 场景里预置的标题 + 一个列表 Transform"
      categoryControls / categoryDisplay / categoryGraphics / categoryAudio   ← 列表节点（控件往这里加）
      ×.parent                                                              ← 容器（标题 + 列表）
  标题文字是**场景对象**，不在代码里创建（所以不能"new 一个标题"，只能克隆）
```

**做法（`VanillaSettings.EnsureCategory`，零 Harmony）**

| 步 | 动作 |
|---|---|
| ① | 克隆 `categoryDisplay.parent`（**一整个分类容器**）→ 字体 / 行距 / 标题样式 / 竖排布局全部自动继承 |
| ② | 清空克隆体里"列表节点"的原版控件（用源容器里 `categoryDisplay` 的 `GetSiblingIndex()` 定位同一个节点） |
| ③ | 标题：**先把原版标题节点一起克隆**（原版每个分类 = "标题块 + 容器"两个兄弟）。标题有 `Localize` 就**换 I2 词条**（艺术字也适用）；只有 `Text` 就写文字；两者都没有（艺术字/图片组件）→ **克隆该节点 → 关掉它原本的 `Graphic`/`Renderer` 美术层 → 在同一个矩形上放我们的 `Text`**（字体/字号取原版开关行标签、颜色取原版标题染色否则用暖灰）⇒ 左边距 / 行高 / 宽度 / 锚点与原版标题完全一致，并打一行结构诊断 |
| ④ | 位置：把四个分类容器按**场景层级顺序**排序（同层比 `siblingIndex`，跨列比共同祖先下的顺序），锚点 = 第 `[UI] SettingsCategoryIndex`（默认 **2**）个容器；插入点 = **锚点前面最近的分类容器之后的第一个节点**（= 锚点标题块最前面），**不是**"紧邻的前一个兄弟" → **我们的标题就是第 2 个**。插完打一行 `分类定位`（锚点/标题参照/插入点）与一行完整层级（每次安装只打一次，用于下次实机定案） |
| ⑤ | 三行开关用原版 `AddBoolWidget(..., overrideParentTransform: 我们克隆的列表)` 创建（仍是原版控件、原版音效、原版导航） |
| ⑥ | 克隆/定位失败 → **退回**"塞进控制列"的旧行为 + Warn（任何原版改版都不会让设置项消失） |
| ⑦ | 卸载时 `DestroyWidgets()` 连**整个分类容器**一起销毁（热重载不会出现两份）；关总开关**保留**（那是逃生门） |

**新 cfg**：`[UI] SettingsCategoryIndex`（默认 `2`，1~5；超出排到最后）。

**为什么用 `Loc` 而不是 I2 词条**：标题是我们自己的品牌文字，不需要进游戏词条表；停用 `Localize` 后由我们每 15 帧写一次，切换语言立即跟上（与原版行的 `ForceUpdate` 同节奏）。

#### B. Together 联机：能兼容吗？—— **接口层已就绪，先交付"只读探测"**

**分析对象**：`Together.dll` **0.6.13 / 协议 14**（`Mono.Cecil` 实读，完整报告见本地 `TOGETHER_V177_COMPAT_PLAN.md`）。

**本轮最关键的三个发现**

| # | 发现 | 意义 |
|---|---|---|
| 1 | 需要的入口**大多已经是 `public static`**：`RaidIdentity.RegisterWave / GetWaveId / IsCommitted / WaveSignature`、`AgentAuthority.RegisterGroup`、`HostRaidManifest.IsWaveReady / CanPublishWave` | 不需要 Harmony 改 Together，纯反射即可 |
| 2 | 私有的 `HostRaidManifest.Capture` + `hostEntries` 不用自己拼 —— **`AfterAccept(waves, firstId, budget, retry)` 一条龙**做完全部登记（IL 逐条确认：`RegisterWave` → `RegisterGroup` → `Capture` → `hostEntries` → 日志） | 只需要反射调**一个**私有方法 |
| 3 | **但 `IsCommitted` 要求 `island.raid.waves.Contains(wave)`**，而把波放进 `raid.waves` 会让原版 `Raid.MaybeLaunchWaves()` 按 `waveIndex` **接管发射** | 与我们的 `FlotillaLauncher` 抢发射权 → 可能双发 / 卡波。**这是必须先解决的硬冲突**（两条路线见本地笔记 §2.3） |

**另一个必须一起改的点**：`WaveSignature` 用 `shipTravel.duration ×1000` 参与签名，而 `ShipSpeedRuntime` 会持续改 `duration`（每秒自愈 / 换档即时）⇒ 联机外部波必须**在 Capture 前定稿**（建议联机中把档位锁 `×1`）。

**本轮交付（只读，默认关）**

| 项 | 内容 |
|---|---|
| 新文件 | `TogetherBridge.cs`：版本 / 协议 / 主机探测 + 账本读取 + 波签名 + 预检（`placed` / `navPos` / 资源名可解析 / 上限 / 是否在 `raid.waves`），**不写任何 Together 状态** |
| 新 cfg | `[Compat] TogetherProbe`（默认 `false`）：`Compat.Tick` 每岛一行"会话探测"（`initial / expected / ready / entries / epoch / fail` + 每条原版波 `id/committed/ready`），`FlotillaLauncher.Launch` 每次投放一行"本波探测"（`id / committed / ready / publish / sig / groups / landings / 预检`） |
| 不变 | **联机让路照旧**（不投放 / 不遥控 / 拦波次被拒）；探测失败只 Warn 一次并停用 |
| 不上线写入的原因 | 见上表第 3 点：在确定"谁发射"之前写入会把整局打停在冻结态（`IOException → Fail`），风险远大于"客机看不见" |

**下一步（v1.7.8 建议）**：用真人双端的探测数据定 A/B 路线 → 落地 `[Compat] ExperimentalExternalWave`（默认关）→ 三组场景验收后再谈默认开。

> **2026-10-17 决策**：作者选定 **路线 A（全部在本 DLL 内、纯反射；不改 `Together.dll`、不加桥接 DLL、不用 Harmony）**。
> 可照抄的实现说明书已写入 **§11.16**（边界 / cfg / 反射成员 / 时序 / 守门 / 签名 / 撤销 / 验收），**本轮不落地代码**。

#### C. 验证

**构建闸门**：`api-check` / `loc-check` / `rules`（含规则⑧）/ `docrefs` 全绿（数字见提交说明）。

**实机验收**
1. ESC → 设置：**第 2 个标题**变成「BadNorthNewMode 模组」，三行开关在它下面（**不是**挂在「图像」标题下面）；标题的字号/颜色/左边距与原版标题一致；原「控制键」分类里不再有我们的项；日志里有一行 `分类定位`（锚点 / 标题参照 / 插入点）；
2. 切中/英：标题与三行文字都跟着变；
3. 把 `[UI] SettingsCategoryIndex` 改成 1 / 3 / 5 重启：标题位置相应变化（越界排到最后）；
4. 联机（Together 在线）且 `TogetherProbe=true`：进入岛屿后应看到一行"会话探测"与每条原版波一行；**不投放也不影响对局**。

### 11.16 Together 联机对接：**路线 A 设计定稿**（全部在本 DLL 内，纯反射；**尚未落地代码**）

> 2026-10-17 作者决策：**走 A** —— 不改 `Together.dll`、**不新增桥接 DLL**、**不用 Harmony 打它的补丁**；
> 所有对接代码都放在本模组自己的 DLL 里，用**反射**调用 Together 已经提供的入口。
> 本节是"可照抄的实现说明书"，等 §11.15B 的只读探测在真人双端跑完、确认发射权与签名时序后再落地。

#### A. 边界（明确不做什么）

| 不做 | 原因 |
|---|---|
| 改 `Together.dll` / 替换它 | 升级即失效，且属于别人的作品 |
| 新增 `BadNorthNewMode.Together.dll` | 玩家要多装一个文件、还要配对版本；用反射已足够，**没有实质收益** |
| Harmony 补丁 `HostRaidManifest` / `CanPublishActor` | 绕过它的清单/签名校验 = 直接 `IOException → Fail` 冻结整局；也违背本项目"零 Harmony"原则 |
| 客机 → 主机的投放 | 本模组在联机里仍是"主机侧功能"；客机只要求**看得见**（由 Together 姿态流驱动） |

#### B. 新增 cfg（全部默认关 / 安全值）

| 键 | 默认 | 说明 |
|---|---|---|
| `[Compat] ExperimentalExternalWave` | `false` | **总开关**。只有明确知道"两端同版本 Together + 同 mod 集合"时才开 |
| `[Compat] ExternalWaveWaitSeconds` | `3.0` | 登记后等客机回执（`IsWaveReady`）的上限；超时撤销本次投放 |
| `[Compat] ExternalWaveFailLimit` | `1` | 允许的连续失败次数；达到后**本会话永久停用**并写回 `false` |
| `[Compat] ExternalWaveLockShipSpeed` | `true` | 联机外部波期间把「靠岸速度」锁 `×1`（见 §F 签名） |

#### C. 反射成员（实读 `Together 0.6.13`；探测层已缓存大部分）

| 用途 | 成员 | 可见性 |
|---|---|---|
| 序号 | `HostRaidManifest.expectedLogicalId`（字段） | private static |
| 账本 | `HostRaidManifest.initialCount` / `ready` / `hostEntries`（字段） | private static |
| **一条龙登记** | `HostRaidManifest.AfterAccept(List<Wave>, int firstId, int budget, int retry)` | **private static** |
| 就绪/放行 | `IsWaveReady(int)` / `CanPublishWave(Wave)` | public static |
| 身份 | `RaidIdentity.RegisterWave(Wave,int)` / `GetWaveId` / `IsCommitted` / `WaveSignature` / `ReleaseWave(int)` | public / internal |
| 群组 | `AgentAuthority.RegisterGroup(ShipGroup, Squad)` | public static |
| 健康度 | `AgentAuthority.Epoch` / `HasFailure` | public static |

> `AfterAccept` 的 IL 已逐条读过：它自己会 `RegisterWave → RegisterGroup → Capture → hostEntries`；
> 我们**不要**自己拼 `Manifest`，也不要在它之外再调 `RegisterWave`（会重复/抛）。

#### D. 投放时序（唯一入口：`FlotillaLauncher.Launch()`）

```
Launch()
 ├ 0. 条件：TogetherProbe 可用 && Session.IsHost && cfg.ExperimentalExternalWave
 │         && 本会话未停用 && !AgentAuthority.HasFailure
 ├ 1. 预检（TogetherBridge.Preflight，已有）：
 │      landing.placed / navPos 有效 / shipPrefab 与 vikingRef 名字可解析 /
 │      groups≤64、landings≤128、loads≤16、agents≤8192 / wave.raid == island.raid
 │      任一失败 → 只走"仅主机可见"（现状），并打一行原因
 ├ 2. id = expectedLogicalId（绝不跳号；读不到就放弃）
 ├ 3. wave.raid = raid；raid.waves.Add(wave)          // IsCommitted 的硬条件
 ├ 4. wave.waveStartTime = float.MaxValue             // 守门：见 §E，防原版抢先发射
 ├ 5. AfterAccept([wave], id, budget: 0, retry: 0)（反射）
 │      抛 IOException → 撤销（§G）并计一次失败
 ├ 6. 等待 IsWaveReady(id)：每帧检查，最多 ExternalWaveWaitSeconds
 │      · 就绪 → wave.waveStartTime = 0f；StartCoroutine(wave.BeginWave())（我们发射）
 │      · 超时 → 撤销（§G）
 └ 7. 发射后：不释放任何 Together 状态（波由 Together 的 Prune/ReleaseWave 自行管理）
```

**为什么"自己发射"而不是让原版调度发射**（发射权，§11.15B 的 A/B 里选 **B**）：

- `Raid.MaybeLaunchWaves()` 只处理 `waves[waveIndex]`，**严格顺序**；
- 把我们的波 `Insert` 到 `waveIndex` 会改变后面所有波的下标（`Raid` 自用 + Together 的 `OnBeginWave(index)`），风险远大于收益；
- `Add` 到末尾又会让"中途投放"必须等完所有待发原版波 —— 实战中不可接受；
- 因此：**保持自建发射**，只把 `waveStartTime` 临时设 `float.MaxValue` 挡住原版，等 `IsWaveReady` 后恢复 `0` 并自己 `BeginWave`。
  `MaxValue` 对 `Landing.startTime` 的影响只存在于 `TryPlace`（此时已执行完），且 `Wave.BeginWave` 只用各 landing `startTime` 的**差值**（同时被抵消）→ 技术上安全，但仍列为**实机必验项**（B10 前科）。

#### E. 守门细节（防"双发"）

| 时刻 | 原版会看到什么 | 我们要保证 |
|---|---|---|
| `Add` 之后、`IsWaveReady` 之前 | `waves[waveIndex]` 可能正好是我们的波；`timer >= MaxValue` 永远 false | **原版不会发射**（`waveStartTime` 守门） |
| 我们自己 `BeginWave()` 之后 | `wave.triggered = true`；`haveAllLaunched` 变 true | `MaybeLaunchWaves` 只会 `waveIndex++`，不会重发 |
| 撤销时 | 波要从 `raid.waves` 移除并销毁 | 否则 `Prune()` 会把它标成"退休 id"，后续同 id 登记会抛 |

#### F. 签名与「靠岸速度」（必须一起做）

`RaidIdentity.LandingSignature` 把 `shipTravel.duration ×1000` 与坐标取整一起混进 `WaveSignature`；而 `ShipSpeedRuntime` 会**持续**改写 `duration`（换档立即、每秒自愈）。规则：

1. `ExternalWaveLockShipSpeed = true`（默认）→ 联机外部波模式期间 `ShipSpeedRuntime` 对**本波 landing** 不写 `duration`（或全局锁 ×1）；
2. 顺序：**先定稿 `duration`，再 `AfterAccept`**；之后不再改；
3. 若做不到（例如玩家在等回执时拖了档位）→ 放弃本次登记（登记前后各算一次 `WaveSignature`，不一致就撤）。

#### G. 失败与撤销（任何一条都不得留半截）

`Abort(reason)` 的顺序（**从后往前退**）：

1. `RaidIdentity.ReleaseWave(id)`（若可反射；失败只 Warn）
2. `raid.waves.Remove(wave)`
3. `wave.StopAllCoroutines()`
4. `UnityEngine.Object.Destroy(wave.gameObject)`（`SpawnLedger` 会在下一帧自动摘掉死根）
5. `IngameMenu.Say("本条未能同步给队友（" + reason + "）")` + `Util.Warn`
6. `failCount++`；达到 `ExternalWaveFailLimit` → `ExperimentalExternalWave=false`（写回 cfg）+ 本会话停用

> 注意：一旦清单已经**发出去**且客机已按它建了波，就**不能**单方面撤销（会造成两端不一致）⇒ 因此步骤 6 只允许在
> "`AfterAccept` 抛异常 / 预检失败 / 回执超时且从未 ready"时触发。

#### H. 与现有模块的交互

| 模块 | 影响 |
|---|---|
| `Compat.DropBlocked` | **保持不变**：联机默认仍让路；只有 `ExperimentalExternalWave=true` 时 `IngameMenu.Toggle` 才放行投放（新增条件） |
| `SpawnLedger` | 波现在**也在 `raid.waves` 里** → 原版结算/清场会一并处理；我们的 `Destroy` 仍保留（幂等） |
| `LevelTools`（拦波次） | 联机中本来就拒绝；无需改动。`EnsureScanned` 仍会跳过 `SpawnLedger.Owns` 的自家波 |
| `ShipSpeedRuntime` | 见 §F |
| `SessionCleanup` | `PurgeBattleState` 已有 `FlotillaLauncher.Cancel()` 会销毁挂起的波；新增"若已登记则先 `ReleaseWave`" |
| HUD | 新增一行（仅 verbose）：`[Together] 本波已登记 id=N，等待队友确认…` |

#### I. 落地顺序（建议 v1.7.8，分三步提交）

1. **探测复跑**：用现有 `[Compat] TogetherProbe` 收双端日志 → 确认 `expectedLogicalId` / `waveIndex` / `initialCount` 关系与 `sig` 稳定性；
2. **只写"登记 + 等回执"**（发射仍走现状，即登记后**不**改变发射逻辑，只观察 `IsWaveReady` 是否变 true）→ 验证清单能被客机接受、且**不冻结**；
3. **开启守门 + 自己发射**（§D/§E），双端跑完 §J。

#### J. 验收清单（真人双端，缺一不可）

| # | 场景 | 判据 |
|---|---|---|
| 1 | 普通关，主机投放 1 艘 | 客机看到**真船/真敌人**并在下船后正常行动；两端 `vikingsAlive` 一致 |
| 2 | 同一关连续投放 3 次（含 Flotilla 合并窗口） | 每次 `id` 连续递增；客机三次都看到；无 `Fail` |
| 3 | 终岛无尽（EndlessFinale 接管波次） | 与它的动态波共享序号流，仍连续；不冻结 |
| 4 | 中途投放（原版仍有未发射波） | 我们的波**立即**出发（不被 `waveIndex` 卡住）；原版波不受影响 |
| 5 | 客机断线 / 回执超时 | 主机侧撤销并提示；不出现"船在开、清单没接受" |
| 6 | 档位 ×2 时投放 | 签名不漂移（或按 §F 锁 ×1）；客机显示一致 |
| 7 | 任意异常一次后 | 开关写回 `false`、本会话不再尝试、**不冻结** |

#### K. 未决 / 需实机确认

1. `waveStartTime = float.MaxValue` 守门是否 100% 不被原版抢发（B10 前科，必须观察到 `BeginWave` 只被调用一次）；
2. `AfterAccept` 内部 `Capture` 对 `navPos.isNull` / 未 `placed` 的容忍度（我们预检已挡，但要确认无 NaN）；
3. Together 升版（协议 14→15）时 `expectedLogicalId` / `AfterAccept` 是否改名 → 探测层已按名字取，缺一项即整体停用。


