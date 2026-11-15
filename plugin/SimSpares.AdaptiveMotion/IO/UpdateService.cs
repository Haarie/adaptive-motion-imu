using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace SimSpares.AdaptiveMotion.IO
{
    /// <summary>One downloadable part of a release (plugin DLL or firmware image).</summary>
    public sealed class UpdateAsset
    {
        public Version Version;
        public string FileName, Url, Sha256;
        public long Size;
        public string Board;          // firmware only: UF2 Board-ID it is built for
        public string SoftDevice;     // firmware only: required SoftDevice (from INFO_UF2.TXT)
    }

    /// <summary>The latest release as described by its manifest.json asset.</summary>
    public sealed class ReleaseInfo
    {
        public string Tag, Name, Notes, HtmlUrl;
        public DateTime Published;
        public UpdateAsset Plugin, Firmware;
    }

    /// <summary>
    /// Update checks against an update channel in a public GitHub repository:
    /// updates/stable/manifest.json (or updates/beta/) on the main branch, pointing at the plugin DLL and the
    /// firmware .uf2 stored next to it. Every download is checked against the SHA-256 in the manifest.
    /// </summary>
    public sealed class UpdateService
    {
        public const string PluginDllName = "SimSpares.AdaptiveMotion.dll";
        public event Action<string> Log;
        public ReleaseInfo Latest { get; private set; }
        public DateTime LastCheck { get; private set; }
        public string LastError { get; private set; } = "";
        public volatile bool Busy;
        public volatile string Progress = "";

        public static Version InstalledPluginVersion => Assembly.GetExecutingAssembly().GetName().Version;

        public static string WorkDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SimSpares", "AdaptiveMotion", "updates");

        /// <summary>Folder SimHub loads the plugin from (normally C:\Program Files (x86)\SimHub).</summary>
        public static string PluginFolder
        {
            get
            {
                try
                {
                    string loc = Assembly.GetExecutingAssembly().Location;
                    if (!string.IsNullOrEmpty(loc)) return Path.GetDirectoryName(loc);
                }
                catch { }
                return AppDomain.CurrentDomain.BaseDirectory;
            }
        }

        static UpdateService()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
        }

        private void Say(string m) { Progress = m; Log?.Invoke(m); }

        private static WebClient Client()
        {
            var wc = new WebClient();
            wc.Headers[HttpRequestHeader.UserAgent] = "SimSpares-AdaptiveMotion/" + InstalledPluginVersion;
            return wc;
        }

        // ------------------------------------------------------------------ check
        /// <summary>Update channel: updates/&lt;channel&gt;/manifest.json on the repository's main branch.</summary>
        public static string ManifestUrl(string repo, bool beta) =>
            "https://raw.githubusercontent.com/" + repo.Trim().Trim('/') + "/main/updates/" + (beta ? "beta" : "stable") + "/manifest.json";

        public Task<ReleaseInfo> CheckAsync(string repo, bool includePrerelease)
        {
            return Task.Run(() =>
            {
                Busy = true;
                try
                {
                    string url = ManifestUrl(repo, includePrerelease);
                    Say("Checking " + repo + (includePrerelease ? " (beta channel)" : "") + " …");
                    var js = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
                    Dictionary<string, object> man;
                    using (var wc = Client())
                    {
                        wc.Headers[HttpRequestHeader.Accept] = "application/json";
                        wc.Headers[HttpRequestHeader.CacheControl] = "no-cache";
                        man = js.Deserialize<Dictionary<string, object>>(wc.DownloadString(url + "?t=" + DateTime.UtcNow.Ticks));
                    }
                    var baseUri = new Uri(url);
                    var info = new ReleaseInfo
                    {
                        Tag = S(man, "tag"), Name = S(man, "name"), Notes = S(man, "notes"),
                        HtmlUrl = "https://github.com/" + repo.Trim().Trim('/') + "/tree/main/updates"
                    };
                    DateTime.TryParse(S(man, "released"), out info.Published);
                    info.Plugin = Asset(man, "plugin", baseUri);
                    info.Firmware = Asset(man, "firmware", baseUri);
                    if (string.IsNullOrEmpty(info.Tag) && info.Plugin?.Version != null) info.Tag = "v" + info.Plugin.Version;

                    Latest = info; LastCheck = DateTime.Now; LastError = "";
                    Say(string.Format("Latest: plugin {0}, firmware {1}",
                        info.Plugin?.Version?.ToString() ?? "-", info.Firmware?.Version?.ToString() ?? "-"));
                    return info;
                }
                catch (Exception e)
                {
                    LastError = Friendly(e); LastCheck = DateTime.Now;
                    Say("Update check failed: " + LastError);
                    return null;
                }
                finally { Busy = false; }
            });
        }

        private static UpdateAsset Asset(Dictionary<string, object> man, string key, Uri baseUri)
        {
            if (!man.ContainsKey(key) || !(man[key] is Dictionary<string, object> d)) return null;
            var a = new UpdateAsset
            {
                FileName = S(d, "file"), Sha256 = S(d, "sha256").ToLowerInvariant(),
                Board = S(d, "board"), SoftDevice = S(d, "softdevice")
            };
            Version.TryParse(S(d, "version"), out a.Version);
            string rel = S(d, "url");
            if (a.FileName.Length == 0 || a.Version == null || rel.Length == 0) return null;
            a.Url = new Uri(baseUri, rel).ToString();
            long.TryParse(S(d, "size"), out a.Size);
            return a;
        }

        private static string S(Dictionary<string, object> d, string k) => d != null && d.ContainsKey(k) && d[k] != null ? Convert.ToString(d[k]) : "";

        private static string Friendly(Exception e)
        {
            if (e is WebException we && we.Response is HttpWebResponse r)
            {
                if ((int)r.StatusCode == 404) return "no update channel found (is the repository public, and has a release been published?)";
                if ((int)r.StatusCode == 403) return "GitHub refused the request (rate limit: try again in an hour)";
                return "HTTP " + (int)r.StatusCode + " " + r.StatusDescription;
            }
            return e.InnerException != null ? e.InnerException.Message : e.Message;
        }

        // ------------------------------------------------------------------ download + verify
        public string Download(UpdateAsset a)
        {
            string dir = Path.Combine(WorkDir, a.Version.ToString());
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, a.FileName);
            if (File.Exists(path) && Sha256Of(path) == a.Sha256) { Say("Already downloaded: " + a.FileName); return path; }
            Say("Downloading " + a.FileName + " …");
            string tmp = path + ".part";
            using (var wc = Client())
            {
                wc.Headers[HttpRequestHeader.Accept] = "application/octet-stream";
                wc.DownloadFile(a.Url, tmp);
            }
            string sha = Sha256Of(tmp);
            if (a.Sha256.Length != 64 || sha != a.Sha256) { File.Delete(tmp); throw new Exception("checksum mismatch for " + a.FileName + " (download corrupted or tampered with)"); }
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
            Say("Downloaded and verified " + a.FileName);
            return path;
        }

        public static string Sha256Of(string path)
        {
            using (var s = File.OpenRead(path)) using (var h = SHA256.Create())
                return BitConverter.ToString(h.ComputeHash(s)).Replace("-", "").ToLowerInvariant();
        }

        // ------------------------------------------------------------------ plugin install
        /// <summary>
        /// Downloads the new DLL, writes a small PowerShell helper and starts it elevated (one UAC prompt).
        /// The helper waits until SimHub has exited, backs up the old DLL, copies the new one and starts
        /// SimHub again as the normal user. The caller then closes SimHub.
        /// </summary>
        public bool PreparePluginInstall(UpdateAsset a, out string error)
        {
            error = null;
            try
            {
                string dll = Download(a);
                var name = AssemblyName.GetAssemblyName(dll);
                if (name.Name != "SimSpares.AdaptiveMotion") throw new Exception("downloaded file is not the Adaptive Motion plugin");
                return StartSwapHelper(dll, a.Version.ToString(), out error);
            }
            catch (Exception e) { error = Friendly(e); Say("Plugin update failed: " + error); return false; }
        }

        public static string BackupPath => Path.Combine(WorkDir, "backup", PluginDllName);
        public static bool HasBackup => File.Exists(BackupPath);

        public bool PrepareRollback(out string error)
        {
            error = null;
            if (!HasBackup) { error = "no previous version saved"; return false; }
            string copy = Path.Combine(WorkDir, "rollback", PluginDllName);
            Directory.CreateDirectory(Path.GetDirectoryName(copy));
            File.Copy(BackupPath, copy, true);
            return StartSwapHelper(copy, "previous version", out error);
        }

        private bool StartSwapHelper(string newDll, string label, out string error)
        {
            error = null;
            string target = Path.Combine(PluginFolder, PluginDllName);
            string simhubExe = Path.Combine(PluginFolder, "SimHubWPF.exe");
            int pid = Process.GetCurrentProcess().Id;
            string backup = BackupPath;
            Directory.CreateDirectory(Path.GetDirectoryName(backup));
            string script = Path.Combine(WorkDir, "install_plugin.ps1");
            string logFile = Path.Combine(WorkDir, "install_plugin.log");
            var ps = new StringBuilder();
            ps.AppendLine("# Written by the Adaptive Motion plugin: swaps the plugin DLL after SimHub has closed.");
            ps.AppendLine("$ErrorActionPreference = 'Stop'");
            ps.AppendLine("Start-Transcript -Path " + Q(logFile) + " -Force | Out-Null");
            ps.AppendLine("try {");
            ps.AppendLine("  $p = Get-Process -Id " + pid + " -ErrorAction SilentlyContinue");
            ps.AppendLine("  if ($p) { Write-Host 'Waiting for SimHub to close ...'; $p.WaitForExit(600000) | Out-Null }");
            ps.AppendLine("  Start-Sleep -Milliseconds 800");
            ps.AppendLine("  if (Get-Process -Id " + pid + " -ErrorAction SilentlyContinue) { throw 'SimHub did not close within 10 minutes' }");
            ps.AppendLine("  if (Test-Path " + Q(target) + ") { Copy-Item -LiteralPath " + Q(target) + " -Destination " + Q(backup) + " -Force }");
            ps.AppendLine("  Copy-Item -LiteralPath " + Q(newDll) + " -Destination " + Q(target) + " -Force");
            ps.AppendLine("  Write-Host 'Installed " + label + "'");
            ps.AppendLine("} catch { Write-Host ('FAILED: ' + $_) }");
            ps.AppendLine("finally {");
            // start SimHub again as the logged-in user, not elevated
            ps.AppendLine("  if (Test-Path " + Q(simhubExe) + ") { Start-Process -FilePath 'explorer.exe' -ArgumentList " + Q("\"" + simhubExe + "\"") + " }");
            ps.AppendLine("  Stop-Transcript | Out-Null");
            ps.AppendLine("}");
            File.WriteAllText(script, ps.ToString(), new UTF8Encoding(true));
            try
            {
                var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + script + "\"")
                {
                    UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(psi);
                Say("Installer ready: SimHub will close, the plugin is replaced and SimHub starts again.");
                return true;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                error = "administrator permission was refused (needed to write into the SimHub folder)";
                Say("Plugin update cancelled: " + error);
                return false;
            }
        }

        private static string Q(string s) => "'" + s.Replace("'", "''") + "'";

        // ------------------------------------------------------------------ firmware
        /// <summary>Finds a mounted UF2 bootloader drive (INFO_UF2.TXT) and returns its root and the file's text.</summary>
        public static bool FindUf2Drive(out string root, out string info)
        {
            root = null; info = null;
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady || d.DriveType != DriveType.Removable) continue;
                    string f = Path.Combine(d.RootDirectory.FullName, "INFO_UF2.TXT");
                    if (!File.Exists(f)) continue;
                    root = d.RootDirectory.FullName; info = File.ReadAllText(f);
                    return true;
                }
                catch { }
            }
            return false;
        }

        /// <summary>
        /// Full firmware update: optional BOOTLOADER command, wait for the UF2 drive (a manual double-tap
        /// on RST also works), check board + SoftDevice, copy the image, wait for the drive to go away.
        /// The caller closes the serial port before and reopens it after.
        /// </summary>
        public bool FlashFirmware(UpdateAsset a, Func<bool> enterBootloader, CancellationToken ct, out string error)
        {
            error = null;
            try
            {
                string uf2 = Download(a);
                string root, info;
                if (!FindUf2Drive(out root, out info))
                {
                    bool sent = enterBootloader != null && enterBootloader();
                    Say(sent ? "Box restarting into update mode …"
                             : "Double-tap RST to GND on the box now (within 60 s) …");
                    var until = DateTime.UtcNow.AddSeconds(sent ? 20 : 60);
                    while (!FindUf2Drive(out root, out info))
                    {
                        if (ct.IsCancellationRequested) throw new OperationCanceledException();
                        if (DateTime.UtcNow > until)
                        {
                            if (sent) { Say("No update drive yet - double-tap RST to GND on the box (40 s) …"); until = DateTime.UtcNow.AddSeconds(40); sent = false; continue; }
                            throw new Exception("the NICENANO update drive did not appear");
                        }
                        Thread.Sleep(500);
                    }
                }
                Say("Update drive found: " + root);
                if (!string.IsNullOrEmpty(a.Board) && info.IndexOf(a.Board, StringComparison.OrdinalIgnoreCase) < 0)
                    throw new Exception("this board reports a different Board-ID than " + a.Board + " - not flashing");
                if (!string.IsNullOrEmpty(a.SoftDevice) && info.IndexOf(a.SoftDevice, StringComparison.OrdinalIgnoreCase) < 0)
                    throw new Exception("bootloader SoftDevice is not " + a.SoftDevice + " - not flashing");

                Say("Copying " + a.FileName + " to the box …");
                string dst = Path.Combine(root, "UPDATE.UF2");
                try
                {
                    using (var src = File.OpenRead(uf2))
                    using (var o = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                    { src.CopyTo(o); o.Flush(true); }
                }
                catch (IOException) { /* the bootloader may reboot and drop the drive as the last block lands */ }

                var gone = DateTime.UtcNow.AddSeconds(20);
                while (Directory.Exists(root) && File.Exists(Path.Combine(root, "INFO_UF2.TXT")) && DateTime.UtcNow < gone) Thread.Sleep(300);
                Say("Firmware written, box restarting …");
                Thread.Sleep(2500);
                return true;
            }
            catch (OperationCanceledException) { error = "cancelled"; Say("Firmware update cancelled"); return false; }
            catch (Exception e) { error = Friendly(e); Say("Firmware update failed: " + error); return false; }
        }

        /// <summary>"INFO fw=1.3.1 who=0x68 ..." -> 1.3.1</summary>
        public static Version ParseFirmwareVersion(string info)
        {
            if (string.IsNullOrEmpty(info)) return null;
            int i = info.IndexOf("fw=", StringComparison.Ordinal);
            if (i < 0) return null;
            string v = new string(info.Substring(i + 3).TakeWhile(ch => char.IsDigit(ch) || ch == '.').ToArray());
            return Version.TryParse(v, out var ver) ? ver : null;
        }

        /// <summary>Compare ignoring an unset 4th component (1.2.0 == 1.2.0.0).</summary>
        public static bool IsNewer(Version latest, Version current)
        {
            if (latest == null) return false;
            if (current == null) return true;
            Func<Version, Version> n = v => new Version(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));
            return n(latest) > n(current);
        }
    }
}
