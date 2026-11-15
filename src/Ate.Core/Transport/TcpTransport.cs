using System.Net.Sockets;

namespace Ate.Core.Transport;

/// <summary>Connects to a DUT or instrument over TCP (e.g. a SCPI port such as 5025).</summary>
public sealed class TcpTransport : StreamLineTransport
{
    private readonly string _host;
    private readonly int _port;
    private TcpClient? _client;

    public TcpTransport(string host, int port)
    {
        _host = host;
        _port = port;
    }

    public override string Description => $"TCP {_host}:{_port}";

    protected override async Task<Stream> OpenStreamAsync(CancellationToken ct)
    {
        _client = new TcpClient { NoDelay = true };
        await _client.ConnectAsync(_host, _port, ct).ConfigureAwait(false);
        return _client.GetStream();
    }

    protected override void CloseLink() => _client?.Close();
}
