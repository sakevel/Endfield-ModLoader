using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace ZmlSetup {
    // Load CreateProcessW launch adapter
    public sealed class NativeLaunch {
        readonly InstallState state; readonly object gate=new object();
        int pid; string ticket; bool pending;
        public NativeLaunch(InstallState value) {state=value;}
        [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr VirtualAllocEx(IntPtr p,IntPtr a,UIntPtr size,uint type,uint protection);
        [DllImport("kernel32.dll")] static extern bool VirtualFreeEx(IntPtr p,IntPtr a,UIntPtr size,uint type);
        [DllImport("kernel32.dll",SetLastError=true)] static extern bool WriteProcessMemory(IntPtr p,IntPtr a,byte[] b,UIntPtr size,out UIntPtr written);
        [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr CreateRemoteThread(IntPtr p,IntPtr sa,UIntPtr stack,IntPtr start,IntPtr argument,uint flags,IntPtr id);
        [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr h,uint ms);
        [DllImport("kernel32.dll")] static extern bool GetExitCodeThread(IntPtr h,out uint code);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32.dll",CharSet=CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr m,string name);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr LoadLibraryEx(string path,IntPtr file,uint flags);
        [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr m);
        static IOException Error(string what) {return new IOException(what+" (Win32 "+Marshal.GetLastWin32Error()+")");}
        static IntPtr Module(Process process,string path) {
            try {
                process.Refresh();foreach(ProcessModule m in process.Modules)
                    if(String.Equals(m.FileName,path,StringComparison.OrdinalIgnoreCase)) return m.BaseAddress;
                return IntPtr.Zero;
            } catch(System.ComponentModel.Win32Exception e) {
                throw new IOException("无法读取原生启动器模块（Win32 "+e.NativeErrorCode+"），请以管理员权限运行 Launcher.exe。",e);
            }
        }
        static uint Call(Process process,IntPtr function,byte[] data,IntPtr raw=default(IntPtr)) {
            var handle=OpenProcess(0x043a,false,process.Id);if(handle==IntPtr.Zero) throw Error("无法接入指定启动器");
            IntPtr memory=IntPtr.Zero,thread=IntPtr.Zero;bool completed=true;
            try {
                if(data!=null) {
                    memory=VirtualAllocEx(handle,IntPtr.Zero,(UIntPtr)data.Length,0x3000,4);if(memory==IntPtr.Zero) throw Error("适配器参数分配失败");
                    UIntPtr written;if(!WriteProcessMemory(handle,memory,data,(UIntPtr)data.Length,out written) || written.ToUInt64()!=(ulong)data.Length) throw Error("适配器参数写入失败");
                }
                thread=CreateRemoteThread(handle,IntPtr.Zero,UIntPtr.Zero,function,data==null?raw:memory,0,IntPtr.Zero);if(thread==IntPtr.Zero) throw Error("普通 LoadLibrary 接入失败");
                completed=WaitForSingleObject(thread,15000)==0;
                if(!completed) throw new IOException("原生启动适配器超时。");
                uint code;if(!GetExitCodeThread(thread,out code)) throw Error("适配器返回值读取失败");return code;
            } finally {
                if(thread!=IntPtr.Zero) CloseHandle(thread);
                // Keep parameter buffer alive
                if(memory!=IntPtr.Zero && completed) VirtualFreeEx(handle,memory,UIntPtr.Zero,0x8000);
                CloseHandle(handle);
            }
        }
        static uint Export(Process process,string dll,string name,byte[] argument) {
            var remote=Module(process,dll);if(remote==IntPtr.Zero) throw new IOException("启动器适配器未加载");
            var local=LoadLibraryEx(dll,IntPtr.Zero,1);if(local==IntPtr.Zero) throw Error("适配器导出解析失败");
            try {var f=GetProcAddress(local,name);if(f==IntPtr.Zero) throw new IOException("适配器版本不兼容");return Call(process,new IntPtr(remote.ToInt64()+f.ToInt64()-local.ToInt64()),argument);}
            finally {FreeLibrary(local);}
        }
        string Dll {get{return Util.Under(state.Root,"ZML\\ZMLNativeLaunch.dll");}}
        public object Options(bool enabled) {lock(gate) {
            var path=Util.Under(state.Root,"ZML\\ZMLLauncherOptions.dll");
            var owned=state.Files.SingleOrDefault(f=>f.Path=="ZML\\ZMLLauncherOptions.dll");
            if(owned==null || Util.HashFile(path)!=owned.After)throw new IOException("原生选项扩展需要安装/修复。");
            using(var process=Launcher()) {
                if(Module(process,path)==IntPtr.Zero) {
                    var loader=GetProcAddress(GetModuleHandle("kernel32.dll"),"LoadLibraryW");
                    ProcessModule owner=null;using(var self=Process.GetCurrentProcess()) foreach(ProcessModule m in self.Modules)
                        if(loader.ToInt64()>=m.BaseAddress.ToInt64() && loader.ToInt64()<m.BaseAddress.ToInt64()+m.ModuleMemorySize){owner=m;break;}
                    if(owner==null)throw new IOException("系统加载模块不可用");
                    var remote=Module(process,owner.FileName);if(remote==IntPtr.Zero)throw new IOException("目标加载模块不可用");
                    Call(process,new IntPtr(remote.ToInt64()+loader.ToInt64()-owner.BaseAddress.ToInt64()),System.Text.Encoding.Unicode.GetBytes(path+"\0"));
                }
                var baseAddress=Module(process,path);if(baseAddress==IntPtr.Zero)throw new IOException("Qt 原生选项接口不可用");
                var local=LoadLibraryEx(path,IntPtr.Zero,1);if(local==IntPtr.Zero)throw Error("选项导出解析失败");
                try {
                    var f=GetProcAddress(local,"ZML_SetOptionsEnabled");if(f==IntPtr.Zero)throw new IOException("选项扩展版本不兼容");
                    var result=Call(process,new IntPtr(baseAddress.ToInt64()+f.ToInt64()-local.ToInt64()),null,new IntPtr(enabled?1:0));
                    if(result!=0)throw new IOException("原生选项接入失败（Win32 "+result+"）");
                }finally{FreeLibrary(local);}
            }
            return new {ok=true};
        }}
        Process Launcher() {
            var paths=state.Files.Where(f=>f.Path.EndsWith("\\res\\web\\index.html",StringComparison.OrdinalIgnoreCase))
                .Select(f=>Util.Under(state.Root,f.Path.Split('\\')[0]+"\\Games.exe")).ToArray();
            Process chosen=null;bool unreadable=false;
            foreach(var p in Process.GetProcessesByName("Games")) {
                try {
                    if(paths.Contains(Util.ProcessImagePath(p.Id),StringComparer.OrdinalIgnoreCase)) {
                        if(chosen!=null) {chosen.Dispose();p.Dispose();throw new IOException("存在多个指定启动器实例，请保留一个。");}
                        chosen=p;continue;
                    }
                } catch(System.ComponentModel.Win32Exception) {unreadable=true;} catch(InvalidOperationException) {}
                p.Dispose();
            }
            if(chosen==null) throw new IOException(unreadable?"无法确认原生启动器路径（权限不足或进程已退出），请通过根目录 Launcher.exe 以管理员权限重新打开。":"未找到登记版本的原生启动器，请从根目录 Launcher.exe 打开，不要直接打开版本目录 Games.exe。");return chosen;
        }
        public object Prepare() {lock(gate) {
            if(pending) {
                try {using(var previous=Process.GetProcessById(pid)) {var old=Export(previous,Dll,"ZML_NativeLaunchStatus",null);pending=old==1 || old==3;}}
                catch(ArgumentException) {pending=false;}
                if(pending) throw new IOException("已有启动请求正在处理中，请等待。");
            }
            var owned=state.Files.FirstOrDefault(f=>f.Path=="ZML\\ZMLNativeLaunch.dll");
            if(owned==null || Util.HashFile(Dll)!=owned.After) throw new IOException("原生启动适配器需要安装/修复");
            var runtimeOwned=state.Files.FirstOrDefault(f=>f.Path=="ZML\\ZMLRuntime.dll");
            if(runtimeOwned==null || Util.HashFile(Util.Under(state.Root,runtimeOwned.Path))!=runtimeOwned.After) throw new IOException("运行时文件已被更改，请先安装/修复");
            Util.Validate(state.Root,state.Game);
            foreach(var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(state.Game))) using(p) {
                throw new IOException("客户端已在运行，请先退出。");
            }
            using(var process=Launcher()) {
                if(Module(process,Dll)==IntPtr.Zero) {
                    var loader=GetProcAddress(GetModuleHandle("kernel32.dll"),"LoadLibraryW");
                    ProcessModule owner=null;using(var self=Process.GetCurrentProcess()) foreach(ProcessModule m in self.Modules)
                        if(loader.ToInt64()>=m.BaseAddress.ToInt64() && loader.ToInt64()<m.BaseAddress.ToInt64()+m.ModuleMemorySize) {owner=m;break;}
                    if(owner==null) throw new IOException("系统 LoadLibrary 模块未找到");
                    var baseAddress=Module(process,owner.FileName);if(baseAddress==IntPtr.Zero) throw new IOException("目标系统加载模块未找到");
                    Call(process,new IntPtr(baseAddress.ToInt64()+loader.ToInt64()-owner.BaseAddress.ToInt64()),System.Text.Encoding.Unicode.GetBytes(Dll+"\0"));
                    if(Module(process,Dll)==IntPtr.Zero) throw new IOException("适配器普通加载失败");
                }
                var request=new byte[8+32768*4];Array.Copy(BitConverter.GetBytes(1u),0,request,0,4);Array.Copy(BitConverter.GetBytes(60u),0,request,4,4);
                var game=System.Text.Encoding.Unicode.GetBytes(state.Game+"\0");var dll=System.Text.Encoding.Unicode.GetBytes(Util.Under(state.Root,"ZML\\ZMLRuntime.dll")+"\0");
                if(game.Length>65536 || dll.Length>65536) throw new IOException("路径过长");
                Array.Copy(game,0,request,8,game.Length);Array.Copy(dll,0,request,8+65536,dll.Length);
                uint code=Export(process,Dll,"ZML_ArmNativeLaunch",request);
                if(code!=0) throw new IOException("原生启动适配器不可用 (Win32 "+code+")");
                pid=process.Id;ticket=Guid.NewGuid().ToString("N");pending=true;return new {ok=true,ticket};
            }
        }}
        public object Status(string id,bool cancel=false) {lock(gate) {
            if(ticket==null || id!=ticket) throw new IOException("启动请求无效");
            using(var process=Process.GetProcessById(pid)) {
                uint code=Export(process,Dll,cancel?"ZML_CancelNativeLaunch":"ZML_NativeLaunchStatus",null);
                if(code!=1 && code!=3) pending=false;
                if(code>=1000) throw new IOException("原生创建客户端或模组接入失败 (Win32 "+(code-1000)+")；原启动器负责显示启动失败。");
                if(code==4) throw new IOException("原生启动超时，未观察到指定客户端创建；没有模拟启动成功。");
                return new {ok=true,state=code==2?"loaded":code==3?"injecting":code==1?"armed":"cancelled"};
            }
        }}
    }
}
