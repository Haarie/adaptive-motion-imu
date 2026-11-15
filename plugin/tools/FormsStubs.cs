// Compile-only stand-in for the System.Windows.Forms bits the plugin uses (FolderBrowserDialog).
namespace System.Windows.Forms
{
    public enum DialogResult { None, OK, Cancel }
    public class FolderBrowserDialog : System.IDisposable
    {
        public string Description { get; set; }
        public string SelectedPath { get; set; }
        public bool ShowNewFolderButton { get; set; }
        public DialogResult ShowDialog() { return DialogResult.Cancel; }
        public void Dispose() { }
    }
}
