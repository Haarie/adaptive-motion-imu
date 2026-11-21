using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SimSpares.AdaptiveMotion
{
    /// <summary>Dialogs in the Sim-Spares style (dark ground, green accent, orange for safety).</summary>
    internal static class SsDialog
    {
        private static Brush B(byte r, byte g, byte b) { var x = new SolidColorBrush(Color.FromRgb(r, g, b)); x.Freeze(); return x; }
        private static readonly Brush Ground = B(0x0A, 0x0C, 0x0B), Panel = B(0x12, 0x15, 0x13), Card = B(0x17, 0x1B, 0x19),
            Line = B(0x26, 0x2B, 0x27), Ink = B(0xF3, 0xF5, 0xF1), Muted = B(0x9A, 0xA3, 0x9C), Accent = B(0xA6, 0xFF, 0x4D), Safety = B(0xFF, 0x7A, 0x29);
        private static readonly FontFamily Head = new FontFamily("Rajdhani, Bahnschrift, Segoe UI"), Mono = new FontFamily("JetBrains Mono, Consolas"), Body = new FontFamily("Roboto, Segoe UI");

        public enum Kind { Info, Warning }

        /// <summary>Safety confirmation before the verification test. True = start.</summary>
        public static bool ConfirmVerification(DependencyObject owner, bool full, double pitchDeg, double rollDeg, double speedDps, double holdSec)
        {
            var w = NewWindow(owner, "RIG VERIFICATION", full ? "FULL MOTION 100 %" : "HALF MOTION 50 %", full ? Safety : Accent, out var body, out var buttons);

            // what will happen
            var poses = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            for (int i = 0; i < 3; i++) poses.ColumnDefinitions.Add(new ColumnDefinition());
            AddStat(poses, 0, "PITCH", string.Format(System.Globalization.CultureInfo.InvariantCulture, "±{0:F1}°", pitchDeg));
            AddStat(poses, 1, "ROLL", string.Format(System.Globalization.CultureInfo.InvariantCulture, "±{0:F1}°", rollDeg));
            AddStat(poses, 2, "SPEED", string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.#}°/s", speedDps));
            body.Children.Add(poses);
            body.Children.Add(Para("The plugin moves the rig through ¼, ½, ¾ and the full pose in pitch and roll and compares each pose with what the IMU measures.", Muted, 0, 0, 0, 14));

            // safety checklist: Start only when every box is ticked
            var safety = new StackPanel();
            safety.Children.Add(new TextBlock { Text = "⚠  SAFETY CHECK", FontFamily = Head, FontWeight = FontWeights.Bold, FontSize = 17, Foreground = Safety, Margin = new Thickness(0, 0, 0, 4) });
            safety.Children.Add(Para("The rig moves on its own and does not react to a person in the seat.", Ink, 0, 0, 0, 8));
            var checks = new[]
            {
                Check("The seat is empty: nobody is in the rig"),
                Check("Nobody is within reach of the moving parts"),
                Check("The e-stop is within my reach"),
            };
            foreach (var c in checks) safety.Children.Add(c);
            body.Children.Add(new Border { Child = safety, Background = Card, BorderBrush = Safety, BorderThickness = new Thickness(2, 2, 2, 2), CornerRadius = new CornerRadius(2), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 12) });

            // what happens afterwards
            var after = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            after.Children.Add(new TextBlock { Text = "AFTER THE TEST", FontFamily = Mono, FontSize = 11, Foreground = Accent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            body.Children.Add(after);
            body.Children.Add(Para(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "The rig holds at centre for {0:F0} s, then moves to its PARK position (the controller parks it when the test stops sending). Stay clear until it has stopped moving. Stop returns the rig to centre at any time.", holdSec), Muted, 0, 0, 0, 0));

            var cancel = MakeButton("CANCEL", false);
            var start = MakeButton("START TEST", true);
            start.IsEnabled = false;
            foreach (var c in checks) { c.Checked += (o, e) => start.IsEnabled = checks.All(x => x.IsChecked == true); c.Unchecked += (o, e) => start.IsEnabled = false; }
            bool ok = false;
            cancel.Click += (o, e) => w.Close();
            start.Click += (o, e) => { ok = true; w.Close(); };
            buttons.Children.Add(cancel);
            buttons.Children.Add(start);
            w.ShowDialog();
            return ok;
        }

        /// <summary>Confirmation before the plugin moves the rig to its centre to capture the level or the driver offset.</summary>
        public static bool ConfirmCentreCapture(DependencyObject owner, bool driver, double holdSec)
        {
            var w = NewWindow(owner, driver ? "DRIVER OFFSET" : "LEVEL AT CENTRE", driver ? "YOU IN THE SEAT" : "RIG EMPTY", driver ? Accent : Safety, out var body, out var buttons);
            body.Children.Add(Para(driver
                ? "The plugin brings the rig out of park: rig active at its centre (not parked), the position SimHub uses for zero. It waits until the rig stands still and measures how much your weight tilts the seat. The rig does not move beyond its centre."
                : "The plugin brings the rig out of park: rig active at its centre (not parked), the position SimHub uses for zero. It waits until the controller is online and the rig stands still, then captures the level there.", Ink, 0, 0, 0, 12));
            var safety = new StackPanel();
            safety.Children.Add(new TextBlock { Text = "⚠  SAFETY CHECK", FontFamily = Head, FontWeight = FontWeights.Bold, FontSize = 17, Foreground = Safety, Margin = new Thickness(0, 0, 0, 6) });
            var checks = driver
                ? new[] { Check("I sit in my normal driving position, hands off the wheel and pedals"), Check("Nobody else is within reach of the moving parts"), Check("The e-stop is within my reach") }
                : new[] { Check("The seat is empty: nobody is in the rig"), Check("Nobody is within reach of the moving parts"), Check("The e-stop is within my reach") };
            foreach (var c in checks) safety.Children.Add(c);
            body.Children.Add(new Border { Child = safety, Background = Card, BorderBrush = Safety, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(2), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 12) });
            body.Children.Add(Para(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "Takes about 10–15 s (the controller unparks first). Afterwards the rig holds at centre for {0:F0} s, then the controller parks it. Stop ends it at any time without saving.", holdSec), Muted, 0, 0, 0, 0));
            var cancel = MakeButton("CANCEL", false);
            var start = MakeButton(driver ? "CAPTURE OFFSET" : "CAPTURE LEVEL", true);
            start.IsEnabled = false;
            foreach (var c in checks) { c.Checked += (o, e) => start.IsEnabled = checks.All(x => x.IsChecked == true); c.Unchecked += (o, e) => start.IsEnabled = false; }
            bool ok = false;
            cancel.Click += (o, e) => w.Close();
            start.Click += (o, e) => { ok = true; w.Close(); };
            buttons.Children.Add(cancel); buttons.Children.Add(start);
            w.ShowDialog();
            return ok;
        }

        /// <summary>A message in the same style (blockers, errors).</summary>
        public static void Show(DependencyObject owner, string title, string intro, IEnumerable<string> points, Kind kind = Kind.Info)
        {
            var color = kind == Kind.Warning ? Safety : Accent;
            var w = NewWindow(owner, title, kind == Kind.Warning ? "ATTENTION" : "INFO", color, out var body, out var buttons);
            if (!string.IsNullOrEmpty(intro)) body.Children.Add(Para(intro, Ink, 0, 0, 0, 10));
            if (points != null)
                foreach (var p in points)
                {
                    var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                    var dot = new TextBlock { Text = "▸", Foreground = color, FontFamily = Mono, Margin = new Thickness(0, 0, 8, 0) };
                    DockPanel.SetDock(dot, Dock.Left);
                    row.Children.Add(dot);
                    row.Children.Add(Para(p, Muted, 0, 0, 0, 0));
                    body.Children.Add(row);
                }
            var ok = MakeButton("OK", true);
            ok.Click += (o, e) => w.Close();
            buttons.Children.Add(ok);
            w.ShowDialog();
        }

        // ---------------------------------------------------------------------------------------
        private static Window NewWindow(DependencyObject owner, string title, string tag, Brush tagColor, out StackPanel body, out StackPanel buttons)
        {
            var w = new Window
            {
                Width = 540, SizeToContent = SizeToContent.Height, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                Background = Ground, ShowInTaskbar = false, Title = title, FontFamily = Body, Foreground = Ink,
                BorderBrush = Line, BorderThickness = new Thickness(1),
            };
            var ow = owner == null ? null : Window.GetWindow(owner);
            if (ow != null) { w.Owner = ow; w.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
            else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            w.KeyDown += (o, e) => { if (e.Key == Key.Escape) w.Close(); };

            var root = new StackPanel();
            // header: accent bar, title, mode tag; drag to move
            var head = new DockPanel { Background = Panel, LastChildFill = true };
            head.MouseLeftButtonDown += (o, e) => { try { w.DragMove(); } catch { } };
            var bar = new Border { Width = 4, Background = tagColor };
            DockPanel.SetDock(bar, Dock.Left);
            head.Children.Add(bar);
            var pill = new Border { Child = new TextBlock { Text = tag, FontFamily = Mono, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Ground },
                Background = tagColor, CornerRadius = new CornerRadius(2), Padding = new Thickness(8, 3, 8, 3), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            DockPanel.SetDock(pill, Dock.Right);
            head.Children.Add(pill);
            var titles = new StackPanel { Margin = new Thickness(16, 12, 0, 12) };
            titles.Children.Add(new TextBlock { Text = "SIM-SPARES · ADAPTIVE MOTION", FontFamily = Mono, FontSize = 10, Foreground = Muted });
            titles.Children.Add(new TextBlock { Text = title, FontFamily = Head, FontSize = 24, FontWeight = FontWeights.Bold, Foreground = Ink });
            head.Children.Add(titles);
            root.Children.Add(head);
            root.Children.Add(new Border { Height = 1, Background = Line });

            body = new StackPanel { Margin = new Thickness(20, 16, 20, 8) };
            root.Children.Add(body);
            buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(20, 8, 20, 18) };
            root.Children.Add(buttons);
            w.Content = root;
            return w;
        }

        private static void AddStat(Grid g, int col, string label, string value)
        {
            var p = new StackPanel();
            p.Children.Add(new TextBlock { Text = label, FontFamily = Mono, FontSize = 10, Foreground = Muted });
            p.Children.Add(new TextBlock { Text = value, FontFamily = Head, FontSize = 26, FontWeight = FontWeights.Bold, Foreground = Ink });
            var c = new Border { Child = p, Background = Card, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2),
                Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(col == 0 ? 0 : 4, 0, col == 2 ? 0 : 4, 0) };
            Grid.SetColumn(c, col);
            g.Children.Add(c);
        }

        private static TextBlock Para(string t, Brush fg, double l, double tp, double r, double b) =>
            new TextBlock { Text = t, TextWrapping = TextWrapping.Wrap, Foreground = fg, FontSize = 13, LineHeight = 19, Margin = new Thickness(l, tp, r, b) };

        private static CheckBox Check(string text) => new CheckBox
        {
            Content = new TextBlock { Text = text, Foreground = Ink, FontSize = 13, TextWrapping = TextWrapping.Wrap },
            Margin = new Thickness(0, 3, 0, 3), VerticalContentAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand,
        };

        private static Button MakeButton(string text, bool primary)
        {
            // flat button with our own template (the default WPF chrome ignores the colours on hover)
            var fac = new FrameworkElementFactory(typeof(Border), "bd");
            fac.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
            fac.SetValue(Border.BorderThicknessProperty, new Thickness(2));
            fac.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            fac.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            fac.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            fac.AppendChild(cp);
            var tpl = new ControlTemplate(typeof(Button)) { VisualTree = fac };
            var dis = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            dis.Setters.Add(new Setter(UIElement.OpacityProperty, 0.35));
            tpl.Triggers.Add(dis);
            var hov = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hov.Setters.Add(new Setter(Border.BorderBrushProperty, primary ? Safety : Accent, "bd"));
            tpl.Triggers.Add(hov);

            return new Button
            {
                Content = text, Template = tpl, FontFamily = Mono, FontSize = 13, FontWeight = FontWeights.Bold, Cursor = Cursors.Hand,
                Padding = new Thickness(18, 8, 18, 8), Margin = new Thickness(10, 0, 0, 0), MinWidth = 110,
                Background = primary ? Accent : Brushes.Transparent, Foreground = primary ? Ground : Ink, BorderBrush = primary ? Accent : Line,
            };
        }
    }
}
