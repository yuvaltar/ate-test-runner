using System.Diagnostics;
using Ate.Core.Transport;

namespace Ate.Core.Sequencing;

/// <summary>Runs a <see cref="TestSequence"/> against one DUT and collects the results.</summary>
public sealed class TestRunner
{
    private readonly ITransport _transport;

    /// <summary>Raised after each step, so a UI / console can show live progress.</summary>
    public event Action<StepResult>? StepCompleted;

    public string Station { get; init; } = Environment.MachineName;

    /// <summary>Query used to read the unit's serial number for the report. Null to skip.</summary>
    public string? SerialNumberQuery { get; init; } = "SN?";

    public TestRunner(ITransport transport) => _transport = transport;

    public async Task<RunResult> RunAsync(TestSequence sequence, CancellationToken ct = default)
    {
        sequence.Validate();
        var started = DateTimeOffset.Now;
        var total = Stopwatch.StartNew();

        if (!_transport.IsConnected)
            await _transport.ConnectAsync(ct).ConfigureAwait(false);

        string? serialNumber = await TryReadSerialNumberAsync(sequence, ct).ConfigureAwait(false);

        var results = new List<StepResult>(sequence.Steps.Count);
        bool failed = false;

        foreach (var step in sequence.Steps)
        {
            ct.ThrowIfCancellationRequested();

            StepResult result = failed && sequence.StopOnFail && !step.AlwaysRun
                ? Skipped(step)
                : await RunStepAsync(step, sequence.DefaultTimeoutMs, ct).ConfigureAwait(false);

            if (result.Status is StepStatus.Fail or StepStatus.Error) failed = true;

            results.Add(result);
            StepCompleted?.Invoke(result);
        }

        return new RunResult(sequence.Name, sequence.Product, serialNumber, Station,
            _transport.Description, started, total.Elapsed, results);
    }

    private async Task<StepResult> RunStepAsync(TestStep step, int defaultTimeoutMs, CancellationToken ct)
    {
        var timeout = TimeSpan.FromMilliseconds(step.TimeoutMs ?? defaultTimeoutMs);
        var sw = Stopwatch.StartNew();
        int maxAttempts = 1 + step.Retries;

        string? response = null;
        Evaluation evaluation = new(StepStatus.Error, null, "Not run");
        int attempt = 0;

        while (attempt < maxAttempts)
        {
            attempt++;
            if (step.DelayMs > 0)
                await Task.Delay(step.DelayMs, ct).ConfigureAwait(false);

            try
            {
                response = await _transport.SendAsync(step.Command, timeout, ct).ConfigureAwait(false);
                evaluation = StepEvaluator.Evaluate(step, response);
            }
            catch (TimeoutException ex)
            {
                response = null;
                evaluation = new Evaluation(StepStatus.Error, null, ex.Message);
            }
            catch (IOException ex)
            {
                // Connection is gone: retrying will not help.
                return Build(step, StepStatus.Error, null, null, ex.Message, attempt, sw.Elapsed);
            }

            if (evaluation.Status == StepStatus.Pass) break;
        }

        return Build(step, evaluation.Status, response, evaluation.Value, evaluation.Message, attempt, sw.Elapsed);
    }

    private async Task<string?> TryReadSerialNumberAsync(TestSequence sequence, CancellationToken ct)
    {
        if (SerialNumberQuery is null) return null;
        try
        {
            string sn = (await _transport.SendAsync(SerialNumberQuery,
                TimeSpan.FromMilliseconds(sequence.DefaultTimeoutMs), ct).ConfigureAwait(false)).Trim();
            return sn.StartsWith("ERR", StringComparison.OrdinalIgnoreCase) ? null : sn;
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    private static StepResult Skipped(TestStep step) =>
        Build(step, StepStatus.Skipped, null, null, "Skipped after earlier failure", 0, TimeSpan.Zero);

    private static StepResult Build(TestStep step, StepStatus status, string? response, double? value,
        string message, int attempts, TimeSpan duration) =>
        new(step.Name, step.Command, status, response, value, step.Unit, step.Min, step.Max, message, attempts, duration);
}
