using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace SimSpares.AdaptiveMotion.Core
{
    /// <summary>
    /// Reads rig dimensions, actuator stroke, axis limits and the UDP output target from SimHub's motion
    /// plugin settings (PluginsData\Common\MotionPlugin.GeneralSettingsV2.json). SimHub writes that file when
    /// it closes, so values changed in the current session only show up after a SimHub restart.
    /// </summary>
    public class SimHubMotionImport
    {
        public bool Ok;
        public string Error;
        public string GeometryType = "";
        public ActuatorLayout Layout = ActuatorLayout.GlobalOnly;
        public double LengthMm, WidthMm, WidthFrontMm, StrokeMm;
        public bool SeparateWidth, CustomCentre;
        public double CentreFrontMm, CentreRightMm;
        public double RollLimitDeg, PitchLimitDeg;     // 0 = limit switched off in SimHub
        public string OutputType = "", OutputHost = "";
        public int OutputPort;
        public string UpdateCommand = "";              // SimHub's "update" command template, e.g. NULLCAT,<Axis1>,…
        public PacketLayout Packet;                    // packet format derived from it (null = unknown)
        public DateTime FileTime;
        public List<string> Notes = new List<string>();

        public static SimHubMotionImport FromJson(string json, DateTime fileTime)
        {
            var r = new SimHubMotionImport { FileTime = fileTime };
            object root;
            try { root = MiniJson.Parse(json); }
            catch (Exception e) { r.Error = "SimHub motion settings could not be read: " + e.Message; return r; }

            var devices = MiniJson.Get(root, "Devices") as List<object>;
            if (devices == null || devices.Count == 0) { r.Error = "no motion device found in SimHub's motion settings"; return r; }
            int di = 0;
            for (int i = 0; i < devices.Count; i++)
                if (MiniJson.Bool(devices[i], "IsEnabled") == true && MiniJson.Get(devices[i], "Geometry.BaseGeometry.Settings") != null) { di = i; break; }
            var dev = devices[di];
            if (devices.Count > 1) r.Notes.Add("SimHub has " + devices.Count + " motion devices; using \"" + (MiniJson.Str(dev, "Name") ?? ("#" + (di + 1))) + "\".");

            r.GeometryType = MiniJson.Str(dev, "Geometry.BaseGeometry.TypeName") ?? MiniJson.Str(dev, "Geometry.TypeName") ?? "";
            var m = Regex.Match(r.GeometryType, @"(\d)Linear", RegexOptions.IgnoreCase);
            int n = m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
            r.Layout = n == 4 ? ActuatorLayout.FourCorner : n == 3 ? ActuatorLayout.ThreeFrontPairRear : n == 2 ? ActuatorLayout.TwoLeftRight : ActuatorLayout.GlobalOnly;
            if (r.Layout == ActuatorLayout.GlobalOnly)
            { r.Error = "SimHub rig type \"" + r.GeometryType + "\" is not supported yet (2, 3 or 4 linear actuators are)"; return r; }

            string g = "Geometry.BaseGeometry.Settings.";
            r.LengthMm = MiniJson.Num(dev, g + "RigLength", 0);
            r.WidthMm = MiniJson.Num(dev, g + "RigWidth", 0);
            r.WidthFrontMm = MiniJson.Num(dev, g + "RigWidthFront", r.WidthMm);
            r.SeparateWidth = MiniJson.Bool(dev, g + "SeparateWidth") == true;
            r.StrokeMm = MiniJson.Num(dev, g + "ActuatorsStroke", 0);
            r.CustomCentre = MiniJson.Bool(dev, g + "CustomCenterOfRotation") == true || MiniJson.Bool(dev, g + "CenterOfRotationOffset.CustomCenterOfRotation") == true;
            r.CentreFrontMm = r.CustomCentre ? MiniJson.Num(dev, g + "CenterOfRotationFrontOffset", 0) : 0;
            r.CentreRightMm = r.CustomCentre ? MiniJson.Num(dev, g + "CenterOfRotationRightOffset", 0) : 0;

            Func<string, double> Limit = name => MiniJson.Bool(dev, g + "AxisLimitingSettings." + name + ".Enabled") == true ? MiniJson.Num(dev, g + "AxisLimitingSettings." + name + ".Limit", 0) : 0;
            r.RollLimitDeg = Limit("RollLimit");
            double fp = Limit("FrontPitchLimit"), rp = Limit("RearPitchLimit");
            r.PitchLimitDeg = fp > 0 && rp > 0 ? Math.Min(fp, rp) : Math.Max(fp, rp);
            if (fp > 0 && rp > 0 && Math.Abs(fp - rp) > 1e-6)
                r.Notes.Add(string.Format(CultureInfo.InvariantCulture, "SimHub has front pitch ±{0}° and rear pitch ±{1}°: the test uses the smaller one.", fp, rp));
            if (r.RollLimitDeg <= 0 || r.PitchLimitDeg <= 0)
                r.Notes.Add("A pitch or roll limit is switched off in SimHub: enter the largest angle you want the test to use.");

            var outs = MiniJson.Get(dev, "OutputEx.AggregatedOutputs") as List<object>;
            if (outs != null)
                foreach (var o in outs)
                {
                    string type = MiniJson.Str(o, "Output.TypeName") ?? "";
                    string host = MiniJson.Str(o, "Output.Settings.TargetIPAddress");
                    double port = MiniJson.Num(o, "Output.Settings.TargetPort", 0);
                    if (!string.IsNullOrEmpty(host) && port > 0)
                    {
                        r.OutputType = type; r.OutputHost = host; r.OutputPort = (int)port;
                        r.UpdateCommand = MiniJson.Str(o, "Output.Settings.GenericProtocolDefinition.UpdateCommands[0].Command") ?? "";
                        int bits = (int)MiniJson.Num(o, "Output.Settings.GenericProtocolDefinition.AxisResolution", 16);
                        r.Packet = PacketLayout.FromSimHubCommand(r.UpdateCommand, bits);
                        if (r.Packet == null && r.UpdateCommand != "") r.Notes.Add("SimHub's update command \"" + r.UpdateCommand + "\" has no axis placeholders: set the packet format on the Advanced tab.");
                        break;
                    }
                }
            if (r.OutputHost == "") r.Notes.Add("No UDP output found in SimHub's motion settings: enter the controller address by hand.");

            if (!(r.WidthMm > 0) || !(r.StrokeMm > 0) || (r.Layout != ActuatorLayout.TwoLeftRight && !(r.LengthMm > 0)))
            { r.Error = "SimHub's rig dimensions or stroke are empty"; return r; }
            r.Ok = true;
            return r;
        }

        /// <summary>Write the imported values into the plugin's rig description.</summary>
        public void ApplyTo(RigGeometry rig)
        {
            rig.TrackMm = WidthMm;
            rig.WheelbaseMm = LengthMm;
            rig.StrokeMm = StrokeMm;
            if (PitchLimitDeg > 0) rig.MaxPitchDeg = PitchLimitDeg;
            if (RollLimitDeg > 0) rig.MaxRollDeg = RollLimitDeg;
            rig.FillFromLayout(Layout);
            if (Layout == ActuatorLayout.FourCorner || Layout == ActuatorLayout.ThreeFrontPairRear)
            {
                double front = (SeparateWidth ? WidthFrontMm : WidthMm) / 2;
                rig.ActuatorY[0] = front; rig.ActuatorY[1] = -front;   // FL, FR
            }
            for (int i = 0; i < rig.Count; i++) { rig.ActuatorX[i] -= CentreFrontMm; rig.ActuatorY[i] += CentreRightMm; }
            rig.Source = "SimHub";
        }

        /// <summary>True when the stored rig no longer matches what SimHub has.</summary>
        public List<string> Differences(RigGeometry rig, ActuatorLayout layout)
        {
            var d = new List<string>();
            var inv = CultureInfo.InvariantCulture;
            if (layout != Layout) d.Add("layout " + layout + " → " + Layout);
            if (Math.Abs(rig.TrackMm - WidthMm) > 0.5) d.Add(string.Format(inv, "width {0} → {1} mm", rig.TrackMm, WidthMm));
            if (Layout != ActuatorLayout.TwoLeftRight && Math.Abs(rig.WheelbaseMm - LengthMm) > 0.5) d.Add(string.Format(inv, "length {0} → {1} mm", rig.WheelbaseMm, LengthMm));
            if (Math.Abs(rig.StrokeMm - StrokeMm) > 0.5) d.Add(string.Format(inv, "stroke {0} → {1} mm", rig.StrokeMm, StrokeMm));
            if (PitchLimitDeg > 0 && Math.Abs(rig.MaxPitchDeg - PitchLimitDeg) > 0.01) d.Add(string.Format(inv, "pitch limit {0} → {1}°", rig.MaxPitchDeg, PitchLimitDeg));
            if (RollLimitDeg > 0 && Math.Abs(rig.MaxRollDeg - RollLimitDeg) > 0.01) d.Add(string.Format(inv, "roll limit {0} → {1}°", rig.MaxRollDeg, RollLimitDeg));
            return d;
        }

        public string Summary() => string.Format(CultureInfo.InvariantCulture,
            "SimHub ({0:yyyy-MM-dd HH:mm}): {1} actuators, {2} × {3} mm, stroke {4} mm, pitch ±{5}°, roll ±{6}°{7}",
            FileTime, Layout == ActuatorLayout.FourCorner ? 4 : Layout == ActuatorLayout.ThreeFrontPairRear ? 3 : 2,
            LengthMm, WidthMm, StrokeMm, PitchLimitDeg > 0 ? PitchLimitDeg.ToString(CultureInfo.InvariantCulture) : "off",
            RollLimitDeg > 0 ? RollLimitDeg.ToString(CultureInfo.InvariantCulture) : "off",
            OutputHost != "" ? ", output " + OutputHost + ":" + OutputPort : "");
    }
}
