using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace ZmlSetup {
    public class OwnedFile {
        public string Path, Before, After, Backup;
    }
    public class InstallState {
        public int Schema = 1;
        public string Status, Root, Game, Token;
        public int Port;
        public List<OwnedFile> Files = new List<OwnedFile>();
    }
    public static class Util {
        [System.Runtime.InteropServices.DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
        [System.Runtime.InteropServices.DllImport("kernel32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode,SetLastError=true)] static extern bool QueryFullProcessImageName(IntPtr process,uint flags,StringBuilder path,ref uint size);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        // Query process image name
        public static string ProcessImagePath(int pid) {
            var handle=OpenProcess(0x1000,false,pid); // PROCESS_QUERY_LIMITED_INFORMATION
            if(handle==IntPtr.Zero) throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
            try {
                var path=new StringBuilder(32768);uint length=32768;
                if(!QueryFullProcessImageName(handle,0,path,ref length)) throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
                return path.ToString();
            } finally {CloseHandle(handle);}
        }
        public static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        public static string Json(object value) { return new JavaScriptSerializer { MaxJsonLength = 8*1024*1024 }.Serialize(value); }
        public static T ReadJson<T>(string path) {
            if (new FileInfo(path).Length > 8*1024*1024) throw new IOException("JSON 文件过大");
            return new JavaScriptSerializer { MaxJsonLength = 8*1024*1024 }.Deserialize<T>(File.ReadAllText(path, Utf8));
        }
        public static string Hash(byte[] bytes) {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        public static string HashFile(string path) { return File.Exists(path) ? Hash(File.ReadAllBytes(path)) : null; }
        public static string Under(string root, string relative) {
            if (String.IsNullOrEmpty(relative) || Path.IsPathRooted(relative) || relative.Contains(":")) throw new IOException("路径越界");
            var full = Path.GetFullPath(Path.Combine(root, relative));
            var prefix = Path.GetFullPath(root).TrimEnd('\\') + "\\";
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new IOException("路径越界");
            NoLinks(full);
            return full;
        }
        public static void NoLinks(string path) {
            for (var p = Path.GetFullPath(path); !String.IsNullOrEmpty(p); p = Path.GetDirectoryName(p)) {
                if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("为避免写出所选目录，不支持符号链接/目录联接：" + p);
            }
        }
        public static void Atomic(string path, byte[] bytes) {
            NoLinks(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temp = Path.Combine(Path.GetDirectoryName(path),".zml-" + Guid.NewGuid().ToString("N"));
            try {
                using (var s = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { s.Write(bytes, 0, bytes.Length); s.Flush(true); }
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            } finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        public static void WriteJson(string path, object value) { Atomic(path, Utf8.GetBytes(Json(value))); }
        public static string Quote(string value) {
            var b = new StringBuilder("\""); int slashes = 0;
            foreach (var c in value) {
                if (c == '\\') { slashes++; continue; }
                b.Append('\\', c == '"' ? slashes*2+1 : slashes); slashes=0; b.Append(c);
            }
            b.Append('\\', slashes*2); return b.Append('"').ToString();
        }
        public static string Run(string exe, string args, string cwd, int seconds) {
            var output = new StringBuilder(); var gate = new object();
            using (var p = new Process()) {
                p.StartInfo = new ProcessStartInfo(exe, args) { WorkingDirectory=cwd, UseShellExecute=false, CreateNoWindow=true, RedirectStandardOutput=true, RedirectStandardError=true, StandardOutputEncoding=Utf8, StandardErrorEncoding=Utf8 };
                DataReceivedEventHandler receive = (s,e) => { if (e.Data!=null) lock(gate) { if(output.Length<65536) output.AppendLine(e.Data); } };
                p.OutputDataReceived+=receive; p.ErrorDataReceived+=receive;
                p.Start(); p.BeginOutputReadLine(); p.BeginErrorReadLine();
                if (!p.WaitForExit(seconds*1000)) { p.Kill(); throw new IOException("加载器超时。"); }
                p.WaitForExit();
                if (p.ExitCode!=0) throw new IOException(output.ToString().Trim());
                return output.ToString().Trim();
            }
        }
        public static string StatePath(string root) { return Under(root, "ZML\\launcher-state.json"); }
        public static InstallState State(string root) {
            var state=ReadJson<InstallState>(StatePath(root));
            if (state==null || state.Schema!=1 || !String.Equals(Path.GetFullPath(root).TrimEnd('\\'), state.Root, StringComparison.OrdinalIgnoreCase) || state.Port<1024 || state.Port>65535 || state.Token==null || !System.Text.RegularExpressions.Regex.IsMatch(state.Token,"^[0-9a-f]{64}$") || state.Files==null || state.Files.Count>256) throw new IOException("安装状态无效");
            var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var known=new[]{"Launcher.exe","Launcher.zml-original.exe","ZML\\ZML.exe","ZML\\ZMLRuntime.dll","ZML\\ZMLNativeLaunch.dll","ZML\\ZMLLauncherOptions.dll","ZML\\ZMLUninstall.exe","ZML\\Qt-LICENSE.txt","ZML\\README.md","ZML\\MinHook-LICENSE.txt","ZML\\loader.ini.example","ZML\\loader.ini","ZML\\lua\\zml.lua","ZML\\docs\\MOD_API.md","ZML\\INSTALLER.md"};
            var digest=new System.Text.RegularExpressions.Regex("^[0-9a-f]{64}$");
            foreach(var f in state.Files) {
                // Validate uninstaller state path
                if(f==null || f.Path==null || !seen.Add(f.Path) || (!known.Contains(f.Path,StringComparer.OrdinalIgnoreCase) && !System.Text.RegularExpressions.Regex.IsMatch(f.Path,@"^\d+\.\d+\.\d+(\.\d+)?\\res\\web\\(index\.html|zml-client\.js|zml-client\.css|zml-endpoint\.js)$")) || f.After==null || !digest.IsMatch(f.After) || (f.Before==null)!=(f.Backup==null) || (f.Before!=null && (!digest.IsMatch(f.Before) || !f.Backup.StartsWith("ZML\\backups\\",StringComparison.Ordinal)))) throw new IOException("恢复计划路径/校验无效");
                Under(root,f.Path); if(f.Backup!=null && !Under(root,f.Backup).StartsWith(Under(root,"ZML\\backups")+"\\",StringComparison.OrdinalIgnoreCase))throw new IOException("备份路径越界");
            }
            return state;
        }
        public static void Validate(string root, string game) {
            Run(Under(root,"ZML\\ZML.exe"), "--game " + Quote(game) + " --dry-run", Under(root,"ZML"), 30);
        }
        public static void CheckLauncherClosed(string root) {
            var prefix=Path.GetFullPath(root).TrimEnd('\\')+"\\";
            foreach(var p in Process.GetProcesses()) using(p) {
                // Check running launcher processes
                if (!new[]{"Launcher","Launcher.zml-original","Games","QtWebEngineProcess"}.Contains(p.ProcessName, StringComparer.OrdinalIgnoreCase)) continue;
                try {
                    var path=ProcessImagePath(p.Id);
                    // Skip helper subprocesses
                    if(path.StartsWith(prefix+"games\\",StringComparison.OrdinalIgnoreCase)) continue;
                    if(path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)) throw new IOException("请先退出鹰角启动器（PID " + p.Id + "），安装器不会自动关闭进程。");
                } catch(System.ComponentModel.Win32Exception) { throw new IOException("无法确认启动器已退出，请退出启动器后重试。"); }
            }
        }
    }
}
