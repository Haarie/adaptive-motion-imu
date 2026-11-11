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
        public Axis MountForward = Axis.PosX;    // which sensor axis points to the front of the rig
        public Axis MountUp = Axis.PosZ;         // which sensor axis points up
        public bool LevelCaptured = false;
        public double LevelPitchDeg = 0;
        public double LevelRollDeg = 0;
        public int DeviceDlpf = 3;
        public int DeviceOutputDivider = 2;

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
    }
}
