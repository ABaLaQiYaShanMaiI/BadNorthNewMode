using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;

namespace Together;

internal sealed class CampaignTypeBinder : SerializationBinder
{
	private readonly Dictionary<string, Type> allowed = new Dictionary<string, Type>();

	public CampaignTypeBinder(Type root)
	{
		Add(root);
	}

	private void Add(Type type)
	{
		if (type == null || type.IsPointer || type.IsInterface || type == typeof(object) || !type.IsSerializable)
		{
			return;
		}
		string key = Key(type.Assembly.GetName().Name, type.FullName);
		if (allowed.ContainsKey(key))
		{
			return;
		}
		allowed.Add(key, type);
		if (type.IsArray)
		{
			Add(type.GetElementType());
		}
		else
		{
			if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(DateTime) || type == typeof(decimal))
			{
				return;
			}
			if (type.IsGenericType)
			{
				Type[] genericArguments = type.GetGenericArguments();
				foreach (Type type2 in genericArguments)
				{
					Add(type2);
				}
				if (type.GetGenericTypeDefinition() == typeof(Dictionary<, >))
				{
					Type[] genericArguments2 = type.GetGenericArguments();
					Add(typeof(KeyValuePair<, >).MakeGenericType(genericArguments2).MakeArrayType());
					object value = typeof(EqualityComparer<>).MakeGenericType(genericArguments2[0]).GetProperty("Default").GetValue(null, null);
					Add(value.GetType());
				}
			}
			FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			foreach (FieldInfo fieldInfo in fields)
			{
				if (!fieldInfo.IsNotSerialized)
				{
					Add(fieldInfo.FieldType);
				}
			}
			Add(type.BaseType);
		}
	}

	public override Type BindToType(string assemblyName, string typeName)
	{
		if (!allowed.TryGetValue(Key(assemblyName, typeName), out var value))
		{
			throw new SerializationException("联机存档包含未允许的类型：" + typeName);
		}
		return value;
	}

	private static string Key(string assemblyName, string typeName)
	{
		string text = assemblyName.Split(',')[0].Trim().Replace("System.Private.CoreLib", "mscorlib");
		string text2 = Regex.Replace(typeName, ", (Version|Culture|PublicKeyToken)=[^,\\]]+", "");
		return text + "|" + text2.Replace("System.Private.CoreLib", "mscorlib");
	}
}
