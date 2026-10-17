using System;
using System.Collections.Generic;
using System.Diagnostics;
using CS.Platform;
using Steamworks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Together.Net;

public sealed class SteamTransport : ITransport, IOptionalTransport
{
	private const int Channel = 7;

	private const string LobbyTag = "together_mod";

	private const string BuildKey = "together_build";

	private const string ExternalWavesKey = "together_external_waves";

	private const float CapabilityCheckInterval = 1f;

	private const byte HandshakeByte = 254;

	internal static readonly string BuildValue = "BadNorthTogether/" + global::Together.Plugin.PluginVersion;

	private Callback<GameLobbyJoinRequested_t> cbJoinRequested;

	private Callback<LobbyChatUpdate_t> cbChatUpdate;

	private Callback<P2PSessionConnectFail_t> cbConnectFail;

	private CallResult<LobbyCreated_t> crLobbyCreated;

	private CallResult<LobbyEnter_t> crLobbyEnter;

	private readonly Queue<byte[]> inbound;

	private readonly PacketFragments fragments;

	private int inboundBytes;

	private byte[] recvBuffer;

	private CSteamID lobby;

	private CSteamID peer;

	private bool peerConfirmed;

	private float nextHandshake;

	private float nextCapabilityCheck;

	public string Name => "Steam";

	public bool IsHost { get; private set; }

	public TransportState State { get; private set; }

	public string StatusText { get; private set; }

	public string PeerName { get; private set; }

	public static bool Available
	{
		get
		{
			try
			{
				return BasePlatformManager.Initialized && SteamAPI.IsSteamRunning();
			}
			catch
			{
				return false;
			}
		}
	}

	private SteamTransport()
	{
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		inbound = new Queue<byte[]>();
		fragments = new PacketFragments();
		recvBuffer = new byte[65536];
		lobby = CSteamID.Nil;
		peer = CSteamID.Nil;
		StatusText = "";
		PeerName = "";
	}

	public static SteamTransport Host()
	{
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		SteamTransport steamTransport = new SteamTransport();
		steamTransport.IsHost = true;
		steamTransport.RegisterCallbacks();
		steamTransport.State = TransportState.Connecting;
		steamTransport.StatusText = "正在创建 Steam 大厅...";
		steamTransport.crLobbyCreated.Set(SteamMatchmaking.CreateLobby((ELobbyType)1, 2), (CallResult<LobbyCreated_t>.APIDispatchDelegate)null);
		return steamTransport;
	}

	public static SteamTransport WaitForInvite()
	{
		SteamTransport steamTransport = new SteamTransport();
		steamTransport.IsHost = false;
		steamTransport.RegisterCallbacks();
		steamTransport.State = TransportState.Connecting;
		steamTransport.StatusText = "等待接受好友邀请（Shift+Tab 打开 Steam 覆盖界面）";
		return steamTransport;
	}

	private void RegisterCallbacks()
	{
		cbJoinRequested = Callback<GameLobbyJoinRequested_t>.Create((Callback<GameLobbyJoinRequested_t>.DispatchDelegate)OnJoinRequested);
		cbChatUpdate = Callback<LobbyChatUpdate_t>.Create((Callback<LobbyChatUpdate_t>.DispatchDelegate)OnChatUpdate);
		cbConnectFail = Callback<P2PSessionConnectFail_t>.Create((Callback<P2PSessionConnectFail_t>.DispatchDelegate)OnConnectFail);
		crLobbyCreated = CallResult<LobbyCreated_t>.Create((CallResult<LobbyCreated_t>.APIDispatchDelegate)OnLobbyCreated);
		crLobbyEnter = CallResult<LobbyEnter_t>.Create((CallResult<LobbyEnter_t>.APIDispatchDelegate)OnLobbyEnter);
	}

	public void OpenInviteDialog()
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		if (lobby != CSteamID.Nil)
		{
			SteamFriends.ActivateGameOverlayInviteDialog(lobby);
		}
	}

	private unsafe void OnLobbyCreated(LobbyCreated_t result, bool ioFailure)
	{
		//IL_0003: Unknown result type (might be due to invalid IL or missing references)
		//IL_0004: Unknown result type (might be due to invalid IL or missing references)
		//IL_000a: Invalid comparison between Unknown and I4
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		//IL_0036: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		if (ioFailure || (int)result.m_eResult != 1)
		{
			Fail("创建大厅失败：" + ((object)(*(EResult*)(&result.m_eResult))/*cast due to constrained. prefix*/).ToString());
			return;
		}
		lobby = new CSteamID(result.m_ulSteamIDLobby);
		SteamMatchmaking.SetLobbyData(lobby, "together_mod", 14.ToString());
		PublishCapabilities();
		StatusText = "大厅已创建，请邀请好友";
		OpenInviteDialog();
	}

	private void OnJoinRequested(GameLobbyJoinRequested_t req)
	{
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_003e: Unknown result type (might be due to invalid IL or missing references)
		if (!IsHost && State != TransportState.Connected)
		{
			StatusText = "正在加入 " + SteamFriends.GetFriendPersonaName(req.m_steamIDFriend) + " 的大厅...";
			crLobbyEnter.Set(SteamMatchmaking.JoinLobby(req.m_steamIDLobby), (CallResult<LobbyEnter_t>.APIDispatchDelegate)null);
		}
	}

	private void OnLobbyEnter(LobbyEnter_t result, bool ioFailure)
	{
		//IL_0003: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0071: Unknown result type (might be due to invalid IL or missing references)
		//IL_0076: Unknown result type (might be due to invalid IL or missing references)
		if (ioFailure || result.m_EChatRoomEnterResponse != 1)
		{
			Fail("加入大厅失败：" + result.m_EChatRoomEnterResponse);
			return;
		}
		lobby = new CSteamID(result.m_ulSteamIDLobby);
		PublishCapabilities();
		if (SteamMatchmaking.GetLobbyData(lobby, "together_mod") != 14.ToString())
		{
			Fail("大厅 Together 协议不兼容，请双方安装相同版本");
		}
		else if (!IsHost)
		{
			SetPeer(SteamMatchmaking.GetLobbyOwner(lobby));
			StatusText = "已进入大厅，正在建立 P2P 连接...";
		}
	}

	private void OnChatUpdate(LobbyChatUpdate_t upd)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_005e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0069: Unknown result type (might be due to invalid IL or missing references)
		//IL_006b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		if (upd.m_ulSteamIDLobby == lobby.m_SteamID)
		{
			CSteamID val = default(CSteamID);
			val = new CSteamID(upd.m_ulSteamIDUserChanged);
			if ((upd.m_rgfChatMemberStateChange & 1) != 0 && IsHost && val != SteamUser.GetSteamID())
			{
				SetPeer(val);
				StatusText = PeerName + " 已进入大厅，正在建立 P2P 连接...";
			}
			else if ((upd.m_rgfChatMemberStateChange & 0x1E) != 0 && val == peer)
			{
				Fail(PeerName + " 已离开");
			}
		}
	}

	private void OnConnectFail(P2PSessionConnectFail_t fail)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		if (fail.m_steamIDRemote == peer)
		{
			Fail("P2P 连接失败，错误码 " + fail.m_eP2PSessionError);
		}
	}

	private void SetPeer(CSteamID id)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		//IL_0051: Unknown result type (might be due to invalid IL or missing references)
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		if (peer != CSteamID.Nil && peer != id)
		{
			Fail("联机大厅只支持两名玩家");
			return;
		}
		peer = id;
		peerConfirmed = false;
		nextHandshake = 0f;
		PeerName = SteamFriends.GetFriendPersonaName(id);
		SteamNetworking.AcceptP2PSessionWithUser(id);
		RefreshPeerCapabilities();
	}

	private void PublishCapabilities()
	{
		if (lobby == CSteamID.Nil)
		{
			return;
		}
		SteamMatchmaking.SetLobbyMemberData(lobby, BuildKey, BuildValue);
		SteamMatchmaking.SetLobbyMemberData(lobby, ExternalWavesKey, "1");
	}

	private void RefreshPeerCapabilities()
	{
		if (lobby == CSteamID.Nil || peer == CSteamID.Nil)
		{
			global::Together.Session.PeerSupportsExternalWaves = false;
			return;
		}
		string build = SteamMatchmaking.GetLobbyMemberData(lobby, peer, BuildKey);
		string externalWaves = SteamMatchmaking.GetLobbyMemberData(lobby, peer, ExternalWavesKey);
		global::Together.Session.PeerSupportsExternalWaves = build == BuildValue && externalWaves == "1";
	}

	private void Fail(string text)
	{
		Shutdown();
		StatusText = text;
		State = TransportState.Failed;
	}

	public void Tick()
	{
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_006a: Unknown result type (might be due to invalid IL or missing references)
		//IL_00eb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ee: Unknown result type (might be due to invalid IL or missing references)
		if (State == TransportState.Idle || State == TransportState.Failed)
		{
			return;
		}
		if (Time.unscaledTime >= nextCapabilityCheck)
		{
			nextCapabilityCheck = Time.unscaledTime + CapabilityCheckInterval;
			RefreshPeerCapabilities();
		}
		fragments.Expire(Time.unscaledTime);
		if (peer != CSteamID.Nil && !peerConfirmed && Time.unscaledTime >= nextHandshake)
		{
			nextHandshake = Time.unscaledTime + 0.5f;
			byte[] array = new byte[1] { 254 };
			SteamNetworking.SendP2PPacket(peer, array, 1u, (EP2PSend)2, 7);
		}
		int num = 0;
		Stopwatch stopwatch = Stopwatch.StartNew();
		uint num2 = default(uint);
		uint num3 = default(uint);
		CSteamID val = default(CSteamID);
		while (num++ < 4096 && SteamNetworking.IsP2PPacketAvailable(out num2, 7) && ((num & 0x1F) != 0 || stopwatch.ElapsedMilliseconds < 8))
		{
			if (num2 == 0 || num2 > 1048576)
			{
				Fail("Steam 消息长度异常：" + num2);
				break;
			}
			if (num2 > recvBuffer.Length)
			{
				recvBuffer = new byte[num2];
			}
			if (!SteamNetworking.ReadP2PPacket(recvBuffer, num2, out num3, out val, 7))
			{
				break;
			}
			if (val != peer)
			{
				continue;
			}
			if (!peerConfirmed)
			{
				peerConfirmed = true;
				State = TransportState.Connected;
				StatusText = "已通过 Steam 连接 " + PeerName;
			}
			if (num3 == 1 && recvBuffer[0] == 254)
			{
				continue;
			}
			byte[] array2 = new byte[num3];
			Buffer.BlockCopy(recvBuffer, 0, array2, 0, (int)num3);
			if (array2[0] == 253)
			{
				try
				{
					array2 = fragments.Accept(array2, Time.unscaledTime);
				}
				catch (Exception ex)
				{
					Fail("Steam 分片错误：" + ex.Message);
					break;
				}
				if (array2 == null)
				{
					continue;
				}
			}
			if (inbound.Count >= 8192 || inboundBytes > 16777216 - array2.Length)
			{
				Fail("Steam 接收队列积压超限");
				break;
			}
			inbound.Enqueue(array2);
			inboundBytes += array2.Length;
		}
	}

	public bool Send(byte[] message, bool reliable)
	{
		//IL_0072: Unknown result type (might be due to invalid IL or missing references)
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		if (State != TransportState.Connected || message == null || message.Length == 0 || message.Length > 1048576)
		{
			return false;
		}
		if (!reliable && message.Length > 1200)
		{
			foreach (byte[] item in fragments.Split(message))
			{
				if (!SteamNetworking.SendP2PPacket(peer, item, (uint)item.Length, (EP2PSend)1, 7))
				{
					return false;
				}
			}
			return true;
		}
		bool num = SteamNetworking.SendP2PPacket(peer, message, (uint)message.Length, (EP2PSend)((!reliable) ? 1 : 2), 7);
		if (!num & reliable)
		{
			Fail("Steam 拒绝可靠消息，连接已关闭");
		}
		return num;
	}

	public bool TryReceive(out byte[] message)
	{
		if (inbound.Count > 0)
		{
			message = inbound.Dequeue();
			inboundBytes -= message.Length;
			return true;
		}
		message = null;
		return false;
	}

	public bool SendOptional(byte[] message)
	{
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		//IL_004c: Unknown result type (might be due to invalid IL or missing references)
		if (State != TransportState.Connected || message == null || message.Length == 0 || message.Length > 1048576)
		{
			return false;
		}
		P2PSessionState_t val = default(P2PSessionState_t);
		if (!SteamNetworking.GetP2PSessionState(peer, out val) || val.m_nPacketsQueuedForSend >= 256 || val.m_nBytesQueuedForSend > 1048576 - message.Length)
		{
			return false;
		}
		return SteamNetworking.SendP2PPacket(peer, message, (uint)message.Length, (EP2PSend)2, 7);
	}

	public void Shutdown()
	{
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		//IL_009d: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ad: Unknown result type (might be due to invalid IL or missing references)
		State = TransportState.Idle;
		global::Together.Session.PeerSupportsExternalWaves = false;
		try
		{
			if (peer != CSteamID.Nil)
			{
				SteamNetworking.CloseP2PSessionWithUser(peer);
			}
			if (lobby != CSteamID.Nil)
			{
				SteamMatchmaking.LeaveLobby(lobby);
			}
		}
		catch
		{
		}
		cbJoinRequested?.Dispose();
		cbChatUpdate?.Dispose();
		cbConnectFail?.Dispose();
		crLobbyCreated?.Dispose();
		crLobbyEnter?.Dispose();
		peer = CSteamID.Nil;
		lobby = CSteamID.Nil;
		inbound.Clear();
		inboundBytes = 0;
		fragments.Clear();
	}
}
