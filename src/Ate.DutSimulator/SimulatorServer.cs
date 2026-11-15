using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Ate.DutSimulator;

/// <summary>Exposes a <see cref="DutModel"/> on a TCP port, one command per line.</summary>
public sealed class SimulatorServer : IAsyncDisposable
{
    private readonly Func<DutModel> _modelFactory;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Action<string>? _log;
    private Task? _acceptLoop;

    /// <param name="port">0 = pick any free port (used by the tests).</param>
    /// <param name="modelFactory">Creates a fresh unit for every connection (= every "unit on the fixture").</param>
    public SimulatorServer(int port, Func<DutModel> modelFactory, Action<string>? log = null)
    {
        _modelFactory = modelFactory;
        _log = log;
        _listener = new TcpListener(IPAddress.Loopback, port);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public void Start()
    {
        _listener.Start();
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(ct);
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                return;
            }
            _ = Task.Run(() => HandleClientAsync(client, ct), ct);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            var model = _modelFactory();
            _log?.Invoke($"Client connected: {client.Client.RemoteEndPoint} (unit {model.SerialNumber})");

            var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\n", AutoFlush = true };

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    string? line = await reader.ReadLineAsync(ct);
                    if (line is null) break;

                    string? response = model.Handle(line);
                    _log?.Invoke($"  << {line,-14} >> {response ?? "(no response)"}");
                    if (response is not null) await writer.WriteLineAsync(response);
                }
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException) { }

            _log?.Invoke("Client disconnected");
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();
        if (_acceptLoop is not null)
        {
            try { await _acceptLoop; } catch { /* stopping */ }
        }
        _cts.Dispose();
    }
}
