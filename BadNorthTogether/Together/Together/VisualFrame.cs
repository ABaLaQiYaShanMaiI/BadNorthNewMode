using System.IO;
using UnityEngine;

namespace Together;

internal sealed class VisualFrame
{
	internal const int MaxNodes = 128;

	public uint LayoutHash;

	public Vector3 RootPosition;

	public Quaternion RootRotation;

	public VisualNodeFrame[] Nodes;

	public void Write(BinaryWriter writer)
	{
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		if (Nodes == null || Nodes.Length == 0 || Nodes.Length > 128)
		{
			throw new IOException("角色表现节点数量非法");
		}
		writer.Write(LayoutHash);
		VisualWire.WriteVector(writer, RootPosition);
		try
		{
			VisualWire.WriteRotation(writer, RootRotation);
		}
		catch (IOException ex)
		{
			throw new IOException("人物根旋转编码失败：" + ex.Message, ex);
		}
		writer.Write((byte)(Nodes.Length - 1));
		for (int i = 0; i < Nodes.Length; i++)
		{
			try
			{
				Nodes[i].Write(writer);
			}
			catch (IOException ex2)
			{
				throw new IOException("人物表现节点 " + i + " 编码失败：" + ex2.Message, ex2);
			}
		}
	}

	public static VisualFrame Read(BinaryReader reader)
	{
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		VisualFrame visualFrame = new VisualFrame();
		visualFrame.LayoutHash = reader.ReadUInt32();
		visualFrame.RootPosition = VisualWire.ReadVector(reader);
		visualFrame.RootRotation = VisualWire.ReadRotation(reader);
		int num = reader.ReadByte() + 1;
		if (num > 128)
		{
			throw new IOException("角色表现节点数量超过上限");
		}
		visualFrame.Nodes = new VisualNodeFrame[num];
		for (int i = 0; i < num; i++)
		{
			visualFrame.Nodes[i] = VisualNodeFrame.Read(reader);
		}
		return visualFrame;
	}
}
