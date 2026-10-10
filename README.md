# Bad North - New Mode  v1.6.2

A BepInEx mod for Bad North that adds two ways to play: dropping enemy ships and remote-controlling enemy units.

## Controls

- `F1`: toggle the drop menu (left-click a unit type and a count, switch 中文/English at the top). Inside the menu, left-click beachhead land to drop; `Esc` or right-click closes it. The panel is rounded, uses the game's own font and can be dragged by its title bar; `-`/`+` fine-tune the count, and Cleanup / Force win are buttons too.
- Messages appear in the game's own notification bar, and Cleanup / Release remote control ask first with the game's own confirm dialog (both can be turned off in the cfg `[UI]` section).
- A **squad bar** sits at the bottom: every dropped / native squad you can command gets a slot with a **cropped portrait from the game's own unit sprite** plus the full unit name and `×count` (counts in Times New Roman). **Left-click a slot to select that whole squad**, `Shift`+click to merge, then click a tile to move it. Slot widths are measured from the text so names are never clipped, and **when you select one of your own squads (the vanilla ability bar appears) our bar automatically moves above it** - or set the position yourself with `[UI] SquadBarAlign / SquadBarBottom`. The UI uses the official-site-like palette (sand panels, blue-grey headers, soft yellow buttons, black text) and the game's own art font. **Both the drop menu and the info box can be dragged.**
- `F2`: clean up — destroys every ship and unit dropped by this mod.
- `F3`: force win — runs the vanilla victory flow (results screen, achievements and save all proceed normally).
- Remote control (after closing the menu) — the mouse scheme follows the game's own cursor setting:
  - Two-button setting: left-click a unit = select that one unit; double-click = select its whole squad; right-click a tile = advance.
  - One-button / touch setting: one button does both, exactly like vanilla — with nothing selected it selects the unit you click, and with a selection it advances when you click a tile.
  - Hold `Shift` and click = add / remove a unit. `R` = select all. Hold `Alt` and drag = box select; dragging **without** `Alt` pans the camera as vanilla.
  - Box-selecting several squads lets you command them together: each squad takes one tile (the clicked tile, then the tiles around it) and lines up on it exactly like your own squads do.
  - You may select units while they are still aboard a ship (native ones too): click a tile to set the rally point they walk to right after landing.
  - Clicking the sea or any invalid tile clears the selection (same as vanilla). Nothing moves unless something is selected.
- The `F1` menu has a one-click **Release remote control** button: every controlled squad goes back to vanilla AI.
- Units line up on arrival (compact, exactly like your own squads) and hold position without twitching, and can burn houses like any viking.
- Your own squads keep the mouse buttons as vanilla.
- Options (`BepInEx/config/badnorth.newmode.cfg`), both toggleable from the `F1` menu: `[Native] ControlNativeUnits` (on by default) lets you select and command the vanilla-spawned enemies (the native units) as well; `[Native] BlockVanillaWaves` = endless custom mode (no vanilla waves, and the battle never ends by itself — press `F3` to leave); `[Remote] RemoteOrderButton` = `Auto` follows your cursor setting, `Left` restores the old v1.5.4 feel. `[UI]` = `UseVanillaUI` (game notification bar + confirm dialog), `UseVanillaFont` / `FontName` (game / system font; empty = the game's own art font), `UiFontSize`, `PanelColor` (sand), `ButtonColor` (soft yellow), `ConfirmDestructive`, `SquadBar` / `SquadBarMax` / `SquadBarAlign` / `SquadBarBottom` (the bottom portrait bar). The old `TextOutline` line can be deleted - text outlines were removed.

## Links & License

- Project: https://github.com/ABaLaQiYaShanMaiI/BadNorthNewMode
- License: MIT

---

# Bad North - New Mode v1.6.2（简体中文）

BepInEx 模组：给《Bad North》加“投放敌舰 + 遥控敌方单位”。

## 操作

- `F1`：开关投放菜单（左键点兵种与数量，顶部切 中文/English）；菜单内点滩头陆地投放，右键或 `Esc` 关闭。菜单是圆角面板、用游戏自带字体，**拖标题栏可移动**；`-`/`+` 微调数量，清场 / 强制胜利也做成了按钮。
- 提示改走**游戏自己的通知条**；清场 / 释放遥控前弹**游戏自己的确认框**（都可在 cfg 的 `[UI]` 段关掉）。
- 屏幕底部有**小队头像条**：场上每一支可指挥的小队占一格（**裁剪过的原版兵种头像** + 完整兵种名 + `×人数`，人数用 Times New Roman），**左键点一下 = 选中整队**、`Shift` + 点 = 并入/移出，再点地块即可前进——不用在地图上找单位。每格宽度按文字实测（名称**不会被裁**）；**你选中我方小队、原版底部弹出技能条时，头像条会自动抬到技能条上方**；想换位置用 `[UI] SquadBarAlign / SquadBarBottom`。整体配色是**仿坏北官网**那套（沙色面板 `#D5D0C8` + 蓝灰标题 `#89A1AD` + 淡黄键 + 黑字），字体借游戏原版艺术字（`Body_Chinese_Simp`）。**投放菜单与提示框都能拖动**，位置会记住。
- `F2`：清场，销毁本模组投放的船与单位。
- `F3`：强制胜利，走原版结算。
- 遥控（关闭菜单后，**鼠标方案跟随游戏的单/双键设置**）：
  - 双键：左键点单位 = 选中这**一个**；双击 = 选整队；右键点地块 = 前进。
  - 单键/触摸：没选中时点单位 = 选中；有选中时点地块 = 前进。
  - `Shift` + 点 = 并入/移出；`R` = 全选；`Alt` + 拖动 = 框选（**不按 Alt** 拖动 = 原版平移相机）。
  - 框选**多支部队**可一起指挥：点地块后它们分头前往该格与相邻格，**每支部队在各自格上排成和我方小队一样的紧凑队形**。
  - 船上也能选（原生单位同样可以）：点地块定下登陆集结点，落地自动前往。
  - 点海面 = 取消选中；**没选中任何单位时点地块不会移动**。
- `F1` 菜单里有一键**释放遥控**（全部小队交还原版 AI）。
- 单位集结后站在目标格上排好队形、不抽动（人多也一样），能烧房子。
- 我方小队仍按原版。
- 选项见 `badnorth.newmode.cfg`：原版敌人也可遥控（默认开），F1 菜单里可切两个接管开关；`[UI]` 段可关掉原版提示条/确认框、换字体（`FontName`，留空 = 自动用原版艺术字）、改菜单字号、面板沙色（`PanelColor`）、按键淡黄（`ButtonColor`）、开关底部头像条并调它的位置（`SquadBar / SquadBarMax / SquadBarAlign / SquadBarBottom`）。（旧 cfg 里若还留着 `TextOutline` 一行可以删掉：描边功能已移除）

## 链接与许可

- 项目地址：https://github.com/ABaLaQiYaShanMaiI/BadNorthNewMode
- 开源许可：MIT

