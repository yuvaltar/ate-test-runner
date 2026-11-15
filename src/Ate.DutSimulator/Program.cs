using Ate.DutSimulator;

// Usage: Ate.DutSimulator [--port 5025] [--sn SIM0001234] [--fault vout-high] [--fault ...]
int port = 5025;
string serial = "SIM0001234";
var faults = new List<Fault>();

for (int i = 0; i < args.Length; i++)
{
    string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Missing value for {args[i]}");
    switch (args[i])
    {
        case "--port": port = int.Parse(Next()); break;
        case "--sn": serial = Next(); break;
        case "--fault": faults.Add(DutModel.ParseFault(Next())); break;
        case "-h" or "--help":
            Console.WriteLine("Ate.DutSimulator [--port 5025] [--sn SIM0001234] [--fault vout-high|temp-timeout|selftest|fw-old|vin-settle]");
            return 0;
        default:
            Console.Error.WriteLine($"Unknown argument: {args[i]}");
            return 1;
    }
}

await using var server = new SimulatorServer(port, () => new DutModel(faults) { SerialNumber = serial },
    msg => Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {msg}"));
server.Start();

Console.WriteLine($"DUT simulator listening on 127.0.0.1:{server.Port}");
Console.WriteLine(faults.Count == 0 ? "No faults injected (healthy unit)." : $"Injected faults: {string.Join(", ", faults)}");
Console.WriteLine("Press Ctrl+C to stop.");

var done = new TaskCompletionSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; done.TrySetResult(); };
await done.Task;
return 0;
