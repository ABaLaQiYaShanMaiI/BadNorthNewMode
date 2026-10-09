using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using UnityEngine;

namespace BadNorthNewMode
{
    /// <summary>中英切换（v1.5.1）：key = 简中原文，英译查表；缺表回退原文，绝不影响原有逻辑。</summary>
    internal static class Loc
    {
        internal static ConfigEntry<string> Language;

        /// <summary>绑定语言项：覆盖界面与日志文案（cfg 说明文案保持简中，不随之切换）。</summary>
        internal static void Bind(ConfigFile cfg)
        {
            Language = cfg.Bind("General", "Language", "auto",
                "语言 / Language：auto = 跟随游戏语言 follow the game language；zh = 简体中文；en = English。");
        }

        /// <summary>zh/en 直接指定；auto 先读游戏语言（只读私有字段、不触发初始化），读不到再按系统语言。</summary>
        internal static bool IsEnglish
        {
            get
            {
                string mode = Util.V(Language, null);
                if (string.Equals(mode, "en", System.StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(mode, "zh", System.StringComparison.OrdinalIgnoreCase)) return false;

                string raw = GameLanguage();
                if (string.IsNullOrEmpty(raw))
                {
                    if (string.IsNullOrEmpty(_systemLanguage)) _systemLanguage = Application.systemLanguage.ToString();
                    raw = _systemLanguage;
                }
                return raw.IndexOf("Chinese", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                       raw.IndexOf("中文", System.StringComparison.Ordinal) < 0;
            }
        }

        /// <summary>切换语言（写回 cfg，下一帧生效）；返回是否真的改了。</summary>
        internal static bool SetLanguage(string mode)
        {
            if (object.ReferenceEquals(Language, null)) return false;
            if (string.Equals(Language.Value, mode, System.StringComparison.OrdinalIgnoreCase)) return false;
            Language.Value = mode;
            return true;
        }

        /// <summary>简中原文 → 当前语言；缺表原样返回中文。</summary>
        internal static string T(string zh)
        {
            if (!IsEnglish || string.IsNullOrEmpty(zh)) return zh;
            string en;
            return En.TryGetValue(zh, out en) ? en : zh;
        }

        /// <summary>带占位符的简中格式串 → 当前语言后再格式化。</summary>
        internal static string F(string zhFormat, params object[] args)
        {
            return string.Format(T(zhFormat), args);
        }

        static FieldInfo _langField;
        static string _systemLanguage;

        /// <summary>只读 I2 的当前语言字段：绝不调用 LocalizationManager（那会把语言提前写进 PlayerPrefs）。</summary>
        static string GameLanguage()
        {
            try
            {
                if (object.ReferenceEquals(_langField, null))
                    _langField = typeof(I2.Loc.LocalizationManager).GetField("mCurrentLanguage",
                        BindingFlags.Static | BindingFlags.NonPublic);
                if (object.ReferenceEquals(_langField, null)) return null;
                return _langField.GetValue(null) as string;
            }
            catch { return null; }
        }

        static readonly Dictionary<string, string> En = new Dictionary<string, string>(System.StringComparer.Ordinal)
        {
            // ---- Plugin ----
            { "[NewMode] 配置绑定失败：{0}", "[NewMode] config bind failed: {0}" },
            { "[NewMode] 菜单拦截面创建失败（{0}）→ 回到旧行为", "[NewMode] failed to create the menu click shield ({0}) -> back to the old behaviour" },
            { "自定义模式（无尽）：本关只有你投放的单位，不会自然结束 —— 按 F3 强制胜利退出（{0} 波原版波次已拦下）", "Custom mode (endless): only your dropped units exist and the battle never ends by itself -- press F3 to force the win and leave ({0} vanilla wave(s) blocked)" },
            { "[NewMode][接管] 已拦下原版波次：{0} 波不再发射（占位一并放行）。本关不会自然结束，退出请按 F3 强制胜利。", "[NewMode][takeover] vanilla waves blocked: {0} wave(s) will never launch (their beach slots are freed). This battle never ends by itself - press F3 to force the win." },
            { "EndOfLevel 未就绪", "EndOfLevel not ready" },
            { "本关已经结束过了", "this battle has already ended" },
            { "强制胜利：已走原版胜利流程", "Force win: the vanilla victory flow has been invoked" },
            { "[NewMode][接管] 强制胜利：EndOfLevel.AllVikingsKilled()（原版胜利流程）", "[NewMode][takeover] force win: EndOfLevel.AllVikingsKilled() (vanilla victory flow)" },
            { "[NewMode] v{0} 已加载：{1} 开关投放模式，点击滩头陆地投放敌舰。", "[NewMode] v{0} loaded: {1} toggles drop mode; click beachhead land to drop enemy ships." },
            { "(热键未绑定)", "(hotkey not bound)" },
            { "已取消投放", "Drop cancelled" },
            { "（指针在菜单上）", "(pointer is over the menu)" },
            { "指针不在陆地上（{0}）", "Pointer is not on land ({0})" },
            { "滩头可用：落差 {0:F2}m，距点击处 {1:F1}m", "Beachhead OK: height offset {0:F2}m, {1:F1}m from the click" },
            { "轮询兜底", "polling fallback" },
            { "那里不是可站立的地面：{0}", "Not walkable ground: {0}" },
            { "那里不是可站立的陆地地块", "That is not a walkable land tile" },
            { "[NewMode][遥控] ", "[NewMode][remote] " },
            { "[NewMode] 找不到 onClick 事件或回调方法 → 改用轮询兜底。", "[NewMode] onClick event or callback not found -> falling back to polling." },
            { "[NewMode] 已反射订阅原版世界点击事件 pointerRationalizer.onClick（同 Navigator/ConfirmButton 的数据源）。", "[NewMode] subscribed to the vanilla world-click event pointerRationalizer.onClick via reflection (same source as Navigator/ConfirmButton)." },
            { "[NewMode] 订阅 onClick 失败：{0} → 改用轮询兜底。", "[NewMode] failed to subscribe to onClick: {0} -> falling back to polling." },
            { "[NewMode] 菜单内点击 → 忽略投放", "[NewMode] click inside the menu -> drop ignored" },
            { "[NewMode][点击] ", "[NewMode][click] " },
            { "游戏事件", "game event" },
            { "[NewMode][点击] 处理异常：{0}", "[NewMode][click] handler exception: {0}" },
            { "[NewMode][点击] 屏幕 ({0:F0},{1:F0}) 来源={2}", "[NewMode][click] screen ({0:F0},{1:F0}) source={2}" },
            { "[NewMode][点击] 地形未命中：{0}", "[NewMode][click] terrain miss: {0}" },
            { "这一点不是陆地地块：{0}", "This point is not a land tile: {0}" },
            { "[NewMode][点击] 命中地形：{0}，点 {1}，海拔 {2:F2}m", "[NewMode][click] terrain hit: {0}, point {1}, height {2:F2}m" },
            { "[NewMode][点击] 无法投放：{0}", "[NewMode][click] cannot drop: {0}" },
            { "无法投放：{0}", "Cannot drop: {0}" },
            { "[NewMode][点击] 投放失败：{0}", "[NewMode][click] drop failed: {0}" },
            { "投放失败：{0}", "Drop failed: {0}" },
            { "不在战局中", "not in a battle" },
            { "已暂停", "paused" },
            { "岛屿未就绪", "island not ready" },
            { "岛屿未进入 Playing 状态", "the island is not in the Playing state" },
            { "Raid 未就绪", "Raid not ready" },
            { "NavSpotCast 命中 {0}", "NavSpotCast hit {0}" },
            { "NavSpotCast 未命中", "NavSpotCast missed" },
            { "NavSpotCast 异常 {0}", "NavSpotCast exception {0}" },
            { "navSpotter 不可用", "navSpotter unavailable" },
            { "；Voxels 层命中 {0}", "; Voxels layer hit {0}" },
            { "；任意碰撞体命中 {0}", "; any collider hit {0}" },
            { "；射线仍未命中（mask={0}，vp={1:F3},{2:F3}）", "; the ray still missed (mask={0}, vp={1:F3},{2:F3})" },
            // ---- IngameMenu ----
            { "投放菜单：左键点兵种选择，再点滩头陆地投放（F1 关闭 / F2 清场 / F3 强制胜利）", "Drop menu: left-click a unit type, then click beachhead land to drop (F1 close / F2 cleanup / F3 force win)" },
            { "已关闭投放菜单", "Drop menu closed" },
            { "BadNorthNewMode · 投放菜单（左键点兵种 → 再点滩头陆地投放；右键或 Esc 关闭）", "BadNorthNewMode · Drop menu (left-click a unit type -> click beachhead land; right-click or Esc to close)" },
            { "\n遥控小队：{0}（共 {1}）", "\nRemote squads: {0} (total {1})" },
            { "\n非原生单位 {0}{1}", "\nForeign units {0}{1}" },
            { ", 已选中 ", ", selected " },
            { "\n框选中…（按兵种自动分队，每队上限 {0}）", "\nBox selecting... (auto-grouped by unit type, max {0} per squad)" },
            { "\n待登陆集结：{0} 人 / {1} 处（落地后自动前往）", "\nBoarding rally: {0} unit(s) / {1} point(s) (they head there right after landing)" },
            { "语言", "Language" },
            { "语言已切换（立即生效）", "Language switched (applies immediately)" },
            { "兵种（左键点选；括号内为 cfg 内部名）", "Unit type (left-click; cfg name in parentheses)" },
            { "当前：", "Current: " },
            { "随机", "Random" },
            { "无", "none" },
            { "（进入战局后才会列出可用兵种）", "(unit types are listed once you are in a battle)" },
            { "{0}{1}. {2}（{3}）", "{0}{1}. {2} ({3})" },
            { "数量（左键点击）", "Count (left-click)" },
            { "单键：点单位 = 选中｜双击 = 整队｜有选中时点地块 = 前进", "One-button: click = select one | double-click = whole squad | with a selection, click a tile = advance" },
            { "双键：左键点单位 = 选中｜双击 = 整队｜右键点地块 = 前进", "Two-button: left-click = select one | double-click = whole squad | right-click a tile = advance" },
            { "Shift + 点 = 并入｜R = 全选｜Alt + 拖动 = 框选", "Shift + click = merge | R = select all | Alt+drag = box select" },
            { "（已取消选择）", " (selection cleared)" },
            { "船上也能选（落地自动去集结点）｜F2 清场 · F3 强制胜利", "Aboard units can be selected too (they go to the rally point after landing) | F2 cleanup · F3 force win" },
            { "\n无尽自定义模式：本关不会自然结束 —— 按 F3 强制胜利退出", "\nEndless custom mode: this battle never ends by itself -- press F3 to force the win and leave" },
            { "\n原生单位 {0}（可遥控）", "\nNative units {0} (remote-controllable)" },
            { "原版波次：拦下", "Vanilla waves: blocked" },
            { "原版波次：正常", "Vanilla waves: normal" },
            { "原生单位：可遥控", "Native units: controllable" },
            { "原生单位：不可", "Native units: not controllable" },
            { "已切换：{0}", "Toggled: {0}" },
            { "没有选中任何单位：先点一个单位选中它", "nothing selected: click a unit to select it first" },
            { "已选择兵种：{0}", "Unit type selected: {0}" },
            { "[NewMode] 已选择兵种：{0}", "[NewMode] unit type selected: {0}" },
            { "已设定数量：{0}（超出船容量会自动裁剪）", "Count set to {0} (clamped to ship capacity)" },
            { "[NewMode] 数量设定：{0}", "[NewMode] count set: {0}" },
            { "[NewMode] 菜单：本关可用兵种 {0} 种", "[NewMode] menu: {0} unit types available on this island" },

            // ---- DropPlanner ----
            { "Raid / landingContainer 未就绪", "Raid / landingContainer not ready" },
            { "敌人生成池为空", "the enemy spawn pool is empty" },
            { "possibleShips 为空", "possibleShips is empty" },
            { "Beaches 未就绪", "Beaches not ready" },
            { "没有可用的 VikingReference", "no usable VikingReference" },
            { "没有可用长船", "no usable longship" },
            { "附近与全岛的滩头进近廊道都被地形/建筑挡住了——换个位置点", "approach corridors to nearby and island-wide beachheads are blocked by terrain/buildings - click somewhere else" },
            { "附近的滩头都被船占着（最近一艘 {0} 离 {1:F1}m，本船需要 ≥{2:F1}m），全岛也没有空位", "nearby beachheads are taken (nearest ship {0} at {1:F1}m, this ship needs >={2:F1}m) and the island has no free spot" },
            { "[NewMode] 候选 {0} 个，选中第 {1} 个（阈值 {2:F1}m{3}{4}）", "[NewMode] {0} candidates, picked #{1} (threshold {2:F1}m{3}{4})" },
            { "，已放宽间距", ", spacing relaxed" },
            { "，已改用全岛最近空滩头", ", switched to the nearest free beachhead island-wide" },
            { "这里是高地/悬崖（海拔 {0:F2}m，上限 {1:F2}m）——请点与海面齐平的滩头", "this is high ground/cliff (height {0:F2}m, limit {1:F2}m) - click a beachhead level with the sea" },
            { "本关没有可用滩头", "no usable beachhead on this island" },
            { "附近没有与海面齐平的滩头（最近 {0:F1}m，上限 {1:F1}m；或岸线余量不足）", "no beachhead level with the sea nearby (nearest {0:F1}m, limit {1:F1}m; or the shoreline margin is too small)" },
            { "已放置的船", "placed ship" },
            // ---- LandingInjector ----
            { "[NewMode] 点击陆地 {0}（海拔 {1:F2}m）→ 滩头 {2} 距离 {3:F2}m 船 {4}", "[NewMode] land click {0} (height {1:F2}m) -> beachhead {2}, distance {3:F2}m, ship {4}" },
            { "附近 {0} 个滩头都被地形/建筑挡住或被占用，换个位置点", "all {0} nearby beachheads are blocked or occupied - click somewhere else" },
            { "[NewMode] 船员异常：{0}（疑似船体叠加，请反馈此日志）", "[NewMode] crew anomaly: {0} (possible ship overlap; please report this log)" },
            { "[NewMode] 采用第 {0} 个候选滩头 {1}（距点击处 {2:F2}m）", "[NewMode] using candidate beachhead #{0} {1} ({2:F2}m from the click)" },
            { "已投放 {0} ×{1}（船 {2}，滩头离点击处 {3:F1}m{4}{5}）", "dropped {0} x{1} (ship {2}, beachhead {3:F1}m from the click{4}{5})" },
            { "，附近满员 → 改用最近空滩头", ", nearby is full -> using the nearest free beachhead" },
            { "[NewMode] 已装配 {0} 个敌人（order→Pirate + 舰上威胁）", "[NewMode] attached behaviours to {0} enemies (order->Pirate + shipboard threat)" },
            { "应有 {0}，实际混入 {1} 个其他单位（如 {2}）", "expected {0} but {1} other unit(s) mixed in (e.g. {2})" },

            // ---- FlotillaLauncher / ForeignUnit ----
            { "[NewMode] 编队出发：{0} 艘（一条接近音乐）", "[NewMode] flotilla launched: {0} ship(s) (one approach track)" },
            { "非原生单位", "foreign unit" },

            // ---- DisembarkWatchdog ----
            { "[NewMode][下船] 到岸后仍未下船：interpolator={0:F3} landed={1} enabled={2} agents={3} haveAllSpawned={4} animator={5}", "[NewMode][disembark] still aboard after landing: interpolator={0:F3} landed={1} enabled={2} agents={3} haveAllSpawned={4} animator={5}" },
            { "有/enabled=", "yes/enabled=" },
            { "[NewMode][下船]   敌 {0}：已销毁", "[NewMode][disembark]   enemy {0}: destroyed" },
            { "[NewMode][下船]   敌 {0}：navPos.island={1} orderDist={2:F3} spawned={3} pirate={4} brainActions={5}(含Pirate={6}) brainOrder={7}(是Pirate={8})", "[NewMode][disembark]   enemy {0}: navPos.island={1} orderDist={2:F3} spawned={3} pirate={4} brainActions={5}(hasPirate={6}) brainOrder={7}(isPirate={8})" },
            { "[NewMode][下船]   该敌人没有 Pirate 组件，跳过（无法走原版下船逻辑）", "[NewMode][disembark]   this enemy has no Pirate component, skipping (cannot use the vanilla disembark path)" },
            { "[NewMode][下船] 兜底下船：把 {0} 个敌人移下船（走原版 RemoveFromShip）", "[NewMode][disembark] fallback disembark: moved {0} enemies off the ship (via the vanilla RemoveFromShip)" },

            // ---- LogFileSwitch ----
            { "[NewMode] 文件日志默认关闭（[Diag] LogToFile = false）：本次不写 BepInEx\\{0}。需要排查时把它设成 true 再启动。", "[NewMode] file logging is off by default ([Diag] LogToFile = false): BepInEx\\{0} will not be written this session. Set it to true and restart when you need to troubleshoot." },
            { "[NewMode] 关闭文件日志失败（不影响游戏）：{0}", "[NewMode] failed to disable file logging (harmless to the game): {0}" },
            { "[NewMode] 已删除启动残留：{0}", "[NewMode] deleted the startup remnant: {0}" },
            { "[NewMode] 暂时删不掉日志残留（{0}）：{1}", "[NewMode] cannot delete the log remnant yet ({0}): {1}" },
            { "[NewMode] 仍未能删除日志残留（{0}）；若不需要日志文件，可把 BepInEx.cfg 的 [Logging.Disk] Enabled 设为 false。", "[NewMode] still could not delete the log remnant ({0}); if you don't want a log file, set [Logging.Disk] Enabled = false in BepInEx.cfg." },
            // ---- MarqueeSelect ----
            { "[NewMode][遥控] 你选中了我方小队 → 已清空遥控选择", "[NewMode][remote] you selected one of your own squads -> remote selection cleared" },
            { "[NewMode][遥控] 开始框选", "[NewMode][remote] box select started" },
            { "[NewMode][遥控] 原版接管了这次点击 → 放弃遥控下令", "[NewMode][remote] the vanilla game took this click -> remote order dropped" },
            { "已清空选择", "Selection cleared" },
            { "已选中 {0}（点地块 = 前进；双击单位 = 选整队）", "Selected {0} (click a tile to advance; double-click a unit to select its whole squad)" },
            { "船上已选中 {0} 人：登陆后自动前往集结点", "Selected {0} unit(s) aboard: they will head to the rally point right after landing" },
            { "登陆单位已就位 → {0}", "landed units are ready -> {0}" },
            { "没有可下令的单位", "no unit to command" },
            { "[NewMode] 点击路由已启用：左键选整队、右键前进（自动跟随单/双键设置）。", "[NewMode] click routing enabled: left-click selects a squad, right-click advances (follows the one/two-button setting)." },
            { "[NewMode] 点击路由异常：{0}", "[NewMode] click routing error: {0}" },
            { "[NewMode] 点击处理异常：{0}", "[NewMode] click handling error: {0}" },
            { "框里没有非原生单位", "no foreign units inside the box" },
            { "已选中 {0} 个单位（点地块 = 成队并前进）", "Selected {0} unit(s) (click a tile to group and advance)" },
            { "[NewMode][遥控] {0}（矩形 {1:F0}×{2:F0}；登记 {3}，可用 {4}，命中 {5}；最近 {6}）", "[NewMode][remote] {0} (rect {1:F0}x{2:F0}; tracked {3}, usable {4}, hit {5}; nearest {6})" },
            { "没有可选单位（可能都还没生成或已阵亡）", "no selectable units (they may not have spawned yet or are already dead)" },
            { "已全选 {0}（右键点地块 = 前进）", "Selected all {0} (right-click a tile to advance)" },
            { "相机不可用", "camera unavailable" },
            { "无（没有可用的非原生单位）", "none (no usable foreign units)" },
            { "{0:F0}px（屏 {1:F0},{2:F0} z={3:F1}｜鼠标 {4:F0},{5:F0}｜屏幕 {6}×{7}｜相机 {8}）", "{0:F0}px (screen {1:F0},{2:F0} z={3:F1} | mouse {4:F0},{5:F0} | display {6}x{7} | camera {8})" },
            { "[NewMode][遥控] 减速中（框选/已选中）", "[NewMode][remote] slowing time (boxing/selected)" },
            { "[NewMode][遥控] 改用相机 {0}（与已知点误差 {1:F0}px；原 {2}）", "[NewMode][remote] switching to camera {0} (error {1:F0}px vs a known point; was {2})" },

            // ---- RemoteGroup ----
            { "、", ", " },
            { "（", " (" },
            { "）", ")" },
            { "没框到非原生单位", "no foreign units were boxed" },
            { "没框到可用的非原生单位", "no usable foreign units were boxed" },
            { "已接管 {0}{1}（点地块 = 前进；再点/再框同兵种可并入）", "Took control of {0}{1} (click a tile to advance; click/box the same unit type again to merge)" },
            { "；另有 {0} 个超过每队上限 {1}，保持原逻辑", "; {0} more exceeded the per-squad cap {1} and keep their original logic" },
            { "还没有遥控小队：先用左键点一个单位选中整队", "no remote squads yet: left-click a unit to select its squad" },
            { "遥控小队前进：{0}", "remote squads advancing: {0}" },
            { "遥控小队已全部阵亡 / 消失", "all remote squads are dead / gone" },
            { "换岛：已清空遥控小队", "island changed: remote squads cleared" },
            { "战局结束：已清空遥控小队", "battle ended: remote squads cleared" },

            // ---- SpawnLedger ----
            { "[NewMode][清理] 手动清场：销毁 {0} 组投放对象", "[NewMode][cleanup] manual cleanup: destroyed {0} dropped object group(s)" },
            { "[NewMode][清理] 离开战局：已清除本 mod 投放的 {0} 组残留（对齐原版 IIslandWipe）", "[NewMode][cleanup] left the battle: cleared {0} remnant group(s) dropped by this mod (matching vanilla IIslandWipe)" },
            { "[NewMode] 已订阅原版战局结束事件 EndOfLevel.postProcess（用于自动清场）。", "[NewMode] subscribed to the vanilla end-of-battle event EndOfLevel.postProcess (for automatic cleanup)." },
            { "[NewMode][清理] 重开战局：已清空上一局的 {0} 组投放对象", "[NewMode][cleanup] battle restarted: cleared {0} dropped object group(s) from the previous attempt" },
            { "[NewMode][清理] 战局结束（{0}）：已清除本 mod 投放的 {1} 组对象", "[NewMode][cleanup] battle ended ({0}): cleared {1} object group(s) dropped by this mod" },
            { "[NewMode][清理] 战局结束清理异常：{0}", "[NewMode][cleanup] exception while cleaning up at the end of the battle: {0}" },

            // ---- UnitCatalog ----
            { "[NewMode] \"{0}\" 不在本关生成池，改用全局引用字典里的同一单位。", "[NewMode] \"{0}\" is not in this island's spawn pool; using the same unit from the global reference dictionary." },
            { "[NewMode] cfg EnemyName=\"{0}\" 既不在生成池也不在引用字典，退回随机。", "[NewMode] cfg EnemyName=\"{0}\" is neither in the spawn pool nor the reference dictionary; falling back to random." },
        };
    }
}
