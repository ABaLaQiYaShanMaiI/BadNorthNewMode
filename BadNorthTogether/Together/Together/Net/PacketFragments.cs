using System;
using System.Collections.Generic;
using System.IO;

namespace Together.Net;

internal sealed class PacketFragments
{
	private sealed class Assembly
	{
		public byte[] Data;

		public bool[] Received;

		public int Count;

		public float Created;
	}

	public const byte Marker = 253;

	private const int PayloadSize = 1100;

	private const int HeaderSize = 17;

	private const int MaxPendingBytes = 4194304;

	private readonly Dictionary<ulong, Assembly> pending = new Dictionary<ulong, Assembly>();

	private int pendingBytes;

	private float nextCleanup;

	private ulong nextId = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0);

	public IEnumerable<byte[]> Split(byte[] message)
	{
		if (message == null || message.Length == 0 || message.Length > 1048576)
		{
			throw new IOException("分片消息大小无效");
		}
		ulong id = ++nextId;
		int count = (message.Length + 1100 - 1) / 1100;
		for (int index = 0; index < count; index++)
		{
			using MessageWriter writer = new MessageWriter();
			writer.Write((byte)253);
			writer.Write(id);
			writer.Write(message.Length);
			writer.Write((ushort)index);
			writer.Write((ushort)count);
			writer.Write(message, index * 1100, Math.Min(1100, message.Length - index * 1100));
			yield return writer.ToArray();
		}
	}

	public byte[] Accept(byte[] packet, float now)
	{
		Expire(now);
		if (packet == null || packet.Length < 17 || packet.Length > 1117)
		{
			throw new IOException("分片长度无效");
		}
		using BinaryReader binaryReader = new BinaryReader(new MemoryStream(packet, writable: false));
		if (binaryReader.ReadByte() != 253)
		{
			throw new IOException("分片标记无效");
		}
		ulong key = binaryReader.ReadUInt64();
		int num = binaryReader.ReadInt32();
		int num2 = binaryReader.ReadUInt16();
		int num3 = binaryReader.ReadUInt16();
		if (num <= 0 || num > 1048576 || num3 != (num + 1100 - 1) / 1100 || num2 >= num3 || packet.Length - 17 != Math.Min(1100, num - num2 * 1100))
		{
			throw new IOException("分片标头无效");
		}
		if (!pending.TryGetValue(key, out var value))
		{
			if (pending.Count >= 1024 || pendingBytes > 4194304 - num)
			{
				return null;
			}
			value = new Assembly
			{
				Data = new byte[num],
				Received = new bool[num3],
				Created = now
			};
			pending.Add(key, value);
			pendingBytes += num;
		}
		if (value.Data.Length != num || value.Received.Length != num3)
		{
			throw new IOException("分片消息标识冲突");
		}
		if (!value.Received[num2])
		{
			Buffer.BlockCopy(packet, 17, value.Data, num2 * 1100, packet.Length - 17);
			value.Received[num2] = true;
			value.Count++;
		}
		if (value.Count != num3)
		{
			return null;
		}
		pending.Remove(key);
		pendingBytes -= num;
		return value.Data;
	}

	public void Expire(float now)
	{
		if (now < nextCleanup)
		{
			return;
		}
		nextCleanup = now + 0.25f;
		List<ulong> list = new List<ulong>();
		foreach (KeyValuePair<ulong, Assembly> item in pending)
		{
			if (now - item.Value.Created > 2f)
			{
				list.Add(item.Key);
			}
		}
		foreach (ulong item2 in list)
		{
			pendingBytes -= pending[item2].Data.Length;
			pending.Remove(item2);
		}
	}

	public void Clear()
	{
		pending.Clear();
		pendingBytes = 0;
		nextCleanup = 0f;
	}
}
