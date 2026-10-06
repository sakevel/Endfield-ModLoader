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
    // Original geometric graphics, no game textures or official branding.
    sealed class SetupHeader : Panel {
        public SetupHeader() {DoubleBuffered=true;BackColor=SetupTheme.Ink;Height=92;Dock=DockStyle.Top;}
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
            float scale=Height/114f;
            using(var p=new Pen(Color.FromArgb(67,67,65),1)) for(int x=Width-260;x<Width+120;x+=14) g.DrawLine(p,x,0,x-110,Height);
            using(var yellow=new SolidBrush(SetupTheme.Yellow)) {
                g.FillPolygon(yellow,new[]{new PointF(28*scale,24*scale),new PointF(80*scale,24*scale),new PointF(80*scale,77*scale),new PointF(66*scale,90*scale),new PointF(28*scale,90*scale)});
                g.FillRectangle(yellow,0,Height-3*scale,Width,3*scale);
            }
            using(var ink=new SolidBrush(SetupTheme.Ink)) for(int y=0;y<2;y++)for(int x=0;x<2;x++)g.FillRectangle(ink,(39+x*18)*scale,(37+y*18)*scale,13*scale,13*scale);
            using(var font=new Font("Microsoft YaHei UI",21*scale,FontStyle.Bold))using(var brush=new SolidBrush(Color.White))g.DrawString("模组加载器",font,brush,101*scale,22*scale);
            using(var font=new Font("Segoe UI",9*scale))using(var brush=new SolidBrush(Color.FromArgb(181,181,175)))g.DrawString("ENDFIELD MOD LOADER",font,brush,104*scale,64*scale);
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
        internal SetupForm(Dictionary<string,byte[]> files,Func<string> discover,Func<bool,string,Task> run,Action<string,bool> showNotice) {
            notice=showNotice??((message,ok)=>MessageBox.Show(this,message,"ZML",MessageBoxButtons.OK,ok?MessageBoxIcon.Information:MessageBoxIcon.Error));
            operation=run??((remove,root)=>Task.Run(()=> {if(remove)InstallEngine.CleanUninstall(root);else {var target=SetupDiscovery.GameFromLauncher(root);InstallEngine.Install(root,target,files,false);InstallEngine.UpgradeLauncher(root,files);}}));
            Text="ZML 安装器";Font=new Font("Microsoft YaHei UI",10);
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
            layout.Controls.Add(SetupTheme.Label("安装前请退出启动器。游戏位置自动识别。",9,SetupTheme.Muted),0,2);
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
            // A click/Enter/event cannot bypass the confirmation screen.
            if(!CanInstall)return;
            var root=launcher.Text.Trim();
            if(remove && MessageBox.Show(this,"恢复原启动器；保留已安装模组、私人配置及备份。继续？",Text,MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
            busy=true;riskPage.Enabled=installPage.Enabled=false;progress.Visible=true;RefreshActions();status.Text=remove?"正在恢复原启动器…":"正在校验、备份和安装…";
            bool completed=false;
            try {
                await operation(remove,root);
                completed=true;
            } catch(Exception e) {notice(e.Message,false);}
            finally {
                if(!completed) {busy=false;progress.Visible=false;riskPage.Enabled=installPage.Enabled=true;RefreshActions();status.Text="未完成，请重试。";}
            }
            if(completed) {
                try {notice(remove?"卸载完成。模组与配置已保留。":"安装完成。重开启动器，勾选“加载模组”即可使用。",true);}
                finally {busy=false;Close();}
            }
        }
    }
}
