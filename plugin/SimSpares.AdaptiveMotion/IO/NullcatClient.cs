using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using SimSpares.AdaptiveMotion.Core;

namespace SimSpares.AdaptiveMotion.IO
{
    /// <summary>
    /// nullCAT's web API (default port 8080): polls /api/status in the background and can send /api/unpark.
    /// Read-only except for Unpark(), which the plugin only calls after the user confirmed the safety dialog.
    /// </summary>
    public sealed class NullcatClient : IDisposable
    {
        private Thread _thread;
        private volatile bool _run;
        private volatile NullcatStatus _status = NullcatStatus.Unreachable("not checked yet", double.NaN);
        public Func<string> Host = () => null;
        public Func<int> Port = () => 8080;
        public Action<string> Log;
        public NullcatStatus Status => _status;

        public void Start()
        {
            if (_run) return;
            _run = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "nullCAT status" };
            _thread.Start();
        }

        public void Stop() { _run = false; _thread?.Join(1500); _thread = null; }
        public void Dispose() => Stop();

        private string Url(string path)
        {
            string h = Host?.Invoke();
            if (string.IsNullOrWhiteSpace(h)) return null;
            return "http://" + h.Trim() + ":" + Port() + path;
        }

        private void Loop()
        {
            while (_run)
            {
                Poll();
                for (int i = 0; i < 20 && _run; i++) Thread.Sleep(100);   // every 2 s
            }
        }

        /// <summary>Read the status now (blocking, ≤ 1.5 s).</summary>
        public NullcatStatus Poll()
        {
            double now = ImuSerialLink.NowSec;
            string url = Url("/api/status");
            if (url == null) return _status = NullcatStatus.Unreachable("no controller address", now);
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET"; req.Timeout = 1500; req.ReadWriteTimeout = 1500; req.Proxy = null;
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return _status = NullcatStatus.FromJson(sr.ReadToEnd(), now);
            }
            catch (Exception e) { return _status = NullcatStatus.Unreachable(Short(e), now); }
        }

        /// <summary>POST /api/unpark. Returns null on success, else the reason.</summary>
        public string Unpark()
        {
            string url = Url("/api/unpark");
            if (url == null) return "no controller address";
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "POST"; req.Timeout = 2000; req.ReadWriteTimeout = 2000; req.Proxy = null;
                req.ContentLength = 0;
                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    Log?.Invoke("nullCAT: unpark sent (" + (int)resp.StatusCode + ")");
                    return null;
                }
            }
            catch (WebException e) when (e.Response is HttpWebResponse r)
            {
                string body = "";
                try { using (var sr = new StreamReader(r.GetResponseStream())) body = sr.ReadToEnd(); } catch { }
                return "nullCAT refused the unpark (" + (int)r.StatusCode + (body.Length > 0 ? ": " + body.Trim() : "") + ")";
            }
            catch (Exception e) { return "nullCAT not reachable: " + Short(e); }
        }

        private static string Short(Exception e) => e is WebException we && we.Status != WebExceptionStatus.ProtocolError ? we.Status.ToString() : e.Message;
    }
}
