using Ate.Core.Sequencing;

namespace Ate.Tests;

public class TestSequenceTests
{
    [Fact]
    public void Parses_json_with_comments_and_enum_names()
    {
        var seq = TestSequence.Parse("""
            // header comment
            {
              "name": "Demo",
              "steps": [
                { "name": "V", "type": "measure", "command": "MEAS:V?", "min": 1, "max": 2, "retries": 2 },
                { "name": "R", "type": "command", "command": "RELAY ON" },
              ]
            }
            """);

        Assert.Equal("Demo", seq.Name);
        Assert.Equal(2, seq.Steps.Count);
        Assert.Equal(StepType.Measure, seq.Steps[0].Type);
        Assert.Equal(2, seq.Steps[0].Retries);
        Assert.True(seq.StopOnFail); // default
    }

    [Fact]
    public void Rejects_measure_without_limits()
    {
        var ex = Assert.Throws<InvalidDataException>(() => TestSequence.Parse("""
            { "name": "Bad", "steps": [ { "name": "V", "type": "measure", "command": "MEAS:V?" } ] }
            """));
        Assert.Contains("needs min and/or max", ex.Message);
    }

    [Fact]
    public void Rejects_min_greater_than_max()
    {
        Assert.Throws<InvalidDataException>(() => TestSequence.Parse("""
            { "name": "Bad", "steps": [ { "name": "V", "type": "measure", "command": "MEAS:V?", "min": 5, "max": 1 } ] }
            """));
    }

    [Fact]
    public void Shipped_sequence_file_is_valid()
    {
        string path = Path.Combine(FindRepoRoot(), "sequences", "opt450_end_of_line.json");
        var seq = TestSequence.LoadFromFile(path);
        Assert.NotEmpty(seq.Steps);
    }

    internal static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "sequences")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Could not find repo root.");
    }
}
