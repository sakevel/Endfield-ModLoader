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
            try {
                using(var response=req.GetResponse())using(var stream=response.GetResponseStream())using(var reader=new StreamReader(stream))return reader.ReadToEnd();
            } catch(WebException e) {
                if(e.Response!=null) using(var err=new StreamReader(e.Response.GetResponseStream())) Console.WriteLine("HTTP ERROR: "+err.ReadToEnd());
                throw;
            }
        }
        static void HttpFails(InstallState s,string path,string token,string origin,int code) {
            try {Request(s,path,token,origin);}catch(WebException e){using(var r=(HttpWebResponse)e.Response)Check((int)r.StatusCode==code,"HTTP "+code+" "+path);return;}throw new Exception("HTTP should fail");
        }
        static IEnumerable<Control> Descendants(Control owner) {
            foreach(Control child in owner.Controls) {yield return child;foreach(var c in Descendants(child))yield return c;}
        }
        static void ClickForFixture(Button b) {
            // Trigger managed test handler
            typeof(Button).GetMethod("OnClick",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(b,new object[]{EventArgs.Empty});
        }
        static void PaintFixture(Control c,Graphics g,Point origin,bool config) {
            // Each control is printed offscreen; Form.DrawToBitmap alone skips children
            // whose parent is intentionally hidden. No Show/Visible=true/desktop capture.
            if((c.Name=="riskPage" && config) || (c.Name=="installPage" && !config))return;
            if(c is Button && ((c.Text=="下一步  →" && config) || (!config && (c.Text=="安装 / 修复" || c.Text=="卸载 ZML" || c.Text=="←  上一步"))))return;
            if(c is ProgressBar)return;
            var position=new Point(origin.X+c.Left,origin.Y+c.Top);
            if(c.Width<1 || c.Height<1)return;
            using(var bitmap=new Bitmap(c.Width,c.Height)){c.DrawToBitmap(bitmap,new Rectangle(Point.Empty,c.Size));g.DrawImageUnscaled(bitmap,position);}
            for(int i=c.Controls.Count-1;i>=0;i--)PaintFixture(c.Controls[i],g,position,config);
        }
        static void Preview(SetupForm form,string path) {
            var handle=form.Handle;foreach(var c in Descendants(form)){handle=c.Handle;c.PerformLayout();}form.PerformLayout();foreach(var c in Descendants(form))c.PerformLayout();
            using(var image=new Bitmap(form.ClientSize.Width,form.ClientSize.Height)) {
                using(var g=Graphics.FromImage(image)) {g.Clear(form.BackColor);for(int i=form.Controls.Count-1;i>=0;i--)PaintFixture(form.Controls[i],g,Point.Empty,form.ConfigurationVisible);}
                image.Save(path);
            }
        }
        static void AwaitFixture(System.Threading.Tasks.Task task) {
            // Pump only our hidden owned fixture's message queue; no desktop input or Show.
            var timer=System.Diagnostics.Stopwatch.StartNew();
            while(!task.IsCompleted) {if(timer.ElapsedMilliseconds>60000)throw new Exception("owned UI task timeout");Application.DoEvents();Thread.Sleep(10);}
            task.GetAwaiter().GetResult();
        }
        static System.Diagnostics.Process BackgroundFixture(string path,string fixture) {
            Directory.CreateDirectory(Path.GetDirectoryName(path));if(!File.Exists(path))File.Copy(fixture,path);
            return System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) {UseShellExecute=false,CreateNoWindow=true,WindowStyle=System.Diagnostics.ProcessWindowStyle.Hidden});
        }
        static void FinishFixture(System.Diagnostics.Process process) {
            if(process==null)return;
            try {if(!process.HasExited){process.Kill();process.WaitForExit(5000);}}finally{process.Dispose();}
        }
        static void Ready(SetupForm form) {var h=form.Handle;foreach(var c in form.RiskBoxes)c.Checked=true;ClickForFixture(form.NextButton);}
        static void ProcessTests(Dictionary<string,byte[]> payload,string fixture) {
            var root=Fixture(fixture);var otherRoot=Fixture(fixture);
            System.Diagnostics.Process launcher=null,web=null,foreign=null,game=null,gameWeb=null;
            try {
                launcher=BackgroundFixture(Path.Combine(root,"Launcher.exe"),fixture);
                web=BackgroundFixture(Util.Under(root,"1.6.0\\QtWebEngineProcess.exe"),fixture);
                foreign=BackgroundFixture(Path.Combine(otherRoot,"Launcher.exe"),fixture);
                game=BackgroundFixture(InstallEngine.SuggestedGame(root),fixture);
                gameWeb=BackgroundFixture(Util.Under(root,"games\\Endfield Game\\QtWebEngineProcess.exe"),fixture);
                using(var capture=LauncherProcesses.Capture(root)) {
                    var targets=capture.Targets;
                    Check(targets.Length==2 && targets.Any(t=>t.Pid==launcher.Id) && targets.Any(t=>t.Pid==web.Id),"capture matches exact launcher and version WebEngine paths, excludes same-name foreign and game processes");
                    using(var dialog=new LauncherCloseForm(targets)) {
                        var h=dialog.Handle;dialog.PerformLayout();
                        Check(!dialog.Visible && dialog.ConfirmButton.Text=="确定" && dialog.ReturnButton.Text=="返回" && dialog.AcceptButton==dialog.ReturnButton && dialog.CancelButton==dialog.ReturnButton && dialog.ReturnButton.DialogResult==DialogResult.Cancel && dialog.ConfirmButton.DialogResult==DialogResult.OK,"cleanup dialog has explicit confirm/return and safe Enter/Escape default");
                        Check(Descendants(dialog).OfType<TextBox>().Single().Text.Contains("PID "+launcher.Id) && Descendants(dialog).OfType<TextBox>().Single().Text.Contains(web.MainModule.FileName),"cleanup prompt lists exact PIDs and executable paths before authorization");
                    }
                }
                Check(!launcher.HasExited && !web.HasExited,"capture and dispose never terminate processes without confirmation");
                Fails(()=>Util.CheckLauncherClosed(root),"noninteractive transaction guard still refuses running launcher rather than implicitly killing it");
                Check(!launcher.HasExited && !web.HasExited,"CLI/engine closed-launcher guard leaves matching processes alive");
                int confirms=0,ops=0,errors=0;int uiThread=Thread.CurrentThread.ManagedThreadId;
                using(var form=new SetupForm(payload,()=>root,(remove,path)=> {ops++;return System.Threading.Tasks.Task.FromResult(0);},(message,ok)=>errors++,targets=> {confirms++;Check(Thread.CurrentThread.ManagedThreadId==uiThread && targets.Length==2,"confirmation delivered on owned UI thread while target handles held");return false;})) {
                    Ready(form);AwaitFixture(form.Execute(false));
                    Check(confirms==1 && ops==0 && errors==0 && !form.IsDisposed && form.CanInstall && !File.Exists(Util.StatePath(root)) && !launcher.HasExited && !web.HasExited,"return cancels with zero install writes or kills and restores retryable UI");
                }
                confirms=ops=0;int successes=0;
                using(var form=new SetupForm(payload,()=>root,(remove,path)=> {
                    ops++;InstallEngine.Install(path,SetupDiscovery.GameFromLauncher(path),payload,false,Int32.MaxValue,false);InstallEngine.UpgradeLauncher(path,payload);return System.Threading.Tasks.Task.FromResult(0);
                },(message,ok)=> {if(!ok)throw new Exception(message);successes++;},targets=> {confirms++;return true;})) {
                    Ready(form);AwaitFixture(form.Execute(false));
                    Check(confirms==1 && ops==1 && successes==1 && form.IsDisposed && Util.State(root).Status=="installed" && launcher.WaitForExit(5000) && web.WaitForExit(5000),"confirmed cleanup terminates held exact fixtures then completes real copy installation and closes");
                }
                Check(!foreign.HasExited && !game.HasExited && !gameWeb.HasExited,"successful cleanup preserves other launcher, game and game-owned WebEngine");
            } finally {FinishFixture(launcher);FinishFixture(web);FinishFixture(foreign);FinishFixture(game);FinishFixture(gameWeb);}
            var raceRoot=Fixture(fixture);System.Diagnostics.Process before=null,after=null;
            try {
                before=BackgroundFixture(Path.Combine(raceRoot,"Launcher.exe"),fixture);int errors=0,ops=0;
                using(var form=new SetupForm(payload,()=>raceRoot,(remove,path)=> {ops++;InstallEngine.Install(path,SetupDiscovery.GameFromLauncher(path),payload,false,Int32.MaxValue,false);return System.Threading.Tasks.Task.FromResult(0);},(message,ok)=> {if(ok)throw new Exception("unexpected race success");errors++;},targets=> {
                    Check(targets.Length==1 && targets[0].Pid==before.Id,"confirmation snapshot contains only displayed process");
                    after=BackgroundFixture(Util.Under(raceRoot,"1.6.0\\QtWebEngineProcess.exe"),fixture);return true;
                })) {
                    Ready(form);AwaitFixture(form.Execute(false));
                    Check(before.WaitForExit(5000) && !after.HasExited && errors==1 && ops==1 && form.CanInstall && !form.IsDisposed && !File.Exists(Util.StatePath(raceRoot)),"new process after confirmation remains alive and transaction fails closed before any install writes");
                }
            } finally {FinishFixture(before);FinishFixture(after);}
            var exitRoot=Fixture(fixture);System.Diagnostics.Process exiting=null,successor=null;
            try {
                exiting=BackgroundFixture(Path.Combine(exitRoot,"Launcher.exe"),fixture);
                using(var capture=LauncherProcesses.Capture(exitRoot)) {
                    exiting.Kill();exiting.WaitForExit();
                    successor=BackgroundFixture(Path.Combine(exitRoot,"Launcher.exe"),fixture);capture.StopConfirmed();
                    Check(exiting.HasExited && !successor.HasExited,"exited snapshot process is skipped; unconfirmed same-path successor is not terminated");
                    capture.Dispose();Fails(()=>capture.StopConfirmed(),"disposed process snapshot cannot be reused for termination");
                }
            } finally {FinishFixture(exiting);FinishFixture(successor);}
            var badRoot=Fixture(fixture);System.Diagnostics.Process blocked=null;
            try {
                blocked=BackgroundFixture(Path.Combine(badRoot,"Launcher.exe"),fixture);File.Delete(InstallEngine.SuggestedGame(badRoot));
                int confirmations=0,operations=0,errors=0;
                using(var form=new SetupForm(payload,()=>badRoot,(remove,path)=> {operations++;return System.Threading.Tasks.Task.FromResult(0);},(message,ok)=> {if(ok)throw new Exception("invalid game succeeded");errors++;},targets=> {confirmations++;return true;})) {
                    Ready(form);AwaitFixture(form.Execute(false));
                    Check(errors==1 && confirmations==0 && operations==0 && !blocked.HasExited && !File.Exists(Util.StatePath(badRoot)) && form.CanInstall,"invalid game preflight fails before cleanup prompt, termination or installation writes");
                }
            } finally {FinishFixture(blocked);}
            var versionRoot=Fixture(fixture);System.Diagnostics.Process native=null,original=null;
            try {
                native=BackgroundFixture(Util.Under(versionRoot,"1.6.0\\Games.exe"),fixture);
                original=BackgroundFixture(Util.Under(versionRoot,"Launcher.zml-original.exe"),fixture);
                using(var capture=LauncherProcesses.Capture(versionRoot)) {
                    Check(capture.Targets.Length==2 && capture.Targets.Any(t=>t.Pid==native.Id) && capture.Targets.Any(t=>t.Pid==original.Id),"native version Games and ZML original entry are recognized by exact paths");
                    capture.StopConfirmed();
                    Check(native.WaitForExit(5000) && original.WaitForExit(5000),"confirmed held-handle termination supports native and original launcher leftovers");
                }
            } finally {FinishFixture(native);FinishFixture(original);}
            Fails(()=>LauncherProcesses.Capture(Path.Combine(area,"missing-launcher")),"invalid launcher rejected before process cleanup");
        }
        static void UiTests(Dictionary<string,byte[]> payload,string fixture) {
            var root=Fixture(fixture);
            Check(SetupDiscovery.FirstLauncher(new[]{"", "missing",Path.Combine(root,"absent"),root})==root,"registry candidate validation ignores stale and relative entries");
            Check(SetupDiscovery.FirstLauncher(new[]{"", "missing"})=="","no valid registry entry leaves blank, never guesses drive locations");
            Check(SetupDiscovery.ValidLauncher("\""+Path.Combine(root,"Launcher.exe")+"\"")==root,"registry executable path with quotes normalizes to installation root");
            var game=InstallEngine.SuggestedGame(root);
            Check(SetupDiscovery.GameFromLauncher(root)==game,"game inferred from launcher standard directory, no client field");
            File.Delete(game);
            Fails(()=>SetupDiscovery.GameFromLauncher(root),"missing game fails before installer writes");
            Check(!File.Exists(Util.StatePath(root)),"discovery failure creates no ZML state");
            var renamed=Path.Combine(root,"games","Renamed installation","Endfield.exe");Directory.CreateDirectory(Path.GetDirectoryName(renamed));File.Copy(fixture,renamed);
            Check(SetupDiscovery.GameFromLauncher(root)==renamed,"unique renamed launcher-owned game directory discovered");
            var second=Path.Combine(root,"games","Another","Endfield.exe");Directory.CreateDirectory(Path.GetDirectoryName(second));File.Copy(fixture,second);
            Fails(()=>SetupDiscovery.GameFromLauncher(root),"ambiguous alternate game directories rejected");
            using(var form=new SetupForm(payload,()=>root)) {
                form.CreateControl();form.PerformLayout();
                Check(!form.Visible && form.RiskBoxes.Length==5 && !form.AllAcknowledged && !form.NextButton.Enabled && !form.CanInstall,"fresh hidden GUI requires all five independent risk confirmations");
                Preview(form,Path.Combine(area,"setup-risk-unchecked.png"));
                Check(Descendants(form).OfType<TextBox>().Count()==1 && form.LauncherBox.Text==root,"GUI has launcher path only with detected prefill");
                for(int mask=0;mask<31;mask++) {
                    for(int i=0;i<5;i++)form.RiskBoxes[i].Checked=(mask&(1<<i))!=0;
                    ClickForFixture(form.NextButton);
                    if(form.ConfigurationVisible || form.NextButton.Enabled || form.CanInstall)throw new Exception("partial consent bypass "+mask);
                }
                Check(true,"all 31 incomplete combinations cannot advance, even forced own click event");
                foreach(var c in form.RiskBoxes)c.Checked=true;
                ClickForFixture(form.NextButton);form.PerformLayout();
                Check(form.ConfigurationVisible && form.CanInstall && form.InstallButton.Enabled,"all confirmations allow install location step");
                form.RiskBoxes[2].Checked=false;ClickForFixture(form.InstallButton);
                Check(!form.CanInstall && !form.InstallButton.Enabled && !File.Exists(Util.StatePath(root)),"installation action rechecks consent and performs zero writes when revoked");
                form.RiskBoxes[2].Checked=true;form.LauncherBox.Text="";
                Check(!form.CanInstall && !form.InstallButton.Enabled,"empty launcher path disables install");
                form.LauncherBox.Text=root;ClickForFixture(form.BackButton);
                Check(!form.ConfigurationVisible && form.NextButton.Enabled && !form.CanInstall,"back returns to consent without installing or losing checked state");
                form.PerformLayout();Preview(form,Path.Combine(area,"setup-risk.png"));
                Check(form.RiskBoxes.All(c=>c.Width>500 && c.Height>=42 && TextRenderer.MeasureText(c.Text,c.Font,new Size(c.Width-c.Padding.Horizontal-32,Int32.MaxValue),TextFormatFlags.WordBreak).Height<=c.Height-c.Padding.Vertical),"all five full risk texts fit layout without clipping");
                ClickForFixture(form.NextButton);form.PerformLayout();Preview(form,Path.Combine(area,"setup-install.png"));
                Check(form.LauncherBox.Parent.Height<=46 && form.LauncherBox.Parent.Width>500,"launcher input stays one line, aligned with browse button");
                Check(!form.Visible,"offscreen previews never show desktop window");
            }
            using(var empty=new SetupForm(payload,()=>null))Check(empty.LauncherBox.Text=="" && empty.RiskBoxes.All(c=>!c.Checked),"new run resets consent and missing detection stays empty");
            var completedRoot=Fixture(fixture);int notices=0,operations=0;bool closed=false;SetupForm completed=null;
            using(completed=new SetupForm(payload,()=>completedRoot,(remove,path)=> {
                operations++;Check(!completed.CanInstall,"completion path blocks repeat actions while work is running");
                InstallEngine.Install(path,SetupDiscovery.GameFromLauncher(path),payload,false);InstallEngine.UpgradeLauncher(path,payload);
                return System.Threading.Tasks.Task.FromResult(0);
            },(message,ok)=> {
                notices++;Check(ok && Util.State(completedRoot).Status=="installed" && !completed.IsDisposed && !closed && !completed.CanInstall && message.StartsWith("安装完成"),"real fixture install finishes before one success notice; window stays blocked until acknowledgement");
            })) {
                var handle=completed.Handle;completed.FormClosed+=(s,e)=>closed=true;
                foreach(var c in completed.RiskBoxes)c.Checked=true;ClickForFixture(completed.NextButton);
                AwaitFixture(completed.Execute(false));
                Check(operations==1 && notices==1 && closed && completed.IsDisposed,"acknowledged completion closes and disposes installer, no idle finished window");
            }
            var failedRoot=Fixture(fixture);int errors=0;SetupForm failed=null;
            using(failed=new SetupForm(payload,()=>failedRoot,(remove,path)=> {throw new IOException("fixture install failure");},(message,ok)=> {errors++;if(ok || message!="fixture install failure")throw new Exception("incorrect failure notice");})) {
                var handle=failed.Handle;foreach(var c in failed.RiskBoxes)c.Checked=true;ClickForFixture(failed.NextButton);
                AwaitFixture(failed.Execute(false));
                Check(errors==1 && !failed.IsDisposed && failed.CanInstall && failed.InstallButton.Enabled && !File.Exists(Util.StatePath(failedRoot)),"failed install reports error and remains retryable without success or closure");
            }

        }
        static void PortTests(Dictionary<string,byte[]> payload,string fixture) {
            var root=Fixture(fixture);var original=Util.HashFile(Path.Combine(root,"Launcher.exe"));
            InstallEngine.Install(root,InstallEngine.SuggestedGame(root),payload,false);
            var state=Util.State(root);var port=state.Port;
            var endpoint=state.Files.Single(f=>f.Path.EndsWith("\\zml-endpoint.js"));
            var endpointPath=Util.Under(root,endpoint.Path);
            var statePath=Util.StatePath(root);var stateBefore=File.ReadAllBytes(statePath);
            var filesBefore=state.Files.ToDictionary(f=>f.Path,f=>Util.HashFile(Util.Under(root,f.Path)));
            using(var server=new BridgeServer(state)) {
                Check(state.Port==port && File.ReadAllBytes(statePath).SequenceEqual(stateBefore),"available registered port retained without configuration writes");
            }
            var busy=new TcpListener(IPAddress.Loopback,port);busy.Server.ExclusiveAddressUse=true;busy.Start();
            try {
                using(var server=new BridgeServer(state)) {
                    var saved=Util.State(root);
                    Check(state.Port!=port && saved.Port==state.Port && saved.Token==state.Token,"occupied port automatically replaced and token preserved");
                    Check(File.ReadAllText(endpointPath).Contains("http://127.0.0.1:"+state.Port) && Util.HashFile(endpointPath)==saved.Files.Single(f=>f.Path==endpoint.Path).After,"new endpoint and uninstall receipt agree");
                    Check(Request(state,"/health",state.Token,"null").Contains("true"),"automatic port serves authenticated file-origin requests");
                    HttpFails(state,"/health","wrong-token",null,403);
                    var steal=new TcpListener(IPAddress.Loopback,state.Port);steal.Server.ExclusiveAddressUse=true;
                    try {Fails(()=>steal.Start(),"selected port remains bound while service runs");} finally {steal.Stop();}
                    Check(state.Files.Where(f=>f.Path!=endpoint.Path).All(f=>Util.HashFile(Util.Under(root,f.Path))==filesBefore[f.Path]),"port migration leaves all other installed files untouched");
                }
            } finally {busy.Stop();}
            var persisted=File.ReadAllBytes(statePath);
            using(var server=new BridgeServer(Util.State(root))) Check(File.ReadAllBytes(statePath).SequenceEqual(persisted),"restart reuses migrated port without rewriting receipt");
            port=state.Port;busy=new TcpListener(IPAddress.Loopback,port);busy.Server.ExclusiveAddressUse=true;busy.Start();
            try {
                var endpointBefore=File.ReadAllBytes(endpointPath);var hash=endpoint.After;
                using(var locked=new FileStream(statePath,FileMode.Open,FileAccess.Read,FileShare.Read))
                    Fails(()=> {using(var server=new BridgeServer(state)) {}},"port migration rejects locked receipt");
                Check(state.Port==port && endpoint.After==hash && File.ReadAllBytes(endpointPath).SequenceEqual(endpointBefore) && File.ReadAllBytes(statePath).SequenceEqual(persisted),"failed receipt publication rolls back endpoint and memory byte-exactly");
                File.AppendAllText(endpointPath,"// external change\n");var changed=File.ReadAllBytes(endpointPath);
                Fails(()=> {using(var server=new BridgeServer(state)) {}},"port migration rejects externally modified endpoint");
                Check(File.ReadAllBytes(endpointPath).SequenceEqual(changed) && File.ReadAllBytes(statePath).SequenceEqual(persisted),"external endpoint and receipt preserved on rejection");
                Util.Atomic(endpointPath,endpointBefore);
            } finally {busy.Stop();}
            // Exercise WSAEACCES on this host when Windows has reserved TCP ranges.
            var ranges=Util.Run(Path.Combine(Environment.SystemDirectory,"netsh.exe"),"interface ipv4 show excludedportrange protocol=tcp",root,10);
            foreach(System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(ranges,@"(?m)^\s*(\d+)\s+(\d+)\s*\*?\s*$")) {
                int reserved=Int32.Parse(match.Groups[1].Value);if(reserved<1024) continue;
                var probe=new TcpListener(IPAddress.Loopback,reserved);probe.Server.ExclusiveAddressUse=true;bool denied=false;
                try {probe.Start();}catch(SocketException e){denied=e.SocketErrorCode==SocketError.AccessDenied;}finally {probe.Stop();}
                if(!denied) continue;
                state.Port=reserved;
                var config=Util.Utf8.GetBytes("window.ZML_LAUNCHER="+Util.Json(new {endpoint="http://127.0.0.1:"+reserved,token=state.Token})+";\n");
                Util.Atomic(endpointPath,config);endpoint.After=Util.Hash(config);Util.WriteJson(statePath,state);
                using(var server=new BridgeServer(state)) Check(state.Port!=reserved && Request(state,"/health",state.Token).Contains("true"),"Windows-reserved port automatically recovered from AccessDenied");
                break;
            }
            InstallEngine.Uninstall(root);
            Check(Util.HashFile(Path.Combine(root,"Launcher.exe"))==original && !File.Exists(endpointPath),"uninstall after automatic port migration restores original launcher and removes owned endpoint");
        }
        [STAThread] public static int Main(string[] args) {
            try {
                area=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"fixtures-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(area);
                bool serve=args.Length==3 && args[0]=="--serve", ui=args.Length==3 && args[0]=="--setup-ui"; var zip=args[serve||ui?1:0];var fixture=args[serve||ui?2:1];
                Dictionary<string,byte[]> payload;using(var stream=File.OpenRead(zip))payload=InstallEngine.Payload(stream);
                if(!serve){UiTests(payload,fixture);ProcessTests(payload,fixture);if(!ui)PortTests(payload,fixture);}
                if(ui) {Console.WriteLine("RESULT "+passed+" UI checks; artifacts "+area);return 0;}
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
                Check(!payload.Keys.Any(k=>k.StartsWith("mods\\",StringComparison.OrdinalIgnoreCase)),"installer payload never carries any Mods");
                var forbidden=new Dictionary<string,byte[]>(payload,StringComparer.OrdinalIgnoreCase);forbidden["mods\\sample\\mod.ini"]=new byte[]{1};
                var forbiddenRoot=Fixture(fixture);Fails(()=>InstallEngine.Install(forbiddenRoot,InstallEngine.SuggestedGame(forbiddenRoot),forbidden,true),"legacy bundled payload rejected even with includeMods requested");
                Check(!File.Exists(Util.StatePath(forbiddenRoot)),"rejected bundle does not create installation state");
                Check(System.Text.Encoding.UTF8.GetString(payload["ZMLLauncherBridge.exe"]).Contains("requestedExecutionLevel level=\"requireAdministrator\""),"bridge PE requests same administrator level as native launcher");
                Fails(()=>Util.ProcessImagePath(Int32.MaxValue),"invalid process discovery fails rather than guessing path");
                var root1=Fixture(fixture);var initial=Util.HashFile(Path.Combine(root1,"Launcher.exe"));var html=Util.HashFile(Path.Combine(root1,"1.6.0","res","web","index.html"));
                InstallEngine.Install(root1,SetupDiscovery.GameFromLauncher(root1),payload,false);
                var s1=Util.State(root1);
                Check(SetupDiscovery.GameFromLauncher(root1)==s1.Game,"existing ZML game record recognized without requesting client path");
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
                    Check(Request(s1,"/index",s1.Token).Contains("mods"),"mod index response");
                    Check(Request(s1,"/check-update",s1.Token).Contains("current"),"update check response");
                    var remoteZip=Path.Combine(root1,"remote-mod.zip");
                    using(var fs=new FileStream(remoteZip,FileMode.Create))
                    using(var arch=new ZipArchive(fs,ZipArchiveMode.Create)) {
                        var e1=arch.CreateEntry("mod.ini");
                        using(var s=e1.Open()) using(var sw=new StreamWriter(s)) {
                            sw.Write("[mod]\r\nid=remote-mod\r\nname=永动接线器\r\nversion=1.0.0\r\nlibrary=Remote.dll\r\nenabled=true\r\napi=1\r\n");
                        }
                        var e2=arch.CreateEntry("Remote.dll");
                        using(var s=e2.Open()) { s.Write(new byte[]{1,2,3},0,3); }
                    }
                    var remoteHash=Util.HashFile(remoteZip);
                    var installBody=Util.Json(new {id="remote-mod",asset_url=remoteZip,sha256=remoteHash});
                    var installRes=Request(s1,"/install-remote",s1.Token,null,"POST",installBody);
                    Check(installRes.Contains("remote-mod"),"remote mod installation succeeds");
                    Check(File.Exists(Path.Combine(root1,"ZML","mods","remote-mod","mod.ini")),"remote mod installed to mods folder");
                    Directory.Delete(Path.Combine(root1,"ZML","mods","remote-mod"),true);
                    Fails(()=>Request(s1,"/install-remote",s1.Token,null,"POST",Util.Json(new {id="remote-mod",asset_url=remoteZip,sha256="wrong"})),"mismatched sha256 rejected");
                    Fails(()=>Request(s1,"/install-remote",s1.Token,null,"POST",Util.Json(new {id="mismatched",asset_url=remoteZip})),"mismatched id rejected");
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
                var forgedBackup=Util.State(repair);var backupTarget=forgedBackup.Files.First(f=>f.Backup!=null);backupTarget.Backup="ZML\\backups\\..\\..\\games\\Endfield Game\\Endfield.exe";
                Util.WriteJson(Util.StatePath(repair),forgedBackup);Fails(()=>Util.State(repair),"forged backup cannot escape ZML backup directory");Util.Atomic(Util.StatePath(repair),validState);
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
                // Test legacy installation upgrade and rollback.
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
                // Hidden control declarations; no desktop GUI operation.
                using(var form=new SetupForm(payload)) {
                    Check(Descendants(form).OfType<Button>().Any(b=>b.Text=="安装 / 修复"),"installer control declarations present without opening GUI");
                    Check(Descendants(form).OfType<CheckBox>().All(c=>c.Name.StartsWith("risk",StringComparison.Ordinal)),"installer has no bundled-Mod checkbox");
                }
                using(var form=new UninstallProgressForm("fixture-root",null)) {
                    var bar=form.Controls.OfType<ProgressBar>().Single();
                    Check(bar.Value==0 && !form.Controls.OfType<Button>().Single().Enabled && !form.ControlBox,"uninstall progress window declarations initially prevent transaction interruption");
                    form.ApplyProgress(new UninstallUpdate(60,"恢复文件"));form.ApplyProgress(new UninstallUpdate(30,"迟到的更新"));
                    Check(bar.Value==60,"uninstall progress never moves backwards");
                    form.ApplyProgress(new UninstallUpdate(150,"完成"));Check(bar.Value==100,"uninstall progress clamps to native control range");
                }
                var clean=Fixture(fixture);var cleanOriginal=Util.HashFile(Path.Combine(clean,"Launcher.exe"));
                InstallEngine.Install(clean,InstallEngine.SuggestedGame(clean),payload,false);Mod(clean,"private",false,null);
                var cleanState=Util.State(clean);var allowed=UninstallProgram.AllowedProcesses(cleanState);
                Check(allowed.Contains(InstallEngine.SuggestedGame(clean)) && !allowed.Contains(fixture),"uninstall process whitelist includes only registered exact paths");
                var forgedGame=cleanState.Game;cleanState.Game=fixture;Fails(()=>UninstallProgram.AllowedProcesses(cleanState),"forged game path cannot authorize arbitrary process shutdown");cleanState.Game=forgedGame;
                Fails(()=>UninstallProgram.ExecuteAsync(clean,null,"changed-receipt").GetAwaiter().GetResult(),"confirmation-time receipt change rejects before any shutdown or restore");
                var different=Fixture(fixture);
                using(var child=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(cleanState.Game){UseShellExecute=false,CreateNoWindow=true,WindowStyle=System.Diagnostics.ProcessWindowStyle.Hidden}))
                using(var other=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(InstallEngine.SuggestedGame(different)){UseShellExecute=false,CreateNoWindow=true,WindowStyle=System.Diagnostics.ProcessWindowStyle.Hidden})) try {
                    UninstallProgram.CloseRegistered(cleanState);
                    Check(child.HasExited && !other.HasExited,"actual held-handle shutdown closes matching fixture and preserves other installation");
                }finally{if(!other.HasExited){other.Kill();other.WaitForExit();}}
                var updates=new List<UninstallUpdate>();var ownerThread=Thread.CurrentThread.ManagedThreadId;bool worker=true;
                var archived=UninstallProgram.ExecuteAsync(clean,(value,message)=>{worker&=Thread.CurrentThread.ManagedThreadId!=ownerThread;updates.Add(new UninstallUpdate(value,message));}).GetAwaiter().GetResult();
                Check(worker && updates.Any(u=>u.Percent==15) && updates.Any(u=>u.Percent==35) && updates.Any(u=>u.Percent>40 && u.Percent<90) && updates.Last().Percent==100,"real uninstall reports process/file/archive progress from worker not UI thread");
                Check(updates.Zip(updates.Skip(1),(a,b)=>a.Percent<=b.Percent).All(v=>v),"actual uninstall stage progress is monotonic");
                Check(!Directory.Exists(Path.Combine(clean,"ZML")) && Util.HashFile(Path.Combine(clean,"Launcher.exe"))==cleanOriginal,"clean uninstall removes active ZML and byte-exact restores native launcher");
                Check(File.Exists(Path.Combine(archived,"mods","private","mod.ini")) && !File.Exists(Path.Combine(clean,"Launcher.zml-original.exe")),"clean uninstall archives user Mods and removes integration backup entry");
                Console.WriteLine("RESULT "+passed+" checks; artifacts "+area);return 0;
            }catch(Exception e){Console.Error.WriteLine(e);return 1;}
        }
    }
}
