using System.IO;

namespace Together;

internal sealed class CommandPauseState
{
	public int Sequence { get; private set; }

	public int LastIntent { get; private set; }

	public bool Paused { get; private set; }

	public bool Known { get; private set; }

	public void Reset()
	{
		int sequence = (LastIntent = 0);
		Sequence = sequence;
		bool paused = (Known = false);
		Paused = paused;
	}

	public bool AcceptIntent(int intent, bool pause)
	{
		if (intent <= LastIntent)
		{
			return false;
		}
		LastIntent = intent;
		Decide(pause);
		return true;
	}

	public void Decide(bool pause)
	{
		if (Sequence == int.MaxValue)
		{
			throw new IOException("暂停裁决序号耗尽");
		}
		Sequence++;
		Paused = pause;
		Known = true;
	}

	public bool AcceptState(int sequence, bool pause)
	{
		if (sequence <= 0)
		{
			throw new IOException("暂停状态序号非法");
		}
		if (sequence <= Sequence)
		{
			return false;
		}
		Sequence = sequence;
		Paused = pause;
		Known = true;
		return true;
	}
}
