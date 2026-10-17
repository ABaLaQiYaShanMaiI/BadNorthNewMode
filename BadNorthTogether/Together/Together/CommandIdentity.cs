namespace Together;

internal static class CommandIdentity
{
	public static bool Matches(int epoch, int currentEpoch, int level, int currentLevel, bool closing)
	{
		if (!closing && epoch > 0 && epoch == currentEpoch)
		{
			return level == currentLevel;
		}
		return false;
	}
}
