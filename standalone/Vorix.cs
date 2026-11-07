using System;
using System.IO;
using System.Drawing;
using System.Diagnostics;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Reflection;
using Microsoft.Win32;

[assembly: AssemblyTitle("Vorix Endpoint")]
[assembly: AssemblyProduct("Vorix Endpoint Standalone")]
[assembly: AssemblyVersion("1.2.1.0")]
[assembly: AssemblyCopyright("Copyright © 2026 Vorix")]
partial class Vorix : Form {
    const string Version = "1.2.1 Pilot";
    static string Root = AppDomain.CurrentDomain.BaseDirectory;
    static string Home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Vorix Endpoint");
    static string Data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Vorix Endpoint");
    static string Db { get { return Path.Combine(Data,"database"); } }
    static bool Admin { get { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); } }
    TextBox output; Label status; Button scan, full, update, install; Process worker; bool busy;
    static string Q(string s) { if(s.IndexOf('"')>=0) throw new ArgumentException("Invalid path"); return "\""+System.Text.RegularExpressions.Regex.Replace(s,@"(\\+)$","$1$1")+"\""; }
    static string PSQ(string s) { return "'"+s.Replace("'","''")+"'"; }
    static int Run(string file, string args) { using(var p=Process.Start(new ProcessStartInfo(file,args){UseShellExecute=false,CreateNoWindow=true})) { p.WaitForExit(); return p.ExitCode; } }
    static void PS(string code) { int rc=Run(Path.Combine(Environment.SystemDirectory,@"WindowsPowerShell\v1.0\powershell.exe"),"-NoProfile -NonInteractive -EncodedCommand "+Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes("$ErrorActionPreference='Stop'; "+code))); if(rc!=0) throw new Exception("Windows setup failed ("+rc+")."); }
    static Process Elevate(string args) { return Process.Start(new ProcessStartInfo(Path.Combine(Root,"Vorix Endpoint.exe"),args){UseShellExecute=true,Verb="runas"}); }
    static void CopyTree(string from,string to) { Directory.CreateDirectory(to); foreach(string f in Directory.GetFiles(from)) File.Copy(f,Path.Combine(to,Path.GetFileName(f)),true); foreach(string d in Directory.GetDirectories(from)) CopyTree(d,Path.Combine(to,Path.GetFileName(d))); }
    static void Install(bool quiet=false,bool updateNow=true) {
        if(!Admin) { using(var p=Elevate("--install")) p.WaitForExit(); return; }
        if(!File.Exists(Path.Combine(Root,@"clamav\freshclam.exe"))) throw new Exception("Extract the complete ZIP before installing.");
        foreach(var process in Process.GetProcessesByName("Vorix Endpoint")) {
            if(process.Id==Process.GetCurrentProcess().Id)continue;
            if(String.Equals(process.MainModule.FileName,Path.Combine(Home,"Vorix Endpoint.exe"),StringComparison.OrdinalIgnoreCase))throw new Exception("Close the running Vorix Endpoint app before installing or repairing it.");
        }
        using(var installGate=ScanMutex()) {
        if(!TakeScan(installGate))throw new Exception("Wait for the active standalone scan to finish before upgrading.");
        try {
        PS("$t=Get-ScheduledTask -TaskName VorixStandaloneMonitor -ErrorAction SilentlyContinue; if($t){Stop-ScheduledTask -TaskName VorixStandaloneMonitor; Start-Sleep -Seconds 2}");
        foreach(var old in Process.GetProcessesByName("VorixStatus")){try{if(String.Equals(old.MainModule.FileName,Path.Combine(Home,"VorixStatus.exe"),StringComparison.OrdinalIgnoreCase)){old.Kill();old.WaitForExit(10000);}}catch(System.ComponentModel.Win32Exception){}}
        Directory.CreateDirectory(Home); Directory.CreateDirectory(Data);
        foreach(string dir in new[]{Home,Data}) {
            if(Run("icacls.exe",Q(dir)+" /inheritance:r /grant:r *S-1-5-18:(OI)(CI)F *S-1-5-32-544:(OI)(CI)F *S-1-5-32-545:(OI)(CI)RX")!=0) throw new Exception("Cannot secure installation folders.");
        }
        if(!String.Equals(Root.TrimEnd('\\'),Home,StringComparison.OrdinalIgnoreCase)) CopyTree(Root.TrimEnd('\\'),Home);
        Directory.CreateDirectory(Db); Directory.CreateDirectory(Path.Combine(Data,"logs"));
        File.WriteAllText(Path.Combine(Data,"freshclam.conf"),"DatabaseMirror database.clamav.net\r\nDatabaseDirectory "+Q(Db)+"\r\nUpdateLogFile "+Q(Path.Combine(Data,@"logs\freshclam.log"))+"\r\nLogTime yes\r\nLogRotate yes\r\nLogFileMaxSize 2M\r\nTestDatabases yes\r\n");
        string exe=Path.Combine(Home,"Vorix Endpoint.exe");
        PS("$a=New-ScheduledTaskAction -Execute "+PSQ(exe)+" -Argument '--update'; $t=New-ScheduledTaskTrigger -Daily -At '12:00'; $p=New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest; $s=New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Minutes 45) -MultipleInstances IgnoreNew; Register-ScheduledTask -TaskName 'VorixStandaloneDefinitions' -Action $a -Trigger $t -Principal $p -Settings $s -Force | Out-Null; "+(updateNow?"Start-ScheduledTask -TaskName 'VorixStandaloneDefinitions'; ":"")+"$w=New-Object -ComObject WScript.Shell; $l=$w.CreateShortcut([IO.Path]::Combine([Environment]::GetFolderPath('CommonPrograms'),'Vorix Endpoint.lnk')); $l.TargetPath="+PSQ(exe)+"; $l.WorkingDirectory="+PSQ(Home)+"; $l.Save()");
        string maintenance=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Vorix Standalone Maintenance");Directory.CreateDirectory(maintenance);
        if(Run("icacls.exe",Q(maintenance)+" /inheritance:r /grant:r *S-1-5-18:(OI)(CI)F *S-1-5-32-544:(OI)(CI)F *S-1-5-32-545:(OI)(CI)RX")!=0)throw new Exception("Cannot secure the uninstaller folder.");
        string uninstaller=Path.Combine(maintenance,"Uninstall.exe");File.Copy(Path.Combine(Home,"Uninstall.exe"),uninstaller,true);
        using(var key=Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\VorixEndpointStandalone")) {
            key.SetValue("DisplayName","Vorix Endpoint Standalone");key.SetValue("DisplayVersion","1.2.1");key.SetValue("Publisher","Vorix");key.SetValue("InstallLocation",Home);key.SetValue("DisplayIcon",exe);key.SetValue("UninstallString",Q(uninstaller));key.SetValue("NoModify",1,RegistryValueKind.DWord);key.SetValue("NoRepair",1,RegistryValueKind.DWord);
        }
        Configure(ReadOptions());
        InstallSecurityMonitor();
        StartStatusIcon();
        } finally {installGate.ReleaseMutex();}
        }
        if(!quiet)MessageBox.Show("Vorix Endpoint installed. Definitions update daily at 12:00 local time and after a missed schedule. The first download is running.\n\nOpen Vorix Endpoint from the Start menu.","Vorix Endpoint");
    }
    static int UpdateDefinitions() {
        if(!Admin) { using(var p=Elevate("--update")) { p.WaitForExit(); return p.ExitCode; } }
        if(!File.Exists(Path.Combine(Data,"freshclam.conf"))) throw new Exception("Install Vorix Endpoint first.");
        FileStream updateLock;
        try{updateLock=new FileStream(Path.Combine(Data,"definitions-update.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
        catch(IOException){return 10;}
        using(updateLock) {
            File.WriteAllText(Path.Combine(Data,"update-status.txt"),DateTime.Now.ToString("s")+" | Definition update in progress…");
            using(var p=Process.Start(new ProcessStartInfo(Path.Combine(Home,@"clamav\freshclam.exe"),"--config-file="+Q(Path.Combine(Data,"freshclam.conf"))){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true})) {
                var output=p.StandardOutput.ReadToEndAsync();var errors=p.StandardError.ReadToEndAsync();
                if(!p.WaitForExit(45*60*1000)){p.Kill();throw new Exception("Definition update timed out. Check the internet connection and FreshClam log.");}
                string details=output.Result+Environment.NewLine+errors.Result;
                File.WriteAllText(Path.Combine(Data,@"logs\update-last-output.log"),details);
                int rc=p.ExitCode;File.WriteAllText(Path.Combine(Data,"update-status.txt"),DateTime.Now.ToString("s")+" | "+(rc==0?"Definitions are up to date.":"Update failed (code "+rc+"). Open update log for details."));return rc;
            }
        }
    }
    static Image Logo() { using(var input=Image.FromFile(Path.Combine(Root,"vorix-logo-transparent.png"))) { var b=new Bitmap(900,550); using(var g=Graphics.FromImage(b)) g.DrawImage(input,new Rectangle(0,0,900,550),new Rectangle(424,190,910,550),GraphicsUnit.Pixel); return b; } }
    static Color Navy=Color.FromArgb(20,32,52), Accent=Color.FromArgb(143,176,196);
    static Label TextAt(Control parent,string text,int x,int y,int width,int height,float size,Color color) { var l=new Label{Text=text,Left=x,Top=y,Width=width,Height=height,ForeColor=color,Font=new Font("Segoe UI",size)}; parent.Controls.Add(l); return l; }
    Button ButtonAt(string text,int x,int y,int width,EventHandler action) { var b=new Button{Text=text,Left=x,Top=y,Width=width,Height=42,FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(35,56,80),ForeColor=Color.White,Font=new Font("Segoe UI",10)}; b.Click+=action; Controls.Add(b); return b; }
    Vorix() {
        Text="Vorix Endpoint — Standalone"; Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath); ClientSize=new Size(920,725); MinimumSize=MaximumSize=Size; StartPosition=FormStartPosition.CenterScreen; BackColor=Navy; Font=new Font("Segoe UI",10);
        Controls.Add(new PictureBox{Image=Logo(),Left=28,Top=20,Width=132,Height=80,SizeMode=PictureBoxSizeMode.Zoom});
        TextAt(this,"Endpoint Protection",186,25,495,40,22,Color.White);
        TextAt(this,"Windows Standalone  •  Powered by ClamAV",188,70,495,30,11,Accent);
        TextAt(this,"Administrator: "+(Admin?"Yes":"No — limited file access"),32,101,470,22,9,Admin?Color.LightGreen:Color.Salmon);
        status=TextAt(this,"Checking definitions…",32,125,850,64,11,Color.White);
        install=ButtonAt("Install / repair",32,204,155,async delegate { await ElevatedAction("--install"); });
        update=ButtonAt("Update definitions",199,204,190,async delegate { await ElevatedAction("--update"); });
        scan=ButtonAt("Scan folder",401,204,150,delegate { using(var d=new FolderBrowserDialog()) if(d.ShowDialog()==DialogResult.OK) StartScan(new[]{d.SelectedPath}); });
        ButtonAt("Scan file",563,204,145,delegate { using(var d=new OpenFileDialog()) if(d.ShowDialog()==DialogResult.OK) StartScan(new[]{d.FileName}); });
        full=ButtonAt("Full scan",720,204,168,delegate { var drives=new System.Collections.Generic.List<string>(); foreach(var d in DriveInfo.GetDrives()) if(d.IsReady && (d.DriveType==DriveType.Fixed||d.DriveType==DriveType.Removable)) drives.Add(d.RootDirectory.FullName); if(drives.Count>0) StartScan(drives.ToArray()); });
        output=new TextBox{Left=32,Top=264,Width=856,Height=282,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,BackColor=Color.FromArgb(12,23,39),ForeColor=Color.FromArgb(213,226,238),Font=new Font("Consolas",9)}; Controls.Add(output);
        ButtonAt("Open scan logs",32,562,170,delegate { string p=LogDir(); Directory.CreateDirectory(p); Process.Start("explorer.exe",Q(p)); });
        ButtonAt("Review quarantine",370,562,195,delegate{ReviewQuarantine();});
        ButtonAt("Stop scan",214,562,145,delegate { if(worker!=null && !worker.HasExited) { worker.Kill(); Append("Scan cancelled — coverage incomplete."); } });
        ButtonAt("Security alerts",578,562,310,delegate{ShowSecurityAlerts();});
        ButtonAt("Check administrator",32,662,190,delegate {MessageBox.Show(Admin?"This application is running as administrator.":"This application is running without administrator access. Use Run as administrator for protected-file scans.","Administrator check");});
        ButtonAt("Run as administrator",234,662,210,delegate {if(Admin){MessageBox.Show("Already running as administrator.");return;}if(busy){MessageBox.Show("Finish or stop the current scan first.");return;}try{Elevate("");Close();}catch(Exception ex){Append(ex.Message);}});
        ButtonAt("Uninstall",456,662,160,delegate {if(busy){MessageBox.Show("Finish or stop the current scan first.");return;}string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),@"Vorix Standalone Maintenance\Uninstall.exe");if(!File.Exists(path)){MessageBox.Show("Install or repair this version first to register its uninstaller.");return;}try{Process.Start(new ProcessStartInfo(path){UseShellExecute=true,Verb="runas"});Close();}catch(Exception ex){Append(ex.Message);}});
        ButtonAt("Open update log",628,662,250,delegate {string path=Path.Combine(Data,@"logs\update-last-output.log");if(!File.Exists(path))path=Path.Combine(Data,@"logs\freshclam.log");if(File.Exists(path))Process.Start(new ProcessStartInfo(path){UseShellExecute=true});else MessageBox.Show("No definition update log is available yet.");});
        TextAt(this,"© "+DateTime.Now.Year+" Vorix  •  Version "+Version+"  |  ClamAV © Cisco Systems, Inc. and contributors",32,623,870,25,9,Accent);
        AutomationUI();
        Shown+=delegate { RefreshStatus(); };
        var statusTimer=new System.Windows.Forms.Timer{Interval=5000};statusTimer.Tick+=delegate{try{RefreshStatus();}catch(IOException){}};statusTimer.Start();FormClosed+=delegate{statusTimer.Dispose();};
        FormClosing+=delegate(object sender,FormClosingEventArgs e) { if(busy) { MessageBox.Show("Stop the running scan or wait for it to finish before closing."); e.Cancel=true; } };
    }
    string LogDir() { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),@"Vorix Endpoint\logs"); }
    void Append(string text) { if(IsDisposed) return; if(InvokeRequired) { BeginInvoke(new Action<string>(Append),text); return; } if(output.TextLength>180000) output.Text=output.Text.Substring(output.TextLength-90000); output.AppendText(text+Environment.NewLine); }
    void RefreshStatus() {
        bool installed=File.Exists(Path.Combine(Data,"freshclam.conf"));
        string[] files=Directory.Exists(Db)?Directory.GetFiles(Db,"*.c?d"):new string[0];
        DateTime newest=DateTime.MinValue; foreach(string f in files) if(File.GetLastWriteTime(f)>newest) newest=File.GetLastWriteTime(f);
        status.Text=(!installed?"Install once to enable daily official definition updates.":newest==DateTime.MinValue?"Definitions not ready. The initial download may take several minutes.":"Definitions on disk: "+newest.ToString("dd MMM yyyy HH:mm")+"  •  Daily updates at 12:00 local time")+"\n"+(File.Exists(Path.Combine(Data,"update-status.txt"))?File.ReadAllText(Path.Combine(Data,"update-status.txt")):"No completed update recorded.");
    }
    async Task ElevatedAction(string args) {
        if(busy){MessageBox.Show("Wait for the current scan or update to finish.");return;}
        if(args=="--update"&&!File.Exists(Path.Combine(Data,"freshclam.conf"))){Append("Install Vorix Endpoint before updating definitions.");return;}
        busy=true;
        try{using(var p=Elevate(args)){await Task.Run(()=>p.WaitForExit());
            if(args=="--update") {
                if(p.ExitCode==0)Append("Definitions are up to date.");
                else if(p.ExitCode==10)Append("A definition update is already running. Please wait; this is not an update failure.");
                else {Append("Definition update failed (code "+p.ExitCode+").");string path=Path.Combine(Data,@"logs\update-last-output.log");if(File.Exists(path)){string detail=File.ReadAllText(path);Append(detail.Length>3000?detail.Substring(detail.Length-3000):detail);}}
            }else Append(p.ExitCode==0?"Installation completed.":"Installation stopped; review the setup message.");
        }}catch(Exception ex){Append(ex.Message);}finally{busy=false;RefreshStatus();}
    }
    async void StartScan(string[] roots) {
        if(busy) { MessageBox.Show("A scan or update is already running."); return; }
        if(!Admin){string elevate="--scan-path";foreach(string root in roots)elevate+=" "+Q(root);try{Elevate(elevate);Close();}catch(Exception ex){Append(ex.Message);}return;}
        if(!Directory.Exists(Db)||Directory.GetFiles(Db,"*.c?d").Length==0) { MessageBox.Show("Install and update definitions before scanning."); return; }
        var scanLock=ScanMutex();if(!TakeScan(scanLock)){scanLock.Dispose();Append("A scheduled scan or app upgrade is already running.");return;}
        busy=true; string log=null;
        try {
            var quarantine=new QuarantineSession();
            Directory.CreateDirectory(LogDir()); log=Path.Combine(LogDir(),"scan-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6)+".txt");
            string args="--database="+Q(Db)+" --recursive --verbose --follow-dir-symlinks=0 --follow-file-symlinks=0 --cross-fs=no --log="+Q(log)+" --exclude-dir="+Q("^"+System.Text.RegularExpressions.Regex.Escape(Data))+" --";
            args=quarantine.Arguments+" "+args;
            foreach(string root in roots) args+=" "+Q(root);
            Append("Starting ClamAV scan. Access errors and engine limits mean coverage may be incomplete.");
            worker=new Process{StartInfo=new ProcessStartInfo(Path.Combine(Root,@"clamav\clamscan.exe"),args){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}};
            worker.OutputDataReceived+=delegate(object s,DataReceivedEventArgs e) { if(e.Data!=null && !e.Data.EndsWith(": OK")) Append(quarantine.Capture(e.Data)); };
            worker.ErrorDataReceived+=delegate(object s,DataReceivedEventArgs e) { if(e.Data!=null) Append(quarantine.Capture(e.Data)); };
            worker.Start(); worker.BeginOutputReadLine(); worker.BeginErrorReadLine(); await Task.Run(()=>worker.WaitForExit());
            Append(worker.ExitCode==0?"Scan finished. No threats reported; review the log for skipped files and limits.":worker.ExitCode==1?"Threats detected. Review quarantine and the log for isolation failures.":"Scan did not complete successfully (code "+worker.ExitCode+"). Review errors."); Append("Log: "+log);
        } catch(Exception ex) { Append("Scan failed: "+ex.Message); } finally { if(worker!=null) worker.Dispose(); worker=null; busy=false; scanLock.ReleaseMutex();scanLock.Dispose(); }
    }
    [STAThread] static int Main(string[] args) {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        try {
            if(args.Length>0&&(args[0]=="--scan-path"||args[0]=="--review-quarantine")){using(var f=new Vorix()){f.Shown+=delegate{if(args[0]=="--review-quarantine")f.ReviewQuarantine();else{var paths=new string[args.Length-1];Array.Copy(args,1,paths,0,paths.Length);f.StartScan(paths);}};Application.Run(f);}return 0;}
            if(args.Length>0 && args[0]=="--security-alerts"){ShowSecurityAlerts();return 0;}
            int automationResult;if(AutomationCommand(args,out automationResult))return automationResult;
            if(args.Length>0 && args[0]=="--install") { Install(); return 0; }
            if(args.Length>0 && args[0]=="--install-quiet") { Install(true); return 0; }
            if(args.Length>0 && args[0]=="--install-no-update") { Install(true,false); return 0; }
            if(args.Length>0 && args[0]=="--update") return UpdateDefinitions();
            if(args.Length>0 && args[0]=="--render-preview") {
                using(var form=new Vorix()) { form.Show(); Application.DoEvents(); form.RefreshStatus(); using(var bitmap=new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height)); bitmap.Save(Path.Combine(Root,"preview.png")); } form.Close(); }
                return 0;
            }
            if(args.Length>0 && args[0]=="--version") { File.WriteAllText(Path.Combine(Root,"version-check.txt"),Version); return 0; }
            using(var splash=new Form{ClientSize=new Size(480,310),FormBorderStyle=FormBorderStyle.None,StartPosition=FormStartPosition.CenterScreen,BackColor=Navy}) {
                splash.Controls.Add(new PictureBox{Image=Logo(),Left=145,Top=30,Width=190,Height=115,SizeMode=PictureBoxSizeMode.Zoom});
                TextAt(splash,"Endpoint Protection",90,170,340,35,21,Color.White); TextAt(splash,"Powered by ClamAV",157,218,300,28,11,Accent); TextAt(splash,"© "+DateTime.Now.Year+" Vorix  •  "+Version,118,273,330,24,9,Accent);
                var timer=new System.Windows.Forms.Timer{Interval=1600}; timer.Tick+=delegate { timer.Stop(); splash.Close(); }; splash.Shown+=delegate { timer.Start(); }; splash.ShowDialog(); timer.Dispose();
            }
            Application.Run(new Vorix()); return 0;
        } catch(Exception ex) { if(args.Length==0 || args[0]=="--install") MessageBox.Show(ex.Message,"Vorix Endpoint");else Console.Error.WriteLine(ex.Message); return 2; }
    }
}



