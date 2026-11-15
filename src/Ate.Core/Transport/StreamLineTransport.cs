using System.Text;
using System.Threading.Channels;

namespace Ate.Core.Transport;

/// <summary>
/// Shared logic for any byte stream (TCP socket, serial port):
/// a background loop reads incoming lines into a channel, and <see cref="SendAsync"/>
/// writes a command and waits for the next line with a timeout.
///
/// Why a background reader: if a response arrives late (after we already timed out),
/// it would otherwise be read as the answer to the NEXT command and shift every
/// result by one. Here, stale lines are discarded before each new command is sent.
/// </summary>
public abstract class StreamLineTransport : ITransport
{
    private const string Terminator = "\n";

    private Stream? _stream;
    private Channel<string> _lines = Channel.CreateUnbounded<string>();
    private CancellationTokenSource? _readerCts;
    private Task? _readerTask;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public abstract string Description { get; }

    public bool IsConnected => _stream is not null && _readerTask is { IsCompleted: false };

    /// <summary>Opens the underlying link and returns its stream.</summary>
    protected abstract Task<Stream> OpenStreamAsync(CancellationToken ct);

    /// <summary>Closes the underlying link (socket / port).</summary>
    protected abstract void CloseLink();

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (IsConnected) return;

        _stream = await OpenStreamAsync(ct).ConfigureAwait(false);
        _lines = Channel.CreateUnbounded<string>();
        _readerCts = new CancellationTokenSource();
        _readerTask = Task.Run(() => ReadLoopAsync(_stream, _lines.Writer, _readerCts.Token));
    }

    public async Task<string> SendAsync(string command, TimeSpan timeout, CancellationToken ct = default)
    {
        if (_stream is null) throw new InvalidOperationException("Transport is not connected.");

        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Drop late responses from earlier timed-out commands.
            while (_lines.Reader.TryRead(out _)) { }

            byte[] payload = Encoding.ASCII.GetBytes(command + Terminator);
            await _stream.WriteAsync(payload, ct).ConfigureAwait(false);
            await _stream.FlushAsync(ct).ConfigureAwait(false);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);
            try
            {
                return await _lines.Reader.ReadAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException($"No response to '{command}' within {timeout.TotalMilliseconds:0} ms.");
            }
            catch (ChannelClosedException)
            {
                throw new IOException($"Connection lost while waiting for response to '{command}'.");
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private static async Task ReadLoopAsync(Stream stream, ChannelWriter<string> writer, CancellationToken ct)
    {
        var buffer = new byte[1024];
        var current = new StringBuilder();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
                if (read == 0) break; // remote side closed

                foreach (char c in Encoding.ASCII.GetString(buffer, 0, read))
                {
                    if (c == '\n')
                    {
                        writer.TryWrite(current.ToString().TrimEnd('\r'));
                        current.Clear();
                    }
                    else
                    {
                        current.Append(c);
                    }
                }
            }
        }
        catch (Exception) when (ct.IsCancellationRequested) { /* normal shutdown */ }
        catch (IOException) { /* link dropped */ }
        catch (ObjectDisposedException) { /* link closed */ }
        finally
        {
            writer.TryComplete();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _readerCts?.Cancel();
        CloseLink();
        if (_readerTask is not null)
        {
            try { await _readerTask.ConfigureAwait(false); } catch { /* already shutting down */ }
        }
        _stream?.Dispose();
        _stream = null;
        _readerCts?.Dispose();
        _sendLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
