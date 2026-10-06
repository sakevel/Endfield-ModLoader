using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZmlSetup {
    static class SetupTheme {
        public static readonly Color Paper=Color.FromArgb(239,239,236), Ink=Color.FromArgb(49,49,49), Muted=Color.FromArgb(106,106,101), Yellow=Color.FromArgb(255,239,0);
        public static Button Button(string text,bool primary=false) {
            var b=new Button {Text=text,FlatStyle=FlatStyle.Flat,BackColor=primary?Yellow:Paper,ForeColor=Ink,Cursor=Cursors.Hand,Size=new Size(156,42),UseVisualStyleBackColor=false};
            b.FlatAppearance.BorderSize=primary?0:1;b.FlatAppearance.BorderColor=Color.FromArgb(187,187,182);
            b.FlatAppearance.MouseOverBackColor=primary?Color.FromArgb(255,247,103):Color.White;
            b.FlatAppearance.MouseDownBackColor=primary?Color.FromArgb(233,220,0):Color.FromArgb(216,216,211);
            return b;
        }
        public static Label Label(string text,float size=10,Color? color=null) {
            return new Label {Text=text,AutoSize=false,ForeColor=color??Ink,Font=new Font("Microsoft YaHei UI",size),Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft};
        }
    }
    // UI graphics initialization
    sealed class SetupHeader : Panel {
        readonly string brand = Util.RandomFullName();
        static readonly float[][] WatchPolys = new float[][] {
            new float[] {15.63f,16.63f, 11.76f,14.34f, 11.43f,14.36f, 7.36f,16.69f, 7.36f,20.94f, 7.72f,21.09f, 10.64f,19.39f, 10.66f,16.3f, 11.5f,15.77f, 11.54f,18.98f, 15.1f,21.09f, 15.41f,21.07f, 15.65f,20.91f},
            new float[] {2.0f,14.52f, 2.02f,18.45f, 2.77f,18.91f, 2.86f,19.4f, 6.06f,21.33f, 6.55f,21.11f, 6.51f,16.82f, 2.51f,14.32f, 2.35f,14.28f},
            new float[] {19.91f,8.94f, 19.76f,8.96f, 17.85f,10.06f, 17.71f,10.19f, 17.71f,10.65f, 17.76f,10.72f, 19.74f,11.9f, 19.87f,11.88f, 21.94f,10.69f, 21.98f,10.61f, 21.94f,10.14f},
            new float[] {6.66f,6.01f, 6.29f,6.27f, 6.29f,10.61f, 9.05f,12.25f, 9.14f,13.04f, 5.91f,11.33f, 2.92f,13.02f, 2.84f,13.59f, 6.75f,16.01f, 10.73f,13.72f, 10.75f,8.5f},
            new float[] {11.49f,2.63f, 7.19f,4.96f, 7.16f,5.28f, 11.52f,7.97f, 11.54f,12.85f, 16.33f,15.73f, 16.35f,21.07f, 16.88f,21.35f, 20.17f,19.4f, 20.22f,18.94f, 21.03f,18.43f, 21.05f,14.39f, 20.18f,13.79f, 20.11f,13.02f, 17.12f,11.33f, 13.87f,13.11f, 13.14f,12.71f, 16.72f,10.65f, 16.73f,6.23f, 15.89f,5.66f, 15.83f,4.94f},
            new float[] {20.07f,14.91f, 20.09f,18.34f, 16.95f,20.1f, 16.94f,16.71f},
            new float[] {15.71f,6.71f, 15.71f,10.17f, 12.53f,11.94f, 12.5f,8.52f}
        };
        public SetupHeader() {DoubleBuffered=true;BackColor=SetupTheme.Ink;Height=92;Dock=DockStyle.Top;}
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
            float scale=Height/114f;
            using(var p=new Pen(Color.FromArgb(67,67,65),1)) for(int x=Width-260;x<Width+120;x+=14) g.DrawLine(p,x,0,x-110,Height);
            using(var yellow=new SolidBrush(SetupTheme.Yellow)) {
                g.FillRectangle(yellow,0,Height-3*scale,Width,3*scale);
                float ox=28*scale, oy=18*scale, s=(60*scale)/24f;
                foreach(var poly in WatchPolys) {
                    var pts = new PointF[poly.Length/2];
                    for(int i=0;i<pts.Length;i++) pts[i] = new PointF(ox+poly[i*2]*s, oy+poly[i*2+1]*s);
                    g.FillPolygon(yellow, pts);
                }
            }
            using(var font=new Font("Microsoft YaHei UI",21*scale,FontStyle.Bold))using(var brush=new SolidBrush(Color.White))g.DrawString("模组加载器",font,brush,101*scale,22*scale);
            using(var font=new Font("Segoe UI",9*scale))using(var brush=new SolidBrush(Color.FromArgb(181,181,175)))g.DrawString(brand,font,brush,104*scale,64*scale);
        }
    }
    internal sealed class LauncherCloseForm : Form {
        internal readonly Button ConfirmButton=SetupTheme.Button("确定",true), ReturnButton=SetupTheme.Button("返回");
        internal LauncherCloseForm(LauncherProcesses.Target[] targets) {
            Text="结束启动器进程？";Font=new Font("Microsoft YaHei UI",10);BackColor=SetupTheme.Paper;
            ClientSize=new Size(720,300);StartPosition=FormStartPosition.CenterParent;
            FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=MinimizeBox=false;AutoScaleMode=AutoScaleMode.Font;
            var text=SetupTheme.Label("发现启动器仍在运行（可能是后台残留）。确定将结束以下进程，再继续操作。\n不会关闭游戏；若正在下载或更新，请选择返回并先手动退出。",10);
            text.Dock=DockStyle.Top;text.Height=82;
            var list=new TextBox {Name="processList",Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=false,Dock=DockStyle.Fill,BackColor=Color.White,
                Text=String.Join(Environment.NewLine,targets.Select(t=>"PID "+t.Pid+"  "+t.Path))};
            var actions=new FlowLayoutPanel {Dock=DockStyle.Bottom,Height=58,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(0,8,0,0)};
            ReturnButton.DialogResult=DialogResult.Cancel;ConfirmButton.DialogResult=DialogResult.OK;
            actions.Controls.Add(ReturnButton);actions.Controls.Add(ConfirmButton);
            var body=new Panel {Dock=DockStyle.Fill,Padding=new Padding(20)};body.Controls.Add(list);body.Controls.Add(actions);body.Controls.Add(text);Controls.Add(body);
            AcceptButton=ReturnButton;CancelButton=ReturnButton;
        }
    }
    public sealed class SetupForm : Form {
        public static readonly string[] RiskStatements={
            "我知道本项目与终末地官方无任何关系。",
            "我了解使用模组违反终末地的用户协议。",
            "我不会利用本项目完成破坏游戏平衡性的行为。",
            "我不会在社区内跳脸官方，或以本项目冒充官方支持。",
            "我了解使用本项目可能导致账号被封禁，并自愿承担一切风险。"
        };
        readonly TextBox launcher=new TextBox {Name="launcherDirectory",BorderStyle=BorderStyle.None,BackColor=Color.White,Dock=DockStyle.Fill};
        readonly Button next=SetupTheme.Button("下一步  →",true), install=SetupTheme.Button("安装 / 修复",true), uninstall=SetupTheme.Button("卸载 ZML"), back=SetupTheme.Button("←  上一步"), browse=SetupTheme.Button("浏览…");
        readonly CheckBox[] acknowledgements;
        readonly Label status=SetupTheme.Label("请逐项阅读并确认。",9,SetupTheme.Muted), step=SetupTheme.Label("01  /  使用须知",10);
        readonly Panel riskPage=new Panel {Name="riskPage",Dock=DockStyle.Fill}, installPage=new Panel {Name="installPage",Dock=DockStyle.Fill,Visible=false};
        readonly FlowLayoutPanel actions=new FlowLayoutPanel {Dock=DockStyle.Right,FlowDirection=FlowDirection.RightToLeft,WrapContents=false,AutoSize=true};
        readonly ProgressBar progress=new ProgressBar {Dock=DockStyle.Top,Height=4,Visible=false,Style=ProgressBarStyle.Marquee};
        readonly SetupHeader header=new SetupHeader();
        readonly Action<string,bool> notice;
        readonly Func<bool,string,Task> operation;
        readonly Func<LauncherProcesses.Target[],bool> confirmProcesses;
        bool busy,configuration;
        internal bool AllAcknowledged {get{return acknowledgements.All(c=>c.Checked);}}
        internal bool CanInstall {get{return !busy && configuration && AllAcknowledged && !String.IsNullOrWhiteSpace(launcher.Text);}}
        internal CheckBox[] RiskBoxes {get{return acknowledgements;}}
        internal Button NextButton {get{return next;}}
        internal Button BackButton {get{return back;}}
        internal Button InstallButton {get{return install;}}
        internal TextBox LauncherBox {get{return launcher;}}
        internal bool ConfigurationVisible {get{return configuration;}}
        public SetupForm(Dictionary<string,byte[]> files) : this(files,SetupDiscovery.LauncherFromRegistry) {}
        internal SetupForm(Dictionary<string,byte[]> files,Func<string> discover) : this(files,discover,null,null) {}
        internal SetupForm(Dictionary<string,byte[]> files,Func<string> discover,Func<bool,string,Task> run,Action<string,bool> showNotice,Func<LauncherProcesses.Target[],bool> confirm=null) {
            confirmProcesses=confirm??(targets=> {using(var dialog=new LauncherCloseForm(targets))return dialog.ShowDialog(this)==DialogResult.OK;});
            notice=showNotice??((message,ok)=>MessageBox.Show(this,message,Util.RandomFullName(),MessageBoxButtons.OK,ok?MessageBoxIcon.Information:MessageBoxIcon.Error));
            operation=run??((remove,root)=>Task.Run(()=> {if(remove)InstallEngine.CleanUninstall(root);else {var target=SetupDiscovery.GameFromLauncher(root);InstallEngine.Install(root,target,files,false);InstallEngine.UpgradeLauncher(root,files);}}));
            Text=Util.RandomFullName()+" 安装器";Font=new Font("Microsoft YaHei UI",10);
            AutoScaleMode=AutoScaleMode.Font;ClientSize=new Size(820,480);
            StartPosition=FormStartPosition.CenterScreen;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;BackColor=SetupTheme.Paper;
            var footer=new Panel {Dock=DockStyle.Bottom,Height=78,Padding=new Padding(28,16,28,16),BackColor=Color.FromArgb(225,225,220)};
            status.Dock=DockStyle.Fill;footer.Controls.Add(status);footer.Controls.Add(actions);
            foreach(var b in new[]{next,install,uninstall,back}) {b.Margin=new Padding(8,0,0,0);actions.Controls.Add(b);}
            var body=new Panel {Dock=DockStyle.Fill,Padding=new Padding(28,0,28,12)};
            var pages=new Panel {Dock=DockStyle.Fill};pages.Controls.Add(installPage);pages.Controls.Add(riskPage);
            step.Dock=DockStyle.Top;step.Height=38;
            body.Controls.Add(pages);body.Controls.Add(step);body.Controls.Add(progress);
            Controls.Add(body);Controls.Add(footer);Controls.Add(header);
            var risks=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=5};risks.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            acknowledgements=new CheckBox[RiskStatements.Length];
            for(int i=0;i<acknowledgements.Length;i++) {
                risks.RowStyles.Add(new RowStyle(SizeType.Percent,20));
                var check=new CheckBox {Name="risk"+i,Text=RiskStatements[i],Dock=DockStyle.Fill,AutoSize=false,Padding=new Padding(15,4,12,4),Margin=new Padding(0,0,0,7),BackColor=Color.FromArgb(251,251,249),ForeColor=SetupTheme.Ink,FlatStyle=FlatStyle.Flat,Cursor=Cursors.Hand,AccessibleName=RiskStatements[i],TabIndex=i};
                check.FlatAppearance.BorderColor=SetupTheme.Ink;check.FlatAppearance.CheckedBackColor=SetupTheme.Yellow;
                check.CheckedChanged+=(s,e)=>RefreshActions();acknowledgements[i]=check;risks.Controls.Add(check,0,i);
            }
            riskPage.Controls.Add(risks);
            BuildInstallPage();
            next.Name="continueAfterRisks";install.Name="installLoader";
            next.Click+=(s,e)=> {if(busy || !AllAcknowledged)return;configuration=true;RefreshActions();launcher.Focus();};
            back.Click+=(s,e)=> {if(busy)return;configuration=false;RefreshActions();};
            launcher.TextChanged+=(s,e)=>RefreshActions();
            browse.Click+=(s,e)=> {using(var picker=new FolderBrowserDialog {Description="选择包含 Launcher.exe 的鹰角启动器目录",SelectedPath=launcher.Text,ShowNewFolderButton=false}) if(picker.ShowDialog(this)==DialogResult.OK)launcher.Text=picker.SelectedPath;};
            install.Click+=async(s,e)=>await Execute(false);uninstall.Click+=async(s,e)=>await Execute(true);
            FormClosing+=(s,e)=> {if(busy)e.Cancel=true;};
            launcher.Text=discover()??"";RefreshActions();
        }
        void BuildInstallPage() {
            var layout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=3};layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            foreach(var height in new[]{24,46,30})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
            layout.Controls.Add(SetupTheme.Label("鹰角启动器目录",10),0,0);
            var path=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};path.RowStyles.Add(new RowStyle(SizeType.Percent,100));path.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));path.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,92));
            var field=new Panel {Dock=DockStyle.Fill,BackColor=Color.White,Padding=new Padding(12,11,12,8),Margin=new Padding(0,0,8,0)};field.Controls.Add(launcher);path.Controls.Add(field,0,0);browse.Dock=DockStyle.Fill;browse.Margin=Padding.Empty;path.Controls.Add(browse,1,0);layout.Controls.Add(path,0,1);
            layout.Controls.Add(SetupTheme.Label("游戏位置自动识别；若启动器仍在运行，将询问是否结束。",9,SetupTheme.Muted),0,2);
            installPage.Controls.Add(layout);
        }
        void RefreshActions() {
            AcceptButton=configuration?install:next;
            next.Visible=!configuration;install.Visible=uninstall.Visible=back.Visible=configuration;
            next.Enabled=!busy && AllAcknowledged;install.Enabled=CanInstall;uninstall.Enabled=CanInstall;back.Enabled=!busy;
            riskPage.Visible=!configuration;installPage.Visible=configuration;
            var height=(int)Math.Round((configuration?320:480)*(header.Height/92f));
            if(ClientSize.Height!=height)ClientSize=new Size(ClientSize.Width,height);
            step.Text=configuration?"02  /  安装":"01  /  使用须知";
            if(!busy)status.Text=configuration?"":"已确认 "+acknowledgements.Count(c=>c.Checked)+" / "+acknowledgements.Length+" 项";
        }
        internal async Task Execute(bool remove) {
            // Require confirmation
            if(!CanInstall)return;
            var root=launcher.Text.Trim();
            if(remove && MessageBox.Show(this,"恢复原启动器；保留已安装模组、私人配置及备份。继续？",Text,MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
            busy=true;riskPage.Enabled=installPage.Enabled=false;progress.Visible=true;RefreshActions();status.Text=remove?"正在恢复原启动器…":"正在校验、备份和安装…";
            bool completed=false,cancelled=false;
            try {
                using(var processes=await Task.Run(()=> {
                    // Validate before prompting
                    if(remove)InstallEngine.PreflightUninstall(root);else SetupDiscovery.GameFromLauncher(root);
                    return LauncherProcesses.Capture(root);
                })) {
                    if(processes.Targets.Length>0) {
                        if(!confirmProcesses(processes.Targets)) {cancelled=true;return;}
                        status.Text="正在结束已确认的启动器进程…";
                        await Task.Run(()=>processes.StopConfirmed());
                    }
                    // Check running processes
                    await operation(remove,root);
                }
                completed=true;
            } catch(Exception e) {notice(e.Message,false);}
            finally {
                if(!completed) {busy=false;progress.Visible=false;riskPage.Enabled=installPage.Enabled=true;RefreshActions();status.Text=cancelled?"已取消，未结束进程或安装。":"未完成，请重试。";}
            }
            if(completed) {
                try {notice(remove?"卸载完成。模组与配置已保留。":"安装完成。重开启动器，勾选“加载模组”即可使用。",true);}
                finally {busy=false;Close();}
            }
        }
    }
}
