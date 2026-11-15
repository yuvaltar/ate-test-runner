using Ate.Core.Sequencing;
using Ate.Core.Transport;

namespace Ate.Tests;

/// <summary>Runner logic tested with a scripted fake transport - no network, no hardware.</summary>
public class TestRunnerTests
{
    private sealed class FakeTransport : ITransport
    {
        private readonly Dictionary<string, Queue<string?>> _script = new();
        public List<string> Sent { get; } = new();

        /// <summary>Queue responses for a command; null = simulate a timeout. The last one repeats.</summary>
        public FakeTransport On(string command, params string?[] responses)
        {
            _script[command] = new Queue<string?>(responses);
            return this;
        }

        public string Description => "Fake";
        public bool IsConnected { get; private set; }
        public Task ConnectAsync(CancellationToken ct = default) { IsConnected = true; return Task.CompletedTask; }

        public Task<string> SendAsync(string command, TimeSpan timeout, CancellationToken ct = default)
        {
            Sent.Add(command);
            if (!_script.TryGetValue(command, out var queue)) return Task.FromResult("ERR:UNKNOWN_COMMAND");

            string? response = queue.Count > 1 ? queue.Dequeue() : queue.Peek();
            return response is null
                ? Task.FromException<string>(new TimeoutException($"timeout on {command}"))
                : Task.FromResult(response);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static TestSequence Sequence(bool stopOnFail, params TestStep[] steps) =>
        new() { Name = "T", StopOnFail = stopOnFail, DefaultTimeoutMs = 50, Steps = steps.ToList() };

    private static TestStep Vin(int retries = 0) =>
        new() { Name = "Vin", Type = StepType.Measure, Command = "MEAS:VIN?", Min = 30, Max = 60, Retries = retries };

    private static TestStep RelayOff() =>
        new() { Name = "Relay off", Type = StepType.Command, Command = "RELAY OFF", AlwaysRun = true };

    private static TestStep Fw() =>
        new() { Name = "FW", Command = "FW?", ExpectEquals = "1.4.2" };

    [Fact]
    public async Task All_steps_pass_gives_pass_and_reads_serial_number()
    {
        var fake = new FakeTransport().On("SN?", "SN42").On("MEAS:VIN?", "40.1").On("RELAY OFF", "OK");

        var result = await new TestRunner(fake).RunAsync(Sequence(true, Vin(), RelayOff()));

        Assert.True(result.Passed);
        Assert.Equal("SN42", result.SerialNumber);
        Assert.Equal(40.1, result.Steps[0].MeasuredValue);
    }

    [Fact]
    public async Task Retries_until_value_settles()
    {
        var fake = new FakeTransport().On("MEAS:VIN?", "12.0", "12.0", "40.0");

        var result = await new TestRunner(fake).RunAsync(Sequence(true, Vin(retries: 3)));

        Assert.True(result.Passed);
        Assert.Equal(3, result.Steps[0].Attempts);
    }

    [Fact]
    public async Task Fails_when_retries_run_out()
    {
        var fake = new FakeTransport().On("MEAS:VIN?", "12.0");

        var result = await new TestRunner(fake).RunAsync(Sequence(true, Vin(retries: 2)));

        Assert.False(result.Passed);
        Assert.Equal(StepStatus.Fail, result.Steps[0].Status);
        Assert.Equal(3, result.Steps[0].Attempts);
    }

    [Fact]
    public async Task Timeout_is_reported_as_error()
    {
        var fake = new FakeTransport().On("MEAS:VIN?", (string?)null);

        var result = await new TestRunner(fake).RunAsync(Sequence(true, Vin()));

        Assert.Equal(StepStatus.Error, result.Steps[0].Status);
        Assert.Contains("timeout", result.Steps[0].Message);
    }

    [Fact]
    public async Task Stop_on_fail_skips_rest_but_still_runs_cleanup_steps()
    {
        var fake = new FakeTransport().On("MEAS:VIN?", "5.0").On("FW?", "1.4.2").On("RELAY OFF", "OK");

        var result = await new TestRunner(fake).RunAsync(Sequence(true, Vin(), Fw(), RelayOff()));

        Assert.Equal(StepStatus.Fail, result.Steps[0].Status);
        Assert.Equal(StepStatus.Skipped, result.Steps[1].Status);
        Assert.Equal(StepStatus.Pass, result.Steps[2].Status);   // relay was still switched off
        Assert.DoesNotContain("FW?", fake.Sent);
    }

    [Fact]
    public async Task Without_stop_on_fail_every_step_runs()
    {
        var fake = new FakeTransport().On("MEAS:VIN?", "5.0").On("FW?", "1.4.2");

        var result = await new TestRunner(fake).RunAsync(Sequence(false, Vin(), Fw()));

        Assert.False(result.Passed);
        Assert.Equal(StepStatus.Pass, result.Steps[1].Status);
    }

    [Fact]
    public async Task Raises_event_for_every_step()
    {
        var fake = new FakeTransport().On("MEAS:VIN?", "40").On("FW?", "1.4.2");
        var runner = new TestRunner(fake);
        var seen = new List<string>();
        runner.StepCompleted += s => seen.Add(s.Name);

        await runner.RunAsync(Sequence(true, Vin(), Fw()));

        Assert.Equal(new[] { "Vin", "FW" }, seen);
    }
}
