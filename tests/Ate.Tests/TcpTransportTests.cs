using System.Net;
using System.Net.Sockets;
using System.Text;
using Ate.Core.Transport;

namespace Ate.Tests;

/// <summary>Tests the transport against a tiny raw TCP server whose timing we control.</summary>
public class TcpTransportTests
{
    /// <summary>"SLOW?" answers after 300 ms, "FAST?" answers right away.</summary>
    private static (TcpListener listener, Task serverTask) StartTimingServer()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var task = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\n", AutoFlush = true };
            string? line;
            while ((line = await reader.ReadLineAsync()) is not null)
            {
                if (line == "SLOW?") { await Task.Delay(300); await writer.WriteLineAsync("LATE"); }
                else if (line == "FAST?") await writer.WriteLineAsync("FAST");
            }
        });
        return (listener, task);
    }

    [Fact]
    public async Task Returns_response_line()
    {
        var (listener, _) = StartTimingServer();
        try
        {
            await using var transport = new TcpTransport("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port);
            await transport.ConnectAsync();

            Assert.Equal("FAST", await transport.SendAsync("FAST?", TimeSpan.FromSeconds(1)));
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public async Task Late_response_is_discarded_and_does_not_shift_the_next_answer()
    {
        var (listener, _) = StartTimingServer();
        try
        {
            await using var transport = new TcpTransport("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port);
            await transport.ConnectAsync();

            await Assert.ThrowsAsync<TimeoutException>(
                () => transport.SendAsync("SLOW?", TimeSpan.FromMilliseconds(100)));

            await Task.Delay(400); // "LATE" arrives now, after we gave up on it

            // Without the stale-line flush this would wrongly return "LATE".
            Assert.Equal("FAST", await transport.SendAsync("FAST?", TimeSpan.FromSeconds(1)));
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public async Task Send_before_connect_throws()
    {
        await using var transport = new TcpTransport("127.0.0.1", 1);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => transport.SendAsync("X?", TimeSpan.FromMilliseconds(10)));
    }
}
