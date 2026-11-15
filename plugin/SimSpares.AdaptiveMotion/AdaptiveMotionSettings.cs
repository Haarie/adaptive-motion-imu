using SimSpares.AdaptiveMotion.Core;

namespace SimSpares.AdaptiveMotion
{
    public enum ApplyMode
    {
        /// <summary>Measure and publish properties only. Nothing is changed.</summary>
        MonitorOnly,
        /// <summary>Recommended: rescale SimHub's Generic-UDP motion packets on their way to the controller.</summary>
        UdpProxy,
        /// <summary>Coarse: raise plugin events that you map (Controls &amp; events) to SimHub's Motion gain down/up.</summary>
        SimHubGainEvents
    }

    /// <summary>Persisted by SimHub (ReadCommonSettings/SaveCommonSettings → JSON).</summary>
    public class AdaptiveMotionSettings
    {
        // --- sensor -------------------------------------------------------------------------
        public string ComPort = "";
        // Defaults match the "side" enclosure on a transverse beam: lid towards the rig front (sensor +Z),
        // flange down / box above the slot (sensor +Y up), assuming the GY-521's X axis runs along the box.
        // On the rear face of the beam use NegZ. Auto-align replaces these with measured axes.
        public Axis MountForward = Axis.PosZ;    // which sensor axis points to the front of the rig
        public Axis MountUp = Axis.PosX;         // which sensor axis points up (bench-measured: +X = away from the flange)
        public bool MountAuto = false;           // use the auto-aligned vectors below instead of MountForward/MountUp
        public double[] MountAutoForward = null; // rig forward in sensor coordinates (Auto-align)
        public double[] MountAutoUp = null;      // rig up in sensor coordinates (Auto-align)
        public bool LevelCaptured = false;
        public double LevelPitchDeg = 0;
        public double LevelRollDeg = 0;
        public int DeviceDlpf = 3;
        public int DeviceOutputDivider = 2;
        public bool AutoGyroCal = true;          // firmware learns gyro bias whenever the rig is still

        // --- park check (warns when the parked pose moved: IMU box shifted / rig parked elsewhere) ---
        public ParkSettings Park = new ParkSettings();
        public bool ParkReferenceLearned = false;
        public double ParkPitchDeg = 0;
        public double ParkRollDeg = 0;

        // --- behaviour ----------------------------------------------------------------------
        public ApplyMode Mode = ApplyMode.MonitorOnly;
        public bool AdaptiveEnabled = true;      // master switch (also an action, map it to a button)
        public ControllerSettings Controller = new ControllerSettings();

        // --- UDP proxy ----------------------------------------------------------------------
        public int ProxyListenPort = 33740;
        public string ProxyTargetHost = "nullcat.local";
        public int ProxyTargetPort = 0;          // set to the port SimHub used to send to directly
        public PacketLayout Packet = new PacketLayout();
        public ActuatorLayout Actuators = ActuatorLayout.GlobalOnly;
        public double[] Neutral = null;          // captured rest values; null = mid-scale
        public bool LayoutVerified = false;      // gains are only applied once you tick this

        // --- SimHub gain-event mode ---------------------------------------------------------
        public double SimHubGainStepPct = 5.0;   // must match one step of SimHub's gain control
        public int SimHubMaxSteps = 8;

        // --- data logging -------------------------------------------------------------------
        public string LogFolder = "";            // empty = Documents\Adaptive Motion IMU\logs
        public int LogRateHz = 100;              // rows per second (max = sensor rate, 250)
        public bool LogAutoStart = false;        // start when a game starts running, stop when it stops
        public bool LogGameData = true;          // add speed, gear, rpm and the game's own accelerations
    }
}
