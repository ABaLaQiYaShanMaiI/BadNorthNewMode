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

**必须绕开的坑（全部实测过）**

| 坑 | 现象 | 正确做法 |
|---|---|---|
| net472 编 params 空数组 | 运行期 `MissingMethodException: System.Array.Empty`，插件静默失效 | 写 `new KeyCode[0]`；API 闸门会拦 |
| `EventSystem.IsPointerOverGameObject()` | 本游戏恒为真 → 点击被静默吞掉、无任何日志 | 不用它做 UI 判断，直接订阅原版 `onClick` |
| `pointerRationalizer.onClick +=` | 事件类型是 System.Core 3.5 的 `Action`2，运行期解析不到 | 反射 `GetEvent` + `Delegate.CreateDelegate` + `AddEventHandler` |
| `EventInfo/MethodInfo == null` | `op_Equality` 是 .NET 4.0 才加的 | 用 `object.ReferenceEquals` |
| 中途投放不下船 | `brain.order` 被 `KillAllEnemies` 抢走 → `orderDist = 1e6` → `MaybeAct` 永不成立 | 生成后 `AttachPirateOrder()` 把 order 交还 `Pirate`（原版靠"生成期我方未部署"天然正确） |
| 幽灵船（波次残留） | 我们的 Wave 不在 `raid.waves` → `Raid.IIslandWipe` 不清它 | `SpawnLedger` 在战局结束 / 离开战局 / 换岛时销毁（订阅 `EndOfLevel.postProcess`） |
| 滩头看似可用却投放失败 | 点击命中 `Modules`，或进近廊道被挡 | 收集候选滩头逐个 `TryPlace`；`CorridorClear()` 预检廊道（悬停预览共用同一判定） |

## 5. 机制设计

- **按键**：`F1` 开关投放菜单（菜单即投放模式）；`F2` 强制清场；右键 / `Esc` 关闭菜单。
- **菜单**（IMGUI，零资源）：上排兵种（本关 `levelNode.enemies` ∪ 全局字典 `Viking_*`，按 **bounty 升序** = 难度递增；显示 `简中名（内部名）+ 默认数`）；下排数量预设（默认 / 1 / 2 / 3 / 4 / 6 / 8 / 10 / 12）。左键点选即写回 cfg（`EnemyName` / `SquadSize`）。菜单区域内的点击被屏蔽（`PointerInMenu`，注意 IMGUI 的 y 轴翻转）。
- **默认装载数**：`UnitNames.DefaultCounts` 梯度表（剑兵 12 / 盾兵 10 / 弓手 8 / 掷斧手 7 / 双手剑士 5 / 狂战士 5 / **巨人级各 1**）；未收录兵种回退原版公式（最小船容量 ÷ 单体面积），最终由最大船容量裁剪。
- **投放链**：`TryResolve`（点击处与落点都须与海面齐平 + 距离 ≤ `MaxShoreDistance`）→ `TrySpawn` 建原版对象树 → 逐个候选 `TryPlace` → `Spawn()` → `AttachPirateOrder()` → `raid.StartCoroutine(wave.BeginWave())`（原版 Launch / 音频 / 到达回调）。
- **落点 UI**：`PlacementMarker` 运行时生成环形 / 内芯贴图（优先加法混合），悬停实时预览（亮青 = 可投放，暗红 = 不可投放）。
- **清理**：`SpawnLedger` 自动（战局结束 / 离开战局 / 换岛）+ `F2` 手动。

## 6. 已知限制 / 待实测

- **T1**：投放波次不在 `raid.waves`，对 UI 进度 / 结算展示的影响（实测无异常，未深究）。
- **T2**：菜单是 IMGUI 覆盖层，点菜单时**游戏自己的世界点击仍会收到**（可能顺带选中压在菜单下的小队）；彻底屏蔽需补游戏侧点击入口，暂未做。
- **T3**：`MaxLandHeight`（0.5m）/ `MaxShoreDistance`（3m）的通用性——按 HUD 显示的实测落差微调。
- **T4**：`DisembarkWatchdog` 兜底路径正常不触发；若某兵种仍卡住，日志会给完整诊断。

## 7. 命名与提交约定

- 产物：`BadNorthNewMode.dll`；命名空间 `BadNorthNewMode`；GUID `badnorth.newmode`；部署到 `BepInEx\plugins`。
- **版本号统一**：`Plugin.VERSION` = `csproj Version` = 仓库提交 `vX.Y.Z`（自 v1.2.0 起）。
- 提交：一个里程碑一个提交，标题由作者撰写。
- 文档纪律：只本文件（+ 可选 `开发日志.md`）；新改动只在 §5 追加一行、§8 表格追加一行，不写长叙事。

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
| **v1.2.3** | **1.2.3** | **本文件精简（249 行/19.8KB → 109 行/6.0KB）+ 注释规范写入 §2** |

| 弓手乘船射击 | 与 Pirate / order 无关（`Archery : Brain` 自驱） | 无需处理；下船时只摘掉 Pirate action，Archery 不受影响 |
