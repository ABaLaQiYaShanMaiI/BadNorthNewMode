// BadNorthTogether 构建兼容层（不属于 Together 原逻辑）
// 反编译代码里 Unity 的空检查被展开成 `global::UnityObjectOps.Truthy(...)`；C# 不允许显式调用转换运算符，
// 这里用一个等价 helper 替代，语义与 Unity 的 implicit operator bool 一致。
internal static class UnityObjectOps
{
    internal static bool Truthy(UnityEngine.Object value)
    {
        return value != null;
    }
}
