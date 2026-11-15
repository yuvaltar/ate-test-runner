using System.IO.Ports;

namespace Ate.Core.Transport;

/// <summary>Connects to a DUT over a serial / UART link (USB-to-serial adapter, RS-232, RS-485).</summary>
public sealed class SerialTransport : StreamLineTransport
{
    private readonly string _portName;
    private readonly int _baudRate;
    private SerialPort? _port;

    public SerialTransport(string portName, int baudRate = 115200)
    {
        _portName = portName;
        _baudRate = baudRate;
    }

    public override string Description => $"Serial {_portName}@{_baudRate}";

    public static string[] AvailablePorts() => SerialPort.GetPortNames();

    protected override Task<Stream> OpenStreamAsync(CancellationToken ct)
    {
        _port = new SerialPort(_portName, _baudRate, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None,
            NewLine = "\n",
        };
        _port.Open();
        return Task.FromResult(_port.BaseStream);
    }

    protected override void CloseLink()
    {
        if (_port is { IsOpen: true }) _port.Close();
    }
}
