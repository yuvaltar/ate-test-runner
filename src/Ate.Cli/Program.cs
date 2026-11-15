using Ate.Core.Reporting;
using Ate.Core.Sequencing;
using Ate.Core.Transport;

// Exit codes (so a production line / CI script can react): 0 = PASS, 1 = FAIL, 2 = setup/connection error.
const int ExitPass = 0, ExitFail = 1, ExitError = 2;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    PrintHelp();
    return ExitPass;
}

if (args[0] == "ports")
{
    var ports = SerialTransport.AvailablePorts();
    Console.WriteLine(ports.Length == 0 ? "No serial ports found." : string.Join(Environment.NewLine, ports));
    return ExitPass;
}

if (args[0] != "run")
{
    Console.Error.WriteLine($"Unknown command '{args[0]}'. Use --help.");
    return ExitError;
}

string? sequencePath = null, tcp = null, serial = null;
string outDir = "reports";
int baud = 115200;

for (int i = 1; i < args.Length; i++)
{
    string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Missing value for {args[i]}");
    switch (args[i])
    {
        case "--sequence" or "-s": sequencePath = Next(); break;
        case "--tcp": tcp = Next(); break;
        case "--serial": serial = Next(); break;
        case "--baud": baud = int.Parse(Next()); break;
        case "--out" or "-o": outDir = Next(); break;
        default:
            Console.Error.WriteLine($"Unknown argument: {args[i]}");
            return ExitError;
    }
}

if (sequencePath is null || (tcp is null) == (serial is null))
{
    Console.Error.WriteLine("Required: --sequence <file> and exactly one of --tcp <host:port> / --serial <port>.");
    return ExitError;
}

TestSequence sequence;
try
{
    sequence = TestSequence.LoadFromFile(sequencePath);
}
catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
{
    Console.Error.WriteLine($"Cannot load sequence: {ex.Message}");
    return ExitError;
}

await using ITransport transport = tcp is not null ? CreateTcp(tcp) : new SerialTransport(serial!, baud);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

Console.WriteLine($"Sequence : {sequence.Name} ({sequence.Steps.Count} steps)");
Console.WriteLine($"Link     : {transport.Description}");
try
{
    await transport.ConnectAsync(cts.Token);
}
catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"Cannot connect to DUT: {ex.Message}");
    return ExitError;
}

var runner = new TestRunner(transport);
runner.StepCompleted += PrintStep;

RunResult result;
try
{
    result = await runner.RunAsync(sequence, cts.Token);
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Aborted by operator.");
    return ExitError;
}

Console.WriteLine();
Console.WriteLine($"Serial   : {result.SerialNumber ?? "(unknown)"}");
Console.WriteLine($"Duration : {result.Duration.TotalSeconds:0.00} s");
WriteColored(result.Passed ? ConsoleColor.Green : ConsoleColor.Red,
    $"RESULT   : {(result.Passed ? "PASS" : "FAIL")}  ({result.PassCount}/{result.Steps.Count} steps passed)");

foreach (string file in ReportWriter.WriteAll(result, outDir))
    Console.WriteLine($"Report   : {file}");

return result.Passed ? ExitPass : ExitFail;

// ---------------------------------------------------------------------------

static TcpTransport CreateTcp(string endpoint)
{
    int colon = endpoint.LastIndexOf(':');
    if (colon <= 0 || !int.TryParse(endpoint[(colon + 1)..], out int port))
        throw new ArgumentException($"--tcp must be host:port, got '{endpoint}'");
    return new TcpTransport(endpoint[..colon], port);
}

static void PrintStep(StepResult s)
{
    var (color, tag) = s.Status switch
    {
        StepStatus.Pass => (ConsoleColor.Green, "PASS"),
        StepStatus.Fail => (ConsoleColor.Red, "FAIL"),
        StepStatus.Error => (ConsoleColor.Magenta, "ERR "),
        _ => (ConsoleColor.DarkGray, "SKIP"),
    };
    Console.Write("  [");
    WriteColored(color, tag, newLine: false);
    string retry = s.Attempts > 1 ? $" (attempt {s.Attempts})" : "";
    Console.WriteLine($"] {s.Name,-28} {s.Message}{retry}");
}

static void WriteColored(ConsoleColor color, string text, bool newLine = true)
{
    var previous = Console.ForegroundColor;
    Console.ForegroundColor = color;
    if (newLine) Console.WriteLine(text); else Console.Write(text);
    Console.ForegroundColor = previous;
}

static void PrintHelp() => Console.WriteLine("""
    ATE Test Runner - runs a production test sequence against a device under test.

    Usage:
      Ate.Cli run --sequence <file.json> --tcp <host:port> [--out <dir>]
      Ate.Cli run --sequence <file.json> --serial <COM3> [--baud 115200] [--out <dir>]
      Ate.Cli ports            List serial ports on this PC

    Exit codes: 0 = PASS, 1 = FAIL, 2 = setup / connection error
    """);
