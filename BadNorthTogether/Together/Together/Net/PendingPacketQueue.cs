using System.Collections.Generic;

namespace Together.Net;

internal sealed class PendingPacketQueue
{
	private sealed class Packet
	{
		public byte[] Data;

		public ulong Key;

		public bool Coalescible;
	}

	private readonly LinkedList<Packet> queue = new LinkedList<Packet>();

	private readonly Dictionary<ulong, LinkedListNode<Packet>> latest = new Dictionary<ulong, LinkedListNode<Packet>>();

	private readonly Queue<byte[]> optional = new Queue<byte[]>();

	private int optionalBytes;

	public int Count => queue.Count + optional.Count;

	public int FirstLength
	{
		get
		{
			if (queue.Count <= 0)
			{
				return optional.Peek().Length;
			}
			return queue.First.Value.Data.Length;
		}
	}

	public int Bytes { get; private set; }

	internal bool TryEnqueueOptional(byte[] data)
	{
		if (optional.Count >= 256 || optionalBytes > 1048576 - data.Length)
		{
			return false;
		}
		optional.Enqueue((byte[])data.Clone());
		optionalBytes += data.Length;
		return true;
	}

	public bool TryEnqueue(byte[] data, bool reliable, int maxMessages, int maxBytes)
	{
		ulong key = 0uL;
		bool flag = !reliable && TryGetActorKey(data, out key);
		if (!flag)
		{
			key = 0uL;
		}
		LinkedListNode<Packet> value = null;
		if (flag)
		{
			latest.TryGetValue(key, out value);
		}
		int num = ((value != null) ? value.Value.Data.Length : 0);
		int num2 = ((value != null) ? 1 : 0);
		if (queue.Count - num2 >= maxMessages || Bytes - num > maxBytes - data.Length)
		{
			return false;
		}
		if (value != null)
		{
			queue.Remove(value);
			Bytes -= num;
		}
		if (reliable)
		{
			latest.Clear();
		}
		Packet value2 = new Packet
		{
			Data = (byte[])data.Clone(),
			Key = key,
			Coalescible = flag
		};
		LinkedListNode<Packet> value3 = queue.AddLast(value2);
		Bytes += data.Length;
		if (flag)
		{
			latest[key] = value3;
		}
		return true;
	}

	public byte[] Dequeue()
	{
		if (queue.Count == 0)
		{
			byte[] array = optional.Dequeue();
			optionalBytes -= array.Length;
			return array;
		}
		LinkedListNode<Packet> first = queue.First;
		Packet value = first.Value;
		queue.RemoveFirst();
		Bytes -= value.Data.Length;
		if (value.Coalescible && latest.TryGetValue(value.Key, out var value2) && value2 == first)
		{
			latest.Remove(value.Key);
		}
		return value.Data;
	}

	public void Clear()
	{
		queue.Clear();
		optional.Clear();
		latest.Clear();
		Bytes = (optionalBytes = 0);
	}

	private static bool TryGetActorKey(byte[] data, out ulong key)
	{
		key = 0uL;
		int num = 33;
		if (data.Length < num + 13 || data[0] != 5 || data[num] != 44 || ReadInt(data, num + 1) != 14)
		{
			return false;
		}
		int num2 = ReadInt(data, num + 5);
		int num3 = ReadInt(data, num + 9);
		if (num2 <= 0 || num3 <= 0)
		{
			return false;
		}
		key = ((ulong)(uint)num2 << 32) | (uint)num3;
		return true;
	}

	private static int ReadInt(byte[] data, int offset)
	{
		return data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);
	}
}
