# Bad North - New Mode

A BepInEx mod for Bad North that adds two ways to play: dropping enemy ships and remote-controlling enemy units.

## Controls

- `F1`: toggle the drop menu (left-click a unit type and a count, switch 中文/English at the top). Inside the menu, left-click beachhead land to drop; `Esc` or right-click closes it.
- `F2`: clean up — destroys every ship and unit dropped by this mod.
- `F3`: force win — runs the vanilla victory flow (results screen, achievements and save all proceed normally).
- Remote control (after closing the menu) — the mouse scheme follows the game's own cursor setting:
  - Two-button setting: left-click a unit = select its whole squad; right-click a tile = regroup and advance.
  - One-button / touch setting: one button does both, exactly like vanilla — with nothing selected it selects our unit, and with a selection it advances when you click a tile.
  - Hold `Shift` and click: add that squad to / remove it from the selection.
  - `R`: select all controllable units (time slows while you have a selection).
  - Hold `Alt` and drag: box select. You can also drag starting on a unit.
  - You may select units while they are still aboard a ship: click a tile to set the rally point they walk to right after landing.
  - Clicking the sea or any invalid tile clears the selection (same as vanilla).
- Your own squads keep the mouse buttons as vanilla.
- Options (`BepInEx/config/badnorth.newmode.cfg`): `[Remote] RemoteOrderButton` (`Right` = the scheme above, `Left` = the old v1.5.4 feel), `[Native] BlockVanillaWaves` (no vanilla waves this battle), `[Native] RemoteNativeUnits` (also remote-control the vanilla enemies).

## Links & License

- Project: https://github.com/ABaLaQiYaShanMaiI/BadNorthNewMode
- License: MIT

---

# Bad North - New Mode（简体中文）

BepInEx 模组：给《Bad North》加“投放敌舰 + 遥控敌方单位”两种玩法。

## 操作

- `F1`：开关投放菜单（左键点兵种与数量，顶部切 中文/English）；菜单内点滩头陆地投放，右键或 `Esc` 关闭。
- `F2`：清场，销毁本模组投放的船与单位。
- `F3`：强制胜利，走原版结算。
- 遥控（关闭菜单后，**鼠标方案跟随游戏的单/双键设置**）：
  - 双键设置：左键点单位 = 选整队；右键点地块 = 前进。
  - 单键/触摸设置：一个键兼两用（没选中时点单位 = 选整队，有选中时点地块 = 前进），与原版一致。
  - 按住 `Shift` + 点：并入或移出该队。
  - `R`：全选（有选中时放慢时间）。
  - 按住 `Alt` 拖动：框选。
  - 船上也能选：点地块定下登陆后的集结点，落地后自动前往。
  - 点海面 = 取消选中。
- 我方小队仍按原版操作。
- 选项见 `badnorth.newmode.cfg`。

## 链接与许可

- 项目地址：https://github.com/ABaLaQiYaShanMaiI/BadNorthNewMode
- 开源许可：MIT

