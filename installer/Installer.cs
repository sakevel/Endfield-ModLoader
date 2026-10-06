using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZmlSetup {
    public sealed class SetupForm : Form {
        readonly TextBox launcher=new TextBox(), game=new TextBox();
        readonly CheckBox mods=new CheckBox();
        readonly Button install=new Button(), uninstall=new Button();
        readonly Label status=new Label();
        readonly Dictionary<string,byte[]> payload;
        bool busy;
        public SetupForm(Dictionary<string,byte[]> files) {
            payload=files;
            Text="Endfield Mod Loader · 安装器"; Font=new Font("Microsoft YaHei UI",10); ClientSize=new Size(720,448);
            StartPosition=FormStartPosition.CenterScreen; FormBorderStyle=FormBorderStyle.FixedDialog; MaximizeBox=false;
            BackColor=Color.FromArgb(245,245,245);
            AddLabel("安装模组加载器",24,22,650,32,18);
            AddLabel("在鹰角启动器中管理模组；原启动方式保留。卸载可恢复原文件。",24,62,670,28,10);
            AddLabel("游戏启动器目录（包含 Launcher.exe）",24,108,640,24,10);
            launcher.SetBounds(24,138,574,28); Controls.Add(launcher);
            var browse=new Button {Text="选择目录"}; browse.SetBounds(610,136,86,32); Controls.Add(browse);
            browse.Click+=(s,e)=> {
                using(var picker=new FolderBrowserDialog {Description="选择鹰角启动器目录",SelectedPath=launcher.Text,ShowNewFolderButton=false})
                    if(picker.ShowDialog(this)==DialogResult.OK) {launcher.Text=picker.SelectedPath;game.Text=InstallEngine.SuggestedGame(picker.SelectedPath);}
            };
            AddLabel("终末地客户端（只读取，文件）",24,186,640,24,10);
            game.SetBounds(24,216,574,28); Controls.Add(game);
            var chooseGame=new Button {Text="选择文件"}; chooseGame.SetBounds(610,214,86,32); Controls.Add(chooseGame);
            chooseGame.Click+=(s,e)=> {using(var picker=new OpenFileDialog {Filter="Endfield.exe|Endfield.exe",CheckFileExists=true}) if(picker.ShowDialog(this)==DialogResult.OK) game.Text=picker.FileName;};
            int count=0; foreach(var f in files.Keys) if(f.StartsWith("mods\\") && f.EndsWith("\\mod.ini")) count++;
            mods.Text=count==0?"此安装包不附带模组；安装后打开模组文件夹添加。":"安装附带的 "+count+" 个模组（不覆盖已有模组）";
            mods.Checked=count>0; mods.Enabled=count>0; mods.SetBounds(24,265,664,32); Controls.Add(mods);
            AddLabel("提示：第三方模组可能引发兼容问题或账号风险。官方启动器更新后可能需要修复。",24,304,674,38,9);
            install.Text="安装 / 修复"; install.BackColor=Color.FromArgb(255,239,0); install.SetBounds(24,361,156,40); Controls.Add(install);
            uninstall.Text="卸载集成"; uninstall.SetBounds(192,361,136,40); Controls.Add(uninstall);
            status.SetBounds(24,410,672,28); status.Text="需要管理员权限 · 不会自动启动或关闭游戏"; Controls.Add(status);
            install.Click+=async(s,e)=>await Execute(false);
            uninstall.Click+=async(s,e)=>await Execute(true);
            FormClosing+=(s,e)=> {if(busy) e.Cancel=true;};
            // No machine-specific path in the distributable: use common drives only when present.
            foreach(var drive in DriveInfo.GetDrives()) if(drive.IsReady) {
                var candidate=Path.Combine(drive.RootDirectory.FullName,"Game","Hypergryph Launcher");
                if(File.Exists(Path.Combine(candidate,"Launcher.exe"))) {launcher.Text=candidate;game.Text=InstallEngine.SuggestedGame(candidate);break;}
            }
        }
        void AddLabel(string text,int x,int y,int w,int h,int size) {var l=new Label {Text=text,Font=new Font(Font.FontFamily,size),AutoSize=false};l.SetBounds(x,y,w,h);Controls.Add(l);}
        async Task Execute(bool remove) {
            var root=launcher.Text; var target=game.Text; var include=mods.Checked;
            if(remove && MessageBox.Show(this,"恢复原启动器和网页；保留已安装模组、私人配置及备份。继续？",Text,MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes) return;
            busy=true; foreach(Control c in Controls) c.Enabled=false; status.Enabled=true; status.Text=remove?"正在恢复原启动器…":"正在校验、备份和安装…";
            try {
                await Task.Run(()=> {if(remove) InstallEngine.Uninstall(root);else {InstallEngine.Install(root,target,payload,include);InstallEngine.UpgradeLauncher(root,payload);}});
                status.Text=remove?"卸载完成，模组与配置保留。":"安装完成。开始游戏按钮内管理模组，下方勾选“加载模组”后直接启动。";
                MessageBox.Show(this,status.Text,Text,MessageBoxButtons.OK,MessageBoxIcon.Information);
            } catch(Exception e) { status.Text="未完成，请查看错误。";MessageBox.Show(this,e.Message,Text,MessageBoxButtons.OK,MessageBoxIcon.Error); }
            finally {busy=false;foreach(Control c in Controls)c.Enabled=true;mods.Enabled=payload.Keys.AnyMod();}
        }
    }
    static class PackageExtensions {public static bool AnyMod(this IEnumerable<string> paths) {foreach(var p in paths)if(p.StartsWith("mods\\"))return true;return false;}}
    public static class InstallerProgram {
        [STAThread] public static int Main(string[] args) {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            try {
                if(!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) throw new IOException("安装器必须以管理员权限启动。");
                Dictionary<string,byte[]> payload;
                using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("ZML.Payload.zip")) {if(s==null)throw new IOException("缺少内嵌安装包");payload=InstallEngine.Payload(s);}
                if(args.Length==0) {Application.Run(new SetupForm(payload));return 0;}
                if(args.Length==2 && args[0]=="--uninstall") InstallEngine.Uninstall(args[1]);
                else if(args.Length==2 && args[0]=="--refresh-ui") InstallEngine.UpdateUi(args[1],payload);
                else if(args.Length==2 && args[0]=="--upgrade-launcher") InstallEngine.UpgradeLauncher(args[1],payload);
                else if(args.Length==3 && args[0]=="--install") {InstallEngine.Install(args[1],args[2],payload,true);InstallEngine.UpgradeLauncher(args[1],payload);}
                else throw new IOException("参数：--install <启动器目录> <Endfield.exe>、--refresh-ui <启动器目录>、--upgrade-launcher <启动器目录> 或 --uninstall <启动器目录>");
                return 0;
            } catch(Exception e) {if(args.Length==0)MessageBox.Show(e.Message,"ZML 安装器",MessageBoxButtons.OK,MessageBoxIcon.Error);else Console.Error.WriteLine(e.Message);return 1;}
        }
    }
}
