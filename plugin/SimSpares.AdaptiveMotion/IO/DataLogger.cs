using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;

namespace SimSpares.AdaptiveMotion.IO
{
    /// <summary>
    /// CSV logger with a background writer thread, so the IMU thread never waits for the disk.
    /// One file per session: AMIMU_yyyy-MM-dd_HH-mm-ss[_game][_car][_track].csv in the chosen folder.
    /// Lines starting with '#' are comments (session info), the rest is plain CSV (Excel, Python, SimHub-free).
    /// </summary>
    public sealed class DataLogger : IDisposable
    {
        private readonly object _lock = new object();
        private BlockingCollection<string> _queue;
        private Thread _thread;
        private StreamWriter _w;
        private long _rows;
        private double _startSec;

        public bool Active { get; private set; }
        public string FilePath { get; private set; } = "";
        public long Rows => Interlocked.Read(ref _rows);
        public long DroppedLines { get; private set; }
        public event Action<string> Log;

        public double Seconds(double nowSec) => Active ? nowSec - _startSec : 0;

        public static string DefaultFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Adaptive Motion IMU", "logs");

        /// <summary>Opens a new file and writes the header. Returns false (and logs why) on failure.</summary>
        public bool Start(string folder, string tag, string header, string[] comments, double nowSec)
        {
            lock (_lock)
            {
                if (Active) Stop();
                try
                {
                    if (string.IsNullOrWhiteSpace(folder)) folder = DefaultFolder;
                    Directory.CreateDirectory(folder);
                    string name = "AMIMU_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                    if (!string.IsNullOrWhiteSpace(tag)) name += "_" + Sanitize(tag);
                    FilePath = Path.Combine(folder, name + ".csv");
                    _w = new StreamWriter(new FileStream(FilePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false), 1 << 16);
                    foreach (var c in comments ?? new string[0]) _w.WriteLine("# " + c);
                    _w.WriteLine(header);
                    _w.Flush();
                    _queue = new BlockingCollection<string>(new ConcurrentQueue<string>(), 200000);
                    Interlocked.Exchange(ref _rows, 0); DroppedLines = 0;
                    _startSec = nowSec;
                    Active = true;
                    var q = _queue; var w = _w;
                    _thread = new Thread(() => WriterLoop(q, w)) { IsBackground = true, Name = "AMIMU log writer", Priority = ThreadPriority.BelowNormal };
                    _thread.Start();
                    Log?.Invoke("Logging to " + FilePath);
                    return true;
                }
                catch (Exception e)
                {
                    Active = false;
                    try { _w?.Dispose(); } catch { }
                    _w = null;
                    Log?.Invoke("Log start failed: " + e.Message);
                    return false;
                }
            }
        }

        /// <summary>Queue one CSV line (no newline). Never blocks: drops the line if the writer is far behind.</summary>
        public void Write(string line)
        {
            var q = _queue;
            if (!Active || q == null) return;
            if (q.TryAdd(line)) Interlocked.Increment(ref _rows);
            else DroppedLines++;
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (!Active) return;
                Active = false;
                try { _queue.CompleteAdding(); } catch { }
                try { _thread?.Join(3000); } catch { }
                try { _w?.Flush(); _w?.Dispose(); } catch { }
                _w = null; _thread = null;
                Log?.Invoke(string.Format("Log closed: {0} ({1} rows{2})", FilePath, Rows,
                    DroppedLines > 0 ? ", " + DroppedLines + " dropped" : ""));
            }
        }

        private static void WriterLoop(BlockingCollection<string> q, StreamWriter w)
        {
            var lastFlush = DateTime.UtcNow;
            try
            {
                foreach (var line in q.GetConsumingEnumerable())
                {
                    w.WriteLine(line);
                    if ((DateTime.UtcNow - lastFlush).TotalSeconds > 1) { w.Flush(); lastFlush = DateTime.UtcNow; }
                }
            }
            catch { /* disk full / file removed: the logger stops writing, SimHub keeps running */ }
        }

        private static string Sanitize(string s)
        {
            var sb = new StringBuilder();
            foreach (char ch in s)
                sb.Append(char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '_');
            string r = sb.ToString();
            while (r.Contains("__")) r = r.Replace("__", "_");
            r = r.Trim('_');
            return r.Length > 80 ? r.Substring(0, 80) : r;
        }

        public void Dispose() => Stop();
    }
}
