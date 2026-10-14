# Bad North - New Mode  v1.7.1

A BepInEx mod for Bad North: drop enemy ships and command enemy units.

## Controls

- `F1`: drop menu (drag its title bar). Pick a unit type, then click beachhead land; `Esc` or right-click closes it.
- Remote control: left-click a unit = select it, double-click = its squad, click a tile = advance. `Shift`+click = add/remove, `R` = select all, `Alt`+drag = box select; clicking the sea clears the selection. With the one-button cursor setting, one button does both (click a unit to select, click a tile to advance).
- Squad bar at the bottom: click a portrait to select that squad, `Shift` to merge. It slides above the vanilla ability bar while one of your own squads is selected.
- `F2`: cleanup (destroy what this mod dropped). `F3`: force win (runs the vanilla results flow).
- In the `F1` menu: a **4-stop shore-speed drag bar** (`x1 / x1.25 / x1.5 / x2`) — speeds up how fast ships (both native raid waves and mod-dropped ones) reach the shore; drag across the stops to change it, applies to ships already sailing.
- Settings: `BepInEx/config/badnorth.newmode.cfg`.

## Compatibility

Coexists with the `旧灰复燃的战旗` mod pack (V3.0+, including its bundled Together co-op): during a Together session dropping and remote command stand down, and the pack's Endless Finale keeps control of the final island's waves. Wave blocking (`Native.BlockVanillaWaves`) still works everywhere else. Switches live in `BepInEx/config/badnorth.newmode.cfg`, section `[Compat]`. Unit mods: rename their units for the drop menu with `[UI] UnitNameOverrides` (e.g. `Viking_Spearman=Spearman`).

## Links & License

- Project: https://github.com/ABaLaQiYaShanMaiI/BadNorthNewMode
- License: MIT

---

# Bad North - New Mode v1.7.1（简体中文）

BepInEx 模组：给《Bad North》加“投放敌舰”和“遥控敌方单位”两种玩法。

## 操作

- `F1`：投放菜单（标题栏可拖动）。点兵种 → 点滩头陆地投放；`Esc` 或右键关闭。
- 遥控：左键点单位 = 选中，双击 = 整队，点地块 = 前进；`Shift` + 点 = 并入/移出，`R` = 全选，`Alt` + 拖动 = 框选；点海面 = 取消选中。单键光标模式下同一个键完成选中与前进。
- 底部头像条：点一下选中整队（`Shift` 并入）；选中我方小队、原版弹出技能条时，它自动让到上方。
- `F2`：清场（销毁本模组投放的船与单位）。`F3`：强制胜利（走原版结算）。
- `F1` 菜单里有一条 **靠岸速度 4 档拖动条**（`×1 / ×1.25 / ×1.5 / ×2`）：加快船只靠岸（**原生波次与投放的船都生效**），按住横扫即换档、在航的船立刻变。
- 配置：`BepInEx/config/badnorth.newmode.cfg`。

## 兼容

可与《旧灰复燃的战旗》整合包（V3.0+，含自带的 Together 联机）共存：联机中「投放 / 遥控」自动让路，整合包的终岛无尽继续接管波次；**「拦下原版波次」在单机照常可用**（联机中会拒绝，因为会让整局冻结）。开关在 `BepInEx/config/badnorth.newmode.cfg` 的 `[Compat]` 段。**加兵种的 mod**：菜单里显示的名字可以用 `[UI] UnitNameOverrides` 改成你想要的（例 `Viking_Spearman=长矛兵`）。

## 链接与许可

- 项目地址：https://github.com/ABaLaQiYaShanMaiI/BadNorthNewMode
- 开源许可：MIT

