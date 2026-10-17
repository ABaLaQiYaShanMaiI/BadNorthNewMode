# BadNorthTogether

**基于 Together 0.6.13 的 0.6.14 补丁版（联机协议 14）**：保留 Together 全部原有功能，只补上
「普通岛屿也能同步外部模组新建的波次」这一条 —— 这样 `BadNorthNewMode` 在联机中投放的
船与敌人，客机也能看见、能攻击。

> 0.6.14 起：外部波增加 **双端能力门控** 与 **登记失败回滚**；原版 Together 客机不会再被普通岛清单打挂。
> 程序集名仍然是 `Together`（反射 / `BepInDependency("com.linfeng.together")` / cfg 文件名都不变），
> 只是**发布文件名**改成 `BadNorthTogether.dll`。

## 安装

1. 删除旧 `BepInEx/plugins/Together.dll`（**不能两份共存**；0.6.14 版本号会让 BepInEx 优先加载本分支，
   但同程序集名同时存在仍容易混淆）；
2. 把 `bin/Release/BadNorthTogether.dll` 放进 `BepInEx/plugins/`；
3. **双端都要用同一份 0.6.14**：协议仍是 14，基础 2 人合作可与原版 Together 互通；
   普通岛外部波只有在双方都声明 `BadNorthTogether/0.6.14` 时才启用，旧 Together 客机会自动退回
   “仅主机可见”，不会冻结。

## 构建

```powershell
# 只编译
.\build.ps1
# 编译 + 部署到游戏（自动把旧 Together.dll 改名备份）
.\build.ps1 -Deploy
```

构建用 .NET SDK 自带的 Roslyn `csc`，直接引用游戏目录里的 **net35** 程序集
（`mscorlib 2.0` / `Assembly-CSharp` / `UnityEngine.*` / `BepInEx` / `0Harmony`），
**不能用** `dotnet build` 的 net472/net8 目标框架，否则生成的程序集会引用 4.x mscorlib，Unity Mono 2.0 无法加载。

`Together/Together.csproj` + `Together.sln` 是给装了 MSBuild 的机器用的等价工程（可选）。

## 改了什么（源码级，约 300 行含注释）

| # | 位置 | 改动 |
|---|---|---|
| 1 | `HostRaidManifest.TryPatch()` | `travelFields` 初始化移到 EndlessFinale 判断之前；缺 EndlessFinale / 无尽工厂接口不兼容时只关闭无尽接管，不再抛异常 |
| 2 | `HostRaidManifest.ResolveResources()` | 去掉“必须有 RollingRaidController”，只校验 `island.navMesh` |
| 3 | `HostRaidManifest.Build()` | `acceptWaves` 调用前判空（普通岛跳过 EndlessFinale 记账） |
| 4 | `HostRaidManifest` | 新增 `nextExternalId`（`BeginIsland = initialCount`，`AfterAccept` 取 max）、`TryAdopt` / `CaptureExternal` / `SignatureOf` |
| 5 | `HostRaidExternal`（`[HarmonyPatch(typeof(Wave), "BeginWave")]`） | 主机 + 联机 + `DeploySync.BattleStarted` + `Epoch>0` 且该波未登记 → 拦下本机开波、登记编号、发清单；客机 `ready` 后恢复开波并补发 `WaveBegin` |
| 6 | `RaidIdentity.IsRegistered` / `WaveSync.SendExternalBegin` | 判别原版波/已登记波；外部波单独补发开波命令（签名取清单里存的那份，避免船速档位在捕获后改写航线导致漂移） |

**保持不变**：协议 14、所有消息格式、原有终岛无尽流程、主机/客机权威模型、UI 与按键。
**新增**：岛内新建的、未登记的 Wave 会被登记并广播（设计上对任何模组的波都生效，不只本模组）。

## 0.6.14 修复与门控

| # | 位置 | 改动 |
|---|---|---|
| 7 | `HostRaidManifest.CaptureExternal()` | `Budget` 由 `0` 改为 `Mathf.Clamp(wave.bounty, 1, 32767)`；`Start/SpreadGroup/SpreadShip` 统一走 `SafeFloat`。修复客机 `ReadManifest` 的 `1..32767` 校验直接抛 `IOException` 的问题 |
| 8 | `HostRaidManifest.TryAdopt()` | 不要求外部波在 `raid.waves` 里（BadNorthNewMode 默认不入列表）；改用 `RaidIdentity.RegisterExternal` 记入 `externalWaves`，让 `IsCommitted` / `Prune` 放行；编号冲突自动跳过；`RegisterExternal` 之后的任何异常都会 `RaidIdentity.UnregisterWave + ReleaseWave` 回滚 |
| 9 | `HostRaidExternal` | 新增 `Session.PeerSupportsExternalWaves` 门控；未确认对端能力时外部波仅主机可见（不冻结）；同一波重复 `BeginWave` 不会二次接管 |
| 10 | `SteamTransport` | 进入大厅后写入 `together_build = BadNorthTogether/0.6.14` 与 `together_external_waves = 1`；主机每秒读取对端成员数据，决定 `PeerSupportsExternalWaves`；TCP 没有大厅数据，默认不启用外部波 |
| 11 | `RaidIdentity` | 新增 `UnregisterWave`，供外部波登记失败回滚；`UnregisterWave` 同时清掉 `externalWaves` |
| 12 | `RaidIdentity.IsCommitted` | 外部波（`RegisterExternal`）不再要求出现在 `raid.waves` 里；`Prune` 也不会把它当孤儿退休 |
| 13 | `BadNorthTogetherFeatures` | 新增公开能力标记（版本 / 协议 / `ExternalWaves` / `SteamCapabilityGate`），供 BadNorthNewMode 等外部模组反射识别本分支 |

> 与 BadNorthNewMode v1.8.0 的旧反射路径**不要同时开**：使用 BadNorthTogether 时，
> 建议关闭 NewMode 的 `[Compat] TogetherExternalWave`（或等待 NewMode 检测到
> `Together.BadNorthTogetherFeatures` 后自动让路），只保留“建波 → `BeginWave()`”。
> 两条路径同时生效可能出现重复敌人或清单序号冲突。



## 已知注意

1. **靠岸速度**：清单在 `BeginWave` 时捕获航线用时；若主机档位 ≠ ×1，`BadNorthNewMode` 的变速会在捕获之后才写到船上 →
   主机本地船比客机快（单位位置仍以主机为准）。联机建议保持 **×1**。
2. **教程关**：Together 在教程关沿用原版自动部署，人物基线仍可能超时（与补丁无关）——先单机过教程再联机。
3. **同 mod 集**：客机缺资源时会在 `ResolveResources` 抛错 → 整局 `Fail`；两端插件与资源必须一致。
4. **Together 升版**：协议/类名一变，需要按新版重新落一遍这些改动（本目录基于 0.6.13 源码，当前产出 0.6.14）。
5. **TCP 直连**：没有 Steam 大厅成员数据，`PeerSupportsExternalWaves` 默认 `false`，外部波仅主机可见；完整同步请走 Steam 好友大厅。
6. **客机确认超时**：当前仍沿用 Together 的 60 秒 `AgentAuthority.Fail`，不是静默降级；这是下一步 P1 要补的“取消/回退”项。
7. **BadNorthNewMode 旧桥接**：当前 NewMode 的投放实际只调用 `wave.BeginWave()`，由本分支 `HostRaidExternal` 接管；
   不要同时启用 NewMode 里未使用的 `TogetherBridge` 登记/借号广播路径。

## 验证

构建/加载、基础双人合作、普通岛外部波、旧路径互斥的检查表见 `TEST_MATRIX.md`。

无头静态闸门（不需要启动游戏）：

```powershell
.\tools\check-badnorthtogether.ps1 -GameDir "D:\Steam\steamapps\common\BadNorth"
```

## 目录

```
build.ps1                 构建 / 部署
Together.sln              可选 VS 解决方案
Together/
  Together.csproj         可选 MSBuild 工程
  Properties/AssemblyInfo.cs
  Together/*.cs           Together 0.6.13 反编译源码（含补丁）
  Together/Net/*.cs
```

反编译说明：用 `ilspycmd -p --nested-directories` 反编译 `Together.dll` 得到可编译工程；
ILSpy 的 `(ref x)` 写法与嵌套类型简写已在本目录修正（`UnityObjectOps.Truthy` 等），
`build.ps1` 会直接编译 `Together/**/*.cs`。
