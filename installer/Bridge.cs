using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace ZmlSetup {
    public sealed class BridgeServer : IDisposable {
        readonly InstallState state; readonly Catalog catalog; readonly NativeLaunch native;
        readonly TcpListener listener; readonly SemaphoreSlim slots=new SemaphoreSlim(8);
        volatile bool stopped;
        public BridgeServer(InstallState s) {
            state=s; catalog=new Catalog(s); native=new NativeLaunch(s);
            listener=new TcpListener(IPAddress.Loopback,s.Port);
            listener.Server.ExclusiveAddressUse=true; listener.Start(16);
            Task.Run((Action)Accept);
        }
        void Accept() {
            while(!stopped) try {
                var client=listener.AcceptTcpClient();
                if(!slots.Wait(0)) { client.Close(); continue; }
                Task.Run(()=> { try { Handle(client); } finally { client.Close(); slots.Release(); } });
            } catch(SocketException) { if(!stopped) Thread.Sleep(100); } catch(ObjectDisposedException) { if(!stopped) throw; }
        }
        // Minimal HTTP/1.1 bridge server implementation
        void Handle(TcpClient client) {
            client.ReceiveTimeout=4000; client.SendTimeout=4000;
            using(var stream=client.GetStream()) try {
                var bytes=new List<byte>(); int b; var deadline=Stopwatch.StartNew();
                while((b=stream.ReadByte())>=0) {
                    if(deadline.ElapsedMilliseconds>5000) throw new IOException("header timeout");
                    bytes.Add((byte)b); int n=bytes.Count;
                    if(n>8192) throw new IOException("header too large");
                    if(n>=4 && bytes[n-4]==13 && bytes[n-3]==10 && bytes[n-2]==13 && bytes[n-1]==10) break;
                }
                var lines=Encoding.ASCII.GetString(bytes.ToArray()).Split(new[]{"\r\n"},StringSplitOptions.None);
                var request=lines[0].Split(' ');
                if(request.Length!=3 || request[2]!="HTTP/1.1") throw new IOException("invalid HTTP");
                var headers=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                foreach(var line in lines.Skip(1).Where(x=>x.Length>0)) {
                    int at=line.IndexOf(':'); if(at<=0) throw new IOException("invalid header");
                    headers.Add(line.Substring(0,at),line.Substring(at+1).Trim());
                }
                Func<string,string> h=k=>headers.ContainsKey(k)?headers[k]:"";
                if(h("Host")!="127.0.0.1:"+state.Port || h("Transfer-Encoding")!="" || (h("Origin")!="" && h("Origin")!="null")) { Reply(stream,403,new {error="来源被拒绝"},false); return; }
                if(request[0]=="OPTIONS") {
                    if(h("Origin")!="null") { Reply(stream,403,new {error="来源被拒绝"},false); return; }
                    Reply(stream,200,new {ok=true},true); return;
                }
                if(h("X-ZML-Token")!=state.Token) { Reply(stream,403,new {error="授权被拒绝"},false); return; }
                int length=0;
                if(h("Content-Length")!="" && (!Int32.TryParse(h("Content-Length"),out length) || length<0 || length>8192)) throw new IOException("invalid length");
                var body=new byte[length]; int read=0;
                while(read<length) { if(deadline.ElapsedMilliseconds>10000) throw new IOException("body timeout"); int n=stream.Read(body,read,length-read); if(n==0) throw new IOException("short body"); read+=n; }
                object result;
                if(request[0]=="GET" && request[1]=="/health") result=new {ok=true,schema=1,nativeLaunch=true};
                else if(request[0]=="GET" && request[1]=="/mods") result=catalog.Listing();
                else if(request[0]=="GET" && request[1]=="/index") result=catalog.GetIndex();
                else if(request[0]=="GET" && request[1]=="/check-update") result=catalog.CheckUpdate();
                else if(request[0]=="POST" && request[1]=="/install-remote") {
                    var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(Util.Utf8.GetString(body));
                    if(data==null || !data.ContainsKey("id") || !(data["id"] is string)) throw new IOException("缺少模组 ID");
                    string id=(string)data["id"];
                    string assetUrl=data.ContainsKey("asset_url") && data["asset_url"] is string ? (string)data["asset_url"] : null;
                    string sha256=data.ContainsKey("sha256") && data["sha256"] is string ? (string)data["sha256"] : null;
                    result=catalog.InstallRemote(id,assetUrl,sha256);
                }
                else if(request[0]=="POST" && request[1]=="/open-folder") { catalog.OpenFolder(); result=new {ok=true}; }
                else if(request[0]=="POST" && request[1]=="/options-theme") {
                    var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(Util.Utf8.GetString(body));
                    if(data==null || data.Count!=1 || !data.ContainsKey("endfield") || !(data["endfield"] is bool))throw new IOException("主题参数无效");
                    result=native.Options((bool)data["endfield"]);
                }
                else if(request[0]=="POST" && request[1]=="/prepare-launch") {
                    var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(Util.Utf8.GetString(body));
                    if(data==null || data.Count!=0) throw new IOException("启动参数无效");
                    result=native.Prepare();
                }
                else if(request[0]=="POST" && (request[1]=="/launch-status" || request[1]=="/cancel-launch")) {
                    var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(Util.Utf8.GetString(body));
                    if(data==null || data.Count!=1 || !data.ContainsKey("ticket") || !(data["ticket"] is string)) throw new IOException("启动请求无效");
                    result=native.Status((string)data["ticket"],request[1]=="/cancel-launch");
                }
                else if(request[0]=="POST" && request[1]=="/toggle") {
                    var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(Util.Utf8.GetString(body));
                    if(data==null || data.Count!=4 || !(data["id"] is string) || !(data["enabled"] is bool) || !(data["revision"] is string) || !(data["apply"] is bool)) throw new IOException("切换参数无效");
                    result=catalog.Toggle((string)data["id"],(bool)data["enabled"],(string)data["revision"],(bool)data["apply"]);
                } else { Reply(stream,404,new {error="接口不存在"},true); return; }
                Reply(stream,200,result,true);
            } catch(Exception e) { try { Reply(stream,400,new {error=e.Message},true); } catch(IOException) {} }
        }
        static void Reply(Stream stream, int code, object result, bool cors) {
            var body=Util.Utf8.GetBytes(Util.Json(result));
            var header=Encoding.ASCII.GetBytes("HTTP/1.1 "+code+" "+(code==200?"OK":"Error")+"\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: "+body.Length+"\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n"+(cors?"Access-Control-Allow-Origin: null\r\nAccess-Control-Allow-Methods: GET, POST, OPTIONS\r\nAccess-Control-Allow-Headers: X-ZML-Token, Content-Type\r\nVary: Origin\r\n":"")+"\r\n");
            stream.Write(header,0,header.Length); stream.Write(body,0,body.Length);
        }
        public void Dispose() { stopped=true; listener.Stop(); }
    }
    public static class BridgeProgram {
        static bool LauncherAlive(string root) {
            foreach(var p in Process.GetProcesses()) using(p) {
                if(p.ProcessName!="Games" && p.ProcessName!="Launcher.zml-original") continue;
                try { if(Util.ProcessImagePath(p.Id).StartsWith(root+"\\",StringComparison.OrdinalIgnoreCase)) return true; } catch(System.ComponentModel.Win32Exception) {}
            }
            return false;
        }
        [STAThread] public static int Main(string[] args) {
            try {
                var root=AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
                var state=Util.State(root);
                if(state.Status!="installed") throw new IOException("集成未完成，请运行安装器修复。");
                var original=Util.Under(root,"Launcher.zml-original.exe");
                if(Util.HashFile(original)!=state.Files.First(f=>f.Path=="Launcher.zml-original.exe").After) throw new IOException("原启动器备份校验失败。");
                bool created;
                using(var mutex=new Mutex(true,"Local\\ZMLLauncher-"+Util.Hash(Util.Utf8.GetBytes(root.ToLowerInvariant())).Substring(0,24),out created)) {
                    if(!created) { StartOriginal(original,root,args); return 0; }
                    try {
                        using(var server=new BridgeServer(state)) {
                            StartOriginal(original,root,args);
                            // Keep bridge alive for launcher process
                            Thread.Sleep(30000);
                            int absent=0;
                            while(absent<3) { if(LauncherAlive(root)) absent=0; else absent++; Thread.Sleep(10000); }
                        }
                    } catch(SocketException) {
                        StartOriginal(original,root,args);
                        MessageBox.Show("模组面板端口被占用。原启动器仍可使用；请退出启动器后运行安装器重新安装。", Util.RandomFullName(), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return 1;
                    }
                }
                return 0;
            } catch(Exception e) { MessageBox.Show(e.Message, Util.RandomFullName(), MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
        }
        static void StartOriginal(string exe,string root,string[] args) { Process.Start(new ProcessStartInfo(exe,String.Join(" ",args.Select(Util.Quote))) {WorkingDirectory=root,UseShellExecute=false}); }
    }
}
