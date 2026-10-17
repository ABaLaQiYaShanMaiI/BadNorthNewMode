namespace Together;

internal static class CampaignFence
{
	public static bool AcceptRevision(int identity, int revision, int epoch, int transition, int currentIdentity, int appliedRevision, int currentEpoch, int currentTransition, bool closing, bool start)
	{
		if (identity <= 0 || revision <= 0 || epoch < 0 || transition < 0 || identity < currentIdentity)
		{
			return false;
		}
		if (identity == currentIdentity && (revision <= appliedRevision || epoch < currentEpoch || (start && epoch == currentEpoch)))
		{
			return false;
		}
		if (transition > 0 && transition < currentTransition)
		{
			return false;
		}
		if (closing && !start && transition != currentTransition)
		{
			return false;
		}
		if (start)
		{
			return epoch > 0;
		}
		return true;
	}

	public static bool ReadyMatches(int transition, int identity, int epoch, int revision, int expectedTransition, int expectedIdentity, int expectedEpoch, int expectedRevision)
	{
		if (transition == expectedTransition && identity == expectedIdentity && epoch == expectedEpoch)
		{
			return revision == expectedRevision;
		}
		return false;
	}
}
