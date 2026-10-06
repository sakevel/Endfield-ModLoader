using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace ZmlSetup {
    // Locate launcher installation path
    public static class SetupDiscovery {
        public static string ValidLauncher(string value) {
            if(String.IsNullOrWhiteSpace(value)) return null;
            try {
                var path=Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
                if(path.Length>32760 || path.StartsWith("\\\\",StringComparison.Ordinal) || path.Length<3 || !Char.IsLetter(path[0]) || path[1]!=':' || (path[2]!='\\' && path[2]!='/')) return null;
                if(String.Equals(Path.GetFileName(path),"Launcher.exe",StringComparison.OrdinalIgnoreCase)) path=Path.GetDirectoryName(path);
                path=Path.GetFullPath(path).TrimEnd('\\');
                Util.NoLinks(path);
                if(!File.Exists(Path.Combine(path,"Launcher.exe"))) return null;
                // Skip versioned subdirectories
                InstallEngine.WebPages(path);
                return path;
            } catch(IOException) {return null;} catch(UnauthorizedAccessException) {return null;} catch(ArgumentException) {return null;} catch(System.Security.SecurityException) {return null;}
        }
        public static string FirstLauncher(IEnumerable<string> candidates) {
            foreach(var value in candidates) {var root=ValidLauncher(value);if(root!=null)return root;}
            return "";
        }
        static void Paths(RegistryKey key,List<string> values,int depth) {
            if(key==null) return;
            foreach(var name in new[]{"install_path","launcher_path","InstallLocation"}) {
                var value=key.GetValue(name,null,RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                if(value!=null)values.Add(value);
            }
            if(depth==0)return;
            foreach(var name in key.GetSubKeyNames().OrderBy(n=>n,StringComparer.OrdinalIgnoreCase).Take(64))
                using(var child=key.OpenSubKey(name,false)) Paths(child,values,depth-1);
        }
        public static string LauncherFromRegistry() {
            var paths=new List<string>();
            foreach(var hive in new[]{RegistryHive.CurrentUser,RegistryHive.LocalMachine})
            foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32}) try {
                using(var root=RegistryKey.OpenBaseKey(hive,view)) {
                    foreach(var vendor in new[]{"Hypergryph","GRYPHLINK","Gryphline"})
                        using(var key=root.OpenSubKey("Software\\"+vendor+"\\Launcher",false)) Paths(key,paths,2);
                    using(var uninstall=root.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall",false)) {
                        if(uninstall==null)continue;
                        foreach(var name in uninstall.GetSubKeyNames().OrderBy(n=>n,StringComparer.OrdinalIgnoreCase))
                            using(var key=uninstall.OpenSubKey(name,false)) {
                                if(key==null)continue;
                                var title=key.GetValue("DisplayName") as string ?? "";
                                if(title.IndexOf("Hypergryph",StringComparison.OrdinalIgnoreCase)>=0 || title.Contains("鹰角启动器") || title.IndexOf("GRYPHLINK",StringComparison.OrdinalIgnoreCase)>=0) Paths(key,paths,0);
                            }
                    }
                }
            } catch(System.Security.SecurityException) {} catch(UnauthorizedAccessException) {} catch(IOException) {}
            return FirstLauncher(paths);
        }
        public static string GameFromLauncher(string root) {
            root=ValidLauncher(root);
            if(root==null)throw new IOException("请选择包含 Launcher.exe 和版本号子目录的鹰角启动器目录。");
            var candidates=new List<string>();
            var receipt=Util.StatePath(root);
            var recorded=File.Exists(receipt)?Util.State(root).Game:null;
            if(recorded!=null)candidates.Add(recorded);
            candidates.Add(InstallEngine.SuggestedGame(root));
            // Find game client path
            var games=Util.Under(root,"games");
            if(Directory.Exists(games))
                foreach(var dir in Directory.GetDirectories(games).OrderBy(n=>n,StringComparer.OrdinalIgnoreCase).Take(64)) candidates.Add(Path.Combine(dir,"Endfield.exe"));
            var found=candidates.Where(p=>!String.IsNullOrWhiteSpace(p) && String.Equals(Path.GetFileName(p),"Endfield.exe",StringComparison.OrdinalIgnoreCase) && File.Exists(p))
                .Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if(found.Length==0)throw new IOException("未从该启动器找到已安装的终末地。请先通过鹰角启动器安装／定位终末地，再运行安装器。");
            // Check recorded path then standard default
            var preferred=recorded!=null && found.Contains(recorded,StringComparer.OrdinalIgnoreCase)?recorded:
                found.Contains(InstallEngine.SuggestedGame(root),StringComparer.OrdinalIgnoreCase)?InstallEngine.SuggestedGame(root):null;
            if(found.Length>1 && preferred==null)
                throw new IOException("启动器目录中有多个终末地安装，无法唯一定位。请在启动器中确认游戏安装位置。");
            var game=preferred ?? found[0];Util.NoLinks(game);return game;
        }
    }
}
