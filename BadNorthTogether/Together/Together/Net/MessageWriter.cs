using System;
using System.IO;
using System.Text;

namespace Together.Net;

public sealed class MessageWriter : BinaryWriter
{
	public MessageWriter()
		: base(new MemoryStream(), Encoding.UTF8)
	{
	}

	public byte[] ToArray()
	{
		Flush();
		return ((MemoryStream)BaseStream).ToArray();
	}

	public void WriteBytes(byte[] data)
	{
		if (data == null)
		{
			throw new ArgumentNullException("data");
		}
		Write(data.Length);
		Write(data);
	}
}
