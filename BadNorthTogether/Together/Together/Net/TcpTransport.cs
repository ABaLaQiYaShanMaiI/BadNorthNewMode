using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Together.Net;

public sealed class TcpTransport : ITransport, IOptionalTransport
{
	private const int MaxQueuedBytes = 16777216;

	private const int MaxQueuedMessages = 8192;

	private readonly object lifecycleLock = new object();

	private readonly object inboundLock = new object();

	private readonly object outboundLock = new object();

	private readonly Queue<byte[]> inbound = new Queue<byte[]>();

	private readonly PendingPacketQueue outbound = new PendingPacketQueue();

	private int inboundBytes;

	private TcpListener listener;

	private TcpClient client;

	private NetworkStream stream;

	private volatile TransportState state;

	private volatile string statusText = "";

	private volatile bool shutdown;

	public string Name => "TCP";

	public bool IsHost { get; private set; }

	public TransportState State => state;

	public string StatusText => statusText;

	public string PeerName { get; set; } = "";

	public static TcpTransport Host(int port)
	{
		ValidatePort(port);
		TcpTransport transport = new TcpTransport
		{
			IsHost = true,
			state = TransportState.Connecting
		};
		transport.statusText = "等待玩家连接，端口 " + port;
		transport.StartThread(delegate
		{
			transport.HostMain(port);
		}, "Together.Tcp.Read");
		return transport;
	}

	public static TcpTransport Join(string ip, int port)
	{
		ValidatePort(port);
		if (string.IsNullOrEmpty(ip))
		{
			throw new ArgumentException("主机地址不能为空", "ip");
		}
		TcpTransport transport = new TcpTransport
		{
			IsHost = false,
			state = TransportState.Connecting
		};
		transport.statusText = "正在连接 " + ip + ":" + port;
		transport.StartThread(delegate
		{
			transport.ClientMain(ip, port);
		}, "Together.Tcp.Read");
		return transport;
	}

	private static void ValidatePort(int port)
	{
		if (port < 1 || port > 65535)
		{
			throw new ArgumentOutOfRangeException("port");
		}
	}

	private void StartThread(ThreadStart body, string name)
	{
		Thread thread = new Thread(body);
		thread.IsBackground = true;
		thread.Name = name;
		thread.Start();
	}

	private void HostMain(int port)
	{
		try
		{
			TcpListener tcpListener;
			lock (lifecycleLock)
			{
				if (shutdown)
				{
					return;
				}
				tcpListener = (listener = new TcpListener(IPAddress.Any, port));
				tcpListener.Start();
			}
			TcpClient tcpClient = tcpListener.AcceptTcpClient();
			lock (lifecycleLock)
			{
				tcpListener.Stop();
				if (shutdown)
				{
					tcpClient.Close();
					return;
				}
				client = tcpClient;
			}
			ConnectedMain("已连接（主机）");
		}
		catch (Exception ex)
		{
			Fail("主机连接失败：" + ex.Message);
		}
	}

	private void ClientMain(string ip, int port)
	{
		try
		{
			TcpClient tcpClient;
			lock (lifecycleLock)
			{
				if (shutdown)
				{
					return;
				}
				tcpClient = (client = new TcpClient());
			}
			IAsyncResult asyncResult = tcpClient.BeginConnect(ip, port, null, null);
			using (WaitHandle waitHandle = asyncResult.AsyncWaitHandle)
			{
				if (!waitHandle.WaitOne(10000, exitContext: false))
				{
					throw new TimeoutException("连接超时（10 秒）");
				}
				tcpClient.EndConnect(asyncResult);
			}
			ConnectedMain("已连接到 " + ip + ":" + port);
		}
		catch (Exception ex)
		{
			Fail("连接失败：" + ex.Message);
		}
	}

	private void ConnectedMain(string text)
	{
		NetworkStream localStream;
		lock (lifecycleLock)
		{
			if (shutdown)
			{
				return;
			}
			client.NoDelay = true;
			client.SendTimeout = 10000;
			localStream = (stream = client.GetStream());
			localStream.WriteTimeout = 10000;
			statusText = text;
			state = TransportState.Connected;
		}
		StartThread(delegate
		{
			WriteLoop(localStream);
		}, "Together.Tcp.Write");
		ReadLoop(localStream);
	}

	private void Fail(string text)
	{
		lock (lifecycleLock)
		{
			if (shutdown || state == TransportState.Failed)
			{
				return;
			}
			statusText = text;
			state = TransportState.Failed;
			CloseSockets();
		}
		lock (outboundLock)
		{
			outbound.Clear();
			Monitor.PulseAll(outboundLock);
		}
		lock (inboundLock)
		{
			inbound.Clear();
			inboundBytes = 0;
		}
	}

	private void ReadLoop(NetworkStream localStream)
	{
		try
		{
			byte[] array = new byte[4];
			while (state == TransportState.Connected && !shutdown && ReadExact(localStream, array))
			{
				int num = (array[0] << 24) | (array[1] << 16) | (array[2] << 8) | array[3];
				if (num <= 0 || num > 1048576)
				{
					Fail("消息长度异常：" + num);
					return;
				}
				byte[] array2 = new byte[num];
				if (!ReadExact(localStream, array2))
				{
					break;
				}
				bool flag;
				lock (inboundLock)
				{
					flag = inbound.Count >= 8192 || inboundBytes > 16777216 - num;
					if (!flag && state == TransportState.Connected && !shutdown)
					{
						inbound.Enqueue(array2);
						inboundBytes += num;
					}
				}
				if (flag)
				{
					Fail("接收队列积压超限");
					return;
				}
			}
			Fail("对端已断开");
		}
		catch (Exception ex)
		{
			Fail("接收失败：" + ex.Message);
		}
	}

	private static bool ReadExact(NetworkStream localStream, byte[] data)
	{
		int num;
		for (int i = 0; i < data.Length; i += num)
		{
			num = localStream.Read(data, i, data.Length - i);
			if (num <= 0)
			{
				return false;
			}
		}
		return true;
	}

	private void WriteLoop(NetworkStream localStream)
	{
		try
		{
			byte[] array = new byte[65536];
			while (true)
			{
				byte[] array2;
				lock (outboundLock)
				{
					while (outbound.Count == 0 && state == TransportState.Connected && !shutdown)
					{
						Monitor.Wait(outboundLock);
					}
					if (state != TransportState.Connected || shutdown)
					{
						break;
					}
					array2 = outbound.Dequeue();
				}
				int num = array2.Length;
				if (num + 4 > array.Length)
				{
					WriteHeader(array, 0, num);
					localStream.Write(array, 0, 4);
					localStream.Write(array2, 0, num);
					continue;
				}
				int num2 = 0;
				while (array2 != null)
				{
					WriteHeader(array, num2, array2.Length);
					Buffer.BlockCopy(array2, 0, array, num2 + 4, array2.Length);
					num2 += array2.Length + 4;
					lock (outboundLock)
					{
						array2 = null;
						if (outbound.Count > 0 && outbound.FirstLength + 4 <= array.Length - num2)
						{
							array2 = outbound.Dequeue();
						}
					}
				}
				localStream.Write(array, 0, num2);
			}
		}
		catch (Exception ex)
		{
			Fail("发送失败：" + ex.Message);
		}
	}

	public void Tick()
	{
	}

	private static void WriteHeader(byte[] data, int offset, int length)
	{
		data[offset] = (byte)(length >> 24);
		data[offset + 1] = (byte)(length >> 16);
		data[offset + 2] = (byte)(length >> 8);
		data[offset + 3] = (byte)length;
	}

	public bool Send(byte[] message, bool reliable)
	{
		if (message == null || message.Length == 0 || message.Length > 1048576)
		{
			return false;
		}
		lock (outboundLock)
		{
			if (state != TransportState.Connected || shutdown)
			{
				return false;
			}
			if (outbound.TryEnqueue(message, reliable, 8192, 16777216))
			{
				Monitor.Pulse(outboundLock);
				return true;
			}
		}
		if (reliable)
		{
			Fail("可靠发送队列积压超限，连接已关闭");
		}
		return false;
	}

	public bool TryReceive(out byte[] message)
	{
		lock (inboundLock)
		{
			if (inbound.Count > 0 && state == TransportState.Connected && !shutdown)
			{
				message = inbound.Dequeue();
				inboundBytes -= message.Length;
				return true;
			}
		}
		message = null;
		return false;
	}

	public bool SendOptional(byte[] message)
	{
		if (message == null || message.Length == 0 || message.Length > 1048576)
		{
			return false;
		}
		lock (outboundLock)
		{
			if (state != TransportState.Connected || shutdown || !outbound.TryEnqueueOptional(message))
			{
				return false;
			}
			Monitor.Pulse(outboundLock);
			return true;
		}
	}

	private void CloseSockets()
	{
		try
		{
			if (stream != null)
			{
				stream.Close();
			}
		}
		catch
		{
		}
		try
		{
			if (client != null)
			{
				client.Close();
			}
		}
		catch
		{
		}
		try
		{
			if (listener != null)
			{
				listener.Stop();
			}
		}
		catch
		{
		}
	}

	public void Shutdown()
	{
		lock (lifecycleLock)
		{
			shutdown = true;
			state = TransportState.Idle;
			CloseSockets();
		}
		lock (outboundLock)
		{
			outbound.Clear();
			Monitor.PulseAll(outboundLock);
		}
		lock (inboundLock)
		{
			inbound.Clear();
			inboundBytes = 0;
		}
	}
}
