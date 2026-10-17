# BadNorthTogether 0.6.14 验证清单

> 目标：确认普通岛外部波在双端 BadNorthTogether 下可见、可打、不冻结；同时确认原版 Together 客机不会被新清单打挂。

## 1. 构建 / 加载

| # | 检查 | 通过标准 |
|---|---|---|
| B1 | 运行 `build.ps1 -Configuration Release` | 退出码 0，输出 `bin\Release\BadNorthTogether.dll` |
| B2 | 检查 DLL 元数据 | 插件 GUID `com.linfeng.together`，显示名 `BadNorthTogether`，版本 `0.6.14` |
| B3 | 只放 `BadNorthTogether.dll` 启动 | 日志出现 `BadNorthTogether v0.6.14 已加载`，无重复 GUID 警告 |
| B4 | 原版 `Together.dll` 与新版同时存在 | BepInEx 优先加载 0.6.14（仍建议删除旧文件） |

## 2. 基础 2 人合作（不投放）

| # | 场景 | 通过标准 |
|---|---|---|
| C1 | 双端 0.6.14，Steam 好友大厅 | 握手成功，F9 显示对方名字/延迟 |
| C2 | 主机选岛 → 客机跟随 | 战役/部署/战斗状态正常更新 |
| C3 | 双方部署 → 第一波 → 结算 | 与原 Together 行为一致，无新增异常 |
| C4 | 主机 0.6.14 + 客机原版 0.6.13 | 基础合作可进；外部波不接管（仅主机可见），不冻结 |
| C5 | 客机断线 / 主机断线 | 按原 Together 行为冻结/回主菜单，无新增报错 |

## 3. 普通岛外部波（核心）

| # | 场景 | 通过标准 |
|---|---|---|
| E1 | 双端 0.6.14，普通岛，第一波开始后主机投放 1 队 | 客机看到船 + 敌人；可攻击/击杀；双方结算一致 |
| E2 | 连续投放 2~3 队 | 逻辑编号连续；无 `波清单序号不连续`；无重复敌人 |
| E3 | 外部波不在 `raid.waves`（BadNorthNewMode 默认） | `TryAdopt` 走 `RegisterExternal`，`IsCommitted` 放行；客机可见，不要求模组把波塞进 `raid.waves` |
| E3b | 登记中途异常（可注入/模拟） | `RaidIdentity.UnregisterWave` 同时清掉 `externalWaves`，下一次外部波编号可继续使用 |
| E4 | 客机缺少船/兵资源 | 客机 `ResolveResources` 报错并按 Together 原有策略失败；日志能定位缺失资源 |
| E5 | 外部波 `waveStartTime` 为 `float.MaxValue` | `SafeFloat` clamp 后清单可读，不出现 `波清单数值非法` |
| E6 | 外部波 `bounty = 0` | `Budget` clamp 到 1，客机不再抛 `波清单整数越界：0` |
| E7 | 同一波重复 `BeginWave` | 不会二次接管/重复发清单 |
| E8 | 登记中途异常（可注入/模拟） | `RaidIdentity` 回滚，下一次外部波编号可继续使用 |
| E9 | 主机开加速器 / 客机直连 | Steam 大厅路径正常；TCP 下外部波仅主机可见（预期） |
| E10 | 终岛 EndlessFinale + 外部波 | 原无尽清单路径不受影响；编号不冲突 |

## 4. 联机中不要同时开的旧路径

| # | 场景 | 预期 |
|---|---|---|
| X1 | BadNorthTogether + NewMode `[Compat] TogetherExternalWave=true` | 可能重复登记/重复敌人；应关闭旧路径 |
| X2 | 关闭 NewMode 旧路径，只保留 `BeginWave()` | `HostRaidExternal` 接管，E1 通过 |
| X3 | 外部模组反射 `Together.BadNorthTogetherFeatures` | 能读到 `Version=0.6.14`、`ExternalWaves=true` |

## 5. 日志关键字

- `[主机波清单] 外部波登记 N（船组 M）`
- `[主机波清单] 已发送逻辑波 N，等待客户端装配确认`
- `[主机波清单] 客户端已装配逻辑波 N；允许发布人物与开波`
- `[波次] 已接管外部波 N，等待客机装配`
- `[波次] 主机开启外部逻辑波 N`
- `[波次] 对端未确认支持普通岛外部波，外部波改为仅主机可见`
- 失败：`波清单整数越界` / `波清单数值非法` / `波清单序号不连续` / `客户端装配波超时`

> 本清单是静态/实机结合的最小验收集；无法在本机代替真人双端跑游戏的部分已在表中标注为需要实机。
