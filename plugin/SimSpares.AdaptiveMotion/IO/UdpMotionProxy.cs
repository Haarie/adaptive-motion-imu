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

        // --- test override (verification): the plugin drives the rig itself --------------------------
        private volatile Func<double[]> _override;
        private Thread _injector;
        private byte[] _template;                      // last raw SimHub motion packet (header/trailer bytes)
        private readonly object _tplLock = new object(), _sendLock = new object();
        public long Injected { get; private set; }
        public bool Injecting => _override != null;
        public bool HasTemplate { get { lock (_tplLock) return _template != null; } }

        /// <summary>
        /// Start sending the axis values returned by <paramref name="axes"/> every 10 ms instead of SimHub's
        /// motion packets (SimHub's packets are dropped meanwhile; other packets still pass). A null return
        /// skips that tick. Needs one motion packet from SimHub first (it supplies header/trailer bytes).
        /// </summary>
        public void BeginOverride(Func<double[]> axes)
        {
            if (!_run) throw new InvalidOperationException("proxy is not running");
            if (!HasTemplate) throw new InvalidOperationException("no motion packet from SimHub yet: switch SimHub motion output on");
            EndOverride();
            _override = axes;
            _injector = new Thread(Inject) { IsBackground = true, Name = "AdaptiveMotion.Test", Priority = ThreadPriority.Highest };
            _injector.Start();
            Log?.Invoke("Test mode: the plugin is driving the rig");
        }

        /// <summary>Hand the rig back to SimHub.</summary>
        public void EndOverride()
        {
            bool was = _override != null;
            _override = null;
            var t = _injector;
            if (t != null && t != Thread.CurrentThread) t.Join(500);
            _injector = null;
            if (was) Log?.Invoke("Test mode ended: SimHub drives the rig again");
        }

        private void Inject()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            long next = 0;
            while (_run)
            {
                var f = _override;
                if (f == null) break;
                try
                {
                    var axes = f();
                    if (axes != null)
                    {
                        byte[] pkt;
                        lock (_tplLock) pkt = MotionPacketCodec.Build(_xf.Layout, axes, _template, _template.Length);
                        lock (_sendLock) _tx.Send(pkt, pkt.Length, _target);
                        Injected++;
                        LastPacketSec = ImuSerialLink.NowSec;
                    }
                }
                catch (ObjectDisposedException) { break; }
                catch (Exception e) { Log?.Invoke("Test sender: " + e.Message); }
                next += 10;
                long wait = next - sw.ElapsedMilliseconds;
                if (wait > 0) Thread.Sleep((int)wait); else next = sw.ElapsedMilliseconds;
            }
        }

        public void Start(int listenPort, string targetHost, int targetPort, MotionPacketTransformer xf,
                          Func<GainSnapshot> gains, Func<bool> active)
        {
            Stop();
            lock (_tplLock) _template = null;
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
            _override = null;
            _run = false;
            try { _rx?.Close(); } catch { }
            try { _tx?.Close(); } catch { }
            _thread?.Join(1000);
            if (_injector != null && _injector != Thread.CurrentThread) _injector.Join(500);
            _thread = null; _injector = null; _rx = null; _tx = null;
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
                    if (MotionPacketCodec.IsMotionPacket(_xf.Layout, p, p.Length))
                    {
                        if (_override != null) continue;   // test mode: the plugin drives the rig, SimHub's packet is dropped
                        lock (_tplLock) _template = original;
                    }
                    bool motion;
                    var outp = _xf.ProcessAny(p, p.Length, _gains(), _active(), out motion);
                    lock (_sendLock) _tx.Send(outp, outp.Length, _target);
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
                    try { lock (_sendLock) _tx.Send(original, original.Length, _target); } catch { }
                    Log?.Invoke("Proxy: " + e.Message);
                }
            }
        }

        public void Dispose() => Stop();
    }
}
