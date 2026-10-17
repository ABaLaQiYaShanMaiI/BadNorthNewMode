namespace Together.Net;

public interface ITransport
{
	string Name { get; }

	bool IsHost { get; }

	TransportState State { get; }

	string StatusText { get; }

	string PeerName { get; }

	void Tick();

	bool Send(byte[] message, bool reliable);

	bool TryReceive(out byte[] message);

	void Shutdown();
}
