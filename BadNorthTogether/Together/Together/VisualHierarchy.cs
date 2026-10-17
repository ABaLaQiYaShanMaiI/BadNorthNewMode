using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Together;

internal static class VisualHierarchy
{
	internal const int MaxNodes = 128;

	internal const int MaxDepth = 24;

	public static List<VisualTreeEntry<T>> Describe<T>(T root, IVisualTree<T> tree) where T : class
	{
		HashSet<T> hashSet = new HashSet<T>();
		Include(root, root, tree, hashSet, 0);
		hashSet.Add(root);
		List<VisualTreeEntry<T>> list = new List<VisualTreeEntry<T>>();
		Collect(root, tree, hashSet, new List<string>(), new List<ushort>(), list);
		if (list.Count > 128)
		{
			throw new IOException("角色永久显示节点超过 " + 128);
		}
		return list;
	}

	private static bool Include<T>(T node, T root, IVisualTree<T> tree, HashSet<T> included, int depth) where T : class
	{
		if (node == null || (node != root && tree.IsLocalOnly(node)))
		{
			return false;
		}
		if (depth > 24)
		{
			throw new IOException("角色显示层级过深");
		}
		bool flag = tree.HasRenderer(node);
		int num = tree.ChildCount(node);
		for (int i = 0; i < num; i++)
		{
			flag |= Include(tree.Child(node, i), root, tree, included, depth + 1);
		}
		if (flag)
		{
			included.Add(node);
		}
		return flag;
	}

	private static void Collect<T>(T node, IVisualTree<T> tree, HashSet<T> included, List<string> names, List<ushort> ordinals, List<VisualTreeEntry<T>> result) where T : class
	{
		if (!included.Contains(node))
		{
			return;
		}
		result.Add(new VisualTreeEntry<T>
		{
			Target = node,
			Names = names.ToArray(),
			Ordinals = ordinals.ToArray()
		});
		Dictionary<string, ushort> dictionary = new Dictionary<string, ushort>(StringComparer.Ordinal);
		for (int i = 0; i < tree.ChildCount(node); i++)
		{
			T val = tree.Child(node, i);
			if (included.Contains(val))
			{
				string text = tree.Name(val);
				dictionary.TryGetValue(text, out var value);
				dictionary[text] = (ushort)(value + 1);
				names.Add(text);
				ordinals.Add(value);
				Collect(val, tree, included, names, ordinals, result);
				names.RemoveAt(names.Count - 1);
				ordinals.RemoveAt(ordinals.Count - 1);
			}
		}
	}

	public static string Key(string[] names, ushort[] ordinals)
	{
		if (names == null || ordinals == null || names.Length != ordinals.Length || names.Length > 24)
		{
			throw new IOException("角色显示路径非法");
		}
		StringBuilder stringBuilder = new StringBuilder();
		for (int i = 0; i < names.Length; i++)
		{
			if (names[i] == null || ordinals[i] > 1024)
			{
				throw new IOException("角色显示路径片段非法");
			}
			stringBuilder.Append(names[i].Length).Append(':').Append(names[i])
				.Append('#')
				.Append(ordinals[i])
				.Append('/');
		}
		return stringBuilder.ToString();
	}

	public static string DisplayPath(string[] names, ushort[] ordinals)
	{
		StringBuilder stringBuilder = new StringBuilder("<人物根>");
		for (int i = 0; i < names.Length; i++)
		{
			stringBuilder.Append('/').Append(names[i]).Append('[')
				.Append(ordinals[i])
				.Append(']');
		}
		return stringBuilder.ToString();
	}

	public static Dictionary<string, T> Index<T>(IEnumerable<VisualTreeEntry<T>> entries) where T : class
	{
		Dictionary<string, T> dictionary = new Dictionary<string, T>(StringComparer.Ordinal);
		foreach (VisualTreeEntry<T> entry in entries)
		{
			string key = Key(entry.Names, entry.Ordinals);
			if (dictionary.ContainsKey(key))
			{
				throw new IOException("角色显示路径重复：" + DisplayPath(entry.Names, entry.Ordinals));
			}
			dictionary.Add(key, entry.Target);
		}
		return dictionary;
	}

	public static T Resolve<T>(Dictionary<string, T> index, string[] names, ushort[] ordinals) where T : class
	{
		if (!index.TryGetValue(Key(names, ordinals), out var value))
		{
			throw new IOException("本地角色显示层级缺失：" + DisplayPath(names, ordinals));
		}
		return value;
	}
}
