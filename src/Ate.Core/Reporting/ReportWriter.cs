using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ate.Core.Sequencing;

namespace Ate.Core.Reporting;

/// <summary>Writes a run's results as CSV (for Excel / production database import), JSON and HTML.</summary>
public static class ReportWriter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Writes all three formats and returns their paths.</summary>
    public static IReadOnlyList<string> WriteAll(RunResult run, string directory)
    {
        Directory.CreateDirectory(directory);
        string baseName = BaseFileName(run);

        string csv = Path.Combine(directory, baseName + ".csv");
        string json = Path.Combine(directory, baseName + ".json");
        string html = Path.Combine(directory, baseName + ".html");

        File.WriteAllText(csv, ToCsv(run), Encoding.UTF8);
        File.WriteAllText(json, ToJson(run), Encoding.UTF8);
        File.WriteAllText(html, ToHtml(run), Encoding.UTF8);

        return new[] { csv, json, html };
    }

    public static string BaseFileName(RunResult run)
    {
        string sn = string.IsNullOrWhiteSpace(run.SerialNumber) ? "UNKNOWN" : run.SerialNumber;
        foreach (char c in Path.GetInvalidFileNameChars()) sn = sn.Replace(c, '_');
        return $"{sn}_{run.StartedAt:yyyyMMdd_HHmmss}_{(run.Passed ? "PASS" : "FAIL")}";
    }

    public static string ToCsv(RunResult run)
    {
        var sb = new StringBuilder();
        sb.AppendLine("SerialNumber,Station,Step,Command,Status,Value,Unit,Min,Max,Response,Message,Attempts,DurationMs");
        foreach (var s in run.Steps)
        {
            sb.AppendJoin(',',
                Csv(run.SerialNumber), Csv(run.Station), Csv(s.Name), Csv(s.Command), s.Status,
                Num(s.MeasuredValue), Csv(s.Unit), Num(s.Min), Num(s.Max),
                Csv(s.Response), Csv(s.Message), s.Attempts,
                s.Duration.TotalMilliseconds.ToString("0", Inv));
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public static string ToJson(RunResult run) =>
        JsonSerializer.Serialize(new
        {
            run.SequenceName,
            run.Product,
            run.SerialNumber,
            run.Station,
            Transport = run.TransportDescription,
            run.StartedAt,
            DurationMs = Math.Round(run.Duration.TotalMilliseconds),
            Result = run.Passed ? "PASS" : "FAIL",
            run.Steps,
        }, new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        });

    public static string ToHtml(RunResult run)
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? "");
        string verdict = run.Passed ? "PASS" : "FAIL";

        var rows = new StringBuilder();
        foreach (var s in run.Steps)
        {
            rows.Append("<tr class=\"").Append(s.Status.ToString().ToLowerInvariant()).Append("\">")
                .Append("<td>").Append(E(s.Name)).Append("</td>")
                .Append("<td><code>").Append(E(s.Command)).Append("</code></td>")
                .Append("<td>").Append(s.Status).Append("</td>")
                .Append("<td>").Append(E(Num(s.MeasuredValue))).Append(' ').Append(E(s.Unit)).Append("</td>")
                .Append("<td>").Append(E(Num(s.Min))).Append("</td>")
                .Append("<td>").Append(E(Num(s.Max))).Append("</td>")
                .Append("<td>").Append(E(s.Message)).Append("</td>")
                .Append("<td>").Append(s.Attempts).Append("</td>")
                .Append("<td>").Append(s.Duration.TotalMilliseconds.ToString("0", Inv)).Append("</td>")
                .AppendLine("</tr>");
        }

        return $$"""
            <!DOCTYPE html>
            <html lang="en"><head><meta charset="utf-8">
            <title>{{E(run.SerialNumber)}} - {{verdict}}</title>
            <style>
              body { font-family: Segoe UI, Arial, sans-serif; margin: 24px; color: #222; }
              .verdict { font-size: 28px; font-weight: 700; padding: 8px 16px; display: inline-block; border-radius: 6px; color: #fff; }
              .verdict.pass { background: #2e7d32; } .verdict.fail { background: #c62828; }
              table { border-collapse: collapse; width: 100%; margin-top: 16px; font-size: 14px; }
              th, td { border: 1px solid #ddd; padding: 6px 8px; text-align: left; }
              th { background: #f3f3f3; }
              tr.fail td, tr.error td { background: #fdecea; } tr.skipped td { color: #999; }
              dl { display: grid; grid-template-columns: max-content auto; gap: 4px 16px; }
              dt { font-weight: 600; }
            </style></head><body>
            <h1>{{E(run.SequenceName)}}</h1>
            <div class="verdict {{verdict.ToLowerInvariant()}}">{{verdict}}</div>
            <dl>
              <dt>Product</dt><dd>{{E(run.Product)}}</dd>
              <dt>Serial number</dt><dd>{{E(run.SerialNumber)}}</dd>
              <dt>Station</dt><dd>{{E(run.Station)}}</dd>
              <dt>Link</dt><dd>{{E(run.TransportDescription)}}</dd>
              <dt>Started</dt><dd>{{run.StartedAt:yyyy-MM-dd HH:mm:ss}}</dd>
              <dt>Duration</dt><dd>{{run.Duration.TotalSeconds.ToString("0.00", Inv)}} s</dd>
              <dt>Steps</dt><dd>{{run.PassCount}} passed, {{run.FailCount}} failed, {{run.Steps.Count}} total</dd>
            </dl>
            <table>
              <tr><th>Step</th><th>Command</th><th>Status</th><th>Value</th><th>Min</th><th>Max</th><th>Message</th><th>Attempts</th><th>ms</th></tr>
            {{rows}}</table>
            </body></html>
            """;
    }

    /// <summary>Quotes a CSV field when it contains a comma, quote or newline (RFC 4180).</summary>
    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }

    private static string Num(double? v) => v?.ToString("0.###", Inv) ?? "";
}
