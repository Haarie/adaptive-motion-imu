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
        // Driver offset: seat attitude with the driver seated, relative to the empty-rig level (applied while driving)
        public bool DriverOffsetCaptured = false;
        public double DriverOffsetPitchDeg = 0, DriverOffsetRollDeg = 0;
        public double LevelRollDeg = 0;
        public int DeviceDlpf = 3;
        public int DeviceOutputDivider = 2;
        public bool AutoGyroCal = true;          // firmware learns gyro bias whenever the rig is still

        // --- park check (warns when the parked pose moved: IMU box shifted / rig parked elsewhere) ---
        public ParkSettings Park = new ParkSettings();
        public bool ParkReferenceLearned = false;
        public double ParkPitchDeg = 0;
        public bool ParkSeatedLearned = false;
        public double ParkSeatedPitchDeg = 0, ParkSeatedRollDeg = 0;
        public bool ControllerLimitsFollowSimHub = true;   // the plugin's own pitch/roll limits = SimHub's limits
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
        public bool LayoutVerified = false;      // (pre-1.3) manual tick; closed loop now needs a passed verification

        // --- rig + verification (1.3) -------------------------------------------------------
        public RigGeometry Rig = new RigGeometry();          // dimensions + limits, as entered in SimHub's motion settings
        public VerifyOptions Verify = new VerifyOptions { MaxFractionPct = 50 };   // first runs at half the limits; set 100 % once it looks right
        public VerificationReport LastVerification = null;   // closed loop only while this passed and still matches the settings
        public bool SetupWizardSeen = false;
        public string PacketTemplate = "";                   // last SimHub motion packet (base64): lets the test run while SimHub is idle

        // --- SimHub gain-event mode ---------------------------------------------------------
        public double SimHubGainStepPct = 5.0;   // must match one step of SimHub's gain control
        public int SimHubMaxSteps = 8;

        // --- data logging -------------------------------------------------------------------
        public string LogFolder = "";            // empty = Documents\Adaptive Motion IMU\logs
        public int LogRateHz = 100;              // rows per second (max = sensor rate, 250)
        public bool LogAutoStart = false;        // start when a game starts running, stop when it stops
        public bool LogGameData = true;          // add speed, gear, rpm and the game's own accelerations

        // --- updates ------------------------------------------------------------------------
        public string UpdateRepo = "Haarie/adaptive-motion-imu";   // public GitHub repo with releases
        public bool UpdateCheckOnStart = true;
        public bool UpdateIncludePrerelease = false;
    }
}
