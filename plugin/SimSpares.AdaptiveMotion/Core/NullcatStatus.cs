using System;

namespace SimSpares.AdaptiveMotion.Core
{
    /// <summary>The parts of nullCAT's /api/status the plugin needs (see nullCAT Docs/COMMAND_CONTRACT.md).</summary>
    public sealed class NullcatStatus
    {
        public bool Reachable;          // false: no answer (yet), the rest is unknown
        public bool Parked;             // all position axes parked
        public bool EstopTripped;
        public bool LoopRunning;
        public bool Homing;
        public bool NeedsRehome;
        public bool TelemetryReceiving;
        public string Error;            // why it is not reachable
        public double AtSec = double.NaN;

        public static NullcatStatus FromJson(string json, double nowSec)
        {
            var r = MiniJson.Parse(json);
            return new NullcatStatus
            {
                Reachable = true,
                Parked = MiniJson.Bool(r, "parked") ?? false,
                EstopTripped = MiniJson.Bool(r, "estopTripped") ?? false,
                LoopRunning = MiniJson.Bool(r, "loopRunning") ?? true,
                Homing = MiniJson.Bool(r, "homing") ?? false,
                NeedsRehome = MiniJson.Bool(r, "needsRehome") ?? false,
                TelemetryReceiving = MiniJson.Bool(r, "telemetryReceiving") ?? false,
                AtSec = nowSec,
            };
        }

        public static NullcatStatus Unreachable(string why, double nowSec) => new NullcatStatus { Reachable = false, Error = why, AtSec = nowSec };

        /// <summary>A problem that stops the rig from moving at all (unpark cannot help).</summary>
        public string Blocker()
        {
            if (!Reachable) return null;
            if (EstopTripped) return "nullCAT: the e-stop is tripped (release it and Initialize in nullCAT)";
            if (!LoopRunning) return "nullCAT: the control loop is not running (start it in nullCAT)";
            if (Homing) return "nullCAT is homing: wait until it has finished";
            if (NeedsRehome) return "nullCAT needs homing first";
            return null;
        }

        public string Describe()
        {
            if (!Reachable) return "nullCAT: not reachable" + (string.IsNullOrEmpty(Error) ? "" : " (" + Error + ")");
            var b = Blocker();
            if (b != null) return b;
            return Parked ? "nullCAT: PARKED" : "nullCAT: ONLINE (unparked)";
        }
    }
}
