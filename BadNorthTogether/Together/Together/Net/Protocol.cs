using System;
using System.IO;
using System.Text;

namespace Together.Net;

public static class Protocol
{
	public const int Version = 14;

	public const int MaxMessageSize = 1048576;

	public const int SessionHeaderSize = 33;

	public static MessageWriter Begin(MsgType type)
	{
		MessageWriter messageWriter = new MessageWriter();
		messageWriter.Write((byte)type);
		return messageWriter;
	}

	public static MsgType PeekType(byte[] message)
	{
		ValidateMessage(message);
		return (MsgType)message[0];
	}

	public static BinaryReader Reader(byte[] message)
	{
		ValidateMessage(message);
		return new BinaryReader(new MemoryStream(message, 1, message.Length - 1, writable: false), Encoding.UTF8);
	}

	public static void ValidateMessage(byte[] message)
	{
		if (message == null || message.Length == 0 || message.Length > 1048576)
		{
			throw new IOException("消息长度无效");
		}
		if (!Enum.IsDefined(typeof(MsgType), message[0]))
		{
			throw new IOException("未知消息类型 " + message[0]);
		}
	}
}
