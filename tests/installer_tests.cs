using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Forms;

namespace ZmlSetup {
    public static class InstallerTests {
        static string area;
        static int passed;
        static System.Diagnostics.Process NativeFixture(string root,string game,string marker) {
            var exe=Util.Under(root,"1.6.0\\Games.exe");
            var source=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","tests","Release","NativeLaunchFixture.exe"));
            File.Copy(source,exe,true);
            var unrelated=Util.Under(root,"unrelated\\ZmlUnrelatedFixture.exe");Directory.CreateDirectory(Path.GetDirectoryName(unrelated));File.Copy(game,unrelated,true);
            return System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe,Util.Quote(game)+" "+Util.Quote(marker)+" "+Util.Quote(unrelated)) {UseShellExecute=false,CreateNoWindow=true,WindowStyle=System.Diagnostics.ProcessWindowStyle.Hidden,RedirectStandardInput=true,RedirectStandardOutput=true});
        }
        static int NativeStart(System.Diagnostics.Process process,string mode) {
            process.StandardInput.WriteLine(mode);process.StandardInput.Flush();
            var task=System.Threading.Tasks.Task.Run(()=>process.StandardOutput.ReadLine());
            if(!task.Wait(45000)) throw new Exception("native fixture start timeout");
            var parts=task.Result.Split(' ');return Int32.Parse(parts[1]);
        }
        static void StopChild(int id) {
            if(id==0) return;
            try {using(var child=System.Diagnostics.Process.GetProcessById(id)){if(!child.WaitForExit(8000)){child.Kill();child.WaitForExit();}}}catch(ArgumentException){}
        }
        static string WriteResult(string root,object value) {var file=Path.Combine(root,"own-result.json");Util.WriteJson(file,value);return file;}
        static void Check(bool ok,string name) { if(!ok)throw new Exception("FAIL "+name); Console.WriteLine("PASS "+name);passed++; }
        static void Fails(Action action,string name) {try{action();}catch(Exception){Check(true,name);return;}throw new Exception("FAIL did not reject "+name);}
        static string Fixture(string fixture) {
            var root=Path.Combine(area,"launcher-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            File.Copy(fixture,Path.Combine(root,"Launcher.exe"));
            Util.Atomic(Path.Combine(root,"1.6.0","res","web","index.html"),Util.Utf8.GetBytes("<!doctype html><html><head><meta charset=\"utf-8\"></head><body><div id=\"root\">fixture</div><script src=\"qrc:///web/js/qwebchannel.js\"></script></body></html>"));
            var game=InstallEngine.SuggestedGame(root);Directory.CreateDirectory(Path.GetDirectoryName(game));File.Copy(fixture,game);
            return root;
        }
        static string Revision(Catalog c) { var json=Util.Json(c.Listing());return new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string,object>>(json)["revision"].ToString(); }
        static void Mod(string root,string id,bool enabled,string dep) {
            var dir=Util.Under(root,"ZML\\mods\\"+id);Directory.CreateDirectory(dir);
            Util.Atomic(Path.Combine(dir,"mod.ini"),Util.Utf8.GetBytes("; preserve comments\r\n[mod]\r\nid="+id+"\r\nname=测试 "+id+"\r\napi=1\r\nversion=1.2.3\r\ndescription=简介 <script>not executable</script>\r\nauthors=fixture\r\ntags=工具,测试\r\nlibrary=Test.dll\r\nenabled="+(enabled?"true":"false")+"\r\n"+(dep==null?"":"depends="+dep+"\r\n")));
            Util.Atomic(Path.Combine(dir,"Test.dll"),new byte[]{1,2,3});
        }
        static string Request(InstallState s,string path,string token,string origin=null,string method="GET",string body=null) {
            var req=(HttpWebRequest)WebRequest.Create("http://127.0.0.1:"+s.Port+path);req.Proxy=null;req.Timeout=70000;req.Method=method;
            if(token!=null)req.Headers.Add("X-ZML-Token",token);if(origin!=null)req.Headers.Add("Origin",origin);
            if(body!=null){var b=Util.Utf8.GetBytes(body);req.ContentType="application/json";req.ContentLength=b.Length;using(var w=req.GetRequestStream())w.Write(b,0,b.Length);}
            using(var response=req.GetResponse())using(var stream=response.GetResponseStream())using(var reader=new StreamReader(stream))return reader.ReadToEnd();
        }
        static void HttpFails(InstallState s,string path,string token,string origin,int code) {
            try {Request(s,path,token,origin);}catch(WebException e){using(var r=(HttpWebResponse)e.Response)Check((int)r.StatusCode==code,"HTTP "+code+" "+path);return;}throw new Exception("HTTP should fail");
        }
        [STAThread] public static int Main(string[] args) {
            try {
                area=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"fixtures-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(area);
                bool serve=args.Length==3 && args[0]=="--serve"; var zip=args[serve?1:0];var fixture=args[serve?2:1];
                Dictionary<string,byte[]> payload;using(var stream=File.OpenRead(zip))payload=InstallEngine.Payload(stream);
                if(serve) {
                    var root=Fixture(fixture);InstallEngine.Install(root,InstallEngine.SuggestedGame(root),payload,false);
                    Mod(root,"core",true,null);Mod(root,"sample",true,"core");
                    foreach(var id in new[]{"core","sample"}) Util.Atomic(Util.Under(root,"ZML\\mods\\"+id+"\\Test.dll"),payload["ZMLRuntime.dll"]);
                    var state=Util.State(root);
                    var own=Path.Combine(root,"games","ZmlInstallerFixture.exe");File.Copy(fixture,own);state.Game=own;
                    // Reuse the real wrapper's server and installed JS; no launcher/game accounts involved.
                    Util.WriteJson(Path.Combine(area,"web-fixture.json"),new {root,port=state.Port,token=state.Token,url=new Uri(Path.Combine(root,"1.6.0","res","web","index.html")).AbsoluteUri,pid=System.Diagnostics.Process.GetCurrentProcess().Id});
                    using(var server=new BridgeServer(state)) { Console.WriteLine("FIXTURE "+Path.Combine(area,"web-fixture.json"));Console.Out.Flush();Thread.Sleep(600000); }
                    return 0;
                }
                Check(System.Text.Encoding.UTF8.GetString(payload["ZMLLauncherBridge.exe"]).Contains("requestedExecutionLevel level=\"requireAdministrator\""),"bridge PE requests same administrator level as native launcher");
                Fails(()=>Util.ProcessImagePath(Int32.MaxValue),"invalid process discovery fails rather than guessing path");
                var root1=Fixture(fixture);var initial=Util.HashFile(Path.Combine(root1,"Launcher.exe"));var html=Util.HashFile(Path.Combine(root1,"1.6.0","res","web","index.html"));
                InstallEngine.Install(root1,InstallEngine.SuggestedGame(root1),payload,false);
                var s1=Util.State(root1);
                Check(s1.Status=="installed" && Util.HashFile(Path.Combine(root1,"Launcher.zml-original.exe"))==initial,"actual framework install and byte-exact original backup");
                Check(File.ReadAllText(Path.Combine(root1,"1.6.0","res","web","index.html")).Contains(InstallEngine.Marker),"native web page extension");
                Check(!Directory.GetFiles(Path.Combine(root1,"ZML","mods"),"mod.ini",SearchOption.AllDirectories).Any(),"framework-only installer does not silently bundle Mods");
                InstallEngine.Install(root1,InstallEngine.SuggestedGame(root1),payload,false);
                Check(Util.State(root1).Token==s1.Token,"idempotent install preserves token and files");
                Mod(root1,"core",false,null);Mod(root1,"sample",false,"core");
                var catalog=new Catalog(s1);var before=Util.HashFile(Path.Combine(root1,"ZML","mods","core","mod.ini"));
                catalog.Toggle("sample",true,Revision(catalog),false,false);
                Check(Util.HashFile(Path.Combine(root1,"ZML","mods","core","mod.ini"))==before,"preview does not write");
                catalog.Toggle("sample",true,Revision(catalog),true,false);
                Check(catalog.Scan().All(m=>m.enabled) && File.ReadAllText(Path.Combine(root1,"ZML","mods","core","mod.ini")).StartsWith("; preserve comments"),"enable dependency closure preserves manifest formatting");
                Fails(()=>catalog.Toggle("sample",false,"stale",true,false),"stale catalog rejected");
                catalog.Toggle("core",false,Revision(catalog),true,false);
                Check(catalog.Scan().All(m=>!m.enabled),"disable cascades consumers");
                var allBefore=catalog.Scan().ToDictionary(m=>m.id,m=>Util.Hash(m.Original));
                Fails(()=>catalog.Toggle("sample",true,Revision(catalog),true,false,1),"toggle partial failure");
                Check(catalog.Scan().All(m=>Util.Hash(m.Original)==allBefore[m.id]),"multi-manifest rollback byte exact");
                // A pending journal from a crashed write is recovered when a new service starts.
                var row=catalog.Scan().First();var after=Util.Utf8.GetBytes(Util.Utf8.GetString(row.Original).Replace("enabled=false","enabled=true"));
                Util.WriteJson(Path.Combine(root1,"ZML","toggle-journal.json"),new[]{new ToggleWrite{path=row.folder+"\\mod.ini",before=Convert.ToBase64String(row.Original),after=Convert.ToBase64String(after)}});
                Util.Atomic(row.Manifest,after);catalog=new Catalog(s1);
                Check(Util.HashFile(row.Manifest)==Util.Hash(row.Original),"crash journal recovery");
                // Loader's actual DLL/PE/dependency validation rejects fake DLLs and rolls back the write.
                Fails(()=>catalog.Toggle("sample",true,Revision(catalog),true,true),"native dry-run rejection");
                Check(catalog.Scan().All(m=>!m.enabled),"native rejection rolls back all switches");
                Mod(root1,"cycle-a",true,"cycle-b");Mod(root1,"cycle-b",true,"cycle-a");
                catalog.Toggle("cycle-a",false,Revision(catalog),true,false);
                Check(catalog.Scan().Where(m=>m.id.StartsWith("cycle-")).All(m=>!m.enabled),"cyclic enabled mods can be safely disabled");
                Fails(()=>catalog.Toggle("cycle-a",true,Revision(catalog),true,false),"cyclic dependencies cannot be enabled");
                using(var server=new BridgeServer(s1)) {
                    Check(Request(s1,"/mods",s1.Token).Contains("测试 core"),"loopback catalog response");
                    Check(Request(s1,"/health",s1.Token,"null").Contains("true"),"file-origin CORS and authenticated health");
                    Fails(()=>Request(s1,"/prepare-launch",s1.Token,null,"POST","{\"renderMode\":\"bad\"}"),"arbitrary native launch parameters rejected");
                    Fails(()=>Request(s1,"/launch-status",s1.Token,null,"POST","{\"ticket\":\"unknown\"}"),"unknown native launch ticket rejected");
                    HttpFails(s1,"/mods",null,null,403);HttpFails(s1,"/mods",s1.Token,"https://evil.invalid",403);HttpFails(s1,"/unknown",s1.Token,null,404);
                }
                var nativeChild=Util.Under(root1,"games\\ZmlNativeFixtureChild.exe");File.Copy(fixture,nativeChild);s1.Game=nativeChild;
                var marker=Path.Combine(root1,"native-marker.txt");
                using(var native=NativeFixture(root1,nativeChild,marker)) try {
                    Check(Util.ProcessImagePath(native.Id)==Util.Under(root1,"1.6.0\\Games.exe"),"limited-information Win32 query resolves actual native fixture path");
                    var adapter=new NativeLaunch(s1);
                    var originalGame=s1.Game;s1.Game=Path.Combine(root1,"missing-game.exe");
                    Fails(()=>adapter.Prepare(),"native preflight failure never invokes native start");s1.Game=originalGame;
                    Check(!File.Exists(marker),"no synthetic native post-start action on preflight failure");
                    var prepared=Util.ReadJson<Dictionary<string,object>>(WriteResult(root1,adapter.Prepare()));var id=(string)prepared["ticket"];
                    Fails(()=>adapter.Prepare(),"concurrent native arm rejected");
                    int unrelatedPid=NativeStart(native,"u");
                    Check(Util.Json(adapter.Status(id)).Contains("armed"),"unrelated executable passes through without consuming arm");StopChild(unrelatedPid);
                    int child=NativeStart(native,"v");
                    Check(child!=0 && Util.Json(adapter.Status(id)).Contains("loaded"),"native-owned creation loads runtime via one-shot adapter");
                    Check(File.ReadAllText(marker).StartsWith("native_started "),"native handler performs own post-start processing after injection");
                    Thread.Sleep(200);Check(File.ReadAllText(marker+".child")=="runtime_loaded","actual child confirms runtime loaded");StopChild(child);
                    prepared=Util.ReadJson<Dictionary<string,object>>(WriteResult(root1,adapter.Prepare()));id=(string)prepared["ticket"];
                    Check(Util.Json(adapter.Status(id,true)).Contains("cancelled"),"cancellation disarms one-shot adapter");
                    int ordinary=NativeStart(native,"v");
                    Check(ordinary!=0 && File.ReadAllText(marker).StartsWith("native_started "),"disarmed original native launch remains functional");
                    using(var ordinaryChild=System.Diagnostics.Process.GetProcessById(ordinary)) Check(!ordinaryChild.Modules.Cast<System.Diagnostics.ProcessModule>().Any(m=>m.ModuleName=="ZMLRuntime.dll"),"disarmed adapter does not inject runtime");
                    StopChild(ordinary);
                    // Rejected DLL validates failure propagation into the native original caller.
                    var good=File.ReadAllBytes(Util.Under(root1,"ZML\\ZMLRuntime.dll"));
                    var rejected=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","tests","rejected","Release","ZMLRuntime.dll"));
                    File.Copy(rejected,Util.Under(root1,"ZML\\ZMLRuntime.dll"),true);
                    Fails(()=>adapter.Prepare(),"runtime ownership mismatch rejected before native launch");
                    var runtimeOwned=s1.Files.First(f=>f.Path=="ZML\\ZMLRuntime.dll");var runtimeHash=runtimeOwned.After;
                    runtimeOwned.After=Util.HashFile(Util.Under(root1,runtimeOwned.Path));
                    prepared=Util.ReadJson<Dictionary<string,object>>(WriteResult(root1,adapter.Prepare()));id=(string)prepared["ticket"];
                    Check(NativeStart(native,"v")==0 && File.ReadAllText(marker).StartsWith("native_failed "),"runtime rejection returns failed CreateProcess to original native handler");
                    Fails(()=>adapter.Status(id),"injection error reported instead of fake native success");
                    Check(System.Diagnostics.Process.GetProcessesByName("ZmlNativeFixtureChild").Length==0,"injection failure rolls back only the native-owned matching child");
                    Util.Atomic(Util.Under(root1,"ZML\\ZMLRuntime.dll"),good);runtimeOwned.After=runtimeHash;
                } finally {if(!native.HasExited){native.Kill();native.WaitForExit();}}
                s1.Game=InstallEngine.SuggestedGame(root1);
                var unrelatedRoot=Fixture(fixture);
                using(var native=NativeFixture(unrelatedRoot,InstallEngine.SuggestedGame(unrelatedRoot),Path.Combine(unrelatedRoot,"marker.txt"))) try {
                    Fails(()=>new NativeLaunch(s1).Prepare(),"other installation native process is never injected");
                } finally {if(!native.HasExited){native.Kill();native.WaitForExit();}}
                var privateConfig=Path.Combine(root1,"ZML","mods","core","private.ini");Util.Atomic(privateConfig,new byte[]{4,5,6});
                InstallEngine.Uninstall(root1);
                Check(Util.HashFile(Path.Combine(root1,"Launcher.exe"))==initial && Util.HashFile(Path.Combine(root1,"1.6.0","res","web","index.html"))==html,"uninstall byte-exact launcher and web restore");
                Check(File.Exists(privateConfig) && File.Exists(row.Manifest),"uninstall preserves all user Mod files");
                var failure=Fixture(fixture);var failureHash=Util.HashFile(Path.Combine(failure,"Launcher.exe"));
                Fails(()=>InstallEngine.Install(failure,InstallEngine.SuggestedGame(failure),payload,false,3),"install partial failure");
                Check(Util.HashFile(Path.Combine(failure,"Launcher.exe"))==failureHash && Util.State(failure).Status=="uninstalled","installer failure rollback");
                var conflict=Fixture(fixture);InstallEngine.Install(conflict,InstallEngine.SuggestedGame(conflict),payload,false);
                File.AppendAllText(Path.Combine(conflict,"1.6.0","res","web","index.html"),"<!-- user edit -->");
                var changed=Util.HashFile(Path.Combine(conflict,"1.6.0","res","web","index.html"));
                Fails(()=>InstallEngine.Uninstall(conflict),"uninstall refuses external changes before touching any file");
                Check(Util.HashFile(Path.Combine(conflict,"1.6.0","res","web","index.html"))==changed,"unrelated web edits preserved");
                var repair=Fixture(fixture);InstallEngine.Install(repair,InstallEngine.SuggestedGame(repair),payload,false);
                var forged=Util.State(repair);forged.Files[0].Path="games\\Endfield Game\\Endfield.exe";
                var validState=File.ReadAllBytes(Util.StatePath(repair));Util.WriteJson(Util.StatePath(repair),forged);
                Fails(()=>InstallEngine.Uninstall(repair),"forged recovery state cannot touch game files");
                Util.Atomic(Util.StatePath(repair),validState);
                var uiPayload=new Dictionary<string,byte[]>(payload,StringComparer.OrdinalIgnoreCase);
                uiPayload["web\\zml-client.css"]=Util.Utf8.GetBytes("/* updated UI */\n");
                uiPayload["ZMLLauncherBridge.exe"]=new byte[]{9,9,9};
                var bridgeBefore=Util.HashFile(Path.Combine(repair,"Launcher.exe"));
                InstallEngine.Install(repair,InstallEngine.SuggestedGame(repair),uiPayload,false);
                Check(File.ReadAllText(Path.Combine(repair,"1.6.0","res","web","zml-client.css")).Contains("updated UI") && Util.HashFile(Path.Combine(repair,"Launcher.exe"))==bridgeBefore,"repair updates CSS and retains existing bridge/framework");
                var beforeUiOnly=Util.State(repair).Files.Where(f=>!f.Path.EndsWith(".css") && !f.Path.EndsWith(".js")).ToDictionary(f=>f.Path,f=>Util.HashFile(Util.Under(repair,f.Path)));
                InstallEngine.UpdateUi(repair,payload);
                Check(beforeUiOnly.All(f=>Util.HashFile(Util.Under(repair,f.Key))==f.Value) && Util.HashFile(Path.Combine(repair,"1.6.0","res","web","zml-client.css"))==Util.Hash(payload["web\\zml-client.css"]),"UI-only update preserves original page, bridge and framework");
                var upgrade=new Dictionary<string,byte[]>(payload,StringComparer.OrdinalIgnoreCase);
                upgrade["ZMLLauncherBridge.exe"]=File.ReadAllBytes(fixture);
                upgrade["web\\zml-client.css"]=Util.Utf8.GetBytes("/* new upgrade */");
                // Schema-1 installation from 0.1.3 did not have the adapter. Test addition/rollback.
                var legacy=Util.State(repair);legacy.Files.RemoveAll(f=>f.Path=="ZML\\ZMLNativeLaunch.dll");
                File.Delete(Util.Under(repair,"ZML\\ZMLNativeLaunch.dll"));Util.WriteJson(Util.StatePath(repair),legacy);
                var upgradeBefore=Util.State(repair).Files.ToDictionary(f=>f.Path,f=>Util.HashFile(Util.Under(repair,f.Path)));
                var stateBefore=Util.HashFile(Util.StatePath(repair));
                Fails(()=>InstallEngine.UpgradeLauncher(repair,upgrade,2),"bridge/UI partial upgrade failure");
                Check(upgradeBefore.All(f=>Util.HashFile(Util.Under(repair,f.Key))==f.Value) && Util.HashFile(Util.StatePath(repair))==stateBefore,"upgrade rollback preserves bridge/UI/state byte exact");
                Check(!File.Exists(Util.Under(repair,"ZML\\ZMLNativeLaunch.dll")),"failed legacy upgrade removes newly-added adapter");
                InstallEngine.UpgradeLauncher(repair,upgrade);
                Check(Util.State(repair).Files.Any(f=>f.Path=="ZML\\ZMLNativeLaunch.dll") && Util.HashFile(Util.Under(repair,"ZML\\ZMLNativeLaunch.dll"))==Util.Hash(payload["ZMLNativeLaunch.dll"]),"legacy upgrade installs and registers own adapter");
                Check(Util.HashFile(Path.Combine(repair,"Launcher.exe"))==Util.Hash(upgrade["ZMLLauncherBridge.exe"]) && upgradeBefore.Where(f=>f.Key!="Launcher.exe" && !f.Key.EndsWith("zml-client.css")).All(f=>Util.HashFile(Util.Under(repair,f.Key))==f.Value),"explicit bridge/UI upgrade preserves native launcher/framework/token/Mods");
                var newHtml="<html><body><div id=\"root\">official update</div><script src=\"qrc:///web/js/qwebchannel.js\"></script></body></html>";
                Util.Atomic(Path.Combine(repair,"1.6.0","res","web","index.html"),Util.Utf8.GetBytes(newHtml));
                InstallEngine.Install(repair,InstallEngine.SuggestedGame(repair),payload,false);InstallEngine.Uninstall(repair);
                Check(File.ReadAllText(Path.Combine(repair,"1.6.0","res","web","index.html"))==newHtml,"repair updates restore baseline after official web update");
                Fails(()=>Util.Under(root1,"..\\escape"),"path traversal rejected");
                using(var m=new MemoryStream()) {
                    using(var z=new ZipArchive(m,ZipArchiveMode.Create,true)){using(var writer=new StreamWriter(z.CreateEntry("../escape").Open()))writer.Write("bad");}
                    m.Position=0;Fails(()=>InstallEngine.Payload(m),"zip traversal rejected");
                }
                int bundled=payload.Keys.Count(k=>k.StartsWith("mods\\") && k.EndsWith("\\mod.ini"));
                if(bundled>0) {
                    var combo=Fixture(fixture);InstallEngine.Install(combo,InstallEngine.SuggestedGame(combo),payload,true);
                    var comboCatalog=new Catalog(Util.State(combo));
                    Check(comboCatalog.Scan().Count==bundled && comboCatalog.Scan().All(m=>m.error==null),"explicit bundled Mods install and native dry-run");
                    var modHash=comboCatalog.Scan().ToDictionary(m=>m.id,m=>Util.Hash(m.Original));InstallEngine.Uninstall(combo);
                    Check(comboCatalog.Scan().All(m=>Util.Hash(m.Original)==modHash[m.id]),"bundled Mods preserved byte exact after uninstall");
                }
                Application.EnableVisualStyles();using(var form=new SetupForm(payload)) {
                    form.Show();Application.DoEvents();
                    using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(0,0,image.Width,image.Height));image.Save(Path.Combine(area,"installer-gui.png"));}
                    Check(form.Controls.OfType<Button>().Any(b=>b.Visible && b.Text=="安装 / 修复"),"GUI installation control visible");
                    form.Close();
                }
                Check(File.Exists(Path.Combine(area,"installer-gui.png")),"real WinForms control render artifact (not interactive UAC proof)");
                Console.WriteLine("RESULT "+passed+" checks; artifacts "+area);return 0;
            }catch(Exception e){Console.Error.WriteLine(e);return 1;}
        }
    }
}
