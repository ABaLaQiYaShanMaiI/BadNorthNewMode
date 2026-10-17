using System;
using System.IO;

namespace Together.Net;

internal sealed class PoseBatch : IDisposable
{
	internal const int MaxPayload = 1167;

	private readonly Action<byte[]> send;

	private readonly MsgType type;

	private MessageWriter pending;

	private byte[] header;

	public PoseBatch(Action<byte[]> send, MsgType type = MsgType.ActorPose)
	{
		HeaderSize(type);
		this.send = send;
		this.type = type;
	}

	private static int HeaderSize(MsgType type)
	{
		return type switch
		{
			MsgType.ActorPose => 13, 
			MsgType.WorldEvent => 11, 
			_ => throw new IOException("批次仅接受人物或投射物姿态"), 
		};
	}

	public void Add(byte[] pose)
	{
		int num = HeaderSize(type);
		if (Protocol.PeekType(pose) != type || pose.Length < ((type == MsgType.ActorPose) ? 21 : 15) || (type == MsgType.WorldEvent && pose[10] != 1))
		{
			throw new IOException("批次不能包含生命周期或非姿态消息");
		}
		if (pose.Length + 3 > 1167)
		{
			Flush();
			send(pose);
			return;
		}
		byte[] array = new byte[num];
		Buffer.BlockCopy(pose, 0, array, 0, (type == MsgType.ActorPose) ? 9 : num);
		if (type == MsgType.ActorPose)
		{
			Buffer.BlockCopy(pose, 17, array, 9, 4);
		}
		bool flag = header != null;
		if (flag)
		{
			for (int i = 0; i < num; i++)
			{
				if (header[i] != array[i])
				{
					flag = false;
					break;
				}
			}
		}
		int num2 = pose.Length - num;
		if (pending != null && (!flag || pending.BaseStream.Length + 2 + num2 > 1167))
		{
			Flush();
		}
		if (pending == null)
		{
			pending = Protocol.Begin((type == MsgType.ActorPose) ? MsgType.ActorPoses : MsgType.ProjectilePoses);
			header = array;
			pending.Write(header);
		}
		pending.Write((ushort)num2);
		if (type == MsgType.ActorPose)
		{
			pending.Write(pose, 9, 8);
			pending.Write(pose, 21, pose.Length - 21);
		}
		else
		{
			pending.Write(pose, num, num2);
		}
	}

	public void Flush()
	{
		if (pending == null)
		{
			return;
		}
		MessageWriter messageWriter = pending;
		pending = null;
		header = null;
		using (messageWriter)
		{
			send(messageWriter.ToArray());
		}
	}

	public static void Read(BinaryReader reader, Action<BinaryReader> receive, MsgType type = MsgType.ActorPose)
	{
		int num = HeaderSize(type);
		if (reader.BaseStream.Length > 1167)
		{
			throw new IOException("批次长度非法");
		}
		byte[] array = reader.ReadExactBytes(num);
		if ((uint)array[0] != (uint)type || (type == MsgType.WorldEvent && array[10] != 1) || reader.BaseStream.Position == reader.BaseStream.Length)
		{
			throw new IOException("批次头非法或没有记录");
		}
		byte[] array2 = new byte[Math.Max(20, (int)reader.BaseStream.Length)];
		bool flag = type == MsgType.ActorPose;
		Buffer.BlockCopy(array, 1, array2, 0, flag ? 8 : (num - 1));
		if (flag)
		{
			Buffer.BlockCopy(array, 9, array2, 16, 4);
		}
		using MemoryStream memoryStream = new MemoryStream(array2, writable: true);
		using BinaryReader binaryReader = new BinaryReader(memoryStream);
		while (reader.BaseStream.Position < reader.BaseStream.Length)
		{
			int num2 = reader.ReadUInt16();
			if (num2 < (flag ? 8 : 4))
			{
				throw new IOException("批次记录不完整");
			}
			if (num2 > reader.BaseStream.Length - reader.BaseStream.Position)
			{
				throw new EndOfStreamException("批次记录被截断");
			}
			memoryStream.SetLength(num - 1 + num2);
			memoryStream.Position = 0L;
			if (flag)
			{
				ReadInto(reader, array2, 8, 8);
				ReadInto(reader, array2, 20, num2 - 8);
			}
			else
			{
				ReadInto(reader, array2, num - 1, num2);
			}
			receive(binaryReader);
			binaryReader.RequireEnd();
		}
	}

	private static void ReadInto(BinaryReader reader, byte[] buffer, int offset, int count)
	{
		while (count > 0)
		{
			int num = reader.Read(buffer, offset, count);
			if (num == 0)
			{
				throw new EndOfStreamException("批次记录被截断");
			}
			offset += num;
			count -= num;
		}
	}

	public void Dispose()
	{
		Flush();
	}
}
