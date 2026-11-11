// Compile-only stubs mirroring the SimHub SDK surface used by the plugin (NOT the real API).
using System;
namespace SimHub.Plugins {
  public class PluginManager { }
  public interface IPlugin { PluginManager PluginManager { get; set; } void Init(PluginManager p); void End(PluginManager p); }
  public interface IDataPlugin : IPlugin { void DataUpdate(PluginManager p, ref GameReaderCommon.GameData d); }
  public interface IWPFSettingsV2 { System.Windows.Controls.Control GetWPFSettingsControl(PluginManager p); System.Windows.Media.ImageSource PictureIcon { get; } string LeftMenuTitle { get; } }
  public class PluginDescriptionAttribute : Attribute { public PluginDescriptionAttribute(string s) { } }
  public class PluginAuthorAttribute : Attribute { public PluginAuthorAttribute(string s) { } }
  public class PluginNameAttribute : Attribute { public PluginNameAttribute(string s) { } }
  public static class Ext {
    public static void AttachDelegate<T>(this IPlugin p, string n, Func<T> f) { }
    public static void AddAction(this IPlugin p, string n, Action<PluginManager, string> a) { }
    public static void AddEvent(this IPlugin p, string n) { }
    public static void TriggerEvent(this IPlugin p, string n) { }
    public static T ReadCommonSettings<T>(this IPlugin p, string n, Func<T> f) => f();
    public static void SaveCommonSettings<T>(this IPlugin p, string n, T v) { }
  }
}
namespace GameReaderCommon { public class GameData { public bool GameRunning; } }
namespace SimHub.Logging { public static class Current { public static void Info(string s) { } } }
