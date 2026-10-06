using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ZmlSetup {
    public class ModRow {
        public string id, name, version, description, authors, icon, error, folder;
        public bool enabled;
        public string[] tags, depends;
        public string Manifest;
        public byte[] Original;
    }
    public class ToggleWrite { public string path, before, after; }
    public class Catalog {
        public readonly InstallState State;
        readonly object gate=new object();
        public Catalog(InstallState state) { State=state; Recover(); }
        public string ModsRoot { get { return Util.Under(State.Root,"ZML\\mods"); } }
        static readonly Regex Id=new Regex(@"^[a-z0-9._-]{1,96}$");
        static string[] Split(string value) { return value.Split(',').Select(s=>s.Trim()).Where(s=>s.Length>0).Distinct().ToArray(); }
        static bool ValidId(string id) { return id!="." && id!=".." && Id.IsMatch(id); }
        public List<ModRow> Scan() {
            var rows=new List<ModRow>();
            Util.NoLinks(ModsRoot); Directory.CreateDirectory(ModsRoot);
            foreach(var dir in Directory.GetDirectories(ModsRoot).OrderBy(s=>s)) {
                if(rows.Count>=256) throw new IOException("模组数量超过 256 个");
                var row=new ModRow {folder=Path.GetFileName(dir),id=Path.GetFileName(dir),name=Path.GetFileName(dir),tags=new string[0],depends=new string[0]};
                var ini=Path.Combine(dir,"mod.ini");
                if(!File.Exists(ini)) continue;
                row.Manifest=ini;
                try {
                    Util.NoLinks(ini);
                    if(new FileInfo(ini).Length>16384) throw new IOException("mod.ini 超过 16 KiB");
                    row.Original=File.ReadAllBytes(ini);
                    var dict=new Dictionary<string,string>(); string section="";
                    foreach(var raw in Util.Utf8.GetString(row.Original).Split('\n')) {
                        var line=raw.Trim(); if(line=="" || line[0]=='#' || line[0]==';') continue;
                        if(line.StartsWith("[") && line.EndsWith("]")) { section=line.Substring(1,line.Length-2); if(section!="mod") throw new IOException("未知 manifest 节"); continue; }
                        var at=line.IndexOf('='); if(section!="mod" || at<0) throw new IOException("manifest 格式错误");
                        var key=line.Substring(0,at).Trim();
                        if(!new[]{"id","name","library","enabled","api","version","description","authors","tags","icon","config","config_entry","config_menu","depends"}.Contains(key)) throw new IOException("未知 manifest 字段："+key);
                        dict.Add(key,line.Substring(at+1).Trim());
                    }
                    Func<string,string> get=k=>dict.ContainsKey(k)?dict[k]:"";
                    row.id=get("id"); if(!ValidId(row.id)) throw new IOException("模组 id 无效");
                    row.name=get("name")==""?row.id:get("name"); row.version=get("version"); row.description=get("description"); row.authors=get("authors");
                    if(get("api")!="1") throw new IOException("不支持的模组 API");
                    if(get("enabled")!="true" && get("enabled")!="false") throw new IOException("enabled 必须为 true/false");
                    row.enabled=get("enabled")=="true";
                    if(dict.ContainsKey("depends")) {
                        var deps=get("depends").Split(',').Select(x=>x.Trim()).ToArray();
                        if(deps.Length>32 || deps.Any(x=>!ValidId(x) || x==row.id) || deps.Distinct().Count()!=deps.Length) throw new IOException("依赖声明无效");
                        row.depends=deps;
                    }
                    row.tags=Split(get("tags")); if(row.tags.Length>16) throw new IOException("标签过多");
                    var library=get("library");
                    if(library=="" || library!=Path.GetFileName(library) || !library.EndsWith(".dll",StringComparison.Ordinal) || !File.Exists(Util.Under(dir,library))) throw new IOException("缺少本地 DLL");
                    foreach(var asset in new[]{"config","config_entry","icon"}) if(get(asset)!="") {
                        var path=Util.Under(dir,get(asset)); if(!File.Exists(path)) throw new IOException("缺少 "+asset);
                        var extension=asset=="config"?".ini":asset=="config_entry"?".lua":".png";
                        if(Path.GetExtension(path)!=extension) throw new IOException("资源扩展名不符");
                    }
                    if(get("icon")!="") {
                        var path=Util.Under(dir,get("icon"));
                        if(new FileInfo(path).Length<=256*1024) row.icon="data:image/png;base64,"+Convert.ToBase64String(File.ReadAllBytes(path));
                    }
                } catch(Exception e) { row.error=e.Message; }
                rows.Add(row);
            }
            foreach(var group in rows.GroupBy(x=>x.id).Where(x=>x.Count()>1)) foreach(var row in group) row.error="重复的模组 id";
            return rows;
        }
        static string Revision(List<ModRow> rows) { return Util.Hash(Util.Utf8.GetBytes(String.Join("\n",rows.Select(r=>r.folder+":"+(r.Original!=null?Util.Hash(r.Original):new FileInfo(r.Manifest).Length+":"+new FileInfo(r.Manifest).LastWriteTimeUtc.Ticks))))); }
        public object Listing() { lock(gate) { var rows=Scan(); return new {revision=Revision(rows),mods=rows.Select(r=>new {r.id,r.name,r.version,r.description,r.authors,r.icon,r.error,r.enabled,r.tags,r.depends}),game=State.Game}; } }
        static List<ModRow> Plan(List<ModRow> rows, string id, bool enabled) {
            if(rows.Any(r=>r.error!=null)) throw new IOException("请先修复列表中的无效模组");
            var map=rows.ToDictionary(r=>r.id);
            if(!map.ContainsKey(id)) throw new IOException("模组不存在");
            var planned=new List<ModRow>(); var visiting=new HashSet<string>(); var done=new HashSet<string>();
            Action<ModRow> visit=null;
            visit=r=> {
                if(!visiting.Add(r.id)) { if(enabled) throw new IOException("模组依赖循环"); return; }
                if(enabled) foreach(var dep in r.depends) {
                    if(!map.ContainsKey(dep)) throw new IOException("缺少依赖："+r.id+" → "+dep);
                    if(!done.Contains(dep)) visit(map[dep]);
                } else foreach(var other in rows.Where(m=>m.enabled && m.depends.Contains(r.id))) if(!done.Contains(other.id)) visit(other);
                visiting.Remove(r.id); done.Add(r.id);
                if(r.enabled!=enabled) planned.Add(r);
            };
            visit(map[id]); return planned;
        }
        public object Toggle(string id, bool enabled, string revision, bool apply, bool validate=true, int failAfter=Int32.MaxValue) {
            lock(gate) {
                var rows=Scan(); var hash=Revision(rows);
                if(revision!=hash) throw new IOException("模组目录已改变，请刷新后重试");
                var plan=Plan(rows,id,enabled);
                if(!apply) return new {revision=hash,changes=plan.Select(r=>new {r.id,r.name,enabled})};
                var writes=plan.Select(r=>new ToggleWrite {path=Path.GetFileName(Path.GetDirectoryName(r.Manifest))+"\\mod.ini",before=Convert.ToBase64String(r.Original),after=Convert.ToBase64String(Util.Utf8.GetBytes(Regex.Replace(Util.Utf8.GetString(r.Original),@"(?m)^(\s*enabled\s*=\s*)(true|false)(\s*)$",m=>m.Groups[1].Value+(enabled?"true":"false")+m.Groups[3].Value)))}).ToList();
                if(writes.Count==0) return Listing();
                var journal=Util.Under(State.Root,"ZML\\toggle-journal.json");
                Util.WriteJson(journal,writes);
                try {
                    int n=0;
                    foreach(var w in writes) { Util.Atomic(Util.Under(ModsRoot,w.path),Convert.FromBase64String(w.after)); if(++n==failAfter) throw new IOException("切换故障注入测试"); }
                    if(validate) Util.Validate(State.Root,State.Game);
                    File.Delete(journal);
                } catch { Recover(); throw; }
                return Listing();
            }
        }
        public void Recover() {
            var journal=Util.Under(State.Root,"ZML\\toggle-journal.json");
            if(!File.Exists(journal)) return;
            var writes=Util.ReadJson<List<ToggleWrite>>(journal);
            if(writes.Count>256) throw new IOException("无效切换日志");
            foreach(var w in writes) {
                var old=Convert.FromBase64String(w.before); var next=Convert.FromBase64String(w.after);
                if(old.Length>16384 || next.Length>16384 || Path.GetFileName(w.path)!="mod.ini") throw new IOException("无效切换日志");
                var hash=Util.HashFile(Util.Under(ModsRoot,w.path));
                if(hash!=Util.Hash(old) && hash!=Util.Hash(next)) throw new IOException("模组已被外部修改；未覆盖："+w.path);
            }
            foreach(var w in writes) Util.Atomic(Util.Under(ModsRoot,w.path),Convert.FromBase64String(w.before));
            File.Delete(journal);
        }
        public void OpenFolder() { System.Diagnostics.Process.Start("explorer.exe",Util.Quote(ModsRoot)); }
    }
}
