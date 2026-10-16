# Bad North - New Mode  v1.7.5

A BepInEx mod for Bad North: drop enemy ships and command enemy units.

## Controls

- `F1`: drop menu (drag its title bar). Pick a unit type, then click beachhead land; `Esc` or right-click closes it.
- Remote control: left-click a unit = select it, double-click = its squad, click a tile = advance. `Shift`+click = add/remove, `R` = select all, `Alt`+drag = box select; clicking the sea clears the selection. With the one-button cursor setting, one button does both (click a unit to select, click a tile to advance).
- Squad bar at the bottom: click a portrait to select that squad, `Shift` to merge. It slides above the vanilla ability bar while one of your own squads is selected.
- `F2`: cleanup (destroy what this mod dropped). `F3`: force win (runs the vanilla results flow).
- In the `F1` menu: a **4-stop shore-speed drag bar** (`x1 / x1.25 / x1.5 / x2`) — speeds up how fast ships (both native raid waves and mod-dropped ones) reach the shore; `x1` = vanilla speed (labelled on the bar), drag across the stops to change it, applies to ships already sailing.
- The menu title bar has a **Fold / Expand** button: folds the whole panel down to its title row when you want to see the island.
- **In the game's own settings** (Esc → Settings → the **top** of the `Controls` column) there are two switches: `BadNorthNewMode: mod enabled` (master switch — off = the mod goes completely quiet: it stops taking over mouse clicks, draws nothing, its hotkeys stop working, and ships already sailing are restored to vanilla speed) and `Native units: controllable` (turn this off and vanilla raiders are never selected/commanded by a mouse click — handy when you just want to play normally). Toggling takes effect immediately, the two rows always stay in the game's own settings (so you can switch back any time), and they follow the F1 menu / the cfg file automatically.
- Settings: `BepInEx/config/badnorth.newmode.cfg`.

## Compatibility

**Scope: single-player.** This mod is adapted to run next to the `旧灰复燃的战旗` mod pack (V3.0+, the pack replaces the game assembly and ships ~21 plugins) in **single-player**: dropping, remote command, cleanup (`F2`) and force win (`F3`) all work; the only automatic stand-down is the pack's **Endless Finale** terminal island, whose waves the pack owns (`[Compat] DropOnTerminal=true` forces dropping back on). Wave blocking (`Native.BlockVanillaWaves`) stays available in single-player.

**Online co-op (the pack's bundled Together) is NOT adapted yet.** In a Together session this mod deliberately stands down as a whole — no dropping, no remote command, and wave blocking is refused — so that it cannot freeze the host's wave pipeline or pollute the other player's ledger. It does not take part in Together's sync protocol at all; leave the session and everything is back. Switches live in `BepInEx/config/badnorth.newmode.cfg`, section `[Compat]`. Unit mods: rename their units for the drop menu with `[UI] UnitNameOverrides` (e.g. `Viking_Spearman=Spearman`).

## Links & License

- Project: https://github.com/ABaLaQiYaShanMaiI/BadNorthNewMode
- License: MIT

---

# Bad North - New Mode v1.7.5（简体中文）

BepInEx 模组：给《Bad North》加“投放敌舰”和“遥控敌方单位”两种玩法。

## 操作

- `F1`：投放菜单（标题栏可拖动）。点兵种 → 点滩头陆地投放；`Esc` 或右键关闭。
- 遥控：左键点单位 = 选中，双击 = 整队，点地块 = 前进；`Shift` + 点 = 并入/移出，`R` = 全选，`Alt` + 拖动 = 框选；点海面 = 取消选中。单键光标模式下同一个键完成选中与前进。
- 底部头像条：点一下选中整队（`Shift` 并入）；选中我方小队、原版弹出技能条时，它自动让到上方。
- `F2`：清场（销毁本模组投放的船与单位）。`F3`：强制胜利（走原版结算）。
- `F1` 菜单里有一条 **靠岸速度 4 档拖动条**（`×1 / ×1.25 / ×1.5 / ×2`）：加快船只靠岸（**原生波次与投放的船都生效**）；`×1` = 原版速度（条上直接标着"原版"），按住横扫即换档、在航的船立刻变。
- 菜单标题栏有 **收起 / 展开**：一键把整块面板收成一行标题，方便看清岛屿。
- **游戏原生设置里就能开关本模组**（ESC → 设置 → **控制** 分类**最前面**两行）：`BadNorthNewMode：启用模组`（**总开关**，关掉后模组完全静默 —— 不再接管鼠标点击、不画任何东西、快捷键失效、在航的船速还原成原版）与 `原生单位：可遥控`（关掉后，点原版上岛的敌方单位不再被选中/遥控 —— 想安安静静按原版玩法时用这一条）。点击**立即生效**；两行始终留在原版设置里（随时能切回来），并会跟着 F1 菜单 / 配置文件的值自动更新。
- 配置：`BepInEx/config/badnorth.newmode.cfg`。

## 兼容

**适用范围：单机。** 与《旧灰复燃的战旗》整合包（V3.0+；包会替换游戏本体程序集并自带约 21 个插件）在**单机**下已适配：投放 / 遥控 / 清场（`F2`）/ 强制胜利（`F3`）全部可用；唯一的自动让路是整合包的 **终岛无尽**（该岛波次由包接管，`[Compat] DropOnTerminal=true` 可强行开放投放）。「拦下原版波次」在单机照常可用。

**联机（包自带的 Together）暂不适配。** 联机中本模组**整体主动让路**（不投放、不遥控，并拒绝对波次动手），以免冻结主机的波次流程或污染对方账本 —— 它完全不参与 Together 的同步协议；退出联机即全部恢复。开关在 `BepInEx/config/badnorth.newmode.cfg` 的 `[Compat]` 段；**加兵种的 mod** 可用 `[UI] UnitNameOverrides` 改菜单里的显示名（例 `Viking_Spearman=长矛兵`）。

## 链接与许可

- 项目地址：https://github.com/ABaLaQiYaShanMaiI/BadNorthNewMode
- 开源许可：MIT

