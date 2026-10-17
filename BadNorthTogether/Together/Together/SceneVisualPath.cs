using System;
using System.Collections.Generic;

namespace Together;

internal static class SceneVisualPath
{
	public static bool Describe<T>(T root, T target, Func<T, T> parent, IVisualTree<T> tree, out string[] names, out ushort[] ordinals) where T : class
	{
		names = null;
		ordinals = null;
		List<string> list = new List<string>();
		List<ushort> list2 = new List<ushort>();
		EqualityComparer<T> equalityComparer = EqualityComparer<T>.Default;
		T val = target;
		while (!equalityComparer.Equals(val, root))
		{
			if (val == null || tree.IsLocalOnly(val) || list.Count >= 24)
			{
				return false;
			}
			T val2 = parent(val);
			if (val2 == null)
			{
				return false;
			}
			int num = 0;
			for (int i = 0; i < tree.ChildCount(val2); i++)
			{
				T val3 = tree.Child(val2, i);
				if (equalityComparer.Equals(val3, val))
				{
					break;
				}
				if (tree.Name(val3) == tree.Name(val) && !tree.IsLocalOnly(val3))
				{
					num++;
				}
			}
			if (num > 1024)
			{
				return false;
			}
			list.Add(tree.Name(val));
			list2.Add((ushort)num);
			val = parent(val);
		}
		if (root == null)
		{
			return false;
		}
		list.Reverse();
		list2.Reverse();
		names = list.ToArray();
		ordinals = list2.ToArray();
		return true;
	}

	public static T Resolve<T>(T root, string[] names, ushort[] ordinals, IVisualTree<T> tree) where T : class
	{
		VisualHierarchy.Key(names, ordinals);
		T val = root;
		int num = 0;
		while (val != null && num < names.Length)
		{
			T val2 = null;
			int num2 = 0;
			for (int i = 0; i < tree.ChildCount(val); i++)
			{
				T val3 = tree.Child(val, i);
				if (!(tree.Name(val3) != names[num]) && !tree.IsLocalOnly(val3) && num2++ == ordinals[num])
				{
					val2 = val3;
					break;
				}
			}
			val = val2;
			num++;
		}
		return val;
	}
}
