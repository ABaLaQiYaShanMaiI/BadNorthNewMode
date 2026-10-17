using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Steamworks;
using Together.Net;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Together;

[BepInPlugin("com.linfeng.together", "BadNorthTogether", "0.6.14")]
[BepInDependency("com.antigravity.firearrows", (BepInDependency.DependencyFlags)2)]
[BepInDependency("com.linfeng.shield_spear", (BepInDependency.DependencyFlags)2)]
[BepInDependency("com.modder.guerrilla_archery", (BepInDependency.DependencyFlags)2)]
[BepInDependency("com.antigravity.sage_time_freeze", (BepInDependency.DependencyFlags)2)]
[BepInDependency("com.modder.giant_command_ring", (BepInDependency.DependencyFlags)2)]
[DefaultExecutionOrder(10000)]
public sealed class Plugin : BaseUnityPlugin
{
	public const string PluginGuid = "com.linfeng.together";

	public const string PluginName = "BadNorthTogether";

	public const string PluginVersion = "0.6.14";

	internal static string LocalName = "Player";

	private static ConfigEntry<int> tcpPort;

	private static ConfigEntry<string> tcpJoinIp;

	private static ConfigEntry<string> playerName;

	private static ConfigEntry<KeyCode> toggleKey;

	private bool uiVisible;

	private Rect windowRect;

	private string portField;

	private string ipField;

	private string nameField;

	private Harmony harmony;

	private bool backgroundOverride;

	private bool previousRunInBackground;

	internal static ManualLogSource Log { get; private set; }

	private void Awake()
	{
		//IL_0184: Unknown result type (might be due to invalid IL or missing references)
		//IL_018e: Expected O, but got Unknown
		//IL_01a8: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ad: Unknown result type (might be due to invalid IL or missing references)
		Log = base.Logger;
		tcpPort = base.Config.Bind<int>("TCP", "Port", 25599, "TCP 直连端口（主机监听 / 客户端连接）");
		tcpJoinIp = base.Config.Bind<string>("TCP", "JoinIp", "127.0.0.1", "TCP 直连时要连接的主机 IP");
		playerName = base.Config.Bind<string>("Player", "Name", "", "玩家名；留空则使用 Steam 昵称");
		toggleKey = base.Config.Bind<KeyCode>("UI", "ToggleKey", (KeyCode)290, "打开/关闭联机调试窗口");
		portField = tcpPort.Value.ToString();
		ipField = tcpJoinIp.Value;
		nameField = playerName.Value;
		Session.Connected += delegate
		{
			Log.LogInfo((object)("[Plugin] 已连接：" + Session.Transport.StatusText));
		};
		Session.Stopped += delegate
		{
			Log.LogInfo((object)"[Plugin] 会话已结束");
		};
		Session.Connected += EnableBackgroundRun;
		Session.Stopped += RestoreBackgroundRun;
		CampaignSync.Initialize();
		SquadCommands.Initialize();
		PauseSync.Initialize();
		TempoSync.Initialize();
		DeploySync.Initialize();
		AgentAuthority.Initialize();
		WaveSync.Initialize();
		WorldAuthority.Initialize();
		AbilityCommands.Initialize();
		CombatEvents.Initialize();
		SurfaceMarks.Initialize();
		SceneDecorations.Initialize();
		FireVisuals.Initialize();
		harmony = new Harmony("com.linfeng.together");
		harmony.PatchAll();
		Log.LogInfo((object)("[Plugin] BadNorthTogether v0.6.14 已加载，按 " + ((object)toggleKey.Value/*cast due to constrained. prefix*/).ToString() + " 打开联机窗口"));
	}

	private void Update()
	{
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			if (Input.GetKeyDown(toggleKey.Value))
			{
				uiVisible = !uiVisible;
			}
			Session.Tick();
			CampaignSync.Tick();
			SquadCommands.Tick();
			PauseSync.Tick();
			TempoSync.Tick();
			DeploySync.Tick();
			AgentAuthority.Tick();
			WaveSync.Tick();
			WorldAuthority.Tick();
			AbilityCommands.Tick();
		}
		catch (Exception ex)
		{
			Log.LogError((object)("[Plugin] Update 出错：" + ex));
			if (Session.Active || AgentAuthority.ClientPlayback)
			{
				AgentAuthority.Fail(ex.Message);
			}
		}
	}

	private void LateUpdate()
	{
		try
		{
			WorldAuthority.LateTick();
			TickCosmetic(SceneDecorations.LateTick, "corpses");
			AgentAuthority.LateTick();
			TickCosmetic(SurfaceMarks.LateTick, "marks");
			TickCosmetic(FireVisuals.LateTick, "fire");
			TickCosmetic(CombatEvents.LateTick, "combat");
			TickCosmetic(AbilityCommands.LateTick, "ability");
		}
		catch (Exception ex)
		{
			Log.LogError((object)("[Plugin] 表现播放出错：" + ex));
			if (Session.Active || AgentAuthority.ClientPlayback)
			{
				AgentAuthority.Fail(ex.Message);
			}
		}
	}

	private static void TickCosmetic(Action tick, string key)
	{
		try
		{
			tick();
		}
		catch (Exception ex)
		{
			SceneVisuals.Warn("tick-" + key, "装饰更新失败，保留权威战斗：" + ex.Message);
		}
	}

	private void OnDestroy()
	{
		Session.Stop();
		AgentAuthority.Reset();
		WorldAuthority.Reset();
		AbilityCommands.Reset();
		CombatEvents.Reset();
		RestoreBackgroundRun();
		if (harmony != null)
		{
			harmony.UnpatchSelf();
		}
	}

	private void EnableBackgroundRun()
	{
		if (!backgroundOverride)
		{
			previousRunInBackground = Application.runInBackground;
			Application.runInBackground = true;
			backgroundOverride = true;
			Log.LogInfo((object)"[暂停] 联机期间允许游戏在后台继续运行");
		}
	}

	private void RestoreBackgroundRun()
	{
		if (backgroundOverride)
		{
			Application.runInBackground = previousRunInBackground;
			backgroundOverride = false;
			Log.LogInfo((object)"[暂停] 已恢复原游戏后台运行设置");
		}
	}

	private void OnGUI()
	{
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		//IL_0060: Expected O, but got Unknown
		//IL_005b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		if (uiVisible)
		{
			windowRect = GUILayout.Window(7365095, windowRect, new GUI.WindowFunction(DrawWindow), "BadNorthTogether 联机 v0.6.14  [" + ((object)toggleKey.Value/*cast due to constrained. prefix*/).ToString() + "]", (GUILayoutOption[])(object)new GUILayoutOption[1] { GUILayout.MinWidth(360f) });
		}
	}

	private void DrawWindow(int id)
	{
		ITransport transport = Session.Transport;
		if (transport == null)
		{
			DrawIdle();
		}
		else
		{
			DrawSession(transport);
		}
		GUI.DragWindow();
	}

	private void DrawIdle()
	{
		GUILayout.Label("玩家名（留空用 Steam 昵称）：", (GUILayoutOption[])(object)new GUILayoutOption[0]);
		nameField = GUILayout.TextField(nameField, (GUILayoutOption[])(object)new GUILayoutOption[0]);
		GUILayout.Space(6f);
		GUILayout.Label("—— Steam（正式联机）——", (GUILayoutOption[])(object)new GUILayoutOption[0]);
		if (SteamTransport.Available)
		{
			GUILayout.BeginHorizontal((GUILayoutOption[])(object)new GUILayoutOption[0]);
			if (GUILayout.Button("创建大厅并邀请", (GUILayoutOption[])(object)new GUILayoutOption[0]))
			{
				Begin(SteamTransport.Host());
			}
			if (GUILayout.Button("等待好友邀请", (GUILayoutOption[])(object)new GUILayoutOption[0]))
			{
				Begin(SteamTransport.WaitForInvite());
			}
			GUILayout.EndHorizontal();
			GUILayout.Label("客户端需先点\"等待好友邀请\"，再在 Steam 里接受邀请。", (GUILayoutOption[])(object)new GUILayoutOption[0]);
		}
		else
		{
			GUILayout.Label("Steam 未初始化，不可用", (GUILayoutOption[])(object)new GUILayoutOption[0]);
		}
		GUILayout.Space(6f);
		GUILayout.Label("—— TCP 直连（调试）——", (GUILayoutOption[])(object)new GUILayoutOption[0]);
		GUILayout.BeginHorizontal((GUILayoutOption[])(object)new GUILayoutOption[0]);
		GUILayout.Label("IP", (GUILayoutOption[])(object)new GUILayoutOption[1] { GUILayout.Width(40f) });
		ipField = GUILayout.TextField(ipField, (GUILayoutOption[])(object)new GUILayoutOption[0]);
		GUILayout.EndHorizontal();
		GUILayout.BeginHorizontal((GUILayoutOption[])(object)new GUILayoutOption[0]);
		GUILayout.Label("端口", (GUILayoutOption[])(object)new GUILayoutOption[1] { GUILayout.Width(40f) });
		portField = GUILayout.TextField(portField, (GUILayoutOption[])(object)new GUILayoutOption[0]);
		GUILayout.EndHorizontal();
		GUILayout.BeginHorizontal((GUILayoutOption[])(object)new GUILayoutOption[0]);
		if (GUILayout.Button("作为主机监听", (GUILayoutOption[])(object)new GUILayoutOption[0]) && TryPort(out var port))
		{
			Begin(TcpTransport.Host(port));
		}
		if (GUILayout.Button("连接主机", (GUILayoutOption[])(object)new GUILayoutOption[0]) && TryPort(out var port2))
		{
			tcpJoinIp.Value = ipField.Trim();
			Begin(TcpTransport.Join(tcpJoinIp.Value, port2));
		}
		GUILayout.EndHorizontal();
	}

	private void DrawSession(ITransport t)
	{
		GUILayout.Label("方式：" + t.Name + (t.IsHost ? "（主机）" : "（客户端）"), (GUILayoutOption[])(object)new GUILayoutOption[0]);
		GUILayout.Label("状态：" + t.State.ToString() + " - " + t.StatusText, (GUILayoutOption[])(object)new GUILayoutOption[0]);
		GUILayout.Label("会话：" + Session.StatusText, (GUILayoutOption[])(object)new GUILayoutOption[0]);
		GUILayout.Label("战役：" + CampaignSync.StatusText, (GUILayoutOption[])(object)new GUILayoutOption[0]);
		if (t.State == TransportState.Connected)
		{
			GUILayout.Label("部署：" + DeploySync.StatusText, (GUILayoutOption[])(object)new GUILayoutOption[0]);
			GUILayout.Label("战斗：" + AgentAuthority.StatusText, (GUILayoutOption[])(object)new GUILayoutOption[0]);
			GUILayout.Label(AgentAuthority.LifeStatus, (GUILayoutOption[])(object)new GUILayoutOption[0]);
			GUILayout.Label("技能：" + AbilityCommands.StatusText, (GUILayoutOption[])(object)new GUILayoutOption[0]);
		}
		if (t.State == TransportState.Connected)
		{
			string text = ((Session.PeerName != "") ? Session.PeerName : t.PeerName);
			GUILayout.Label("对方：" + text + "    延迟：" + Session.LastPingMs.ToString("0") + " ms", (GUILayoutOption[])(object)new GUILayoutOption[0]);
			if (Session.PeerVersionMismatch)
			{
				GUILayout.Label("警告：双方协议版本不一致，请安装同一版本的 mod", (GUILayoutOption[])(object)new GUILayoutOption[0]);
			}
		}
		if (t is SteamTransport { IsHost: not false } steamTransport && t.State == TransportState.Connecting && GUILayout.Button("重新打开邀请界面", (GUILayoutOption[])(object)new GUILayoutOption[0]))
		{
			steamTransport.OpenInviteDialog();
		}
		GUILayout.Space(6f);
		if (GUILayout.Button((t.State == TransportState.Connected) ? "断开" : "取消 / 关闭", (GUILayoutOption[])(object)new GUILayoutOption[0]))
		{
			Session.Stop();
		}
	}

	private bool TryPort(out int port)
	{
		if (int.TryParse(portField.Trim(), out port) && port > 0 && port < 65536)
		{
			tcpPort.Value = port;
			return true;
		}
		Log.LogWarning((object)("[Plugin] 端口无效：" + portField));
		return false;
	}

	private void Begin(ITransport transport)
	{
		playerName.Value = nameField.Trim();
		LocalName = ResolveName(playerName.Value);
		Session.Start(transport);
	}

	private static string ResolveName(string configured)
	{
		if (configured != "")
		{
			return configured;
		}
		if (SteamTransport.Available)
		{
			try
			{
				return SteamFriends.GetPersonaName();
			}
			catch
			{
			}
		}
		return "Player";
	}

	public Plugin()
	{
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		windowRect = new Rect(20f, 20f, 360f, 0f);
	}
}
