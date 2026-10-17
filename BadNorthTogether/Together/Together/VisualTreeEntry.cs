namespace Together;

internal sealed class VisualTreeEntry<T> where T : class
{
	public T Target;

	public string[] Names;

	public ushort[] Ordinals;
}
