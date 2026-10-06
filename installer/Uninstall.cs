using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZmlSetup {
    public sealed class UninstallUpdate {
        public readonly int Percent;
        public readonly string Message;
        public UninstallUpdate(int percent,string message) {Percent=Math.Max(0,Math.Min(100,percent));Message=message??"";}
    }
    public sealed class UninstallProgressForm : Form {
        readonly string root, receipt;
        readonly Label status, result;
        readonly ProgressBar progress;
        readonly Button close;
        bool finished;
        public int ExitCode {get;private set;}
        public UninstallProgressForm(string root,string receipt) {
            this.root=root;this.receipt=receipt;ExitCode=1;
            Text="卸载 "+Util.RandomFullName();ClientSize=new Size(520,224);StartPosition=FormStartPosition.CenterScreen;
            FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;ControlBox=false;
            BackColor=Color.FromArgb(36,39,43);ForeColor=Color.White;Font=new Font("Microsoft YaHei UI",10);
            var title=new Label {Text="正在卸载 "+Util.RandomFullName(),AutoSize=true,Location=new Point(24,22)};Controls.Add(title);
            status=new Label {Text="正在准备…",Location=new Point(24,60),Size=new Size(472,24)};Controls.Add(status);
            progress=new ProgressBar {Name="uninstallProgress",Minimum=0,Maximum=100,Value=0,Location=new Point(24,90),Size=new Size(472,18),Style=ProgressBarStyle.Continuous};Controls.Add(progress);
            result=new Label {Location=new Point(24,122),Size=new Size(472,48),AutoEllipsis=true};Controls.Add(result);
            close=new Button {Text="关闭",Enabled=false,Location=new Point(392,178),Size=new Size(104,30),FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(255,239,0),ForeColor=Color.Black};
            close.FlatAppearance.BorderSize=0;close.Click+=(s,e)=>Close();Controls.Add(close);
            FormClosing+=(s,e)=> {if(!finished && e.CloseReason==CloseReason.UserClosing)e.Cancel=true;};
        }
        public void ApplyProgress(UninstallUpdate update) {
            if(finished)return;
            progress.Value=Math.Max(progress.Value,update.Percent);status.Text=update.Message;
        }
        protected override async void OnShown(EventArgs e) {
            base.OnShown(e);
            // Captured on the WinForms UI thread. Blocking process/file work runs
            // on a worker; progress and completion are marshalled back by WinForms.
            IProgress<UninstallUpdate> updates=new Progress<UninstallUpdate>(ApplyProgress);
            try {
                var archive=await UninstallProgram.ExecuteAsync(root,(value,message)=>updates.Report(new UninstallUpdate(value,message)),receipt);
                ApplyProgress(new UninstallUpdate(100,Util.RandomFullName()+" 已卸载，官方启动器已恢复。"));
                result.Text="模组与备份已保留：\n"+archive;ExitCode=0;
            } catch(Exception error) {status.Text="卸载未完成";result.ForeColor=Color.FromArgb(255,166,125);result.Text=error.Message;}
            finished=true;close.Enabled=true;ControlBox=true;
        }
    }
    public static class UninstallProgram {
        [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool QueryFullProcessImageName(IntPtr process,uint flags,StringBuilder path,ref uint size);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll",SetLastError=true)] static extern bool TerminateProcess(IntPtr process,uint code);
        [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr process,uint milliseconds);
        delegate bool WindowCallback(IntPtr window,IntPtr data);
        [DllImport("user32.dll")] static extern bool EnumWindows(WindowCallback callback,IntPtr data);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window,uint message,IntPtr w,IntPtr l);
        public static HashSet<string> AllowedProcesses(InstallState state) {
            var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase) {Util.Under(state.Root,"Launcher.exe"),Util.Under(state.Root,"Launcher.zml-original.exe")};
            // Verify targets against recorded state
            if(!String.Equals(state.Game,InstallEngine.SuggestedGame(state.Root),StringComparison.OrdinalIgnoreCase)) throw new IOException("自动关闭仅支持本启动器 games\\Endfield Game\\Endfield.exe；其它位置请手动退出并使用安装器卸载。");
            paths.Add(state.Game);
            foreach(var page in state.Files.Where(f=>f.Path.EndsWith("\\res\\web\\index.html",StringComparison.OrdinalIgnoreCase))) {
                var version=page.Path.Split('\\')[0];
                paths.Add(Util.Under(state.Root,version+"\\Games.exe"));
                paths.Add(Util.Under(state.Root,version+"\\QtWebEngineProcess.exe"));
            }
            return paths;
        }
        // Terminate target processes
        public static void CloseRegistered(InstallState state,Action<int,string> progress=null) {
            if(progress!=null)progress(15,"正在确认此安装目录的游戏和启动器进程…");
            var allowed=AllowedProcesses(state);var handles=new Dictionary<int,IntPtr>();
            try {
                foreach(var process in Process.GetProcesses()) using(process) {
                    if(!new[]{"Launcher","Launcher.zml-original","Games","QtWebEngineProcess","Endfield"}.Contains(process.ProcessName,StringComparer.OrdinalIgnoreCase)) continue;
                    string path;try {path=Util.ProcessImagePath(process.Id);}catch(System.ComponentModel.Win32Exception){if(process.HasExited)continue;throw new IOException("无法确认 PID "+process.Id+" 的路径，未继续关闭或卸载。请手动退出后重试。");}
                    if(!allowed.Contains(path)) continue;
                    var handle=OpenProcess(0x101001,false,process.Id); // synchronize/query-limited/terminate
                    if(handle==IntPtr.Zero) throw new IOException("无法关闭进程 PID "+process.Id+"（Win32 "+Marshal.GetLastWin32Error()+"），请手动退出。");
                    var actual=new StringBuilder(32768);uint length=32768;
                    if(!QueryFullProcessImageName(handle,0,actual,ref length) || !allowed.Contains(actual.ToString())) {CloseHandle(handle);throw new IOException("进程已改变，未继续关闭。");}
                    handles.Add(process.Id,handle);
                }
                if(progress!=null)progress(20,"正在关闭游戏和启动器…");
                EnumWindows((w,d)=> {uint pid;GetWindowThreadProcessId(w,out pid);if(handles.ContainsKey((int)pid))PostMessage(w,0x10,IntPtr.Zero,IntPtr.Zero);return true;},IntPtr.Zero);
                var deadline=Stopwatch.StartNew();
                while(deadline.ElapsedMilliseconds<8000 && handles.Values.Any(h=>WaitForSingleObject(h,0)!=0)) {
                    int done=handles.Values.Count(h=>WaitForSingleObject(h,0)==0);
                    if(progress!=null)progress(20+12*done/Math.Max(1,handles.Count),"正在等待进程退出（"+done+"/"+handles.Count+"）…");
                    System.Threading.Thread.Sleep(200);
                }
                foreach(var pair in handles) if(WaitForSingleObject(pair.Value,0)!=0) {
                    if(progress!=null)progress(33,"正在结束已核验的残留进程…");
                    if(!TerminateProcess(pair.Value,0) || WaitForSingleObject(pair.Value,10000)!=0) throw new IOException("指定 PID "+pair.Key+" 未退出，卸载已停止。");
                }
                if(progress!=null)progress(35,"对应游戏和启动器已退出。");
            } finally {foreach(var handle in handles.Values)CloseHandle(handle);}
        }
        public static Task<string> ExecuteAsync(string root,Action<int,string> progress,string expectedReceipt=null) {
            return Task.Run(()=> {
                if(progress!=null)progress(5,"正在核验 "+Util.RandomFullName()+" 安装状态与备份…");
                if(expectedReceipt!=null && Util.HashFile(Util.StatePath(root))!=expectedReceipt)throw new IOException("确认期间安装状态已改变，未继续关闭或卸载。请重试。");
                var state=Util.State(root);InstallEngine.PreflightUninstall(root);
                CloseRegistered(state,progress);
                return InstallEngine.CleanUninstall(root,progress);
            });
        }
        [STAThread] public static int Main(string[] args) {
            Application.EnableVisualStyles();
            try {
                if(args.Length!=2 || args[0]!="--uninstall-running") throw new IOException("卸载请求无效。");
                var root=Path.GetFullPath(args[1]).TrimEnd('\\');var state=Util.State(root);
                bool created;using(var mutex=new System.Threading.Mutex(true,"Local\\ZMLUninstall-"+Util.Hash(Util.Utf8.GetBytes(root.ToLowerInvariant())).Substring(0,24),out created)) {
                if(!created)return 0;
                var owned=state.Files.SingleOrDefault(f=>f.Path=="ZML\\ZMLUninstall.exe");
                if(owned==null || Util.HashFile(Process.GetCurrentProcess().MainModule.FileName)!=owned.After) throw new IOException("卸载程序校验失败。");
                InstallEngine.PreflightUninstall(root);AllowedProcesses(state);
                var receipt=Util.HashFile(Util.StatePath(root));
                if(MessageBox.Show("请先保存游戏进度。\n\n将关闭此安装目录的终末地客户端和启动器，卸载 ZML 并恢复官方文件。\n模组与备份保留在 ZML-uninstalled-* 目录；私人配置保留。\n不会卸载游戏。\n\n现在继续？","卸载 "+Util.RandomFullName(),MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return 0;
                using(var form=new UninstallProgressForm(root,receipt)) {Application.Run(form);return form.ExitCode;}
                }
            } catch(Exception e) {MessageBox.Show(e.Message,"卸载 "+Util.RandomFullName()+" 未完成",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
        }
    }
}
