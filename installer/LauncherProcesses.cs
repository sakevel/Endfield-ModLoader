using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace ZmlSetup {
    // Process termination helper
    internal sealed class LauncherProcesses : IDisposable {
        [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool QueryFullProcessImageName(IntPtr process,uint flags,StringBuilder path,ref uint size);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll",SetLastError=true)] static extern bool TerminateProcess(IntPtr process,uint code);
        [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr process,uint milliseconds);
        internal sealed class Target {
            internal readonly int Pid;
            internal readonly string Path;
            internal readonly IntPtr Handle;
            internal Target(int pid,string path,IntPtr handle) {Pid=pid;Path=path;Handle=handle;}
        }
        readonly List<Target> targets=new List<Target>();
        bool disposed;
        internal Target[] Targets {get{return targets.ToArray();}}
        static HashSet<string> Allowed(string root) {
            var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase) {Util.Under(root,"Launcher.exe"),Util.Under(root,"Launcher.zml-original.exe")};
            foreach(var dir in Directory.GetDirectories(root)) {
                var version=System.IO.Path.GetFileName(dir);
                if(!Regex.IsMatch(version,@"^\d+\.\d+\.\d+(\.\d+)?$"))continue;
                foreach(var name in new[]{"Launcher.exe","Games.exe","QtWebEngineProcess.exe"}) paths.Add(Util.Under(root,version+"\\"+name));
            }
            return paths;
        }
        internal static LauncherProcesses Capture(string root) {
            root=SetupDiscovery.ValidLauncher(root);
            if(root==null)throw new IOException("请选择有效的鹰角启动器目录。");
            var allowed=Allowed(root);var result=new LauncherProcesses();
            int self;using(var current=Process.GetCurrentProcess())self=current.Id;
            try {
                foreach(var process in Process.GetProcesses()) using(process) {
                    if(process.Id==self || !new[]{"Launcher","Launcher.zml-original","Games","QtWebEngineProcess"}.Contains(process.ProcessName,StringComparer.OrdinalIgnoreCase))continue;
                    string path;
                    try {path=Util.ProcessImagePath(process.Id);}
                    catch(System.ComponentModel.Win32Exception) {if(process.HasExited)continue;throw new IOException("无法核验 PID "+process.Id+" 的路径，请手动退出启动器后重试。未结束任何进程。");}
                    if(!allowed.Contains(path))continue;
                    // Retain open process handle
                    var handle=OpenProcess(0x101001,false,process.Id); // synchronize/query-limited/terminate
                    if(handle==IntPtr.Zero) {if(process.HasExited)continue;throw new IOException("无法取得 PID "+process.Id+" 的结束权限，请手动退出后重试。");}
                    try {
                        if(WaitForSingleObject(handle,0)==0)continue;
                        var actual=new StringBuilder(32768);uint length=32768;
                        if(!QueryFullProcessImageName(handle,0,actual,ref length) || !String.Equals(path,actual.ToString(),StringComparison.OrdinalIgnoreCase) || !allowed.Contains(actual.ToString()))
                            throw new IOException("进程路径已改变，未继续操作。请重试。");
                        Util.NoLinks(actual.ToString());
                        result.targets.Add(new Target(process.Id,actual.ToString(),handle));handle=IntPtr.Zero;
                    } finally {if(handle!=IntPtr.Zero)CloseHandle(handle);}
                }
                return result;
            } catch {result.Dispose();throw;}
        }
        // Terminate confirmed processes
        internal void StopConfirmed() {
            if(disposed)throw new ObjectDisposedException("LauncherProcesses");
            foreach(var target in targets) {
                var state=WaitForSingleObject(target.Handle,0);
                if(state==0)continue;
                if(state!=258)throw new IOException("无法确认 PID "+target.Pid+" 的状态，操作已停止。");
                if(!TerminateProcess(target.Handle,0) && WaitForSingleObject(target.Handle,0)!=0)
                    throw new IOException("无法结束 PID "+target.Pid+"，请手动退出后重试。");
                if(WaitForSingleObject(target.Handle,5000)!=0)throw new IOException("PID "+target.Pid+" 未退出，安装已停止。");
            }
        }
        public void Dispose() {
            if(disposed)return;
            disposed=true;foreach(var target in targets)CloseHandle(target.Handle);
        }
    }
}
