using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Together.Net;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Together;

public static class Session
{
	private static readonly Dictionary<MsgType, Action<BinaryReader>> handlers;

	private static Guid localNonce;

	private static Guid peerNonce;

	private static bool helloSent;

	private static bool handshakeComplete;

	private static bool stoppedRaised;

	private static float helloAt;

	private static float nextPing;

	private static float lastReceived;

	private static float pingSentAt;

	private static int pingSequence;

	private static int optionalCount;

	private static int optionalBytes;

	private static float optionalWindow;

	public static bool ApplyingRemote;

	public static bool OptionalEnabled { get; private set; }

	public static ITransport Transport { get; private set; }

	public static string PeerName { get; private set; }

	public static string StatusText { get; private set; }

	public static bool PeerVersionMismatch { get; private set; }

	public static bool PeerSupportsExternalWaves { get; internal set; }

	public static float LastPingMs { get; private set; }

	public static bool Active
	{
		get
		{
			if (handshakeComplete && Transport != null)
			{
				return Transport.State == TransportState.Connected;
			}
			return false;
		}
	}

	public static bool IsHost
	{
		get
		{
			if (Active)
			{
				return Transport.IsHost;
			}
			return false;
		}
	}

	public static bool IsClient
	{
		get
		{
			if (Active)
			{
				return !Transport.IsHost;
			}
			return false;
		}
	}

	public static event Action OptionalStopped;

	public static event Action Connected;

	public static event Action Stopped;

	static Session()
	{
		handlers = new Dictionary<MsgType, Action<BinaryReader>>();
		OptionalEnabled = true;
		PeerName = "";
		StatusText = "未连接";
		On(MsgType.OptionalVisualStop, ReceiveOptionalStop);
	}

	public static void On(MsgType type, Action<BinaryReader> handler)
	{
		handlers[type] = handler;
	}

	public static void Start(ITransport transport)
	{
		if (transport == null)
		{
			throw new ArgumentNullException("transport");
		}
		Stop();
		Transport = transport;
		localNonce = Guid.NewGuid();
		peerNonce = Guid.Empty;
		helloSent = (handshakeComplete = (stoppedRaised = false));
		ApplyingRemote = false;
		PeerName = "";
		PeerVersionMismatch = false;
		PeerSupportsExternalWaves = false;
		LastPingMs = 0f;
		nextPing = 0f;
		StatusText = "等待传输连接";
		ResetOptional();
		Plugin.Log.LogInfo((object)("[Session] 启动 " + transport.Name + (transport.IsHost ? "（主机）" : "（客户端）")));
	}

	public static void Stop()
	{
		if (Transport != null)
		{
			ITransport transport = Transport;
			Transport = null;
			handshakeComplete = false;
			ApplyingRemote = false;
			PeerSupportsExternalWaves = false;
			transport.Shutdown();
			StatusText = "已断开";
			RaiseStopped();
		}
	}

	public static void Fail(string reason)
	{
		if (Transport != null && !stoppedRaised)
		{
			handshakeComplete = false;
			ApplyingRemote = false;
			PeerSupportsExternalWaves = false;
			StatusText = reason;
			Plugin.Log.LogError((object)("[Session] " + reason));
			Transport.Shutdown();
			RaiseStopped();
		}
	}

	private static void RaiseStopped()
	{
		if (!stoppedRaised)
		{
			stoppedRaised = true;
			Plugin.Log.LogInfo((object)("[Session] 会话结束：" + StatusText));
			InvokeSafely(Stopped);
		}
	}

	private static void InvokeSafely(Action actions)
	{
		if (actions == null)
		{
			return;
		}
		Delegate[] invocationList = actions.GetInvocationList();
		for (int i = 0; i < invocationList.Length; i++)
		{
			Action action = (Action)invocationList[i];
			try
			{
				action();
			}
			catch (Exception ex)
			{
				Plugin.Log.LogError((object)("[Session] 状态回调异常：" + ex));
			}
		}
	}

	public static bool Send(byte[] message, bool reliable = true)
	{
		if (!Active)
		{
			return false;
		}
		Protocol.ValidateMessage(message);
		if (message.Length > 1048543)
		{
			throw new IOException("业务消息超过会话长度上限");
		}
		using MessageWriter messageWriter = Protocol.Begin(MsgType.SessionData);
		messageWriter.Write(localNonce.ToByteArray());
		messageWriter.Write(peerNonce.ToByteArray());
		messageWriter.Write(message);
		return Transport.Send(messageWriter.ToArray(), reliable);
	}

	public static void ResetOptional()
	{
		OptionalEnabled = true;
		optionalCount = (optionalBytes = 0);
		optionalWindow = Time.unscaledTime;
	}

	public static bool SendOptional(byte[] message)
	{
		if (!Active || !OptionalEnabled)
		{
			return false;
		}
		Protocol.ValidateMessage(message);
		float unscaledTime = Time.unscaledTime;
		if (unscaledTime - optionalWindow >= 1f)
		{
			optionalWindow = unscaledTime;
			optionalCount = (optionalBytes = 0);
		}
		IOptionalTransport optionalTransport = Transport as IOptionalTransport;
		bool flag = false;
		if (message.Length <= 1048543 && optionalCount < 512 && optionalBytes <= 2097152 - message.Length && optionalTransport != null)
		{
			using MessageWriter messageWriter = Protocol.Begin(MsgType.SessionData);
			messageWriter.Write(localNonce.ToByteArray());
			messageWriter.Write(peerNonce.ToByteArray());
			messageWriter.Write(message);
			flag = optionalTransport.SendOptional(messageWriter.ToArray());
		}
		if (flag)
		{
			optionalCount++;
			optionalBytes += message.Length;
			return true;
		}
		OptionalEnabled = false;
		Plugin.Log.LogWarning((object)"[Session] 装饰预算/可靠入队被拒绝，本战斗关闭装饰通道，权威同步继续");
		using (MessageWriter messageWriter2 = Protocol.Begin(MsgType.OptionalVisualStop))
		{
			messageWriter2.Write(14);
			messageWriter2.Write(AgentAuthority.Epoch);
			if (!Send(messageWriter2.ToArray()))
			{
				Fail("装饰停止通知发送失败");
			}
		}
		InvokeSafely(OptionalStopped);
		return false;
	}

	private static void ReceiveOptionalStop(BinaryReader reader)
	{
		int num = reader.ReadInt32();
		int num2 = reader.ReadInt32();
		reader.RequireEnd();
		if (IsClient && num == 14 && num2 == AgentAuthority.Epoch && OptionalEnabled)
		{
			OptionalEnabled = false;
			InvokeSafely(OptionalStopped);
			Plugin.Log.LogWarning((object)"[Session] 主机装饰通道已降级，本战斗清除装饰显示");
		}
	}

	public static void Tick()
	{
		ITransport transport = Transport;
		if (transport == null || stoppedRaised)
		{
			return;
		}
		try
		{
			transport.Tick();
		}
		catch (Exception ex)
		{
			Fail("传输层异常：" + ex.Message);
			return;
		}
		if (transport.State == TransportState.Failed)
		{
			Fail(transport.StatusText);
		}
		else
		{
			if (transport.State != TransportState.Connected)
			{
				return;
			}
			float unscaledTime = Time.unscaledTime;
			if (!helloSent)
			{
				helloAt = (lastReceived = unscaledTime);
				using (MessageWriter messageWriter = Protocol.Begin(MsgType.Hello))
				{
					messageWriter.Write(14);
					messageWriter.Write(localNonce.ToByteArray());
					messageWriter.Write(transport.IsHost);
					string text = Plugin.LocalName ?? "Player";
					messageWriter.Write((text.Length > 64) ? text.Substring(0, 64) : text);
					if (!transport.Send(messageWriter.ToArray(), reliable: true))
					{
						Fail("握手发送失败");
						return;
					}
				}
				helloSent = true;
				StatusText = "连接已建立，正在校验协议";
			}
			int num = 0;
			Stopwatch stopwatch = Stopwatch.StartNew();
			byte[] message;
			while (Transport == transport && !stoppedRaised && num++ < 4096 && transport.TryReceive(out message))
			{
				Dispatch(message);
				if ((num & 0x1F) == 0 && stopwatch.ElapsedMilliseconds >= 8)
				{
					break;
				}
			}
			if (Transport != transport || stoppedRaised)
			{
				return;
			}
			if (!handshakeComplete)
			{
				if (unscaledTime - helloAt > 15f)
				{
					Fail("协议握手超时，请确认双方 Together 版本相同");
				}
			}
			else if (unscaledTime - lastReceived > 30f)
			{
				Fail("超过 30 秒未收到对端消息");
			}
			else if (unscaledTime >= nextPing)
			{
				nextPing = unscaledTime + 2f;
				pingSentAt = unscaledTime;
				using MessageWriter messageWriter2 = Protocol.Begin(MsgType.Ping);
				messageWriter2.Write(++pingSequence);
				Send(messageWriter2.ToArray(), reliable: false);
			}
		}
	}

	private static void Dispatch(byte[] message)
	{
		try
		{
			MsgType msgType = Protocol.PeekType(message);
			using BinaryReader binaryReader = Protocol.Reader(message);
			switch (msgType)
			{
			case MsgType.Hello:
				ReceiveHello(binaryReader);
				break;
			case MsgType.HelloAck:
				ReceiveAck(binaryReader);
				break;
			default:
				throw new IOException("握手后仅接受带连接标识的消息");
			case MsgType.SessionData:
			{
				Guid guid = new Guid(binaryReader.ReadExactBytes(16));
				Guid guid2 = new Guid(binaryReader.ReadExactBytes(16));
				if (Active && !(guid != peerNonce) && !(guid2 != localNonce))
				{
					DispatchPayload(binaryReader.ReadExactBytes((int)(binaryReader.BaseStream.Length - binaryReader.BaseStream.Position)));
					lastReceived = Time.unscaledTime;
				}
				break;
			}
			}
		}
		catch (Exception ex)
		{
			Fail("处理网络消息失败：" + ex.Message);
			Plugin.Log.LogError((object)ex);
		}
	}

	private static void ReceiveHello(BinaryReader reader)
	{
		int num = reader.ReadInt32();
		if (num != 14)
		{
			PeerVersionMismatch = true;
			Fail("协议版本不一致：本地 " + 14 + "，对方 " + num);
			return;
		}
		Guid guid = new Guid(reader.ReadExactBytes(16));
		bool flag = reader.ReadBoolean();
		if (reader.BaseStream.Length > 300)
		{
			throw new IOException("玩家名称过长");
		}
		string text = reader.ReadString();
		reader.RequireEnd();
		if (guid == Guid.Empty || text.Length > 64 || flag == Transport.IsHost)
		{
			throw new IOException("握手角色或身份无效");
		}
		if (handshakeComplete && guid != peerNonce)
		{
			return;
		}
		peerNonce = guid;
		PeerName = text;
		using MessageWriter messageWriter = Protocol.Begin(MsgType.HelloAck);
		messageWriter.Write(14);
		messageWriter.Write(localNonce.ToByteArray());
		messageWriter.Write(peerNonce.ToByteArray());
		if (!Transport.Send(messageWriter.ToArray(), reliable: true))
		{
			Fail("握手确认发送失败");
		}
	}

	private static void ReceiveAck(BinaryReader reader)
	{
		if (reader.ReadInt32() != 14)
		{
			throw new IOException("握手确认版本无效");
		}
		Guid guid = new Guid(reader.ReadExactBytes(16));
		Guid guid2 = new Guid(reader.ReadExactBytes(16));
		reader.RequireEnd();
		if (!handshakeComplete && !(peerNonce == Guid.Empty) && !(guid != peerNonce) && !(guid2 != localNonce))
		{
			handshakeComplete = true;
			lastReceived = Time.unscaledTime;
			StatusText = "已连接 " + PeerName + "（协议 v" + 14 + "）";
			Plugin.Log.LogInfo((object)("[Session] " + StatusText));
			InvokeSafely(Connected);
		}
	}

	private static void DispatchPayload(byte[] message)
	{
		MsgType msgType = Protocol.PeekType(message);
		using BinaryReader binaryReader = Protocol.Reader(message);
		switch (msgType)
		{
		case MsgType.Ping:
		{
			int value2 = binaryReader.ReadInt32();
			binaryReader.RequireEnd();
			using MessageWriter messageWriter = Protocol.Begin(MsgType.Pong);
			messageWriter.Write(value2);
			Send(messageWriter.ToArray(), reliable: false);
			break;
		}
		case MsgType.Pong:
		{
			int num = binaryReader.ReadInt32();
			binaryReader.RequireEnd();
			if (num == pingSequence)
			{
				LastPingMs = (Time.unscaledTime - pingSentAt) * 1000f;
			}
			break;
		}
		default:
		{
			if (!handlers.TryGetValue(msgType, out var value))
			{
				throw new IOException("没有注册消息处理器：" + msgType);
			}
			value(binaryReader);
			break;
		}
		}
	}
}
