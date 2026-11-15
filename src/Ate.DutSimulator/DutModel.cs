using System.Globalization;

namespace Ate.DutSimulator;

/// <summary>Faults that can be injected to check that the tester really catches bad units.</summary>
public enum Fault
{
    /// <summary>Output voltage far above spec.</summary>
    VoutHigh,
    /// <summary>Never answers the temperature query (dead sensor / hung firmware).</summary>
    TempTimeout,
    /// <summary>Self-test reports error code 3.</summary>
    SelfTest,
    /// <summary>Old firmware version loaded.</summary>
    FwOld,
    /// <summary>Input voltage reads low for the first 2 queries, then settles (tests the retry logic).</summary>
    VinSettle,
}

/// <summary>
/// A simulated power optimizer: answers SCPI-style text commands the way a real unit's
/// test firmware would. Pure logic (no sockets), so it can be unit tested directly.
/// </summary>
public sealed class DutModel
{
    private readonly Random _random;
    private readonly HashSet<Fault> _faults;
    private int _vinReads;

    public string Model { get; init; } = "OPT-450";
    public string SerialNumber { get; init; } = "SIM0001234";
    public string Firmware => _faults.Contains(Fault.FwOld) ? "1.3.9" : "1.4.2";
    public bool RelayOn { get; private set; }

    public DutModel(IEnumerable<Fault>? faults = null, int? seed = null)
    {
        _faults = new HashSet<Fault>(faults ?? Enumerable.Empty<Fault>());
        _random = seed is null ? new Random() : new Random(seed.Value);
    }

    /// <summary>Returns the response line, or null when the unit should not answer.</summary>
    public string? Handle(string rawCommand)
    {
        string cmd = rawCommand.Trim().ToUpperInvariant();

        switch (cmd)
        {
            case "*IDN?": return $"SIMTECH,{Model},{SerialNumber},{Firmware}";
            case "*RST": RelayOn = false; _vinReads = 0; return "OK";
            case "SN?": return SerialNumber;
            case "FW?": return Firmware;
            case "SELFTEST?": return _faults.Contains(Fault.SelfTest) ? "3" : "0";

            case "RELAY ON": RelayOn = true; return "OK";
            case "RELAY OFF": RelayOn = false; return "OK";
            case "RELAY?": return RelayOn ? "ON" : "OFF";

            case "MEAS:VIN?":
                _vinReads++;
                if (_faults.Contains(Fault.VinSettle) && _vinReads <= 2) return Num(Noise(12.0, 0.5));
                return Num(Noise(40.0, 0.3));

            case "MEAS:VOUT?":
                // With the output relay off the unit stays in safe mode (~1 V on the output).
                if (!RelayOn) return Num(Noise(1.0, 0.05));
                return Num(_faults.Contains(Fault.VoutHigh) ? Noise(65.0, 0.5) : Noise(39.5, 0.3));

            case "MEAS:IOUT?":
                return Num(RelayOn ? Noise(8.2, 0.1) : Noise(0.0, 0.01));

            case "MEAS:TEMP?":
                return _faults.Contains(Fault.TempTimeout) ? null : Num(Noise(32.0, 1.0));

            default:
                return "ERR:UNKNOWN_COMMAND";
        }
    }

    private double Noise(double nominal, double spread) => nominal + (_random.NextDouble() * 2 - 1) * spread;

    private static string Num(double v) => Math.Max(0, v).ToString("0.000", CultureInfo.InvariantCulture);

    public static Fault ParseFault(string name) => name.Trim().ToLowerInvariant() switch
    {
        "vout-high" => Fault.VoutHigh,
        "temp-timeout" => Fault.TempTimeout,
        "selftest" => Fault.SelfTest,
        "fw-old" => Fault.FwOld,
        "vin-settle" => Fault.VinSettle,
        _ => throw new ArgumentException(
            $"Unknown fault '{name}'. Valid: vout-high, temp-timeout, selftest, fw-old, vin-settle"),
    };
}
