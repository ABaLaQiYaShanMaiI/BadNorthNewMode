namespace Together;

internal interface IVisualTree<T> where T : class
{
	string Name(T node);

	int ChildCount(T node);

	T Child(T node, int index);

	bool HasRenderer(T node);

	bool IsLocalOnly(T node);
}
