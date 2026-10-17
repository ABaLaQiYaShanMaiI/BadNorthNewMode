using UnityEngine;

namespace Together;

internal struct ProjectileNodePose
{
	public const byte Mesh = 0;

	public const byte Sprite = 1;

	public const byte Batched = 2;

	public byte Kind;

	public Vector3 Position;

	public Vector3 Scale;

	public Quaternion Rotation;

	public bool Visible;

	public bool FlipX;

	public bool FlipY;

	public Color Color;

	public ushort SpriteId;
}
