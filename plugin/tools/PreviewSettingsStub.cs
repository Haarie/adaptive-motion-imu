// Off-Windows preview/stub builds only: the real SettingsControl needs the full WPF surface and is
// checked by the real-reference build (build_release.sh) instead.
namespace SimSpares.AdaptiveMotion
{
    public class SettingsControl : System.Windows.Controls.UserControl
    {
        public SettingsControl(AdaptiveMotionPlugin p) { }
    }
    internal static class MenuIcon { public static System.Windows.Media.ImageSource Create() => null; }
}
