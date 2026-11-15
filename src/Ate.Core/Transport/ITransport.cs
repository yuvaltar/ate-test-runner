namespace Ate.Core.Transport;

/// <summary>
/// A line-based link to a Device Under Test (DUT).
/// Every command sent gets exactly one response line back (SCPI-style).
/// </summary>
public interface ITransport : IAsyncDisposable
{
    /// <summary>Human readable description, e.g. "TCP 127.0.0.1:5025" or "Serial COM3@115200".</summary>
    string Description { get; }

    bool IsConnected { get; }

    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>Sends a command and waits for a single response line.</summary>
    /// <exception cref="TimeoutException">No response arrived within <paramref name="timeout"/>.</exception>
    Task<string> SendAsync(string command, TimeSpan timeout, CancellationToken ct = default);
}
