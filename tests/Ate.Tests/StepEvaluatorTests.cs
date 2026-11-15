using Ate.Core.Sequencing;

namespace Ate.Tests;

public class StepEvaluatorTests
{
    private static TestStep Measure(double? min, double? max) =>
        new() { Name = "V", Type = StepType.Measure, Command = "MEAS:V?", Min = min, Max = max, Unit = "V" };

    [Theory]
    [InlineData("40.0", StepStatus.Pass)]
    [InlineData("30", StepStatus.Pass)]      // exactly on the lower limit
    [InlineData("60", StepStatus.Pass)]      // exactly on the upper limit
    [InlineData("29.999", StepStatus.Fail)]
    [InlineData("60.001", StepStatus.Fail)]
    [InlineData("abc", StepStatus.Fail)]
    [InlineData("NaN", StepStatus.Fail)]
    [InlineData("", StepStatus.Fail)]
    public void Measure_checks_limits(string response, StepStatus expected)
    {
        var result = StepEvaluator.Evaluate(Measure(30, 60), response);
        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public void Measure_parses_value_with_invariant_culture()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            // German uses ',' as decimal separator - the DUT always sends '.'
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            var result = StepEvaluator.Evaluate(Measure(30, 60), "39.5");
            Assert.Equal(StepStatus.Pass, result.Status);
            Assert.Equal(39.5, result.Value);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Measure_with_only_max_limit()
    {
        Assert.Equal(StepStatus.Pass, StepEvaluator.Evaluate(Measure(null, 1.0), "0.2").Status);
        Assert.Equal(StepStatus.Fail, StepEvaluator.Evaluate(Measure(null, 1.0), "1.5").Status);
    }

    [Fact]
    public void Query_exact_match()
    {
        var step = new TestStep { Name = "FW", Command = "FW?", ExpectEquals = "1.4.2" };
        Assert.Equal(StepStatus.Pass, StepEvaluator.Evaluate(step, "1.4.2\r").Status);
        Assert.Equal(StepStatus.Fail, StepEvaluator.Evaluate(step, "1.3.9").Status);
    }

    [Fact]
    public void Query_regex_match()
    {
        var step = new TestStep { Name = "IDN", Command = "*IDN?", ExpectRegex = "^[A-Z]+,OPT-450,.+$" };
        Assert.Equal(StepStatus.Pass, StepEvaluator.Evaluate(step, "SIMTECH,OPT-450,SN1,1.4.2").Status);
        Assert.Equal(StepStatus.Fail, StepEvaluator.Evaluate(step, "SIMTECH,OPT-300,SN1,1.4.2").Status);
    }

    [Fact]
    public void Command_expects_ok()
    {
        var step = new TestStep { Name = "Relay", Type = StepType.Command, Command = "RELAY ON" };
        Assert.Equal(StepStatus.Pass, StepEvaluator.Evaluate(step, "OK").Status);
        Assert.Equal(StepStatus.Fail, StepEvaluator.Evaluate(step, "BUSY").Status);
    }

    [Fact]
    public void Error_response_always_fails()
    {
        var step = new TestStep { Name = "X", Command = "X?", ExpectRegex = ".*" };
        var result = StepEvaluator.Evaluate(step, "ERR:UNKNOWN_COMMAND");
        Assert.Equal(StepStatus.Fail, result.Status);
        Assert.Contains("ERR:UNKNOWN_COMMAND", result.Message);
    }
}
