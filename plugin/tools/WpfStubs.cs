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
    public double ActualWidth { get; } public double ActualHeight { get; } public Thickness Margin { get; set; }
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
  public struct Color { public static Color FromRgb(byte r, byte g, byte b) => new Color(); public static Color FromArgb(byte a, byte r, byte g, byte b) => new Color(); }
  public class Brush : Freezable { } public class SolidColorBrush : Brush { public SolidColorBrush(Color c) { } }
  public static class Brushes { public static SolidColorBrush White, Gainsboro; }
  public class DashStyle : Freezable { public DashStyle(IEnumerable<double> dashes, double offset) { } }
  public class Pen : Freezable { public Pen(Brush b, double t) { } public DashStyle DashStyle { get; set; } }
  public class FontFamily { public FontFamily(string s) { } }
  public class Typeface { public Typeface(FontFamily f, FontStyle s, FontWeight w, FontStretch st) { } }
  public class FormattedText { public FormattedText(string t, System.Globalization.CultureInfo c, FlowDirection d, Typeface tf, double size, Brush b, double pixelsPerDip) { } public double Width { get; } public double Height { get; } }
  public class Transform : Freezable { }
  public class TranslateTransform : Transform { public TranslateTransform(double x, double y) { } }
  public class ScaleTransform : Transform { public ScaleTransform(double x, double y) { } }
  public class RotateTransform : Transform { public RotateTransform(double a, double cx, double cy) { } }
  public class Geometry : Freezable { public static Geometry Parse(string s) => null; }
  public class EllipseGeometry : Geometry { public EllipseGeometry(Point c, double rx, double ry) { } }
  public class GeometryGroup : Geometry { public List<Geometry> Children = new List<Geometry>(); }
  public abstract class StreamGeometryContext : IDisposable { public abstract void BeginFigure(Point p, bool filled, bool closed); public abstract void LineTo(Point p, bool stroked, bool smooth); public void Dispose() { } }
  public class StreamGeometry : Geometry { public StreamGeometryContext Open() => null; }
  public abstract class DrawingContext {
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
