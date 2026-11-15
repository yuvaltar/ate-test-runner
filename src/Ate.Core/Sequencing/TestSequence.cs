using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ate.Core.Sequencing;

public enum StepType
{
    /// <summary>Send a command, expect "OK".</summary>
    Command,
    /// <summary>Send a query, compare the text response (exact match or regex).</summary>
    Query,
    /// <summary>Send a query, parse a number, check it against min/max limits.</summary>
    Measure,
}

public sealed class TestStep
{
    public required string Name { get; init; }
    public StepType Type { get; init; } = StepType.Query;
    public required string Command { get; init; }

    // Query checks
    public string? ExpectEquals { get; init; }
    public string? ExpectRegex { get; init; }

    // Measure limits
    public double? Min { get; init; }
    public double? Max { get; init; }
    public string? Unit { get; init; }

    /// <summary>Extra attempts after a failure or timeout (useful for values that need to settle).</summary>
    public int Retries { get; init; }
    public int? TimeoutMs { get; init; }

    /// <summary>Wait before sending the command (e.g. let a relay settle).</summary>
    public int DelayMs { get; init; }

    /// <summary>Always run this step, even after a failure with StopOnFail (e.g. "turn relay off").</summary>
    public bool AlwaysRun { get; init; }
}

public sealed class TestSequence
{
    public required string Name { get; init; }
    public string? Product { get; init; }
    public bool StopOnFail { get; init; } = true;
    public int DefaultTimeoutMs { get; init; } = 1000;
    public required List<TestStep> Steps { get; init; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static TestSequence LoadFromFile(string path) => Parse(File.ReadAllText(path));

    public static TestSequence Parse(string json)
    {
        var sequence = JsonSerializer.Deserialize<TestSequence>(json, JsonOptions)
                       ?? throw new InvalidDataException("Sequence file is empty.");
        sequence.Validate();
        return sequence;
    }

    /// <summary>Catches mistakes in the sequence file before touching the hardware.</summary>
    public void Validate()
    {
        if (Steps.Count == 0) throw new InvalidDataException("Sequence has no steps.");

        var errors = new List<string>();
        foreach (var step in Steps)
        {
            if (string.IsNullOrWhiteSpace(step.Command))
                errors.Add($"Step '{step.Name}': command is empty.");

            if (step.Type == StepType.Measure && step.Min is null && step.Max is null)
                errors.Add($"Step '{step.Name}': measure step needs min and/or max.");

            if (step.Min is { } min && step.Max is { } max && min > max)
                errors.Add($"Step '{step.Name}': min ({min}) is greater than max ({max}).");

            if (step.Type == StepType.Query && step.ExpectEquals is null && step.ExpectRegex is null)
                errors.Add($"Step '{step.Name}': query step needs expectEquals or expectRegex.");

            if (step.Retries < 0)
                errors.Add($"Step '{step.Name}': retries cannot be negative.");
        }

        if (errors.Count > 0)
            throw new InvalidDataException("Invalid sequence:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
    }
}
