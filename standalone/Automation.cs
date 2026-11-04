using System;
using System.IO;
using System.Net;
using System.Text;
using System.Drawing;
using System.Diagnostics;
using System.Collections.Generic;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;

partial class Vorix {
    const string Repository="rajasngit/Vorix-Endpoint-Standalone";
    public class Options {
        public bool ScanEnabled=true;
        public int ScanDay=0;
        public string ScanTime="02:00";
        public bool AppChecksEnabled=true;
        public string UpgradeMode="prompt";
    }
    static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=1048576};
    static string OptionsPath {get{return Path.Combine(Data,"automation.json");}}
    static string PrivatePath {get{return Path.Combine(Data,"private");}}
    static string AppStatusPath {get{return Path.Combine(Data,"app-update-status.json");}}
    static Options ReadOptions() {var o=File.Exists(OptionsPath)?Json.Deserialize<Options>(File.ReadAllText(OptionsPath)):new Options();ValidateOptions(o);return o;}
    static void ValidateOptions(Options o) {
        DateTime time;if(o==null||o.ScanDay<0||o.ScanDay>6||!DateTime.TryParseExact(o.ScanTime,"HH:mm",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out time)||!(o.UpgradeMode=="prompt"||o.UpgradeMode=="auto"))throw new Exception("Invalid schedule or app-update setting.");
    }
    static void Report(string file,object value) {string tmp=file+".tmp";File.WriteAllText(tmp,Json.Serialize(value));if(File.Exists(file))File.Delete(file);File.Move(tmp,file);}
    static void SecurePrivate() {
        Directory.CreateDirectory(PrivatePath);if((File.GetAttributes(PrivatePath)&FileAttributes.ReparsePoint)!=0)throw new Exception("Linked private folder refused.");
        var acl=new DirectorySecurity();acl.SetAccessRuleProtection(true,false);
        foreach(string sid in new[]{"S-1-5-18","S-1-5-32-544"})acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid),FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
        new DirectoryInfo(PrivatePath).SetAccessControl(acl);
    }
    static Mutex ScanMutex() {
        var acl=new MutexSecurity();foreach(string sid in new[]{"S-1-5-18","S-1-5-32-544"})acl.AddAccessRule(new MutexAccessRule(new SecurityIdentifier(sid),MutexRights.FullControl,AccessControlType.Allow));
        acl.AddAccessRule(new MutexAccessRule(new SecurityIdentifier("S-1-5-32-545"),MutexRights.Synchronize|MutexRights.Modify,AccessControlType.Allow));bool created;
        return new Mutex(false,@"Global\VorixStandaloneScan",out created,acl);
    }
    static bool TakeScan(Mutex mutex) {try{return mutex.WaitOne(0);}catch(AbandonedMutexException){return true;}}
    static void Configure(Options o) {
        if(!Admin)throw new Exception("Administrator access is required to change scheduled tasks.");ValidateOptions(o);
        Directory.CreateDirectory(Data);SecurePrivate();File.WriteAllText(OptionsPath,Json.Serialize(o));
        string exe=Path.Combine(Home,"Vorix Endpoint.exe");
        string common="$p=New-ScheduledTaskPrincipal -UserId SYSTEM -LogonType ServiceAccount -RunLevel Highest; $s=New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Hours 24); ";
        string code=common+"$a=New-ScheduledTaskAction -Execute "+PSQ(exe)+" -Argument '--silent-full-scan'; $t=New-ScheduledTaskTrigger -Weekly -WeeksInterval 1 -DaysOfWeek "+((DayOfWeek)o.ScanDay)+" -At "+PSQ(o.ScanTime)+"; Register-ScheduledTask -TaskName VorixStandaloneFullScan -Action $a -Trigger $t -Principal $p -Settings $s -Force | Out-Null; "+(o.ScanEnabled?"Enable-ScheduledTask":"Disable-ScheduledTask")+" -TaskName VorixStandaloneFullScan | Out-Null; ";
        code+="$a=New-ScheduledTaskAction -Execute "+PSQ(exe)+" -Argument '--scheduled-app-check'; $t=New-ScheduledTaskTrigger -Daily -At '14:00'; $r=New-CimInstance -ClassName MSFT_TaskRepetitionPattern -Namespace Root/Microsoft/Windows/TaskScheduler -ClientOnly -Property @{Interval='PT1H';Duration='P1D'}; $t.Repetition=$r; $s=New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes 45); Register-ScheduledTask -TaskName VorixStandaloneAppUpdates -Action $a -Trigger $t -Principal $p -Settings $s -Force | Out-Null; "+(o.AppChecksEnabled?"Enable-ScheduledTask":"Disable-ScheduledTask")+" -TaskName VorixStandaloneAppUpdates | Out-Null";
        PS(code);
    }
    static int SilentFullScan() {
        if(!Admin)throw new Exception("Scheduled full scans require administrator access.");if(!ReadOptions().ScanEnabled)return 0;
        SecurePrivate();using(var mutex=ScanMutex()) {
            if(!TakeScan(mutex))return 10;
            try {
                string folder=Path.Combine(PrivatePath,"scan-logs");Directory.CreateDirectory(folder);
                string log=Path.Combine(folder,"scheduled-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".log");
                string report=Path.Combine(Data,"scheduled-scan-status.json");Report(report,new{state="running",at=DateTimeOffset.Now.ToUnixTimeSeconds(),message="Silent full-system scan running"});
                var quarantine=new QuarantineSession();
                string args=quarantine.Arguments+" --database="+Q(Db)+" --recursive --follow-dir-symlinks=0 --follow-file-symlinks=0 --cross-fs=no --exclude-dir="+Q("^"+System.Text.RegularExpressions.Regex.Escape(Data))+" --exclude="+Q(@"^[A-Za-z]:[\\/](pagefile|swapfile|hiberfil)\.sys$")+" --log="+Q(log)+" --";
                int roots=0;foreach(var drive in DriveInfo.GetDrives())if(drive.IsReady&&drive.DriveType==DriveType.Fixed){args+=" "+Q(drive.RootDirectory.FullName);roots++;}if(roots==0)throw new Exception("No ready local fixed drives found.");
                var tail=new StringBuilder();object gate=new object();
                using(var p=new Process{StartInfo=new ProcessStartInfo(Path.Combine(Home,@"clamav\clamscan.exe"),args){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}}) {
                    System.Diagnostics.DataReceivedEventHandler capture=delegate(object sender,DataReceivedEventArgs e){if(e.Data==null||e.Data.EndsWith(": OK"))return;lock(gate){tail.AppendLine(quarantine.Capture(e.Data));if(tail.Length>16000)tail.Remove(0,tail.Length-8000);}};
                    p.OutputDataReceived+=capture;p.ErrorDataReceived+=capture;p.Start();try{p.PriorityClass=ProcessPriorityClass.BelowNormal;}catch{}p.BeginOutputReadLine();p.BeginErrorReadLine();
                    bool finished=p.WaitForExit(24*60*60*1000);if(!finished){p.Kill();p.WaitForExit();}else p.WaitForExit();
                    int rc=finished?p.ExitCode:2;string message=!finished?"Scan timed out; coverage incomplete":rc==0?"Scan completed; no detections reported":rc==1?"Scan completed; threats reported â€” review private scan logs":"Scan incomplete; review private scan errors";
                    File.WriteAllText(log+".summary.txt",tail.ToString());Report(report,new{state=rc<2?"completed":"incomplete",at=DateTimeOffset.Now.ToUnixTimeSeconds(),exit_code=rc,message=message});
                    var old=new DirectoryInfo(folder).GetFiles("scheduled-*.log");Array.Sort(old,(a,b)=>b.LastWriteTime.CompareTo(a.LastWriteTime));for(int i=8;i<old.Length;i++){old[i].Delete();if(File.Exists(old[i].FullName+".summary.txt"))File.Delete(old[i].FullName+".summary.txt");}return rc;
                }
            }finally{mutex.ReleaseMutex();}
        }
    }
    static byte[] Fetch(string url,int maxBytes) {
        for(int hop=0;hop<6;hop++) {
            var uri=new Uri(url);if(uri.Scheme!="https"||!uri.IsDefaultPort||uri.UserInfo.Length>0||!(uri.Host=="api.github.com"||uri.Host=="github.com"||uri.Host=="release-assets.githubusercontent.com"||uri.Host=="objects.githubusercontent.com"))throw new Exception("Untrusted app-update URL.");
            var request=(HttpWebRequest)WebRequest.Create(uri);request.UserAgent="Vorix-Endpoint-Standalone/"+CurrentVersion();request.AllowAutoRedirect=false;request.Timeout=30000;request.ReadWriteTimeout=30000;
            using(var response=(HttpWebResponse)request.GetResponse()) {
                if((int)response.StatusCode>=300&&(int)response.StatusCode<400){url=new Uri(uri,response.Headers["Location"]).AbsoluteUri;continue;}
                if(response.ContentLength>maxBytes)throw new Exception("Update response is too large.");
                using(var memory=new MemoryStream())using(var stream=response.GetResponseStream()){byte[] buffer=new byte[65536];int n;while((n=stream.Read(buffer,0,buffer.Length))>0){if(memory.Length+n>maxBytes)throw new Exception("Update response exceeded its size limit.");memory.Write(buffer,0,n);}return memory.ToArray();}
            }
        }throw new Exception("Too many update redirects.");
    }
    static string CurrentVersion(){return typeof(Vorix).Assembly.GetName().Version.ToString(3);}
    static Dictionary<string,object> VerifiedManifest(byte[] bytes,byte[] signature) {
        using(var rsa=new RSACryptoServiceProvider()){rsa.PersistKeyInCsp=false;using(var reader=new StreamReader(typeof(Vorix).Assembly.GetManifestResourceStream("update-key.xml")))rsa.FromXmlString(reader.ReadToEnd());if(!rsa.VerifyData(bytes,CryptoConfig.MapNameToOID("SHA256"),signature))throw new Exception("The app-update signature is invalid. No installer will run.");}
        var m=Json.Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(bytes));
        System.Version version;if(Convert.ToString(m["product"])!="Vorix-Endpoint-Standalone"||!System.Version.TryParse(Convert.ToString(m["version"]),out version)||!System.Text.RegularExpressions.Regex.IsMatch(Convert.ToString(m["sha256"]),"^[a-fA-F0-9]{64}$")||Convert.ToString(m["fileName"])!="Vorix-Standalone-Setup-"+version.ToString(3)+"-x64.exe"||Convert.ToInt64(m["sizeBytes"])<=0||Convert.ToInt64(m["sizeBytes"])>512*1024*1024)throw new Exception("Invalid signed update metadata.");return m;
    }
    static Dictionary<string,object> FindAsset(Dictionary<string,object> release,string name) {
        foreach(var raw in (System.Collections.IEnumerable)release["assets"]){var item=(Dictionary<string,object>)raw;if(Convert.ToString(item["name"])==name)return item;}throw new Exception("Release asset missing: "+name);
    }
    static string AssetURL(Dictionary<string,object> asset) {string url=Convert.ToString(asset["browser_download_url"]);if(!url.StartsWith("https://github.com/"+Repository+"/releases/download/",StringComparison.Ordinal))throw new Exception("The release asset is outside the approved repository.");return url;}
    static int CheckAppUpdate(bool apply,bool scheduled) {
        if(!Admin)throw new Exception("Administrator access is required for app updates.");SecurePrivate();
        FileStream updateLock;try{updateLock=new FileStream(Path.Combine(PrivatePath,"app-update.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}catch(IOException){return 10;}
        using(updateLock)return CheckAppUpdateCore(apply,scheduled);
    }
    static int CheckAppUpdateCore(bool apply,bool scheduled) {
        if(!Admin)throw new Exception("Administrator access is required for the app-update worker.");SecurePrivate();var options=ReadOptions();if(scheduled&&!options.AppChecksEnabled)return 0;
        try {
            ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
            var release=Json.Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(Fetch("https://api.github.com/repos/"+Repository+"/releases/latest",1048576)));
            var bytes=Fetch(AssetURL(FindAsset(release,"vorix-update.json")),65536);var signature=Convert.FromBase64String(Encoding.ASCII.GetString(Fetch(AssetURL(FindAsset(release,"vorix-update.sig")),8192)).Trim());var manifest=VerifiedManifest(bytes,signature);
            string version=Convert.ToString(manifest["version"]);if(new System.Version(version)<=new System.Version(CurrentVersion())){Report(AppStatusPath,new{state="current",version=version,message="App is up to date",at=DateTimeOffset.Now.ToUnixTimeSeconds()});return 0;}
            Report(AppStatusPath,new{state="available",version=version,message="App update "+version+" is available",at=DateTimeOffset.Now.ToUnixTimeSeconds()});
            if(!apply&&!(scheduled&&options.UpgradeMode=="auto"))return 0;
            if(apply)Thread.Sleep(2500);
            using(var mutex=ScanMutex()) {
                if(!TakeScan(mutex))return 10;
                try {
                    foreach(var p in Process.GetProcessesByName("Vorix Endpoint"))if(p.Id!=Process.GetCurrentProcess().Id){try{if(p.MainModule.FileName.StartsWith(Home+"\\",StringComparison.OrdinalIgnoreCase)){Report(AppStatusPath,new{state="available",version=version,message="Update deferred until the app and scans are closed",at=DateTimeOffset.Now.ToUnixTimeSeconds()});return 10;}}catch(System.ComponentModel.Win32Exception){}}
                    string cache=Path.Combine(PrivatePath,"updates");Directory.CreateDirectory(cache);string path=Path.Combine(cache,Convert.ToString(manifest["fileName"]));
                    var data=Fetch(AssetURL(FindAsset(release,Convert.ToString(manifest["fileName"]))),512*1024*1024);
                    if(data.LongLength!=Convert.ToInt64(manifest["sizeBytes"]))throw new Exception("Installer size mismatch.");
                    using(var hash=SHA256.Create()){string actual=BitConverter.ToString(hash.ComputeHash(data)).Replace("-","").ToLowerInvariant();if(actual!=Convert.ToString(manifest["sha256"]).ToLowerInvariant())throw new Exception("Installer hash mismatch. No installer will run.");}
                    File.WriteAllBytes(path,data);Report(AppStatusPath,new{state="installing",version=version,message="Verified app upgrade starting",at=DateTimeOffset.Now.ToUnixTimeSeconds()});
                    Process.Start(new ProcessStartInfo(path,"--auto-upgrade --wait-for-process "+Process.GetCurrentProcess().Id){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=cache});return 0;
                }finally{mutex.ReleaseMutex();}
            }
        }catch(WebException ex){var r=ex.Response as HttpWebResponse;string message=r!=null&&r.StatusCode==HttpStatusCode.NotFound?"No published app release is available yet":ex.Message;Report(AppStatusPath,new{state="error",message=message,at=DateTimeOffset.Now.ToUnixTimeSeconds()});return 2;}
        catch(Exception ex){Report(AppStatusPath,new{state="error",message=ex.Message,at=DateTimeOffset.Now.ToUnixTimeSeconds()});return 2;}
    }
    string lastAutomation="",offeredVersion="";
    void RefreshAutomation() {
        var options=ReadOptions();string summary="Silent full scan: "+(options.ScanEnabled?((DayOfWeek)options.ScanDay)+" at "+options.ScanTime:"disabled")+". App upgrades: "+options.UpgradeMode+".";
        foreach(string file in new[]{Path.Combine(Data,"scheduled-scan-status.json"),AppStatusPath})if(File.Exists(file)){var item=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(file));summary+="\n"+Convert.ToString(item["message"]);}
        if(summary!=lastAutomation){lastAutomation=summary;Append(summary);}
        if(!busy&&File.Exists(AppStatusPath)&&options.UpgradeMode=="prompt") {
            var item=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(AppStatusPath));
            if(Convert.ToString(item["state"])=="available"&&Convert.ToString(item["version"])!=offeredVersion){
                offeredVersion=Convert.ToString(item["version"]);
                if(MessageBox.Show("Vorix "+offeredVersion+" is available. Upgrade now? The app will close; settings and definitions are retained.","Vorix app update",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes){Elevate("--apply-app-update");Close();}
            }
        }
    }
    void AutomationUI() {
        var settings=ButtonAt("Settings / schedules",690,78,198,delegate{SettingsDialog();});settings.Height=34;settings.BringToFront();
        var check=ButtonAt("Check app updates",690,27,198,async delegate{await ManualAppCheck();});check.Height=34;check.BringToFront();
        Shown+=delegate{try{RefreshAutomation();}catch{}};var timer=new System.Windows.Forms.Timer{Interval=10000};timer.Tick+=delegate{try{RefreshAutomation();}catch{}};timer.Start();FormClosed+=delegate{timer.Dispose();};
    }
    void SettingsDialog() {
        Options o=ReadOptions();using(var dialog=new Form{Text="Vorix schedules and app updates",ClientSize=new Size(590,410),StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false}) {
            var scanCheck=new CheckBox{Left=22,Top=24,Width=540,Text="Enable silent weekly full-system scan",Checked=o.ScanEnabled};dialog.Controls.Add(scanCheck);
            var day=new ComboBox{Left=22,Top=65,Width=200,DropDownStyle=ComboBoxStyle.DropDownList};day.Items.AddRange(Enum.GetNames(typeof(DayOfWeek)));day.SelectedIndex=o.ScanDay;dialog.Controls.Add(day);
            var time=new DateTimePicker{Left=242,Top=65,Width=130,Format=DateTimePickerFormat.Custom,CustomFormat="HH:mm",ShowUpDown=true,Value=DateTime.Today.Add(TimeSpan.Parse(o.ScanTime))};dialog.Controls.Add(time);
            dialog.Controls.Add(new Label{Left=22,Top=108,Width=540,Height=45,Text="Runs silently as SYSTEM, even while signed out. Missed scans run when\nthe PC is available. Results appear in the app; detections move into quarantine."});
            var checks=new CheckBox{Left=22,Top=170,Width=540,Text="Periodically check GitHub for app updates (hourly)",Checked=o.AppChecksEnabled};dialog.Controls.Add(checks);
            var mode=new ComboBox{Left=22,Top=210,Width=540,DropDownStyle=ComboBoxStyle.DropDownList};mode.Items.AddRange(new object[]{"Prompt me before installing an app update","Automatically upgrade when the app/scanner is idle"});mode.SelectedIndex=o.UpgradeMode=="auto"?1:0;dialog.Controls.Add(mode);
            dialog.Controls.Add(new Label{Left=22,Top=254,Width=540,Height=55,Text="Only releases with the pinned publisher signature and matching SHA-256\nare accepted. Scan/definition schedules are separate from app upgrades."});
            var save=new Button{Left=22,Top=333,Width=180,Height=38,Text="Save settings"};dialog.Controls.Add(save);save.Click+=async delegate{save.Enabled=false;try{string args="--configure-automation "+(scanCheck.Checked?"1":"0")+" "+day.SelectedIndex+" "+time.Value.ToString("HH:mm")+" "+(checks.Checked?"1":"0")+" "+(mode.SelectedIndex==1?"auto":"prompt");using(var p=Elevate(args)){await Task.Run(()=>p.WaitForExit());if(p.ExitCode!=0)throw new Exception("Settings were not saved.");}RefreshAutomation();dialog.Close();}catch(Exception ex){MessageBox.Show(ex.Message);}finally{save.Enabled=true;}};dialog.ShowDialog(this);
        }
    }
    async Task ManualAppCheck() {
        if(busy){MessageBox.Show("Wait for the current scan/update to finish.");return;}if(!File.Exists(OptionsPath)){Append("Install or repair this version to enable app-update checks.");return;}
        busy=true;try{using(var p=Elevate("--check-app-updates")){await Task.Run(()=>p.WaitForExit());}RefreshAutomation();var item=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(AppStatusPath));if(Convert.ToString(item["state"])=="available"&&MessageBox.Show(Convert.ToString(item["message"])+". Install now? The app will close; your definitions/settings will be retained.","Vorix app update",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes){busy=false;Elevate("--apply-app-update");Close();}}catch(Exception ex){Append(ex.Message);}finally{busy=false;}
    }
    static bool AutomationCommand(string[] args,out int result) {
        result=0;if(args.Length==0)return false;
        switch(args[0]) {
            case "--silent-full-scan":result=SilentFullScan();return true;
            case "--check-app-updates":result=CheckAppUpdate(false,false);return true;
            case "--scheduled-app-check":result=CheckAppUpdate(false,true);return true;
            case "--apply-app-update":result=CheckAppUpdate(true,false);return true;
            case "--configure-automation":if(args.Length!=6||(args[1]!="0"&&args[1]!="1")||(args[4]!="0"&&args[4]!="1"))throw new Exception("Invalid automation settings");Configure(new Options{ScanEnabled=args[1]=="1",ScanDay=int.Parse(args[2]),ScanTime=args[3],AppChecksEnabled=args[4]=="1",UpgradeMode=args[5]});return true;
        }return false;
    }
}
