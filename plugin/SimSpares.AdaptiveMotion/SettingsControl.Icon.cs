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
            // Closed loop (two arrows) around a tilted horizon with the sensor ring in the centre.
            // White like SimHub's own menu icons; drawn in code, so no resource file is needed.
            var white = Brushes.White;
            Func<double, Pen> pen = w => { var pn = new Pen(white, w) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }; pn.Freeze(); return pn; };
            var dg = new DrawingGroup();
            dg.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new System.Windows.Rect(0, 0, 24, 24))));   // fixed 24×24 bounds
            dg.Children.Add(new GeometryDrawing(null, pen(2.0), Geometry.Parse("M18.82,6.76 A8.6,8.6 0 0 0 3.67,9.85 M5.18,17.24 A8.6,8.6 0 0 0 20.33,14.15")));
            dg.Children.Add(new GeometryDrawing(white, null, Geometry.Parse("M3.05,12.24 L1.82,8.86 L5.78,9.88 Z M20.95,11.76 L22.18,15.14 L18.22,14.12 Z")));
            dg.Children.Add(new GeometryDrawing(null, pen(2.2), Geometry.Parse("M3.33,14.41 L7.95,13.12 M16.05,10.88 L20.67,9.59")));
            dg.Children.Add(new GeometryDrawing(null, pen(1.8), new EllipseGeometry(new System.Windows.Point(12, 12), 2.6, 2.6)));
            var img = new DrawingImage(dg);
            img.Freeze();
            return _icon = img;
        }
    }
}
