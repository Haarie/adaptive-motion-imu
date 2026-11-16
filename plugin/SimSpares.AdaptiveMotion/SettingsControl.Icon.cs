using System;
using System.Windows.Media;

namespace SimSpares.AdaptiveMotion
{
    internal static class MenuIcon
    {
        private static ImageSource _icon;
        public static ImageSource Create()
        {
            if (_icon != null) return _icon;
            // Tilted platform on two actuators, the IMU (orange) on it and two measuring arcs (green).
            // Drawn in code so no resource file is needed; Sim-Spares colours.
            Func<byte, byte, byte, Brush> col = (r, gg, bb) => { var x = new SolidColorBrush(Color.FromRgb(r, gg, bb)); x.Freeze(); return x; };
            Brush ink = col(0xF3, 0xF5, 0xF1), muted = col(0x9A, 0xA3, 0x9C), accent = col(0xA6, 0xFF, 0x4D), orange = col(0xFF, 0x7A, 0x29);
            Func<Brush, double, Pen> pen = (br, w) => { var pn = new Pen(br, w) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }; pn.Freeze(); return pn; };
            var dg = new DrawingGroup();
            dg.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new System.Windows.Rect(0, 0, 24, 24))));   // fixed 24×24 bounds
            dg.Children.Add(new GeometryDrawing(null, pen(ink, 1.6), Geometry.Parse("M3,21.2 H21")));
            dg.Children.Add(new GeometryDrawing(null, pen(muted, 1.8), Geometry.Parse("M7.5,21.2 V16.2 M16.5,21.2 V13.6")));
            dg.Children.Add(new GeometryDrawing(null, pen(ink, 2.2), Geometry.Parse("M3.5,17.2 L20.5,12.4")));
            dg.Children.Add(new GeometryDrawing(null, pen(accent, 1.6), Geometry.Parse("M9.17,9.77 A4,4 0 0 1 14.83,9.77")));
            var outer = new GeometryDrawing(null, pen(col(0x7C, 0xBF, 0x3A), 1.6), Geometry.Parse("M6.7,7.3 A7.5,7.5 0 0 1 17.3,7.3"));
            dg.Children.Add(outer);
            dg.Children.Add(new GeometryDrawing(orange, null, new RectangleGeometry(new System.Windows.Rect(10.2, 12.0, 3.6, 2.6), 0.5, 0.5, new RotateTransform(-15.8, 12, 13.3))));
            var img = new DrawingImage(dg);
            img.Freeze();
            return _icon = img;
        }
    }
}
