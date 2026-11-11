using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SimSpares.AdaptiveMotion.UI
{
    /// <summary>Stand-alone, resizable window with the live visualizer — put it on a second screen.</summary>
    public class VisualizerWindow : Window
    {
        private static VisualizerWindow _open;

        /// <summary>Show the window (one instance); a second call brings it to the front.</summary>
        public static void ShowFor(AdaptiveMotionPlugin plugin)
        {
            if (_open != null) { _open.Activate(); return; }
            _open = new VisualizerWindow(plugin);
            _open.Closed += (o, e) => _open = null;
            _open.Show();
        }

        public VisualizerWindow(AdaptiveMotionPlugin plugin)
        {
            Title = "Adaptive Motion — seat IMU";
            Width = 960; Height = 600;
            MinWidth = 400; MinHeight = 260;
            Background = new SolidColorBrush(Color.FromRgb(0x12, 0x13, 0x16));
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var top = new CheckBox
            {
                Content = "Always on top",
                Foreground = Brushes.Gainsboro,
                Margin = new Thickness(10, 6, 10, 6),
                VerticalAlignment = VerticalAlignment.Center
            };
            top.Checked += (o, e) => Topmost = true;
            top.Unchecked += (o, e) => Topmost = false;

            var dock = new DockPanel();
            DockPanel.SetDock(top, Dock.Top);
            dock.Children.Add(top);
            dock.Children.Add(new MotionVisualizer(plugin) { Margin = new Thickness(8) });
            Content = dock;
        }
    }
}
