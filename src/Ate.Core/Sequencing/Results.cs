namespace Ate.Core.Sequencing;

public enum StepStatus
{
    Pass,
    Fail,
    /// <summary>Could not get a valid answer (timeout, lost connection).</summary>
    Error,
    /// <summary>Not run because an earlier step failed and StopOnFail is set.</summary>
    Skipped,
}

public sealed record StepResult(
    string Name,
    string Command,
    StepStatus Status,
    string? Response,
    double? MeasuredValue,
    string? Unit,
    double? Min,
    double? Max,
    string Message,
    int Attempts,
    TimeSpan Duration);

public sealed record RunResult(
    string SequenceName,
    string? Product,
    string? SerialNumber,
    string Station,
    string TransportDescription,
    DateTimeOffset StartedAt,
    TimeSpan Duration,
    IReadOnlyList<StepResult> Steps)
{
    /// <summary>A unit passes only if every single step passed.</summary>
    public bool Passed => Steps.Count > 0 && Steps.All(s => s.Status == StepStatus.Pass);

    public int PassCount => Steps.Count(s => s.Status == StepStatus.Pass);
    public int FailCount => Steps.Count(s => s.Status is StepStatus.Fail or StepStatus.Error);
}

/// <summary>Outcome of checking one response against a step's expectations.</summary>
public sealed record Evaluation(StepStatus Status, double? Value, string Message);
