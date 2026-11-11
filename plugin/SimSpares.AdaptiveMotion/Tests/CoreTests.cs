// Stand-alone tests for the SimHub-independent core. Run (Mono or .NET Framework):
//   mcs -out:CoreTests.exe Tests/CoreTests.cs Core/*.cs && mono CoreTests.exe [frames.bin]
using System;
using System.IO;
using System.Collections.Generic;
using SimSpares.AdaptiveMotion.Core;

static class CoreTests
{
    static int fails = 0, passes = 0;
    static void Check(bool ok, string what)
    {
        if (ok) passes++; else { fails++; Console.WriteLine("FAIL: " + what); }
    }
    static bool Near(double a, double b, double tol) => Math.Abs(a - b) <= tol;

    static int Main(string[] args)
    {
        ParserTests(args.Length > 0 ? args[0] : null);
        EstimatorTests();
        ControllerTests();
        ScalerTests();
        PacketTests();
        LagTests();
        Console.WriteLine($"{passes} passed, {fails} failed");
        return fails == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------------------------------
    static byte[] BuildFrame(byte type, byte seq, byte[] payload)
    {
        var f = new List<byte> { 0xA5, 0x5A, type, (byte)payload.Length, seq };
        f.AddRange(payload);
        var arr = f.ToArray();
        f.Add(Crc8.Compute(arr, 2, 3 + payload.Length));
        return f.ToArray();
    }

    static byte[] ImuPayload(short ax, short ay, short az, short q0, short q1, short q2, short q3, ushort peak, byte flags)
    {
        var p = new List<byte>();
        p.AddRange(BitConverter.GetBytes((uint)123456));
        foreach (var v in new short[] { ax, ay, az, 10, -20, 30, q0, q1, q2, q3 }) p.AddRange(BitConverter.GetBytes(v));
        p.AddRange(BitConverter.GetBytes(peak));
        p.AddRange(BitConverter.GetBytes((ushort)55));
        p.Add(flags); p.Add(0);
        return p.ToArray();
    }

    static void ParserTests(string externalFrames)
    {
        // CRC check value for "123456789" with poly 0x07 init 0 is 0xF4
        var s = System.Text.Encoding.ASCII.GetBytes("123456789");
        Check(Crc8.Compute(s, 0, s.Length) == 0xF4, "CRC-8 check value");

        var parser = new FrameParser();
        var frames = new List<ImuFrame>(); var texts = new List<string>();
        parser.ImuFrameReceived += frames.Add; parser.TextReceived += texts.Add;

        var good = BuildFrame(1, 7, ImuPayload(12, -34, 1001, 16384, 0, 0, 0, 250, 0x02));
        var text = BuildFrame(2, 8, System.Text.Encoding.ASCII.GetBytes("PONG"));
        var bad = (byte[])good.Clone(); bad[10] ^= 0xFF;
        var stream = new List<byte> { 0x00, 0xA5, 0x13 };         // junk incl. a false sync
        stream.AddRange(good); stream.AddRange(bad); stream.AddRange(text); stream.AddRange(good);
        var arr = stream.ToArray();
        // feed in awkward chunk sizes
        for (int i = 0; i < arr.Length; i += 7)
        {
            int n = Math.Min(7, arr.Length - i);
            var chunk = new byte[n]; Array.Copy(arr, i, chunk, 0, n);
            parser.Feed(chunk, n, 0);
        }
        Check(frames.Count == 2, "two good IMU frames decoded, got " + frames.Count);
        Check(texts.Count == 1 && texts[0] == "PONG", "text frame decoded");
        Check(parser.CrcErrors == 1, "one CRC error counted, got " + parser.CrcErrors);
        if (frames.Count > 0)
        {
            var f = frames[0];
            Check(Near(f.Ax, 0.012, 1e-9) && Near(f.Ay, -0.034, 1e-9) && Near(f.Az, 1.001, 1e-9), "accel scaling");
            Check(Near(f.Gx, 1.0, 1e-9) && Near(f.Gy, -2.0, 1e-9), "gyro scaling");
            Check(Near(f.Q0, 1.0, 1e-9) && Near(f.PeakDevG, 0.25, 1e-9), "quat/peak scaling");
            Check(f.Flags == ImuFlags.GyroCalibrated && f.DeviceMicros == 123456, "flags/time");
        }

        // Optional: frames produced by the firmware's own encoder (host build), see firmware/host_test
        if (externalFrames != null && File.Exists(externalFrames))
        {
            var ext = File.ReadAllBytes(externalFrames);
            var p2 = new FrameParser(); var got = new List<ImuFrame>();
            p2.ImuFrameReceived += got.Add;
            p2.Feed(ext, ext.Length, 0);
            Check(got.Count == 3 && p2.CrcErrors == 0, $"firmware-encoded frames: {got.Count} ok, {p2.CrcErrors} crc errors");
            if (got.Count == 3)
                Check(Near(got[2].Ay, 0.7071, 0.002) && Near(got[2].Az, 0.7071, 0.002) && Near(got[2].Q0, 0.9239, 0.001) && Near(got[2].Q1, 0.3827, 0.001),
                      $"firmware frame values az={got[2].Az} q0={got[2].Q0} q1={got[2].Q1}");
        }
    }

    // ---------------------------------------------------------------------------------------
    static ImuFrame FrameFromEuler(double rollDeg, double pitchDeg, double ax = 0, double ay = 0, double az = 1)
    {
        double r = rollDeg * Math.PI / 360, p = pitchDeg * Math.PI / 360;
        // same convention as the firmware's seedFromAccel (yaw 0)
        return new ImuFrame
        {
            Q0 = Math.Cos(r) * Math.Cos(p), Q1 = Math.Sin(r) * Math.Cos(p),
            Q2 = Math.Cos(r) * Math.Sin(p), Q3 = -Math.Sin(r) * Math.Sin(p),
            Ax = ax, Ay = ay, Az = az
        };
    }

    static void EstimatorTests()
    {
        var e = new PlatformEstimator();
        e.SetMounting(Axis.PosX, Axis.PosZ);
        // firmware convention: pitch = atan2(-ax, ...) i.e. nose-up gives negative sensor ax... the
        // quaternion "up" vector x-component is -sin(pitch_fw). Rig +pitch = nose up = up.x > 0.
        var st = e.Update(FrameFromEuler(0, -5), 0);
        Check(Near(st.PitchDeg, 5, 0.01), "nose-up pitch positive, got " + st.PitchDeg);
        st = e.Update(FrameFromEuler(4, 0), 0);
        Check(Near(st.RollDeg, 4, 0.01), "roll sign, got " + st.RollDeg);
        e.SetLevelReference(0, 4);
        st = e.Update(FrameFromEuler(4, 0), 0);
        Check(Near(st.RollDeg, 0, 0.01), "level reference removes offset");

        // Sensor mounted rotated 90° (sensor +Y forward): a sensor-frame tilt about X is rig pitch
        var e2 = new PlatformEstimator();
        e2.SetMounting(Axis.PosY, Axis.PosZ);
        st = e2.Update(FrameFromEuler(6, 0), 0);
        Check(Near(Math.Abs(st.PitchDeg), 6, 0.01) && Near(st.RollDeg, 0, 0.01), $"rotated mounting maps roll->pitch ({st.PitchDeg},{st.RollDeg})");

        // specific force: 0.3 g forward cue
        st = e.Update(FrameFromEuler(4, 0, 0.3, 0, 1), 0);
        Check(Near(st.CueSurgeG, 0.3, 1e-9), "surge cue");
        bool threw = false; try { e.SetMounting(Axis.PosX, Axis.NegX); } catch (ArgumentException) { threw = true; }
        Check(threw, "invalid mounting rejected");
    }

    // ---------------------------------------------------------------------------------------
    static void ControllerTests()
    {
        var cs = new ControllerSettings();
        var c = new AdaptiveGainController(cs);
        double t = 0;
        Func<double, double, PlatformState> S2 = (pitch, peak) => new PlatformState { PitchDeg = pitch, PeakDevG = peak, TimeSec = t };
        Func<double, PlatformState> S = pitch => S2(pitch, 0);

        for (int i = 0; i < 250; i++) { t += 0.004; c.Update(S(2)); }
        Check(Near(c.Snapshot.Pitch, 1, 1e-9) && c.Snapshot.Status == ControllerStatus.Ok, "quiet ride keeps unity gain");

        // sustained over-pitch: 10° against an 8° limit -> pitch gain must come down, global untouched
        for (int i = 0; i < 250; i++) { t += 0.004; c.Update(S(10 * c.Snapshot.Pitch)); }   // plant responds to gain
        double gp = c.Snapshot.Pitch;
        Check(gp < 0.75 && gp > 0.6, "pitch compressed towards knee (expected ~0.68), got " + gp);
        Check(Near(c.Snapshot.Global, 1, 1e-9), "global untouched by pitch overload");
        Check(c.Snapshot.Status == ControllerStatus.Compressing, "status compressing");

        // calm -> slow release
        for (int i = 0; i < 250; i++) { t += 0.004; c.Update(S(1)); }
        double after1s = c.Snapshot.Pitch;
        Check(after1s > gp && after1s < 0.9, "release is slow (1 s), got " + after1s);
        for (int i = 0; i < 250 * 20; i++) { t += 0.004; c.Update(S(1)); }
        Check(c.Snapshot.Pitch > 0.99, "fully released after 20 s");

        // impact -> -10 % global and hold
        t += 0.004; c.Update(S2(0, 2.0));
        Check(Near(c.Snapshot.Global, 0.9, 1e-6) && c.ImpactCount == 1, "impact steps global down");
        for (int i = 0; i < 250; i++) { t += 0.004; c.Update(S(0)); }
        Check(Near(c.Snapshot.Global, 0.9, 1e-6) && c.Snapshot.Status == ControllerStatus.ImpactHold, "held during impact hold");

        // hard trip
        string tripMsg = null; c.TripRaised += m => tripMsg = m;
        t += 0.004; c.Update(S(15));
        Check(c.Tripped && tripMsg != null && Near(c.Snapshot.Global, cs.MinGain, 1e-9), "hard trip to min gain");
        for (int i = 0; i < 500; i++) { t += 0.004; c.Update(S(0)); }
        Check(c.Tripped, "trip latched without auto-reset");
        c.ResetTrip();
        for (int i = 0; i < 250 * 20; i++) { t += 0.004; c.Update(S(0)); }
        Check(!c.Tripped && c.Snapshot.Global > 0.95, "recovers after manual reset");

        // sensor loss -> ramp to failsafe, never up
        c.Tick(t);
        for (int i = 0; i < 120; i++) { t += 0.016; c.Tick(t); }
        Check(c.Snapshot.Status == ControllerStatus.SensorLost && Near(c.Snapshot.Global, cs.FailsafeGain, 0.01), "sensor loss -> failsafe gain, got " + c.Snapshot.Global);

        // all gains always within bounds under random abuse
        var rnd = new Random(1); bool inBounds = true;
        var c2 = new AdaptiveGainController(new ControllerSettings { TripAutoReset = true });
        for (int i = 0; i < 20000; i++)
        {
            t += 0.004;
            c2.Update(new PlatformState { PitchDeg = rnd.NextDouble() * 30 - 15, RollDeg = rnd.NextDouble() * 30 - 15, PeakDevG = rnd.NextDouble() * 2, VibRmsG = rnd.NextDouble(), CueSurgeG = rnd.NextDouble(), TimeSec = t });
            var g = c2.Snapshot;
            foreach (var v in new[] { g.Global, g.Pitch, g.Roll, g.Heave }) if (v < 0.3 - 1e-9 || v > 1 + 1e-9) inBounds = false;
        }
        Check(inBounds, "gains stay within [MinGain, 1] under random input");
    }

    // ---------------------------------------------------------------------------------------
    static void ScalerTests()
    {
        var g = new GainSnapshot(1.0, 0.5, 1.0, 1.0, ControllerStatus.Ok);   // halve pitch only
        var s4 = new ActuatorModalScaler(ActuatorLayout.FourCorner);
        var pitchOnly = new double[] { 100, 100, -100, -100 };
        s4.Apply(pitchOnly, 4, g);
        Check(Near(pitchOnly[0], 50, 1e-9) && Near(pitchOnly[2], -50, 1e-9) && Near(pitchOnly[1], 50, 1e-9), "4-corner pure pitch halved");
        var rollOnly = new double[] { 80, -80, 80, -80 };
        s4.Apply(rollOnly, 4, g);
        Check(Near(rollOnly[0], 80, 1e-9) && Near(rollOnly[1], -80, 1e-9), "4-corner roll untouched by pitch gain");

        var s3 = new ActuatorModalScaler(ActuatorLayout.ThreeFrontPairRear);
        var mixed = new double[] { 120, 40, -60 };
        var copy = (double[])mixed.Clone();
        s3.Apply(mixed, 3, GainSnapshot.Unity);
        Check(Near(mixed[0], copy[0], 1e-9) && Near(mixed[1], copy[1], 1e-9) && Near(mixed[2], copy[2], 1e-9), "3-actuator identity at unity gain");

        var s2 = new ActuatorModalScaler(ActuatorLayout.TwoLeftRight);
        var lr = new double[] { 100, 0, 30 };   // 3rd axis (e.g. traction loss) beyond layout
        s2.Apply(lr, 3, new GainSnapshot(0.5, 1, 0, 1, ControllerStatus.Ok));
        Check(Near(lr[0], 25, 1e-9) && Near(lr[1], 25, 1e-9) && Near(lr[2], 15, 1e-9), $"2-actuator roll removed + extra axis global ({lr[0]},{lr[1]},{lr[2]})");
    }

    static void PacketTests()
    {
        var l = new PacketLayout { HeaderBytes = 1, AxisCount = 4, BytesPerAxis = 2, BigEndian = true, MaxValue = 65535, TrailerBytes = 1 };
        var xf = new MotionPacketTransformer(l, ActuatorLayout.FourCorner);
        Func<int[], byte[]> PktA = v =>
        {
            var p = new byte[l.MotionPacketLength]; p[0] = 0xAA; p[p.Length - 1] = 0x55;
            MotionPacketCodec.Encode(l, Array.ConvertAll(v, x => (double)x), p);
            return p;
        };
        Func<int, int, int, int, byte[]> Pkt = (a0, a1, a2, a3) => PktA(new[] { a0, a1, a2, a3 });
        var a = Pkt(40000, 40000, 25535, 25535);
        var orig = (byte[])a.Clone();
        xf.Process(a, a.Length, new GainSnapshot(0.5, 0.5, 0.5, 0.5, ControllerStatus.Ok), active: false);
        Check(BytesEqual(a, orig), "inactive transform is byte-identical");

        var half = new GainSnapshot(1, 0.5, 1, 1, ControllerStatus.Ok);
        xf.Process(a, a.Length, half, active: true);
        var axes = new double[4]; MotionPacketCodec.Decode(l, a, axes);
        Check(Near(axes[0], 32767.5 + (40000 - 32767.5) / 2, 1) && a[0] == 0xAA && a[a.Length - 1] == 0x55, $"active pitch halved, header/trailer kept (got {axes[0]})");

        var other = new byte[] { 1, 2, 3 }; var oc = (byte[])other.Clone();
        Check(!xf.Process(other, other.Length, half, true) && BytesEqual(other, oc), "non-motion packet untouched");

        // never further from neutral than the furthest original actuator, never outside range
        var rnd = new Random(3); bool ok = true;
        var xf3 = new MotionPacketTransformer(l, ActuatorLayout.FourCorner);
        for (int i = 0; i < 5000; i++)
        {
            var v = new int[4]; for (int k = 0; k < 4; k++) v[k] = rnd.Next(0, 65536);
            var p = PktA(v);
            var g = new GainSnapshot(0.3 + rnd.NextDouble() * 0.7, 0.3 + rnd.NextDouble() * 0.7, 0.3 + rnd.NextDouble() * 0.7, 0.3 + rnd.NextDouble() * 0.7, ControllerStatus.Ok);
            xf3.Process(p, p.Length, g, true);
            var o = new double[4]; MotionPacketCodec.Decode(l, p, o);
            double maxIn = 0, maxOut = 0;
            for (int k = 0; k < 4; k++) { maxIn = Math.Max(maxIn, Math.Abs(v[k] - 32767.5)); maxOut = Math.Max(maxOut, Math.Abs(o[k] - 32767.5)); }
            if (maxOut > maxIn + 1) ok = false;
        }
        Check(ok, "output envelope never exceeds input envelope (5000 random packets)");

        // neutral capture
        var xf4 = new MotionPacketTransformer(l, ActuatorLayout.GlobalOnly);
        xf4.BeginNeutralCapture();
        for (int i = 0; i < 20; i++) { var p = Pkt(1000, 2000, 3000, 4000); xf4.Process(p, p.Length, GainSnapshot.Unity, false); }
        Check(xf4.EndNeutralCapture() && Near(xf4.Neutral[0], 1000, 1e-9) && Near(xf4.Neutral[3], 4000, 1e-9), "neutral capture averages");
        var lowNeutral = Pkt(1500, 2000, 3000, 4000);
        xf4.Process(lowNeutral, lowNeutral.Length, new GainSnapshot(0.5, 1, 1, 1, ControllerStatus.Ok), true);
        var o4 = new double[4]; MotionPacketCodec.Decode(l, lowNeutral, o4);
        Check(Near(o4[0], 1250, 1) && Near(o4[1], 2000, 1), "scales around captured neutral, not mid-scale");

        // 8-bit layout
        var l8 = new PacketLayout { AxisCount = 2, BytesPerAxis = 1, MaxValue = 255 };
        var p8 = new byte[] { 255, 0 }; var x8 = new MotionPacketTransformer(l8, ActuatorLayout.GlobalOnly);
        x8.Process(p8, 2, new GainSnapshot(0.5, 1, 1, 1, ControllerStatus.Ok), true);
        Check(p8[0] == 191 && p8[1] == 64, $"8-bit scaling ({p8[0]},{p8[1]})");
    }

    static bool BytesEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    // ---------------------------------------------------------------------------------------
    static void LagTests()
    {
        var le = new LagEstimator(250, 3, 200);
        var rnd = new Random(5);
        // band-limited random command, platform = 3.5 * cmd delayed 40 ms (10 samples) + noise
        var hist = new Queue<double>();
        double x = 0;
        for (int i = 0; i < 250 * 8; i++)
        {
            x += (rnd.NextDouble() - 0.5) * 0.05; x *= 0.995;
            hist.Enqueue(x);
            double delayed = hist.Count > 10 ? hist.Dequeue() : 0;
            le.Push(x, 3.5 * delayed + (rnd.NextDouble() - 0.5) * 0.01);
        }
        Check(le.Valid && Near(le.DelayMs, 40, 4.1) && Near(le.Scale, 3.5, 0.2), $"lag estimator delay={le.DelayMs} r={le.Coherence:F3} scale={le.Scale:F2}");
    }
}
