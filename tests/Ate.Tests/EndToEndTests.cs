using Ate.Core.Reporting;
using Ate.Core.Sequencing;
using Ate.Core.Transport;
using Ate.DutSimulator;

namespace Ate.Tests;

/// <summary>
/// Full stack over a real TCP socket: runner -> TcpTransport -> simulator -> DutModel.
/// Each fault must be caught by the shipped production sequence.
/// </summary>
public class EndToEndTests
{
    private static readonly TestSequence ProductionSequence = TestSequence.LoadFromFile(
        Path.Combine(TestSequenceTests.FindRepoRoot(), "sequences", "opt450_end_of_line.json"));

    private static async Task<RunResult> RunAgainstSimulator(params Fault[] faults)
    {
        await using var server = new SimulatorServer(0, () => new DutModel(faults, seed: 1));
        server.Start();

        await using var transport = new TcpTransport("127.0.0.1", server.Port);
        return await new TestRunner(transport).RunAsync(ProductionSequence);
    }

    [Fact]
    public async Task Healthy_unit_passes()
    {
        var result = await RunAgainstSimulator();

        Assert.True(result.Passed, string.Join("\n", result.Steps.Select(s => $"{s.Name}: {s.Status} {s.Message}")));
        Assert.Equal("SIM0001234", result.SerialNumber);
    }

    [Theory]
    [InlineData(Fault.VoutHigh, "Output voltage")]
    [InlineData(Fault.SelfTest, "Self test")]
    [InlineData(Fault.FwOld, "Firmware version")]
    [InlineData(Fault.TempTimeout, "Board temperature")]
    public async Task Faulty_unit_fails_on_the_right_step(Fault fault, string failingStep)
    {
        var result = await RunAgainstSimulator(fault);

        Assert.False(result.Passed);
        var firstFailure = result.Steps.First(s => s.Status is StepStatus.Fail or StepStatus.Error);
        Assert.Equal(failingStep, firstFailure.Name);
        Assert.Equal(StepStatus.Pass, result.Steps[^1].Status); // relay always switched off at the end
    }

    [Fact]
    public async Task Slow_settling_input_passes_thanks_to_retries()
    {
        var result = await RunAgainstSimulator(Fault.VinSettle);

        Assert.True(result.Passed);
        Assert.Equal(3, result.Steps.Single(s => s.Name == "Input voltage").Attempts);
    }

    [Fact]
    public async Task Writes_csv_json_and_html_reports()
    {
        var result = await RunAgainstSimulator();
        string dir = Path.Combine(Path.GetTempPath(), "ate-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var files = ReportWriter.WriteAll(result, dir);

            Assert.Equal(3, files.Count);
            Assert.All(files, f => Assert.True(File.Exists(f)));
            Assert.Contains("_PASS", Path.GetFileName(files[0]));
            string csv = File.ReadAllText(files[0]);
            Assert.Equal(result.Steps.Count + 1, csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
