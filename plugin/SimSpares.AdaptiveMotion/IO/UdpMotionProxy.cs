using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using SimSpares.AdaptiveMotion.Core;

namespace SimSpares.AdaptiveMotion.IO
{
    /// <summary>
    /// SimHub Generic-UDP motion output → (this proxy on 127.0.0.1) → your motion controller (nullCAT…).
    /// Each motion packet is rescaled with the current gains; everything else is forwarded verbatim.
    /// If this plugin stops, motion stops reaching the controller — the controller's own comms-loss
    /// timeout must park the rig (verify that before relying on this path).
    /// </summary>
    public sealed class UdpMotionProxy : IDisposable
    {
        private UdpClient _rx, _tx;
        private Thread _thread;
        private volatile bool _run;
        private IPEndPoint _target;
        private MotionPacketTransformer _xf;
        private Func<GainSnapshot> _gains;
        private Func<bool> _active;

        public event Action<string> Log;
        public event Action<double, double> CommandModes;   // pitchMode, rollMode after each motion packet
        public long Forwarded { get; private set; }
        public double LastPacketSec { get; private set; } = double.NaN;
        public MotionPacketTransformer Transformer => _xf;
        public bool Running => _run;

        public void Start(int listenPort, string targetHost, int targetPort, MotionPacketTransformer xf,
                          Func<GainSnapshot> gains, Func<bool> active)
        {
            Stop();
            _xf = xf; _gains = gains; _active = active;
            var addrs = Dns.GetHostAddresses(targetHost);
            IPAddress ip = null;
            foreach (var a in addrs) if (a.AddressFamily == AddressFamily.InterNetwork) { ip = a; break; }
            if (ip == null) throw new InvalidOperationException("Cannot resolve " + targetHost);
            _target = new IPEndPoint(ip, targetPort);
            if (IPAddress.IsLoopback(ip) && targetPort == listenPort)
                throw new InvalidOperationException("Proxy would forward to itself");

            _rx = new UdpClient(new IPEndPoint(IPAddress.Loopback, listenPort));
            _rx.Client.ReceiveTimeout = 500;
            _tx = new UdpClient();
            _run = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "AdaptiveMotion.UDP", Priority = ThreadPriority.Highest };
            _thread.Start();
            Log?.Invoke(string.Format("Proxy 127.0.0.1:{0} -> {1}", listenPort, _target));
        }

        public void Stop()
        {
            _run = false;
            try { _rx?.Close(); } catch { }
            try { _tx?.Close(); } catch { }
            _thread?.Join(1000);
            _thread = null; _rx = null; _tx = null;
        }

        private void Loop()
        {
            var from = new IPEndPoint(IPAddress.Any, 0);
            while (_run)
            {
                byte[] p;
                try { p = _rx.Receive(ref from); }
                catch (SocketException) { continue; }          // timeout
                catch (ObjectDisposedException) { break; }
                var original = (byte[])p.Clone();
                try
                {
                    bool motion = _xf.Process(p, p.Length, _gains(), _active());
                    _tx.Send(p, p.Length, _target);
                    Forwarded++;
                    LastPacketSec = ImuSerialLink.NowSec;
                    if (motion)
                    {
                        try { CommandModes?.Invoke(_xf.PitchMode, _xf.RollMode); }
                        catch (Exception e) { Log?.Invoke("Proxy listener: " + e.Message); }
                    }
                }
                catch (Exception e)
                {
                    // Forward the original if the transform failed — never swallow motion silently
                    try { _tx.Send(original, original.Length, _target); } catch { }
                    Log?.Invoke("Proxy: " + e.Message);
                }
            }
        }

        public void Dispose() => Stop();
    }
}
