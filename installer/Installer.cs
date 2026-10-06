using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Windows.Forms;

namespace ZmlSetup {
    public static class InstallerProgram {
        [STAThread] public static int Main(string[] args) {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            try {
                if(!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) throw new IOException("安装器必须以管理员权限启动。");
                Dictionary<string,byte[]> payload;
                using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("ZML.Payload.zip")) {if(s==null)throw new IOException("缺少内嵌安装包");payload=InstallEngine.Payload(s);}
                if(args.Length==0) {Application.Run(new SetupForm(payload));return 0;}
                if(args.Length==2 && args[0]=="--uninstall") InstallEngine.CleanUninstall(args[1]);
                else if(args.Length==2 && args[0]=="--refresh-ui") InstallEngine.UpdateUi(args[1],payload);
                else if(args.Length==2 && args[0]=="--upgrade-launcher") InstallEngine.UpgradeLauncher(args[1],payload);
                else if((args.Length==2 || args.Length==3) && args[0]=="--install") {var game=args.Length==3?args[2]:SetupDiscovery.GameFromLauncher(args[1]);InstallEngine.Install(args[1],game,payload,false);InstallEngine.UpgradeLauncher(args[1],payload);}
                else throw new IOException("参数：--install <启动器目录> [Endfield.exe]、--refresh-ui <启动器目录>、--upgrade-launcher <启动器目录> 或 --uninstall <启动器目录>");
                return 0;
            } catch(Exception e) {if(args.Length==0)MessageBox.Show(e.Message,"ZML 安装器",MessageBoxButtons.OK,MessageBoxIcon.Error);else Console.Error.WriteLine(e.Message);return 1;}
        }
    }
}
