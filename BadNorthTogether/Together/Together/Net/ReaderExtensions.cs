using System.IO;

namespace Together.Net;

public static class ReaderExtensions
{
	public static byte[] ReadBytesPrefixed(this BinaryReader reader)
	{
		int num = reader.ReadInt32();
		if (num < 0 || num > 1048576)
		{
			throw new IOException("字节数组长度无效 " + num);
		}
		return reader.ReadExactBytes(num);
	}

	public static byte[] ReadExactBytes(this BinaryReader reader, int length)
	{
		if (length < 0 || length > reader.BaseStream.Length - reader.BaseStream.Position)
		{
			throw new EndOfStreamException("消息内容不完整");
		}
		byte[] array = reader.ReadBytes(length);
		if (array.Length != length)
		{
			throw new EndOfStreamException("消息内容不完整");
		}
		return array;
	}

	public static void RequireEnd(this BinaryReader reader)
	{
		if (reader.BaseStream.Position != reader.BaseStream.Length)
		{
			throw new IOException("消息含多余内容");
		}
	}
}
