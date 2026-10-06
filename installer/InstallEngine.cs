using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace ZmlSetup {
    public static class InstallEngine {
        public const string Marker="<!-- ZML launcher integration v1 -->";
        public static List<string> WebPages(string root) {
            var pages=new List<string>();
            foreach(var dir in Directory.GetDirectories(root).OrderBy(s=>s,StringComparer.OrdinalIgnoreCase)) {
                if(!Regex.IsMatch(Path.GetFileName(dir), @"^\d+\.\d+\.\d+(\.\d+)?$")) continue;
                var page=Util.Under(root,Path.GetFileName(dir)+"\\res\\web\\index.html");
                if (!File.Exists(page)) continue;
                var html=File.ReadAllText(page, Util.Utf8);
                if(html.Length>128*1024 || !html.Contains("qwebchannel.js") || !html.Contains("id=\"root\"") || !html.Contains("</body>"))
                    throw new IOException("启动器网页结构不兼容："+page);
                pages.Add(page.Substring(root.TrimEnd('\\').Length+1));
            }
            if(pages.Count==0) throw new IOException("未找到支持的 Qt WebEngine 启动器界面。请选择含 Launcher.exe 和版本号子目录的启动器目录。");
            return pages;
        }
        public static string SuggestedGame(string root) { return Path.Combine(root,"games","Endfield Game","Endfield.exe"); }
        public static Dictionary<string,byte[]> Payload(Stream stream) {
            var files=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
            long total=0;
            using(var zip=new ZipArchive(stream,ZipArchiveMode.Read)) foreach(var e in zip.Entries) {
                if(e.FullName.EndsWith("/")) continue;
                var path=e.FullName.Replace('/','\\');
                if(path.StartsWith("\\") || path.Contains(":") || path.Split('\\').Any(x=>x==".." || x=="." || x.Length==0) || files.ContainsKey(path) || e.Length>128*1024*1024 || (total+=e.Length)>512*1024*1024)
                    throw new IOException("安装包条目无效");
                using(var s=e.Open()) using(var m=new MemoryStream()) { s.CopyTo(m); files.Add(path,m.ToArray()); }
            }
            foreach(var required in new[]{"ZML.exe","ZMLRuntime.dll","ZMLNativeLaunch.dll","lua\\zml.lua","ZMLLauncherBridge.exe","web\\zml-client.js","web\\zml-client.css"})
                if(!files.ContainsKey(required)) throw new IOException("安装包缺少 "+required);
            return files;
        }
        static int FreePort() { var l=new TcpListener(IPAddress.Loopback,0); l.Start(); var p=((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return p; }
        static void GrantOwner(string path) {
            var acl=Directory.GetAccessControl(path);
            acl.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User, FileSystemRights.Modify, InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            Directory.SetAccessControl(path,acl);
        }
        static void VerifyOwnership(InstallState state, bool allowWebUpdate) {
            foreach(var f in state.Files) {
                var current=Util.HashFile(Util.Under(state.Root,f.Path));
                if(current!=f.After && !(allowWebUpdate && f.Path.EndsWith("\\res\\web\\index.html",StringComparison.OrdinalIgnoreCase)))
                    throw new IOException("文件已被其他程序更改，未覆盖："+f.Path+"。请备份后处理冲突。");
                if(f.Backup!=null && Util.HashFile(Util.Under(state.Root,f.Backup))!=f.Before) throw new IOException("备份校验失败："+f.Path);
            }
        }
        static void Restore(InstallState state) {
            // Preflight every path before restoring anything; never clobber a third-party edit.
            foreach(var f in state.Files) {
                var hash=Util.HashFile(Util.Under(state.Root,f.Path));
                if(hash!=f.After && hash!=f.Before) throw new IOException("恢复遇到外部更改："+f.Path);
                if(f.Before!=null && Util.HashFile(Util.Under(state.Root,f.Backup))!=f.Before) throw new IOException("恢复备份损坏："+f.Path);
            }
            foreach(var f in state.Files.AsEnumerable().Reverse()) {
                var path=Util.Under(state.Root,f.Path);
                if(f.Before==null) { if(File.Exists(path)) File.Delete(path); }
                else Util.Atomic(path,File.ReadAllBytes(Util.Under(state.Root,f.Backup)));
            }
        }
        public static void Uninstall(string root) {
            root=Path.GetFullPath(root).TrimEnd('\\'); Util.NoLinks(root); Util.CheckLauncherClosed(root);
            var state=Util.State(root);
            Restore(state);
            state.Status="uninstalled"; Util.WriteJson(Util.StatePath(root),state);
            // Installed mods and their settings are user data: deliberately keep them.
            // Original backups and receipt also stay available for auditing.
        }
        public static void UpdateUi(string root, Dictionary<string,byte[]> payload) {
            // Safe while the launcher is open: the current document remains loaded.
            // Only existing owned JS/CSS change on disk; next reopen reads them.
            root=Path.GetFullPath(root).TrimEnd('\\'); Util.NoLinks(root);
            var state=Util.State(root);
            if(state.Status!="installed") throw new IOException("没有完成的启动器集成");
            VerifyOwnership(state,false);
            var pages=state.Files.Where(f=>f.Path.EndsWith("\\res\\web\\index.html",StringComparison.OrdinalIgnoreCase)).Select(f=>f.Path).ToList();
            Repair(state,pages,payload);
        }
        public static void UpgradeLauncher(string root, Dictionary<string,byte[]> payload, int failAfter=Int32.MaxValue) {
            root=Path.GetFullPath(root).TrimEnd('\\'); Util.NoLinks(root); Util.CheckLauncherClosed(root);
            var state=Util.State(root);
            if(state.Status!="installed") throw new IOException("没有完成的启动器集成");
            VerifyOwnership(state,false);
            var pages=state.Files.Where(f=>f.Path.EndsWith("\\res\\web\\index.html",StringComparison.OrdinalIgnoreCase)).Select(f=>f.Path).ToList();
            Repair(state,pages,payload,true,failAfter);
        }
        public static void Install(string root, string game, Dictionary<string,byte[]> payload, bool includeMods, int failAfter=Int32.MaxValue, bool validate=true) {
            root=Path.GetFullPath(root).TrimEnd('\\'); game=Path.GetFullPath(game);
            Util.NoLinks(root); Util.NoLinks(game); Util.CheckLauncherClosed(root);
            if(!File.Exists(Util.Under(root,"Launcher.exe"))) throw new IOException("目录中没有 Launcher.exe");
            if(Path.GetFileName(game)!="Endfield.exe" || !File.Exists(game)) throw new IOException("请选择实际的 Endfield.exe");
            var pages=WebPages(root);
            var statePath=Util.StatePath(root);
            if(File.Exists(statePath)) {
                var previous=Util.State(root);
                if(previous.Status=="installing") { Restore(previous); previous.Status="uninstalled"; Util.WriteJson(statePath,previous); }
                else if(previous.Status=="installed") { Repair(previous,pages,payload); return; }
            }
            var zml=Util.Under(root,"ZML");
            if(Directory.Exists(zml) && Directory.GetFiles(zml).Any(p=>Path.GetFileName(p)!="launcher-state.json")) {
                if(!File.Exists(statePath) || Util.State(root).Status!="uninstalled") throw new IOException("ZML 目录已存在且不是本安装器管理的安装，未覆盖。");
            }
            Directory.CreateDirectory(zml); GrantOwner(zml);
            var state=new InstallState { Root=root,Game=game,Status="installing",Port=FreePort() };
            var key=new byte[32]; using(var rng=RandomNumberGenerator.Create()) rng.GetBytes(key); state.Token=BitConverter.ToString(key).Replace("-","").ToLowerInvariant();
            var edits=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach(var f in payload) {
                if(f.Key.StartsWith("web\\",StringComparison.OrdinalIgnoreCase) || f.Key=="ZMLLauncherBridge.exe") continue;
                if(f.Key.StartsWith("mods\\",StringComparison.OrdinalIgnoreCase)) {
                    if(!includeMods) continue;
                    // Existing user mods are never overwritten, even on reinstall.
                    var pieces=f.Key.Split('\\');
                    var modDir=Util.Under(root,"ZML\\mods\\"+pieces[1]);
                    if(Directory.Exists(modDir) && Directory.EnumerateFileSystemEntries(modDir).Any()) continue;
                    edits.Add("ZML\\"+f.Key,f.Value); continue;
                }
                edits.Add("ZML\\"+f.Key,f.Value);
            }
            // GetPrivateProfileStringW expects UTF-16 BOM for non-ANSI paths.
            edits.Add("ZML\\loader.ini",System.Text.Encoding.Unicode.GetBytes("\uFEFF[loader]\r\ngame="+game+"\r\n"));
            var original=Util.Under(root,"Launcher.zml-original.exe");
            if(File.Exists(original)) throw new IOException("Launcher.zml-original.exe 已存在，未覆盖。");
            edits.Add("Launcher.zml-original.exe",File.ReadAllBytes(Util.Under(root,"Launcher.exe")));
            foreach(var page in pages) AddWeb(state,edits,page,payload);
            edits.Add("Launcher.exe",payload["ZMLLauncherBridge.exe"]);
            // All immutable files are covered by the durable rollback plan. Mods are kept on uninstall.
            var batch=Guid.NewGuid().ToString("N");
            foreach(var pair in edits) {
                var path=Util.Under(root,pair.Key);
                if(pair.Key.StartsWith("ZML\\mods\\",StringComparison.OrdinalIgnoreCase) && File.Exists(path)) throw new IOException("模组文件冲突："+path);
                var item=new OwnedFile { Path=pair.Key, Before=Util.HashFile(path), After=Util.Hash(pair.Value) };
                if(item.Before!=null) { item.Backup="ZML\\backups\\"+batch+"\\"+pair.Key; Util.Atomic(Util.Under(root,item.Backup),File.ReadAllBytes(path)); }
                if(!pair.Key.StartsWith("ZML\\mods\\",StringComparison.OrdinalIgnoreCase)) state.Files.Add(item);
            }
            Util.WriteJson(statePath,state);
            int count=0; var appliedMods=new List<string>();
            try {
                foreach(var pair in edits) {
                    Util.Atomic(Util.Under(root,pair.Key),pair.Value);
                    if(pair.Key.StartsWith("ZML\\mods\\",StringComparison.OrdinalIgnoreCase)) appliedMods.Add(pair.Key);
                    if(++count==failAfter) throw new IOException("安装故障注入测试");
                }
                Directory.CreateDirectory(Util.Under(root,"ZML\\mods"));
                if(validate) Util.Validate(root,game);
                state.Status="installed"; Util.WriteJson(statePath,state);
            } catch {
                Restore(state);
                foreach(var name in appliedMods) { var path=Util.Under(root,name); if(Util.HashFile(path)==Util.Hash(edits[name])) File.Delete(path); }
                state.Status="uninstalled"; Util.WriteJson(statePath,state); throw;
            }
        }
        static void AddWeb(InstallState state, Dictionary<string,byte[]> edits, string page, Dictionary<string,byte[]> payload) {
            var html=File.ReadAllText(Util.Under(state.Root,page),Util.Utf8);
            if(html.Contains(Marker)) throw new IOException("界面中存在未登记的 ZML 扩展："+page);
            var dir=Path.GetDirectoryName(page);
            var config="window.ZML_LAUNCHER="+Util.Json(new { endpoint="http://127.0.0.1:"+state.Port,token=state.Token })+";\n";
            var injection=Marker+"<link rel=\"stylesheet\" href=\"./zml-client.css\"><script src=\"./zml-endpoint.js\"></script><script defer src=\"./zml-client.js\"></script>";
            edits[page]=Util.Utf8.GetBytes(html.Replace("</body>",injection+"</body>"));
            edits[Path.Combine(dir,"zml-endpoint.js")]=Util.Utf8.GetBytes(config);
            edits[Path.Combine(dir,"zml-client.js")]=payload["web\\zml-client.js"];
            edits[Path.Combine(dir,"zml-client.css")]=payload["web\\zml-client.css"];
        }
        static void Repair(InstallState state, List<string> pages, Dictionary<string,byte[]> payload, bool upgradeBridge=false, int failAfter=Int32.MaxValue) {
            VerifyOwnership(state,true);
            // Repair updates only UI assets for the existing schema-1 bridge.
            // Framework/Mods stay untouched. Bridge changes require explicit closed-launcher upgrade.
            var edits=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
            if(upgradeBridge && Util.HashFile(Util.Under(state.Root,"Launcher.exe"))!=Util.Hash(payload["ZMLLauncherBridge.exe"])) edits["Launcher.exe"]=payload["ZMLLauncherBridge.exe"];
            if(upgradeBridge) {
                var adapter="ZML\\ZMLNativeLaunch.dll";
                if(Util.HashFile(Util.Under(state.Root,adapter))!=Util.Hash(payload["ZMLNativeLaunch.dll"])) edits[adapter]=payload["ZMLNativeLaunch.dll"];
            }
            foreach(var page in pages) {
                var old=state.Files.FirstOrDefault(f=>f.Path==page);
                if(old==null || Util.HashFile(Util.Under(state.Root,page))!=old.After) AddWeb(state,edits,page,payload);
                else foreach(var name in new[]{"zml-client.js","zml-client.css"}) {
                    var relative=Path.Combine(Path.GetDirectoryName(page),name);
                    var bytes=payload["web\\"+name];
                    if(Util.HashFile(Util.Under(state.Root,relative))!=Util.Hash(bytes)) edits[relative]=bytes;
                }
            }
            if(edits.Count==0) return;
            var beforeState=Util.Utf8.GetBytes(Util.Json(state));
            var undo=new Dictionary<string,byte[]>();
            var batch=Guid.NewGuid().ToString("N");
            var updateBackup="ZML\\backups\\u-"+batch.Substring(0,16)+"\\";
            Util.Atomic(Util.Under(state.Root,updateBackup+"launcher-state.json"),beforeState);
            int count=0;
            try {
                foreach(var pair in edits) {
                    var path=Util.Under(state.Root,pair.Key); undo[pair.Key]=File.Exists(path)?File.ReadAllBytes(path):null;
                    if(undo[pair.Key]!=null) Util.Atomic(Util.Under(state.Root,updateBackup+pair.Key),undo[pair.Key]);
                    var existing=state.Files.FirstOrDefault(f=>f.Path==pair.Key);
                    // New official index is now the baseline. Own generated assets can be refreshed.
                    if(existing!=null && !pair.Key.EndsWith("index.html")) { existing.After=Util.Hash(pair.Value); }
                    else {
                        if(existing!=null) state.Files.Remove(existing);
                        var f=new OwnedFile {Path=pair.Key,Before=Util.HashFile(path),After=Util.Hash(pair.Value)};
                        if(f.Before!=null) { f.Backup="ZML\\backups\\"+batch+"\\"+pair.Key; Util.Atomic(Util.Under(state.Root,f.Backup),undo[pair.Key]); }
                        state.Files.Add(f);
                    }
                    Util.Atomic(path,pair.Value);
                    if(++count==failAfter) throw new IOException("界面升级故障注入测试");
                }
                Util.WriteJson(Util.StatePath(state.Root),state);
            } catch {
                foreach(var pair in undo) { var path=Util.Under(state.Root,pair.Key); if(pair.Value==null) { if(File.Exists(path)) File.Delete(path); } else Util.Atomic(path,pair.Value); }
                Util.Atomic(Util.StatePath(state.Root),beforeState); throw;
            }
        }
    }
}
