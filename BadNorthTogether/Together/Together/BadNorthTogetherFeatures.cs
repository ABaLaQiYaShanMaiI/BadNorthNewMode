namespace Together;

/// <summary>BadNorthTogether 的能力标记。
/// 外部模组（例如 BadNorthNewMode）可以反射这个类型来确认：
/// 当前安装的是 BadNorthTogether 分支，而不是原版 Together 0.6.13。
/// 原版 Together 没有这个类型，因此不会误判。</summary>
public static class BadNorthTogetherFeatures
{
	public const string Version = Plugin.PluginVersion;

	public const int ProtocolVersion = global::Together.Net.Protocol.Version;

	/// <summary>普通岛屿外部波清单通道（HostRaidExternal + HostRaidManifest.TryAdopt）。</summary>
	public const bool ExternalWaves = true;

	/// <summary>预留：外部波是否要求 Steam 大厅成员数据双方都声明支持（当前**未实现**，恒 false；
	/// 目前靠"双端同一份 BadNorthTogether + 协议 14"来保证）。</summary>
	public const bool SteamCapabilityGate = false;
}
