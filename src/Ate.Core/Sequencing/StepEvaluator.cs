using System.Globalization;
using System.Text.RegularExpressions;

namespace Ate.Core.Sequencing;

/// <summary>
/// Pure pass/fail logic: given a step and the DUT's response, decide the result.
/// No I/O here, so every rule is easy to unit test.
/// </summary>
public static class StepEvaluator
{
    public const string OkResponse = "OK";

    public static Evaluation Evaluate(TestStep step, string response)
    {
        response = response.Trim();

        if (response.StartsWith("ERR", StringComparison.OrdinalIgnoreCase))
            return new Evaluation(StepStatus.Fail, null, $"DUT returned error: {response}");

        return step.Type switch
        {
            StepType.Command => EvaluateCommand(response),
            StepType.Query => EvaluateQuery(step, response),
            StepType.Measure => EvaluateMeasure(step, response),
            _ => throw new ArgumentOutOfRangeException(nameof(step), step.Type, "Unknown step type."),
        };
    }

    private static Evaluation EvaluateCommand(string response) =>
        string.Equals(response, OkResponse, StringComparison.OrdinalIgnoreCase)
            ? new Evaluation(StepStatus.Pass, null, "OK")
            : new Evaluation(StepStatus.Fail, null, $"Expected '{OkResponse}', got '{response}'");

    private static Evaluation EvaluateQuery(TestStep step, string response)
    {
        if (step.ExpectEquals is not null && !string.Equals(response, step.ExpectEquals, StringComparison.Ordinal))
            return new Evaluation(StepStatus.Fail, null, $"Expected '{step.ExpectEquals}', got '{response}'");

        if (step.ExpectRegex is not null && !Regex.IsMatch(response, step.ExpectRegex, RegexOptions.None, TimeSpan.FromSeconds(1)))
            return new Evaluation(StepStatus.Fail, null, $"'{response}' does not match /{step.ExpectRegex}/");

        return new Evaluation(StepStatus.Pass, null, "Match");
    }

    private static Evaluation EvaluateMeasure(TestStep step, string response)
    {
        // Invariant culture: "12.5" must parse the same on every PC, whatever its regional settings.
        if (!double.TryParse(response, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            || double.IsNaN(value) || double.IsInfinity(value))
        {
            return new Evaluation(StepStatus.Fail, null, $"Not a number: '{response}'");
        }

        string unit = step.Unit is null ? "" : " " + step.Unit;

        if (step.Min is { } min && value < min)
            return new Evaluation(StepStatus.Fail, value, $"{Format(value)}{unit} below min {Format(min)}{unit}");

        if (step.Max is { } max && value > max)
            return new Evaluation(StepStatus.Fail, value, $"{Format(value)}{unit} above max {Format(max)}{unit}");

        return new Evaluation(StepStatus.Pass, value, $"{Format(value)}{unit} in [{Format(step.Min)} .. {Format(step.Max)}]");
    }

    private static string Format(double? v) => v?.ToString("0.###", CultureInfo.InvariantCulture) ?? "-";
}
