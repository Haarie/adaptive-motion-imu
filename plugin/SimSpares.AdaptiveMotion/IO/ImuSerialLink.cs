using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Threading;
using SimSpares.AdaptiveMotion.Core;

namespace SimSpares.AdaptiveMotion.IO
{
    /// <summary>
    /// Owns the COM port to the Arduino. Reads on a dedicated thread, reconnects on its own.
    /// Never scans other COM ports: opening an idle Nano-style port resets that board.
    /// </summary>
    public sealed class ImuSerialLink : IDisposable
    {
        private readonly FrameParser _parser = new FrameParser();
        private readonly object _txLock = new object();
        private Thread _thread;
        private volatile bool _run;
        private SerialPort _port;
        private string _portName;

        public static readonly Stopwatch Clock = Stopwatch.StartNew();
        public static double NowSec => Clock.Elapsed.TotalSeconds;

        public event Action<ImuFrame> Frame { add { _parser.ImuFrameReceived += value; } remove { _parser.ImuFrameReceived -= value; } }
        public event Action<string> Text { add { _parser.TextReceived += value; } remove { _parser.TextReceived -= value; } }
        public event Action<string> Log;

        public bool Connected => _port != null && _port.IsOpen;
        public string PortName => _portName;
        public FrameParser Parser => _parser;

        public void Start(string portName)
        {
            Stop();
            if (string.IsNullOrWhiteSpace(portName)) return;
            _portName = portName;
            _run = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "AdaptiveMotion.IMU", Priority = ThreadPriority.AboveNormal };
            _thread.Start();
        }

        public void Stop()
        {
            _run = false;
            try { _port?.Close(); } catch { }
            _thread?.Join(1000);
            _thread = null;
            _port = null;
        }

        public bool Send(string command)
        {
            lock (_txLock)
            {
                try
                {
                    if (!Connected) return false;
                    _port.Write(command + "\n");
                    return true;
                }
                catch (Exception e) { Log?.Invoke("TX failed: " + e.Message); return false; }
            }
        }

        private void Loop()
        {
            var buf = new byte[512];
            while (_run)
            {
                try
                {
                    _port = new SerialPort(_portName, 1000000)   // baud ignored by 32U4 CDC; used by Nano
                    {
                        DtrEnable = true,   // REQUIRED: the 32U4 only streams while DTR is asserted
                        RtsEnable = false,
                        ReadTimeout = 300,
                        WriteTimeout = 200,
                        ReadBufferSize = 16384
                    };
                    _port.Open();
                    Log?.Invoke("Opened " + _portName);
                    Thread.Sleep(50);
                    // A bare newline first: other software probing COM ports (e.g. SimHub's Arduino
                    // detection) can leave a half line in the device's command buffer -> "ERR unknown".
                    Send("");
                    Send("INFO");
                    var stream = _port.BaseStream;
                    while (_run && _port.IsOpen)
                    {
                        int n;
                        try { n = stream.Read(buf, 0, buf.Length); }
                        catch (TimeoutException) { continue; }
                        if (n <= 0) continue;
                        _parser.Feed(buf, n, Clock.ElapsedTicks);
                    }
                }
                catch (Exception e)
                {
                    if (_run) Log?.Invoke("Serial " + _portName + ": " + e.Message);
                }
                finally
                {
                    try { _port?.Close(); } catch { }
                }
                for (int i = 0; i < 10 && _run; i++) Thread.Sleep(100);   // 1 s reconnect back-off
            }
        }

        public void Dispose() => Stop();
    }
}
