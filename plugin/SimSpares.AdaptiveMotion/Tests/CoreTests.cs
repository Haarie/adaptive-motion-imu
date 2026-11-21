// Stand-alone tests for the SimHub-independent core. Run (Mono or .NET Framework):
//   mcs -out:CoreTests.exe Tests/CoreTests.cs Core/*.cs && mono CoreTests.exe [frames.bin]
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
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
        ParkTests();
        VisualTests();
        AlignTests();
        GeometryTests();
        VerifyTests();
        ImportTests();
        TextPacketTests();
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
        // mechanical maximum and hard trip (4-corner 1250 x 660 mm, stroke 200 mm: SimHub reports 9.09° / 16.86°)
        var gm = new RigGeometry { StrokeMm = 200, WheelbaseMm = 1250, TrackMm = 660 };
        gm.FillFromLayout(ActuatorLayout.FourCorner);
        Check(Near(gm.MechanicalMaxDeg(true), 9.09, 0.02) && Near(gm.MechanicalMaxDeg(false), 16.86, 0.02), $"mechanical max {gm.MechanicalMaxDeg(true):F2}/{gm.MechanicalMaxDeg(false):F2}");
        Check(Near(RigGeometry.HardTrip(5, 9.09), 7.0, 0.01) && Near(RigGeometry.HardTrip(7, 9.09), 8.8, 0.01) && Near(RigGeometry.HardTrip(7, 16.86), 9.4, 0.01)
              && Near(RigGeometry.HardTrip(8.9, 9.09), 9.4, 0.01) && Near(RigGeometry.HardTrip(5, double.NaN), 7.0, 0.01), "hard trip capped below the rig maximum");

        // driver offset: subtracted only while active (driving)
        e.SetLevelReference(0, 0);
        e.SetDriverOffset(-0.6, 0.1);
        st = e.Update(FrameFromEuler(0.1, 0.6), 0);          // seated pose: pitch -0.6 (nose down), roll 0.1
        Check(Near(st.PitchDeg, -0.6, 0.02) && Near(st.RollDeg, 0.1, 0.02), $"driver offset not applied when inactive ({st.PitchDeg:F2},{st.RollDeg:F2})");
        e.DriverOffsetActive = true;
        st = e.Update(FrameFromEuler(0.1, 0.6), 0);
        Check(Near(st.PitchDeg, 0, 0.02) && Near(st.RollDeg, 0, 0.02), $"driver offset makes the seated pose read 0 ({st.PitchDeg:F2},{st.RollDeg:F2})");
        e.DriverOffsetActive = false; e.SetDriverOffset(0, 0);
        e.SetLevelReference(0, 4);

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

    // ---------------------------------------------------------------------------------------
    static void ParkTests()
    {
        var pm = new ParkMonitor(new ParkSettings());
        double t = 0;
        double learnedP = double.NaN; int checks = 0; bool lastShifted = false;
        pm.ReferenceLearned += (p, r) => learnedP = p;
        pm.Checked += (d, sh) => { checks++; lastShifted = sh; };
        Action<double, double, bool, bool, double> run = (pitch, roll, still, game, secs) =>
        {
            for (int i = 0; i < secs * 250; i++)
            {
                t += 0.004;
                pm.Update(new PlatformState { PitchDeg = pitch, RollDeg = roll, TimeSec = t, Flags = still ? ImuFlags.Still : ImuFlags.None }, game);
            }
        };

        run(-3.0, 0.2, true, true, 10);     // still but a game is running: never a park check
        Check(pm.State == ParkState.Moving && !pm.HasReference, "no park check while a game runs");

        run(-3.0, 0.2, true, false, 4);     // first park: learn reference
        Check(pm.HasReference && Near(learnedP, -3.0, 1e-6) && pm.State == ParkState.Parked, "first park learns the parked pose");

        run(0, 0, false, false, 2);         // drive / move
        run(-3.2, 0.3, true, false, 4);     // park again, 0.22 deg off: OK
        Check(checks == 1 && !lastShifted && !pm.MountShifted && Near(pm.LastDeviationDeg, 0.2236, 0.001), "small difference passes");

        run(0, 0, false, false, 2);
        run(-2.0, 0.2, true, false, 4);     // 1 deg off: box shifted
        Check(checks == 2 && lastShifted && pm.MountShifted, "1 deg difference flags MountShifted");
        Check(Near(pm.RefPitchDeg, -3.0, 1e-9), "reference is never overwritten by a check");

        run(-2.0, 0.2, true, false, 10);    // staying parked: no repeated checks
        Check(checks == 2, "one check per park");

        pm.ClearReference();
        Check(!pm.HasReference && !pm.MountShifted, "clear reference resets warning");
        run(-2.0, 0.2, true, false, 4);     // still parked -> re-measures and learns the new pose
        Check(pm.HasReference && Near(pm.RefPitchDeg, -2.0, 1e-6), "re-learn after clear, " + pm.RefPitchDeg);

        // driver still seated after a session: ~0.55 deg of seat flex is learned as the seated pose, not a warning
        var pm3 = new ParkMonitor(new ParkSettings()); double t3 = 0; int seatedLearned = 0, warns = 0;
        pm3.SeatedReferenceLearned += (p, r) => seatedLearned++;
        pm3.Checked += (d, sh) => { if (sh) warns++; };
        Action<double, bool> park3 = (pitch, after) =>
        {
            pm3.AfterSession = after;
            for (int i = 0; i < 500; i++) { t3 += 0.004; pm3.Update(new PlatformState { TimeSec = t3 }, false); }                          // moving
            for (int i = 0; i < 1250; i++) { t3 += 0.004; pm3.Update(new PlatformState { PitchDeg = pitch, TimeSec = t3, Flags = ImuFlags.Still }, false); }
        };
        park3(-0.01, false);                 // empty rig: reference
        park3(-0.56, false);                 // 0.55 off, but not after a session: warning (could be a real shift)
        Check(warns == 1 && seatedLearned == 0, "seat-flex offset outside a session still warns");
        park3(-0.56, true);                  // right after a session with the driver seated: learned as seated pose
        Check(seatedLearned == 1 && warns == 1 && !pm3.MountShifted && pm3.LastWasSeated, "first park after a session learns the seated pose");
        park3(-0.60, true);                  // next session: matches the seated pose
        park3(0.02, false);                  // driver out: matches the empty pose
        Check(warns == 1 && !pm3.MountShifted && !pm3.LastWasSeated, "seated and empty poses both pass afterwards");
        park3(-2.5, true);                   // 2.5 deg off both: real shift, never learned as seated
        Check(warns == 2 && pm3.MountShifted && seatedLearned == 1, "large offset after a session still warns");

        // known driver offset: seated pose comes from it, nothing learned; the empty pose is not learned right after a session
        var pm4 = new ParkMonitor(new ParkSettings()) { LearnSeated = false }; double t4 = 0; int learned4 = 0, seated4 = 0, warn4 = 0;
        pm4.ReferenceLearned += (p, r) => learned4++; pm4.SeatedReferenceLearned += (p, r) => seated4++; pm4.Checked += (d, sh) => { if (sh) warn4++; };
        Action<double, bool> park4 = (pitch, after) =>
        {
            pm4.AfterSession = after;
            for (int i = 0; i < 500; i++) { t4 += 0.004; pm4.Update(new PlatformState { TimeSec = t4 }, false); }
            for (int i = 0; i < 1250; i++) { t4 += 0.004; pm4.Update(new PlatformState { PitchDeg = pitch, TimeSec = t4, Flags = ImuFlags.Still }, false); }
        };
        park4(-0.6, true);                   // first park right after a session (seated): not used as the empty pose
        Check(learned4 == 0 && !pm4.HasReference, "empty pose is not learned right after a session");
        park4(0.0, false);                   // empty rig: learned
        pm4.SetSeatedReference(pm4.RefPitchDeg - 0.6, pm4.RefRollDeg);
        park4(-0.62, true); park4(0.03, false);
        Check(learned4 == 1 && seated4 == 0 && warn4 == 0, "seated pose from the driver offset passes, empty passes");
        park4(-1.2, true);                   // 0.6 off the seated pose: warns, nothing learned
        Check(warn4 == 1 && seated4 == 0, "with a known offset nothing is learned and a shift warns");

        // standing still while something commands motion (SimHub's motion test holding 6°) is not a park
        var pm5 = new ParkMonitor(new ParkSettings()); double t5 = 0; int chk5 = 0; pm5.Checked += (d, sh) => chk5++;
        pm5.SetReference(0, 0); pm5.CommandActive = true;
        for (int i = 0; i < 1500; i++) { t5 += 0.004; pm5.Update(new PlatformState { PitchDeg = 6, TimeSec = t5, Flags = ImuFlags.Still }, false); }
        Check(chk5 == 0 && !pm5.MountShifted, "no park check while motion is commanded");
        pm5.CommandActive = false;
        for (int i = 0; i < 1500; i++) { t5 += 0.004; pm5.Update(new PlatformState { PitchDeg = 0.1, TimeSec = t5, Flags = ImuFlags.Still }, false); }
        Check(chk5 == 1 && !pm5.MountShifted, "park check once SimHub stops sending");

        // lead-in: the zero step waits for the controller to unpark and blend in before it measures
        var vl = new VerificationRun(5, 5, new VerifyOptions { LeadInSec = 6 }); double tl = 0;
        while (vl.Steps[0].Grade == VerifyGrade.Pending && tl < 30) { vl.Update(tl, 0, 0); tl += 0.01; }
        Check(tl >= 6 + 1.5 + 1.0 - 0.02, $"zero step measured after the lead-in ({tl:F2} s)");

        // brief stillness shorter than settle + average never completes a check
        var pm2 = new ParkMonitor(new ParkSettings()); double t2 = 0; int c2 = 0;
        pm2.ReferenceLearned += (p, r) => c2++;
        for (int k = 0; k < 10; k++)
        {
            for (int i = 0; i < 500; i++) { t2 += 0.004; pm2.Update(new PlatformState { TimeSec = t2, Flags = ImuFlags.Still }, false); }   // 2 s still
            for (int i = 0; i < 50; i++) { t2 += 0.004; pm2.Update(new PlatformState { TimeSec = t2 }, false); }
        }
        Check(c2 == 0, "short still periods (red lights, pauses < 3 s) don't count as park");
    }

    // ---------------------------------------------------------------------------------------
    static void VisualTests()
    {
        var tb = new TrendBuffer(5);
        for (int i = 0; i < 8; i++) tb.Add(i, i * 10, -i);
        tb.Get(0, out double t0, out double a0, out double b0);
        tb.Get(4, out double t4, out _, out _);
        Check(tb.Count == 5 && t0 == 3 && a0 == 30 && b0 == -3 && t4 == 7, "trend buffer keeps the newest samples, oldest first");

        Check(VisualMath.Frac(5, 0, 10) == 0.5 && VisualMath.Frac(-1, 0, 10) == 0 && VisualMath.Frac(20, 0, 10) == 1 && VisualMath.Frac(double.NaN, 0, 1) == 0, "Frac clamps");

        VisualMath.GgPoint(0.5, 0, 1.0, 100, out double x, out double y, out bool clip);
        Check(Near(x, 0, 1e-9) && Near(y, -50, 1e-9) && !clip, "forward cue drawn up");
        VisualMath.GgPoint(0, 0.5, 1.0, 100, out x, out y, out clip);
        Check(Near(x, -50, 1e-9), "+sway (left) drawn left");
        VisualMath.GgPoint(3, 4, 1.0, 100, out x, out y, out clip);
        Check(clip && Near(Math.Sqrt(x * x + y * y), 100, 1e-9), "g-g point clamped to the rim");

        VisualMath.Horizon(5, 3, 4, 100, out double rot, out double sh);
        Check(Near(rot, -5, 1e-9) && Near(sh, 12, 1e-9), "horizon: +roll counter-clockwise, nose-up moves it down");
        VisualMath.Horizon(0, 90, 4, 100, out rot, out sh);
        Check(Near(sh, 90, 1e-9), "horizon shift clamped inside the instrument");
        Check(VisualMath.NiceRange(16.1) == 20 && VisualMath.NiceRange(0.7) == 1, "nice axis range");
    }

    // ---------------------------------------------------------------------------------------
    // Auto-align: sensor bolted at an arbitrary angle. Build the sensor->rig rotation from Euler
    // angles, simulate rest + nose-up captures, then check that the estimator reads the rig's own
    // pitch/roll correctly for new poses.
    static double[,] Rot(double rx, double ry, double rz)   // rig -> sensor, degrees
    {
        double a = rx * Math.PI / 180, b = ry * Math.PI / 180, c = rz * Math.PI / 180;
        var X = new double[,] { { 1, 0, 0 }, { 0, Math.Cos(a), -Math.Sin(a) }, { 0, Math.Sin(a), Math.Cos(a) } };
        var Y = new double[,] { { Math.Cos(b), 0, Math.Sin(b) }, { 0, 1, 0 }, { -Math.Sin(b), 0, Math.Cos(b) } };
        var Z = new double[,] { { Math.Cos(c), -Math.Sin(c), 0 }, { Math.Sin(c), Math.Cos(c), 0 }, { 0, 0, 1 } };
        return Mul(Z, Mul(Y, X));
    }
    static double[,] Mul(double[,] a, double[,] b)
    {
        var r = new double[3, 3];
        for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) for (int k = 0; k < 3; k++) r[i, j] += a[i, k] * b[k, j];
        return r;
    }
    static double[] Apply(double[,] m, double[] v) => new[]
    {
        m[0,0]*v[0]+m[0,1]*v[1]+m[0,2]*v[2], m[1,0]*v[0]+m[1,1]*v[1]+m[1,2]*v[2], m[2,0]*v[0]+m[2,1]*v[1]+m[2,2]*v[2]
    };
    // world "up" seen in the RIG frame for a rig pose (+pitch nose up, +roll right side down)
    static double[] UpInRig(double pitchDeg, double rollDeg)
    {
        double p = pitchDeg * Math.PI / 180, r = rollDeg * Math.PI / 180;
        return new[] { Math.Sin(p), Math.Cos(p) * Math.Sin(r), Math.Cos(p) * Math.Cos(r) };
    }
    // a still IMU frame whose quaternion encodes the given sensor-frame up vector (yaw arbitrary)
    static ImuFrame FrameForUp(double[] u)
    {
        // quaternion rotating sensor z to u: up = (2(q1q3-q0q2), 2(q0q1+q2q3), q0^2-q1^2-q2^2+q3^2)
        double roll = Math.Atan2(u[1], u[2]), pitch = Math.Atan2(-u[0], Math.Sqrt(u[1] * u[1] + u[2] * u[2]));
        double cr = Math.Cos(roll / 2), sr = Math.Sin(roll / 2), cp = Math.Cos(pitch / 2), sp = Math.Sin(pitch / 2);
        return new ImuFrame { Q0 = cr * cp, Q1 = sr * cp, Q2 = cr * sp, Q3 = -sr * sp, Ax = u[0], Ay = u[1], Az = u[2] };
    }

    static void AlignTests()
    {
        var mounts = new[] { new[] { 0.0, 0, 0 }, new[] { 90.0, 0, 0 }, new[] { 90.0, 0, 33 }, new[] { -90.0, 17, 120 }, new[] { 180.0, 5, -60 } };
        foreach (var m in mounts)
        {
            var R = Rot(m[0], m[1], m[2]);
            var al = new MountAligner();
            al.BeginCapture(); var rest = Apply(R, UpInRig(0, 0)); for (int i = 0; i < 300; i++) al.Add(rest[0], rest[1], rest[2]);
            al.EndRest(out _);
            al.BeginCapture(); var nu = Apply(R, UpInRig(4, 0)); for (int i = 0; i < 300; i++) al.Add(nu[0], nu[1], nu[2]);
            al.EndNoseUp(out _);
            bool ok = al.Solve(out var fwd, out var up, out var tilt, out var err);
            var est = new PlatformEstimator();
            if (ok) est.SetMountingVectors(fwd, up);
            var s1 = est.Update(FrameForUp(Apply(R, UpInRig(6, 0))), 0);
            var s2 = est.Update(FrameForUp(Apply(R, UpInRig(0, -5))), 0);
            var s3 = est.Update(FrameForUp(Apply(R, UpInRig(3, 4))), 0);
            Check(ok && Near(tilt, 4, 0.01) && Near(s1.PitchDeg, 6, 0.05) && Near(s1.RollDeg, 0, 0.05) && Near(s2.RollDeg, -5, 0.05) && Near(s2.PitchDeg, 0, 0.05)
                  && Near(s3.PitchDeg, 3, 0.05) && Near(s3.RollDeg, 4, 0.1),
                  $"auto-align mount ({m[0]},{m[1]},{m[2]}): pitch {s1.PitchDeg:F2}/{s3.PitchDeg:F2} roll {s2.RollDeg:F2}/{s3.RollDeg:F2} {err}");
        }
        var bad = new MountAligner();
        bad.BeginCapture(); for (int i = 0; i < 300; i++) bad.Add(0, 0, 1); bad.EndRest(out _);
        bad.BeginCapture(); for (int i = 0; i < 300; i++) bad.Add(0.01, 0, 1); bad.EndNoseUp(out _);
        Check(!bad.Solve(out _, out _, out _, out var e2) && e2.Contains("at least"), "too small a nose-up tilt is refused");
        var moving = new MountAligner(); moving.BeginCapture(); for (int i = 0; i < 300; i++) moving.Add(0, 0, 1.4);
        Check(!moving.EndRest(out var e3) && e3.Contains("not still"), "capture while moving is refused");
    }
    static void GeometryTests()
    {
        var g = new RigGeometry { TrackMm = 660, WheelbaseMm = 500, StrokeMm = 225, MaxPitchDeg = 10, MaxRollDeg = 15 };
        g.FillFromLayout(ActuatorLayout.FourCorner);
        var pk = new PacketLayout { AxisCount = 4, MaxValue = 65535 };
        Check(g.Problem(ActuatorLayout.FourCorner, pk) == null, "4-corner geometry complete: " + g.Problem(ActuatorLayout.FourCorner, pk));
        // Haarie's rig: SimHub 15° roll -> left actuators 200 mm, right 25 mm around 112.5
        var z = g.HeightsMm(0, 15);
        Check(Near(z[0], 88.4, 0.2) && Near(z[1], -88.4, 0.2) && Near(z[2], 88.4, 0.2), $"15° roll on 660 mm track: {z[0]:F1}/{z[1]:F1} mm");
        g.Pose(new[] { 87.5, -87.5, 87.5, -87.5 }, out var p0, out var r0, out var h0);
        Check(Near(r0, 14.85, 0.02) && Near(p0, 0, 1e-6) && Near(h0, 0, 1e-6), $"175 mm difference over 660 mm = 14.85° roll ({r0:F2})");
        g.Pose(g.HeightsMm(-4, 6, 12), out var p1, out var r1, out var h1);
        Check(Near(p1, -4, 1e-6) && Near(r1, 6, 1e-6) && Near(h1, 12, 1e-6), "pose round trip with heave");
        var neutral = new[] { 32767.5, 32767.5, 32767.5, 32767.5 };
        var v = g.HeightsToValues(g.HeightsMm(3, -5), neutral, 65535);
        g.Pose(g.ValuesToHeights(v, neutral, 65535), out var p2, out var r2, out _);
        Check(Near(p2, 3, 0.01) && Near(r2, -5, 0.01), $"values round trip ({p2:F3}/{r2:F3})");
        Check(Near(g.HeadroomMm(neutral, 65535), 112.5, 0.01), "headroom = half the stroke at mid-scale");
        var g2 = new RigGeometry { TrackMm = 500, PivotOffsetMm = 400, StrokeMm = 100, MaxPitchDeg = 5, MaxRollDeg = 5 };
        g2.FillFromLayout(ActuatorLayout.TwoLeftRight);
        var z2 = g2.HeightsMm(5, 0);
        Check(z2[0] < 0 && z2[1] < 0, "2-actuator seat mover: nose up = actuators behind the pivot go down");
        g2.Pose(g2.HeightsMm(2, -3), out var p3, out var r3, out _);
        Check(Near(p3, 2, 1e-6) && Near(r3, -3, 1e-6), "2-actuator pose round trip");
        Check(new RigGeometry().Problem(ActuatorLayout.GlobalOnly, pk) != null, "missing layout is reported");
    }

    // Simulated rig: measured = scale * command (+ cross-talk), first-order lag
    static VerificationRun SimRun(double pitchScale, double rollScale, double cross, double limitP = 10, double limitR = 15, double clipRoll = 99)
    {
        var run = new VerificationRun(limitP, limitR, new VerifyOptions { RampDps = 20, SettleSec = 0.3, MeasureSec = 0.2 });
        double mp = 0, mr = 0, t = 0;
        for (int i = 0; i < 20000 && !run.Done; i++)
        {
            t += 0.01;
            double tp = run.CommandPitchDeg * pitchScale + run.CommandRollDeg * cross;
            double tr = Math.Max(-clipRoll, Math.Min(clipRoll, run.CommandRollDeg * rollScale));
            mp += (tp - mp) * 0.2; mr += (tr - mr) * 0.2;
            run.Update(t, mp, mr);
        }
        return run;
    }

    static void VerifyTests()
    {
        var ok = SimRun(1.0, 0.98, 0);
        Check(ok.Phase == VerifyPhase.Finished && ok.Overall == VerifyGrade.Pass, $"good rig passes ({ok.Phase} {ok.Overall})");
        Check(ok.Steps.Count == 17 && ok.Steps.Any(s => Near(s.TargetDeg, -15, 1e-9)) && ok.Steps.All(s => Math.Abs(s.TargetDeg) <= 15), "poses are fractions of the SimHub limits");
        Check(Near(ok.CommandPitchDeg, 0, 1e-9) && Near(ok.CommandRollDeg, 0, 1e-9), "run ends back at zero");
        Check(Near(ok.Scale(VerifyAxis.Roll), 0.98, 0.01), "roll scale measured");

        var half = SimRun(0.5, 0.5, 0);
        Check(half.Overall == VerifyGrade.Fail && half.Findings().Any(f => f.Contains("factor of about 0.5") && f.Contains("ball-screw")), "factor 2 (ball-screw lead) is diagnosed: " + string.Join(" / ", half.Findings()));

        var wrong = SimRun(1, -1, 0);
        Check(wrong.Phase == VerifyPhase.Aborted && wrong.AbortReason.Contains("wrong way"), "reversed axis aborts the test: " + wrong.AbortReason);
        Check(Near(wrong.CommandRollDeg, 0, 1e-9), "abort still ramps back to zero");

        var dbl = SimRun(2.0, 1.0, 0);
        Check(dbl.Phase == VerifyPhase.Aborted, "rig moving twice as far is stopped before it overshoots the limit: " + dbl.AbortReason);

        var xt = SimRun(1, 1, 0.08);
        Check(xt.Overall == VerifyGrade.Fail && xt.Findings().Any(f => f.Contains("influence each other")), "cross-talk is diagnosed");

        var clip = SimRun(1, 1, 0, 10, 15, 13);
        Check(clip.Findings().Any(f => f.Contains("not reachable") && f.Contains("13.0")), "unreachable limit is diagnosed: " + string.Join(" / ", clip.Findings()));

        var g = new VerificationRun(10, 10, new VerifyOptions());
        Check(g.GradeError(2, 0.25) == VerifyGrade.Pass && g.GradeError(2, 0.45) == VerifyGrade.Warn && g.GradeError(15, 0.7) == VerifyGrade.Pass && g.GradeError(15, 1.6) == VerifyGrade.Fail,
              "tolerance = max(5 %, 0.3°) / warn max(10 %, 0.6°)");
        var stop = new VerificationRun(10, 10, new VerifyOptions { RampDps = 10 });
        for (int i = 1; i < 40; i++) stop.Update(i * 0.01, stop.CommandPitchDeg, stop.CommandRollDeg);
        stop.RequestStop();
        for (int i = 40; i < 400 && !stop.Done; i++) stop.Update(i * 0.01, stop.CommandPitchDeg, stop.CommandRollDeg);
        Check(stop.Phase == VerifyPhase.Aborted && stop.AbortReason == "stopped by user" && stop.CommandPitchDeg == 0, "stop ramps home and ends");
        var lost = new VerificationRun(10, 10, new VerifyOptions());
        lost.Update(0, 0, 0); lost.Update(0.01, double.NaN, 0);
        Check(lost.Phase == VerifyPhase.Aborted && lost.AbortReason == "no sensor data", "lost sensor ends the run (already at zero)");
        var rep = VerificationReport.From(ok, "abc", "1.3.0", "fw");
        Check(rep.Passed && rep.ValidFor("abc") && !rep.ValidFor("abd"), "report is tied to the settings fingerprint");
        var geo = new RigGeometry { TrackMm = 660 }; var f1 = VerificationReport.Fingerprint(geo, 1.0); geo.TrackMm = 661;
        Check(f1 != VerificationReport.Fingerprint(geo, 1.0), "fingerprint changes when the geometry changes");
    }
    static void ImportTests()
    {
        string json = "\uFEFF{\"Devices\":[{\"Name\":\"Rig\",\"IsEnabled\":true,\"Geometry\":{\"TypeName\":\"GenericGeometry3Dof4Linear\",\"BaseGeometry\":{\"Settings\":{"
            + "\"RigLength\":1250.0,\"RigWidth\":660.0,\"RigWidthFront\":600.0,\"SeparateWidth\":false,\"ActuatorsStroke\":200.0,\"CustomCenterOfRotation\":false,"
            + "\"CenterOfRotationRightOffset\":0.0,\"CenterOfRotationFrontOffset\":0.0,\"AxisLimitingSettings\":{\"RollLimit\":{\"Enabled\":true,\"Limit\":5.0},"
            + "\"FrontPitchLimit\":{\"Enabled\":true,\"Limit\":6.0},\"RearPitchLimit\":{\"Enabled\":true,\"Limit\":5.0}},\"Note\":\"a \\\"quoted\\\" \\u00e9\"},"
            + "\"TypeName\":\"Geometry3Dof4Linear\"}},\"OutputEx\":{\"AggregatedOutputs\":[{\"Output\":{\"TypeName\":\"GenericUDPOutputV2\",\"Settings\":{\"TargetIPAddress\":\"192.168.50.1\",\"TargetPort\":4444}}}]},"
            + "\"Profiles\":[],\"X\":[1,-2.5e-3,null,NaN]}]}";
        var r = SimHubMotionImport.FromJson(json, new DateTime(2026, 11, 16, 20, 28, 0));
        Check(r.Ok && r.Layout == ActuatorLayout.FourCorner && r.LengthMm == 1250 && r.WidthMm == 660 && r.StrokeMm == 200, "SimHub 3DOF/4 linear geometry imported: " + r.Error);
        Check(r.RollLimitDeg == 5 && r.PitchLimitDeg == 5 && r.Notes.Any(n => n.Contains("smaller")), "pitch limit = smaller of front/rear");
        Check(r.OutputHost == "192.168.50.1" && r.OutputPort == 4444, "UDP output target imported");
        var g = new RigGeometry(); r.ApplyTo(g);
        Check(g.Problem(r.Layout, new PacketLayout { AxisCount = 4 }) == null && g.ActuatorX[0] == 625 && g.ActuatorY[1] == -330 && g.Source == "SimHub", "import fills a complete rig");
        Check(r.Differences(g, ActuatorLayout.FourCorner).Count == 0, "no differences right after import");
        g.StrokeMm = 225;
        Check(r.Differences(g, ActuatorLayout.FourCorner).Any(d => d.Contains("stroke")), "a changed stroke is detected");
        var bad = SimHubMotionImport.FromJson("{\"Devices\":[{\"Geometry\":{\"BaseGeometry\":{\"TypeName\":\"Geometry6DofStewart\",\"Settings\":{}}}}]}", DateTime.Now);
        Check(!bad.Ok && bad.Error.Contains("not supported"), "unsupported rig type is reported");
    }
    static byte[] Ascii(string t) => System.Text.Encoding.ASCII.GetBytes(t);
    static string Str(byte[] b) => System.Text.Encoding.ASCII.GetString(b);

    static void TextPacketTests()
    {
        var l = PacketLayout.FromSimHubCommand("NULLCAT,<Axis1>,<Axis2>,<Axis3>,<Axis4>,<Axis5>,<Axis6>,<Axis7>,<Axis8>,<Axis9>,<Axis10>", 16);
        Check(l != null && l.Text && l.TextPrefix == "NULLCAT" && l.AxisCount == 10 && l.MaxValue == 65535, "nullCAT text command recognised: " + (l == null ? "null" : l.Describe()));
        var b = PacketLayout.FromSimHubCommand("S<Axis1a><Axis1b><Axis2a><Axis2b>E", 16);
        Check(b != null && !b.Text && b.HeaderBytes == 1 && b.TrailerBytes == 1 && b.AxisCount == 2 && b.BytesPerAxis == 2 && b.BigEndian, "binary command recognised: " + (b == null ? "null" : b.Describe()));
        Check(PacketLayout.FromSimHubCommand("<Axis1b><Axis1a>", 16).BigEndian == false, "low byte first recognised");

        var idle = Ascii("NULLCAT,32767,32767,32767,32767,0,0,0,0,0,0\n");
        Check(MotionPacketCodec.IsMotionPacket(l, idle, idle.Length) && !MotionPacketCodec.IsMotionPacket(new PacketLayout(), idle, idle.Length), "text packet matches only the text layout");
        var xf = new MotionPacketTransformer(l, ActuatorLayout.FourCorner);
        xf.BeginNeutralCapture();
        for (int i = 0; i < 20; i++) { bool m; xf.ProcessAny(idle, idle.Length, GainSnapshot.Unity, false, out m); }
        Check(xf.EndNeutralCapture() && xf.Neutral[0] == 32767 && xf.Neutral[5] == 0, "neutral captured from text packets");
        var roll = Ascii("NULLCAT,42767,22767,42767,22767,0,0,0,0,0,0\n");
        bool mo; var same = xf.ProcessAny(roll, roll.Length, GainSnapshot.Unity, false, out mo);
        Check(mo && ReferenceEquals(same, roll) && xf.LastAxes[0] == 42767, "pass-through returns the identical packet");
        var outp = xf.ProcessAny(roll, roll.Length, new GainSnapshot(1, 1, 0.5, 1, ControllerStatus.Ok), true, out mo);
        Check(Str(outp) == "NULLCAT,37767,27767,37767,27767,0,0,0,0,0,0\n", "roll gain 0.5 rewrites the text packet: " + Str(outp).TrimEnd());
        var other = Ascii("NULLCAT,START");
        var o2 = xf.ProcessAny(other, other.Length, GainSnapshot.Unity, true, out mo);
        Check(!mo && ReferenceEquals(o2, other) && xf.LastOtherSample == "NULLCAT,START", "non-motion text command forwarded untouched and shown");

        var det = MotionPacketCodec.DetectText(idle, idle.Length, 65535);
        Check(det != null && det.TextPrefix == "NULLCAT" && det.AxisCount == 10 && !det.TextHex, "format detected from a live packet");
        var hx = Ascii("M,7FFF,7FFF,00A0\r\n");
        var dh = MotionPacketCodec.DetectText(hx, hx.Length, 65535);
        Check(dh != null && dh.TextHex && dh.TextPrefix == "M" && dh.AxisCount == 3, "hex text detected");
        var rebuilt = MotionPacketCodec.Build(dh, new double[] { 32768, 1, 161 }, hx, hx.Length);
        Check(Str(rebuilt) == "M,8000,1,00A1\r\n", "hex values, zero padding and line end kept: " + Str(rebuilt).TrimEnd());
        Check(MotionPacketCodec.DetectText(new byte[] { 0x7F, 0xFF, 0x00, 0x10 }, 4, 65535) == null, "binary packet is not text");
        // what Haarie's SimHub really sends: 9 values, the 10th placeholder left as text
        var real = Ascii("NULLCAT,32767,32767,32767,32767,32767,32767,32767,32767,32767,<Axis10>");
        Check(MotionPacketCodec.IsMotionPacket(l, real, real.Length), "unfilled <Axis10> placeholder accepted");
        var xr = new MotionPacketTransformer(l, ActuatorLayout.FourCorner);
        bool mr; var pass = xr.ProcessAny(real, real.Length, GainSnapshot.Unity, false, out mr);
        Check(mr && ReferenceEquals(pass, real) && xr.LastAxes[1] == 32767, "real nullCAT packet decoded, passed through untouched");
        var moved = MotionPacketCodec.Build(l, new double[] { 40000, 25534, 40000, 25534, 32767, 32767, 32767, 32767, 32767, 0 }, real, real.Length);
        Check(Str(moved) == "NULLCAT,40000,25534,40000,25534,32767,32767,32767,32767,32767,<Axis10>", "test packet keeps <Axis10> as SimHub sends it: " + Str(moved));
        var dr = MotionPacketCodec.DetectText(real, real.Length, 65535);
        Check(dr != null && dr.TextPrefix == "NULLCAT" && dr.AxisCount == 10 && !dr.TextHex, "format detected from the real packet");
        var syn = MotionPacketCodec.Synthesize(l);
        Check(MotionPacketCodec.IsMotionPacket(l, syn, syn.Length) && Str(MotionPacketCodec.Build(l, new double[] { 32767, 32767, 32767, 32767, 0, 0, 0, 0, 0, 0 }, syn, syn.Length)) == "NULLCAT,32767,32767,32767,32767,0,0,0,0,0,0",
              "test packets can be built without SimHub sending (idle)");
        Check(MotionPacketCodec.Synthesize(new PacketLayout { HeaderBytes = 1 }) == null, "binary with header needs a real packet first");

        string json = "{\"Devices\":[{\"IsEnabled\":true,\"Geometry\":{\"BaseGeometry\":{\"TypeName\":\"Geometry3Dof4Linear\",\"Settings\":{\"RigLength\":1250,\"RigWidth\":660,\"ActuatorsStroke\":200}}},"
            + "\"OutputEx\":{\"AggregatedOutputs\":[{\"Output\":{\"TypeName\":\"GenericUDPOutputV2\",\"Settings\":{\"TargetIPAddress\":\"192.168.50.1\",\"TargetPort\":4444,"
            + "\"GenericProtocolDefinition\":{\"AxisResolution\":16,\"UpdateCommands\":[{\"Command\":\"NULLCAT,<Axis1>,<Axis2>,<Axis3>,<Axis4>\"}]}}}}]}}]}";
        var r = SimHubMotionImport.FromJson(json, DateTime.Now);
        Check(r.Ok && r.Packet != null && r.Packet.Text && r.Packet.AxisCount == 4, "packet format imported from SimHub's update command");
    }
}
