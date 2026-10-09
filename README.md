# Bad North - New Mode

A BepInEx mod for Bad North that adds two ways to play: dropping enemy ships and remote-controlling enemy units.

## Controls

- `F1`: toggle the drop menu (left-click a unit type and a count, switch 中文/English at the top). Inside the menu, left-click beachhead land to drop; `Esc` or right-click closes it.
- `F2`: clean up — destroys every ship and unit dropped by this mod.
- `F3`: force win — runs the vanilla victory flow (results screen, achievements and save all proceed normally).
- Remote control (after closing the menu) — the mouse scheme follows the game's own cursor setting:
  - Two-button setting: left-click a unit = select that one unit; double-click = select its whole squad; right-click a tile = advance.
  - One-button / touch setting: one button does both, exactly like vanilla — with nothing selected it selects the unit you click, and with a selection it advances when you click a tile.
  - Hold `Shift` and click = add / remove a unit. `R` = select all. Hold `Alt` and drag = box select.
  - You may select units while they are still aboard a ship: click a tile to set the rally point they walk to right after landing.
  - Clicking the sea or any invalid tile clears the selection (same as vanilla).
- Dropped units stand still once they reach the rally point (no more twitching) and can burn houses like any viking.
- Your own squads keep the mouse buttons as vanilla.
- Options (`BepInEx/config/badnorth.newmode.cfg`), both toggleable from the `F1` menu: `[Native] RemoteNativeUnits` (on by default) lets you select and command the vanilla enemies too; `[Native] BlockVanillaWaves` = endless custom mode (no vanilla waves, and the battle never ends by itself — press `F3` to leave); `[Remote] RemoteOrderButton` = `Auto` follows your cursor setting, `Left` restores the old v1.5.4 feel.

## Links & License

- Project: https://github.com/ABaLaQiYaShanMaiI/BadNorthNewMode
- License: MIT

---

# Bad North - New Mode（简体中文）

BepInEx 模组：给《Bad North》加“投放敌舰 + 遥控敌方单位”。

## 操作

- `F1`：开关投放菜单（左键点兵种与数量，顶部切 中文/English）；菜单内点滩头陆地投放，右键或 `Esc` 关闭。
- `F2`：清场，销毁本模组投放的船与单位。
- `F3`：强制胜利，走原版结算。
- 遥控（关闭菜单后，**鼠标方案跟随游戏的单/双键设置**）：
  - 双键：左键点单位 = 选中这**一个**；双击 = 选整队；右键点地块 = 前进。
  - 单键/触摸：没选中时点单位 = 选中；有选中时点地块 = 前进。
  - `Shift` + 点 = 并入/移出；`R` = 全选；`Alt` + 拖动 = 框选。
  - 船上也能选：点地块定下登陆集结点，落地自动前往。
  - 点海面 = 取消选中；单位集结后站得住、能烧房子。
- 我方小队仍按原版。
- 选项见 `badnorth.newmode.cfg`：原版敌人也可遥控（默认开），F1 菜单里可切两个接管开关。

## 链接与许可

- 项目地址：https://github.com/ABaLaQiYaShanMaiI/BadNorthNewMode
- 开源许可：MIT

