// Compile-only stand-ins for the WPF surface used by the plugin UI (mirrors real WPF signatures).
using System; using System.Collections.Generic;
namespace System.Windows {
  public delegate void RoutedEventHandler(object sender, RoutedEventArgs e);
  public class RoutedEventArgs : EventArgs { }
  public struct Size { public Size(double w, double h) { Width = w; Height = h; } public double Width, Height; }
  public struct Point { public Point(double x, double y) { X = x; Y = y; } public double X { get; set; } public double Y { get; set; } }
  public struct Rect { public Rect(double x, double y, double w, double h) { Left = x; Top = y; Width = w; Height = h; } public double Left { get; } public double Top { get; } public double Width { get; } public double Height { get; } public double Right => Left + Width; public double Bottom => Top + Height; }
  public struct Thickness { public Thickness(double u) { } public Thickness(double l, double t, double r, double b) { } }
  public struct FontStyle { } public static class FontStyles { public static FontStyle Normal; }
  public struct FontWeight { } public static class FontWeights { public static FontWeight Normal, SemiBold, Bold; }
  public struct FontStretch { } public static class FontStretches { public static FontStretch Normal; }
  public enum FlowDirection { LeftToRight, RightToLeft }
  public enum VerticalAlignment { Top, Center, Bottom, Stretch }
  public enum TextWrapping { NoWrap, Wrap }
  public enum WindowStartupLocation { Manual, CenterScreen, CenterOwner }
  public class DependencyObject { }
  public class Visual : DependencyObject { }
  public class UIElement : Visual { public void InvalidateVisual() { } protected virtual void OnRender(System.Windows.Media.DrawingContext dc) { } public bool SnapsToDevicePixels { get; set; } }
  public class FrameworkElement : UIElement {
    public double MinWidth { get; set; } public double MinHeight { get; set; } public double Width { get; set; } public double Height { get; set; }
    public double ActualWidth { get; set; } public double ActualHeight { get; set; } public Thickness Margin { get; set; }
    public VerticalAlignment VerticalAlignment { get; set; }
    public event RoutedEventHandler Loaded; public event RoutedEventHandler Unloaded;
    protected virtual Size MeasureOverride(Size availableSize) => availableSize;
  }
  public class Window : System.Windows.Controls.ContentControl {
    public string Title { get; set; } public bool Topmost { get; set; } public System.Windows.Media.Brush Background { get; set; }
    public WindowStartupLocation WindowStartupLocation { get; set; } public event EventHandler Closed; public void Show() { } public bool Activate() => true;
  }
}
namespace System.Windows.Media {
  public class Freezable : DependencyObject { public void Freeze() { } }
  public struct Color { public byte A,R,G,B; public static Color FromRgb(byte r, byte g, byte b) => new Color{A=255,R=r,G=g,B=b}; public static Color FromArgb(byte a, byte r, byte g, byte b) => new Color{A=a,R=r,G=g,B=b}; public string Css => $"rgba({R},{G},{B},{A/255.0:0.###})"; }
  public class Brush : Freezable { public Color C; } public class SolidColorBrush : Brush { public SolidColorBrush(Color c) { C = c; } }
  public static class Brushes { public static SolidColorBrush White = new SolidColorBrush(Color.FromRgb(255,255,255)), Gainsboro = new SolidColorBrush(Color.FromRgb(220,220,220)); }
  public class DashStyle : Freezable { public List<double> D; public DashStyle(IEnumerable<double> dashes, double offset) { D = new List<double>(dashes); } }
  public class Pen : Freezable { public Brush B; public double T; public Pen(Brush b, double t) { B=b; T=t; } public DashStyle DashStyle { get; set; } }
  public class FontFamily { public FontFamily(string s) { } }
  public class Typeface { public bool Bold; public Typeface(FontFamily f, FontStyle s, FontWeight w, FontStretch st) { Bold = ReferenceEquals(null,null) && w.Equals(FontWeights.SemiBold) && false; } }
  public class FormattedText { public string S; public double Size; public Brush B; public Typeface TF; public FormattedText(string t, System.Globalization.CultureInfo c, FlowDirection d, Typeface tf, double size, Brush b, double pixelsPerDip) { S=t; Size=size; B=b; TF=tf; } public double Width => S.Length * Size * 0.56; public double Height => Size * 1.33; }
  public class Transform : Freezable { }
  public class TranslateTransform : Transform { public string Svg; public TranslateTransform(double x, double y) { Svg=$"translate({x:0.###},{y:0.###})"; } }
  public class ScaleTransform : Transform { public string Svg; public ScaleTransform(double x, double y) { Svg=$"scale({x:0.####},{y:0.####})"; } }
  public class RotateTransform : Transform { public string Svg; public RotateTransform(double a, double cx, double cy) { Svg=$"rotate({a:0.###},{cx:0.###},{cy:0.###})"; } }
  public class Geometry : Freezable { public static Geometry Parse(string s) => null; }
  public class EllipseGeometry : Geometry { public Point C; public double Rx, Ry; public EllipseGeometry(Point c, double rx, double ry) { C=c; Rx=rx; Ry=ry; } }
  public class GeometryGroup : Geometry { public List<Geometry> Children = new List<Geometry>(); }
  public class StreamGeometryContext : IDisposable { public System.Text.StringBuilder Sb = new System.Text.StringBuilder(); bool _closed; public void BeginFigure(Point p, bool filled, bool closed) { _closed = closed; Sb.Append($"M{p.X:0.##} {p.Y:0.##} "); } public void LineTo(Point p, bool stroked, bool smooth) { Sb.Append($"L{p.X:0.##} {p.Y:0.##} "); } public void Dispose() { if (_closed) Sb.Append("Z"); } }
  public class StreamGeometry : Geometry { public StreamGeometryContext Ctx = new StreamGeometryContext(); public StreamGeometryContext Open() => Ctx; }
  public abstract class DrawingContext_ {
    public abstract void DrawRoundedRectangle(Brush b, Pen p, Rect r, double rx, double ry); public abstract void DrawRectangle(Brush b, Pen p, Rect r);
    public abstract void DrawLine(Pen p, Point a, Point b); public abstract void DrawEllipse(Brush b, Pen p, Point c, double rx, double ry);
    public abstract void DrawText(FormattedText t, Point o); public abstract void DrawGeometry(Brush b, Pen p, Geometry g);
    public abstract void PushTransform(Transform t); public abstract void PushClip(Geometry g); public abstract void Pop();
  }
  public class ImageSource : Freezable { }
  public class GeometryDrawing { public GeometryDrawing(Brush b, Pen p, Geometry g) { } }
  public class DrawingImage : ImageSource { public DrawingImage(GeometryDrawing d) { } }
  public struct DpiScale { public double PixelsPerDip { get; } }
  public static class VisualTreeHelper { public static DpiScale GetDpi(Visual v) => new DpiScale(); }
}
namespace System.Windows.Threading {
  public enum DispatcherPriority { Render, Background, Normal }
  public class DispatcherTimer { public DispatcherTimer() { } public DispatcherTimer(DispatcherPriority p) { } public TimeSpan Interval { get; set; } public event EventHandler Tick; public void Start() { } public void Stop() { } }
}
namespace System.Windows.Controls {
  using System.Windows; using System.Windows.Media;
  public enum Dock { Left, Top, Right, Bottom }
  public enum ScrollBarVisibility { Disabled, Auto, Hidden, Visible }
  public delegate void SelectionChangedEventHandler(object s, SelectionChangedEventArgs e); public class SelectionChangedEventArgs : RoutedEventArgs { }
  public class UIElementCollection { public int Add(UIElement e) => 0; }
  public class ItemCollection { public int Add(object o) => 0; }
  public class Control : FrameworkElement { public Brush Foreground { get; set; } public Thickness Padding { get; set; } public FontFamily FontFamily { get; set; } public double FontSize { get; set; } }
  public class ContentControl : Control { public object Content { get; set; } }
  public class UserControl : ContentControl { }
  public class Panel : FrameworkElement { public UIElementCollection Children { get; } = new UIElementCollection(); }
  public class StackPanel : Panel { } public class WrapPanel : Panel { }
  public class DockPanel : Panel { public static void SetDock(UIElement e, Dock d) { } }
  public struct GridLength { public GridLength(double v) { } public static GridLength Auto => new GridLength(); }
  public class ColumnDefinition { public GridLength Width { get; set; } }
  public class Grid : Panel { public List<ColumnDefinition> ColumnDefinitions { get; } = new List<ColumnDefinition>(); public static void SetColumn(UIElement e, int c) { } }
  public class TextBlock : FrameworkElement { public string Text { get; set; } public TextWrapping TextWrapping { get; set; } public FontFamily FontFamily { get; set; } public double FontSize { get; set; } public FontWeight FontWeight { get; set; } }
  public class Button : ContentControl { public event RoutedEventHandler Click; }
  public class TextBox : Control { public string Text { get; set; } public event RoutedEventHandler LostFocus; }
  public class CheckBox : ContentControl { public bool? IsChecked { get; set; } public event RoutedEventHandler Checked; public event RoutedEventHandler Unchecked; }
  public class ComboBox : Control { public bool IsEditable { get; set; } public string Text { get; set; } public object SelectedItem { get; set; } public System.Collections.IEnumerable ItemsSource { get; set; } public event EventHandler DropDownOpened; public event RoutedEventHandler LostFocus; public event SelectionChangedEventHandler SelectionChanged; }
  public class ScrollViewer : ContentControl { public ScrollBarVisibility VerticalScrollBarVisibility { get; set; } }
  public class TabControl : Control { public ItemCollection Items { get; } = new ItemCollection(); }
  public class TabItem : ContentControl { public object Header { get; set; } }
}

namespace System.Windows.Media {
  public class DrawingContext : DrawingContext_ {
    public System.Text.StringBuilder Sb = new System.Text.StringBuilder(); int clipN; 
    static string F(Brush b) => b == null ? "none" : b.C.Css;
    static string P(Pen p) => p == null ? "stroke=\"none\"" : $"stroke=\"{p.B.C.Css}\" stroke-width=\"{p.T:0.##}\"" + (p.DashStyle != null ? $" stroke-dasharray=\"{string.Join(",", p.DashStyle.D.ConvertAll(d => (d * p.T).ToString("0.##")))}\"" : "");
    public override void DrawRoundedRectangle(Brush b, Pen p, Rect r, double rx, double ry) => Sb.Append($"<rect x='{r.Left:0.##}' y='{r.Top:0.##}' width='{r.Width:0.##}' height='{r.Height:0.##}' rx='{rx}' fill='{F(b)}' {P(p)}/>\n");
    public override void DrawRectangle(Brush b, Pen p, Rect r) => DrawRoundedRectangle(b, p, r, 0, 0);
    public override void DrawLine(Pen p, Point a, Point b) => Sb.Append($"<line x1='{a.X:0.##}' y1='{a.Y:0.##}' x2='{b.X:0.##}' y2='{b.Y:0.##}' {P(p)}/>\n");
    public override void DrawEllipse(Brush b, Pen p, Point c, double rx, double ry) => Sb.Append($"<ellipse cx='{c.X:0.##}' cy='{c.Y:0.##}' rx='{rx:0.##}' ry='{ry:0.##}' fill='{F(b)}' {P(p)}/>\n");
    public override void DrawText(FormattedText t, Point o) => Sb.Append($"<text x='{o.X:0.##}' y='{o.Y + t.Size * 0.95:0.##}' font-family='Segoe UI, DejaVu Sans, sans-serif' font-size='{t.Size}' fill='{F(t.B)}'>{System.Security.SecurityElement.Escape(t.S)}</text>\n");
    public override void DrawGeometry(Brush b, Pen p, Geometry g) { if (g is StreamGeometry sg) Sb.Append($"<path d='{sg.Ctx.Sb}' fill='{F(b)}' {P(p)}/>\n"); }
    public override void PushTransform(Transform t) { string s = t is TranslateTransform a ? a.Svg : t is ScaleTransform b ? b.Svg : ((RotateTransform)t).Svg; Sb.Append($"<g transform='{s}'>\n"); }
    public override void PushClip(Geometry g) { var e = (EllipseGeometry)g; clipN++; Sb.Append($"<clipPath id='c{clipN}'><ellipse cx='{e.C.X}' cy='{e.C.Y}' rx='{e.Rx}' ry='{e.Ry}'/></clipPath><g clip-path='url(#c{clipN})'>\n"); }
    public override void Pop() => Sb.Append("</g>\n");
  }
}
