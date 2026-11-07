using System;
using System.IO;
using System.IO.Compression;
using System.Drawing;
using System.Diagnostics;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using Microsoft.Win32;
class Setup : Form {
    static bool Managed {
        get {
#if MANAGED
            return true;
#else
            return false;
#endif
        }
    }
    TextBox token,name,log;Button install,remove;Label installedStatus;bool working;
    static bool Admin {get{return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);}}
    static bool StandaloneInstalled {
        get {using(var key=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\VorixEndpointStandalone"))return key!=null||File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),@"Vorix Endpoint\Vorix Endpoint.exe"));}
    }
    static void Secure(string path) {
        Directory.CreateDirectory(path);if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new Exception("Linked setup folders are not allowed.");var acl=new DirectorySecurity();acl.SetAccessRuleProtection(true,false);
        foreach(string sid in new[]{"S-1-5-18","S-1-5-32-544"})acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid),FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
        acl.SetOwner(new SecurityIdentifier("S-1-5-32-544"));new DirectoryInfo(path).SetAccessControl(acl);
    }
    static void Extract(string destination) {
        string prefix=Path.GetFullPath(destination).TrimEnd('\\')+"\\";
        using(var input=Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
        using(var zip=new ZipArchive(input,ZipArchiveMode.Read))foreach(var entry in zip.Entries) {
            string target=Path.GetFullPath(Path.Combine(destination,entry.FullName.Replace('/','\\')));
            if(!target.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)||entry.FullName.IndexOf(':')>=0)throw new Exception("Invalid installer payload path.");
            if(entry.FullName.EndsWith("/")){Directory.CreateDirectory(target);continue;}
            Directory.CreateDirectory(Path.GetDirectoryName(target));using(var source=entry.Open())using(var output=File.Create(target))source.CopyTo(output);
        }
    }
    Setup() {
        Text=Managed?"Vorix Managed Agent Setup":"Vorix Standalone Setup";Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);ClientSize=new Size(650,580);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;BackColor=Color.FromArgb(20,32,52);ForeColor=Color.White;Font=new Font("Segoe UI",10);
        using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("logo.png"))using(var source=Image.FromStream(s)){var image=new Bitmap(900,550);using(var g=Graphics.FromImage(image))g.DrawImage(source,new Rectangle(0,0,900,550),new Rectangle(424,190,910,550),GraphicsUnit.Pixel);Controls.Add(new PictureBox{Left=22,Top=12,Width=125,Height=85,Image=image,SizeMode=PictureBoxSizeMode.Zoom});}
        Label heading=new Label{Left=168,Top=25,Width=450,Height=70,Text=(Managed?"Managed Endpoint Agent":"Endpoint Protection")+"\nPowered by ClamAV",Font=new Font("Segoe UI",16)};Controls.Add(heading);
        Controls.Add(new Label{Left=24,Top=110,Width=600,Height=28,ForeColor=Admin?Color.LightGreen:Color.Salmon,Text="Administrator: "+(Admin?"Yes":"No")+"  •  Windows x64"});
        var check=new Button{Left=24,Top=142,Width=210,Height=34,Text="Check administrator"};check.Click+=delegate{MessageBox.Show(Admin?"Administrator access is active.":"Run this installer as administrator.");};Controls.Add(check);
        name=new TextBox{Left=24,Top=211,Width=600,Text=Environment.MachineName};token=new TextBox{Left=24,Top=279,Width=600,UseSystemPasswordChar=true,MaxLength=100};
        if(Managed) {
            Controls.Add(new Label{Left=24,Top=185,Width=600,Text="Device name — server: https://console.example.invalid"});Controls.Add(name);
            Controls.Add(new Label{Left=24,Top=252,Width=600,Text="One-use enrollment token (30 minutes); leave blank for an enrolled PC"});Controls.Add(token);
        } else Controls.Add(new Label{Left=24,Top=195,Width=600,Height=95,Text="Standalone antivirus scans and daily official definition updates.\nIncludes Installed Apps removal and administrator controls.\nKeep Microsoft Defender enabled for real-time protection."});
        Controls.Add(new Label{Left=24,Top=325,Width=600,Height=55,Text=Managed?"New PCs start with USB storage blocked. Close files on USB drives first.\nPython and ClamAV are bundled; no separate Python installation is needed.":"Installs to Program Files. Internet is needed for definition updates.\nUninstall completely removes standalone files, definitions, settings and logs."});
        install=new Button{Left=24,Top=389,Width=200,Height=40,Text="Install / repair"};install.Click+=async delegate{await Install();};Controls.Add(install);
        if(!Managed) {
            installedStatus=new Label{Left=24,Top=294,Width=600,Height=24,Text=StandaloneInstalled?"Standalone installation detected":"Standalone is not installed on this PC"};Controls.Add(installedStatus);
            remove=new Button{Left=240,Top=389,Width=200,Height=40,Text="Uninstall",Enabled=StandaloneInstalled};remove.Click+=async delegate{await UninstallStandalone();};Controls.Add(remove);
        }
        log=new TextBox{Left=24,Top=442,Width=600,Height=84,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical};Controls.Add(log);
        Controls.Add(new Label{Left=24,Top=546,Width=600,Height=24,Text="© "+DateTime.Now.Year+" Vorix  •  "+(Managed?"Agent package 0.7.0":"Standalone 1.2.0")+" (Pilot)"});
        FormClosing+=delegate(object sender,FormClosingEventArgs e){if(working){e.Cancel=true;MessageBox.Show("Wait for setup to finish before closing.");}};
    }
    async Task Install() {
        if(working)return;if(!Admin){MessageBox.Show("Administrator access is required.");return;}
        working=true;install.Enabled=false;if(remove!=null)remove.Enabled=false;string stage=null;
        try {
            string json=new JavaScriptSerializer().Serialize(new {name=name.Text,token=token.Text});token.Clear();
            log.Text="Preparing the bundled installer…";
            stage=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Vorix Setup","stage-"+Guid.NewGuid().ToString("N"));
            Secure(Path.GetDirectoryName(stage));Secure(stage);await Task.Run(()=>Extract(stage));
            var start=Managed?new ProcessStartInfo(Path.Combine(stage,@"python\python.exe"),"-I \""+Path.Combine(stage,"setup.py")+"\""):new ProcessStartInfo(Path.Combine(stage,"Vorix Endpoint.exe"),"--install");
            start.UseShellExecute=false;start.CreateNoWindow=true;start.RedirectStandardInput=Managed;start.RedirectStandardOutput=true;start.RedirectStandardError=true;start.WorkingDirectory=stage;
            using(var p=Process.Start(start)) {
                if(Managed){p.StandardInput.Write(json);p.StandardInput.Close();}json=null;
                Task<string> output=p.StandardOutput.ReadToEndAsync(), errors=p.StandardError.ReadToEndAsync();await Task.Run(()=>p.WaitForExit());
                string result=(await output)+(await errors);log.Text=result.Length>8000?result.Substring(result.Length-8000):result;
                if(p.ExitCode!=0)throw new Exception("Setup stopped. Review the details above and retry after correcting the issue.");
            }
            MessageBox.Show(Managed?"Managed agent installed. Check this PC in the HO dashboard. Initial definitions download in the background.":"Standalone installed. Open Vorix Endpoint from the Start menu.","Vorix Setup");
        }catch(Exception ex){MessageBox.Show(ex.Message,"Vorix Setup");}
        finally {working=false;install.Enabled=true;RefreshInstalled();if(stage!=null)try{Directory.Delete(stage,true);}catch{} }
    }
    void RefreshInstalled() {
        if(Managed)return;
        bool installed=StandaloneInstalled;remove.Enabled=!working&&installed;installedStatus.Text=installed?"Standalone installation detected":"Standalone is not installed on this PC";
    }
    async Task UninstallStandalone() {
        if(Managed||working)return;
        if(!Admin){MessageBox.Show("Administrator access is required before uninstalling.");return;}
        if(!StandaloneInstalled){log.Text="Standalone is not installed on this PC.";RefreshInstalled();return;}
        working=true;install.Enabled=false;remove.Enabled=false;string stage=null;
        try {
            log.Text="Preparing the standalone uninstaller. Close the standalone app before continuing.";
            stage=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Vorix Setup","remove-"+Guid.NewGuid().ToString("N"));Secure(Path.GetDirectoryName(stage));Secure(stage);
            // Extract only the bundled uninstaller. This also supports older
            // standalone installations that did not register an uninstaller.
            using(var input=Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))using(var zip=new ZipArchive(input,ZipArchiveMode.Read)) {
                var entry=zip.GetEntry("Uninstall.exe");if(entry==null)throw new Exception("Bundled uninstaller is missing.");
                using(var source=entry.Open())using(var output=File.Create(Path.Combine(stage,"Uninstall.exe")))source.CopyTo(output);
            }
            using(var process=Process.Start(new ProcessStartInfo(Path.Combine(stage,"Uninstall.exe"),"--from-setup"){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=stage,RedirectStandardError=true,RedirectStandardOutput=true})) {
                var errors=process.StandardError.ReadToEndAsync();var output=process.StandardOutput.ReadToEndAsync();await Task.Run(()=>process.WaitForExit());
                string detail=((await errors)+Environment.NewLine+(await output)).Trim();
                if(process.ExitCode!=0){log.Text=detail.Length>0?detail:"Uninstaller exited with code "+process.ExitCode;throw new Exception(log.Text);}
            }
            log.Text=StandaloneInstalled?"Uninstall cancelled; the standalone installation remains.":"Standalone completely removed: program files, definitions, settings and logs. Final maintenance cleanup completes automatically.";
        } catch(Exception ex){MessageBox.Show(ex.Message,"Vorix Setup");}
        finally {working=false;install.Enabled=true;RefreshInstalled();if(stage!=null)try{Directory.Delete(stage,true);}catch{} }
    }
    [STAThread] static int Main(string[] args) {
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        if(args.Length>0&&args[0]=="--auto-upgrade")return AutoUpgrade(args);
        if(!Admin){MessageBox.Show("Run this installer as administrator.","Vorix Setup");return 1;}
        if(args.Length==2&&args[0]=="--verify-payload") {Secure(args[1]);Extract(args[1]);return 0;}
        Application.Run(new Setup());return 0;
    }
    static int AutoUpgrade(string[] args) {
        string stage=null;
        string data=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Vorix Endpoint");
        try {
            int pid;if(Managed||!Admin||args.Length!=3||args[1]!="--wait-for-process"||!int.TryParse(args[2],out pid)||pid<=0)throw new Exception("Invalid automatic-upgrade request.");
            try{using(var parent=Process.GetProcessById(pid)){if(!parent.WaitForExit(120000))throw new Exception("The update worker did not exit.");}}catch(ArgumentException){}
            stage=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Vorix Setup","stage-"+Guid.NewGuid().ToString("N"));Secure(Path.GetDirectoryName(stage));Secure(stage);Extract(stage);
            using(var p=Process.Start(new ProcessStartInfo(Path.Combine(stage,"Vorix Endpoint.exe"),"--install-no-update"){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=stage,RedirectStandardError=true,RedirectStandardOutput=true})) {
                var output=p.StandardOutput.ReadToEndAsync();var errors=p.StandardError.ReadToEndAsync();if(!p.WaitForExit(600000))throw new Exception("App upgrade timed out.");
                if(p.ExitCode!=0)throw new Exception("App upgrade failed: "+errors.Result+output.Result);
            }
            File.WriteAllText(Path.Combine(data,"app-update-status.json"),new JavaScriptSerializer().Serialize(new{state="current",message="App upgrade completed",at=DateTimeOffset.Now.ToUnixTimeSeconds()}));return 0;
        }catch(Exception ex){try{File.WriteAllText(Path.Combine(data,"app-update-status.json"),new JavaScriptSerializer().Serialize(new{state="error",message=ex.Message,at=DateTimeOffset.Now.ToUnixTimeSeconds()}));}catch{}return 2;}
        finally{if(stage!=null)try{Directory.Delete(stage,true);}catch{}}
    }
}


