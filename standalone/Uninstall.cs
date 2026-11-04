using System;
using System.IO;
using System.Diagnostics;
using System.Security.Principal;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Reflection;
class Uninstall {
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool MoveFileEx(string from,string to,int flags);
#if MANAGED
    const string Folder="EndpointConsole", Product="Vorix Managed Endpoint Agent", RegistryName="VorixEndpointManaged";
    static string[] Tasks={"EndpointConsoleAgent","EndpointConsoleSignatures","EndpointConsoleUSBWorker","VorixFinalizeAgent","VorixManagedResume"};
#else
    const string Folder="Vorix Endpoint", Product="Vorix Endpoint Standalone", RegistryName="VorixEndpointStandalone";
    static string[] Tasks={"VorixStandaloneDefinitions","VorixStandaloneFullScan","VorixStandaloneAppUpdates"};
#endif
    static string Home=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Folder);
    static string Data=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),Folder);
    static string Maintenance=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
#if MANAGED
        "Vorix Managed Maintenance"
#else
        "Vorix Standalone Maintenance"
#endif
    );
    static void Log(string message) {try{Directory.CreateDirectory(Maintenance);File.AppendAllText(Path.Combine(Maintenance,"uninstall-last.log"),DateTime.Now.ToString("s")+" "+message+Environment.NewLine);}catch{}}
    static void RemoveFile(string path) {
        var attributes=File.GetAttributes(path);
        // Do not follow file links when changing attributes. Deleting a link only
        // removes the link; regular installation files may legitimately be read-only.
        if((attributes&FileAttributes.ReparsePoint)==0&&(attributes&FileAttributes.ReadOnly)!=0)File.SetAttributes(path,attributes&~FileAttributes.ReadOnly);
        File.Delete(path);
    }
    static object Call(object target,string method,params object[] args) {return target.GetType().InvokeMember(method,BindingFlags.InvokeMethod,null,target,args);}
    static int TaskState(object task) {return Convert.ToInt32(task.GetType().InvokeMember("State",BindingFlags.GetProperty,null,task,null));}
    static Exception Cause(Exception error) {while(error is TargetInvocationException&&error.InnerException!=null)error=error.InnerException;return error;}
    static void RemoveProductTasks() {
        object scheduler=null,folder=null;
        try {
            scheduler=Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service",true));Call(scheduler,"Connect");folder=Call(scheduler,"GetFolder",@"\");
            foreach(string name in Tasks) {
                object task=null;
                try {
                    try{task=Call(folder,"GetTask",name);}
                    catch(Exception ex){var cause=Cause(ex);if(cause.HResult==unchecked((int)0x80070002)||cause.HResult==unchecked((int)0x80070003)){Log("Task already absent: "+name);continue;}throw;}
                    int state=TaskState(task);
                    if(state==2||state==4) {
                        try{Call(task,"Stop",0);}catch(Exception){state=TaskState(task);if(state==2||state==4)throw;}
                    }
                    Call(folder,"DeleteTask",name,0);Log("Task removed: "+name);
                }catch(Exception ex){throw new Exception("Cannot remove scheduled task "+name+": "+Cause(ex).Message,Cause(ex));}
                finally{if(task!=null&&Marshal.IsComObject(task))Marshal.FinalReleaseComObject(task);}
            }
        } finally {
            if(folder!=null&&Marshal.IsComObject(folder))Marshal.FinalReleaseComObject(folder);
            if(scheduler!=null&&Marshal.IsComObject(scheduler))Marshal.FinalReleaseComObject(scheduler);
        }
    }
    static void RemoveTree(string path) {
        if(!Directory.Exists(path))return;
        if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new Exception("Unexpected linked installation folder; removal stopped.");
        foreach(string f in Directory.GetFiles(path))RemoveFile(f);
        foreach(string d in Directory.GetDirectories(path)) {
            if((File.GetAttributes(d)&FileAttributes.ReparsePoint)!=0)Directory.Delete(d);else RemoveTree(d);
        }
        var attributes=File.GetAttributes(path);if((attributes&FileAttributes.ReadOnly)!=0)File.SetAttributes(path,attributes&~FileAttributes.ReadOnly);
        Directory.Delete(path);
    }
    static void RemoveUserLogs() {
#if !MANAGED
        using(var profiles=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList")) {
            if(profiles!=null)foreach(string sid in profiles.GetSubKeyNames())using(var key=profiles.OpenSubKey(sid)) {
                string profile=Environment.ExpandEnvironmentVariables(Convert.ToString(key.GetValue("ProfileImagePath","")));
                if(!Path.IsPathRooted(profile))continue;
                string owned=Path.GetFullPath(Path.Combine(profile,@"AppData\Local\Vorix Endpoint"));
                string prefix=Path.GetFullPath(profile).TrimEnd('\\')+"\\";
                if(!owned.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new Exception("Invalid user data path");
                if(Directory.Exists(owned)){Log("Removing standalone user logs: "+owned);RemoveTree(owned);}
            }
        }
#endif
    }
    static string PSQuote(string value){return "'"+value.Replace("'","''")+"'";}
#if MANAGED
    static bool RequireAdminCode() {
        try {
            var json=new System.Web.Script.Serialization.JavaScriptSerializer();
            var identity=json.Deserialize<System.Collections.Generic.Dictionary<string,object>>(File.ReadAllText(Path.Combine(Data,@"state\identity.json")));
            if(!identity.ContainsKey("uninstall_hash"))throw new Exception("This client has not received its uninstall policy. Connect it to HO and retry.");
            using(var form=new Form{Text="Managed client — administrator authorization",ClientSize=new System.Drawing.Size(500,180),StartPosition=FormStartPosition.CenterScreen,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false}) {
                form.Controls.Add(new Label{Left=20,Top=18,Width=460,Height=44,Text="Enter this node's unique uninstall administrator code.\nIn HO, select Show uninstall code for this exact device."});
                var input=new TextBox{Left=20,Top=72,Width=460,UseSystemPasswordChar=true,MaxLength=30};form.Controls.Add(input);
                var ok=new Button{Left=20,Top=118,Width=170,Text="Authorize uninstall",DialogResult=DialogResult.OK};form.Controls.Add(ok);form.AcceptButton=ok;
                if(form.ShowDialog()!=DialogResult.OK)return false;
                string actual;using(var hash=System.Security.Cryptography.SHA256.Create())actual=BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input.Text.Trim().ToUpperInvariant()+":"+Convert.ToString(identity["id"])))).Replace("-","").ToLowerInvariant();input.Clear();
                string expected=Convert.ToString(identity["uninstall_hash"]);int difference=actual.Length^expected.Length;for(int i=0;i<Math.Min(actual.Length,expected.Length);i++)difference|=actual[i]^expected[i];
                if(difference!=0)throw new Exception("Incorrect administrator code. No service or file was changed.");return true;
            }
        }catch(Exception ex){MessageBox.Show(ex.Message,"Vorix managed protection");return false;}
    }
#endif
    static void RemovePendingProductDeletes() {
        using(var key=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager",true)) {
            var pending=key.GetValue("PendingFileRenameOperations") as string[];if(pending==null)return;
            var keep=new System.Collections.Generic.List<string>();bool changed=false;
            for(int i=0;i<pending.Length;i+=2) {
                string source=pending[i],destination=i+1<pending.Length?pending[i+1]:"";
                string normalized=source.StartsWith(@"\??\")?source.Substring(4):source;
                bool owned=normalized.StartsWith(Maintenance+"\\",StringComparison.OrdinalIgnoreCase)||normalized.StartsWith(Home+"\\",StringComparison.OrdinalIgnoreCase);
                if(owned&&destination.Length==0){changed=true;continue;}
                keep.Add(source);keep.Add(destination);
            }
            if(changed){if(keep.Count==0)key.DeleteValue("PendingFileRenameOperations",false);else key.SetValue("PendingFileRenameOperations",keep.ToArray(),RegistryValueKind.MultiString);}
        }
    }
    static void FinishCleanup() {
        string stage=Path.GetDirectoryName(Application.ExecutablePath);
        string stageRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Vorix Setup");
        string script="$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue'; Wait-Process -Id "+Process.GetCurrentProcess().Id+" -ErrorAction SilentlyContinue; $paths=@("+PSQuote(Maintenance);
        // A downloaded setup EXE is the user's file. Only our private runtime
        // maintenance folder and a recognised temporary removal stage are removed.
        if(stage.StartsWith(stageRoot+"\\remove-",StringComparison.OrdinalIgnoreCase))script+=","+PSQuote(stage);
        script+="); foreach($path in $paths){ $resolved=[IO.Path]::GetFullPath($path); if($resolved -ne "+PSQuote(Maintenance)+" -and !$resolved.StartsWith("+PSQuote(stageRoot+"\\remove-")+",[StringComparison]::OrdinalIgnoreCase)){throw 'Unexpected cleanup path'}; for($i=0;$i -lt 20;$i++){try{if(Test-Path -LiteralPath $resolved){Remove-Item -LiteralPath $resolved -Recurse -Force -ErrorAction Stop};break}catch{Start-Sleep -Milliseconds 250}} }; if((Test-Path -LiteralPath "+PSQuote(stageRoot)+") -and @(Get-ChildItem -LiteralPath "+PSQuote(stageRoot)+" -Force).Count -eq 0){Remove-Item -LiteralPath "+PSQuote(stageRoot)+" -Force}";
        Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,@"WindowsPowerShell\v1.0\powershell.exe"),"-NoProfile -NonInteractive -OutputFormat Text -EncodedCommand "+Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script))){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetTempPath()});
    }
    [STAThread] static int Main(string[] args) {
        bool fromSetup=Array.IndexOf(args,"--from-setup")>=0;
        bool confirmed=Array.IndexOf(args,"--confirmed-uninstall")>=0;
        Application.EnableVisualStyles();
        if(!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) {
            try { Process.Start(new ProcessStartInfo(Application.ExecutablePath){UseShellExecute=true,Verb="runas"});return 0; } catch {return 1;}
        }
#if MANAGED
        if(!RequireAdminCode())return 3;
#endif
        if(Array.IndexOf(args,"--tasks-only")>=0){try{RemoveProductTasks();return 0;}catch(Exception ex){Console.Error.WriteLine(Cause(ex).Message);return 2;}}
        string vault=Path.Combine(Data,
#if MANAGED
            @"state\quarantine"
#else
            @"private\quarantine"
#endif
        );
        if(Directory.Exists(vault)&&Directory.GetFiles(vault,"*.blocked",SearchOption.AllDirectories).Length>0){MessageBox.Show("Review quarantined files before uninstalling. Restore files you have reviewed as safe, or explicitly delete them in quarantine review. No quarantined files were deleted.","Vorix quarantine");return 4;}
        Log("Removal started: "+Product);
        if(!confirmed&&MessageBox.Show("Completely uninstall "+Product+"?\n\nAll of this product's files, definitions, settings, credentials and logs will be deleted. Running scans will stop.","Vorix Endpoint Uninstall",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes){Log("Cancelled by user");return 0;}
#if MANAGED
        if(!confirmed&&File.Exists(Path.Combine(Data,@"state\running.json"))){MessageBox.Show("Wait for the active managed scan to finish before uninstalling.");return 2;}
#endif
        try {
            System.Threading.Thread.Sleep(1500);
            foreach(var name in new[]{"Vorix Endpoint","python","pythonw","clamscan","freshclam"})foreach(var p in Process.GetProcessesByName(name)) {
                if(p.Id==Process.GetCurrentProcess().Id)continue;
                try{if(p.MainModule.FileName.StartsWith(Home.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase)){p.Kill();p.WaitForExit(10000);}}
                catch(System.ComponentModel.Win32Exception){/* Other products' protected processes are outside this installation. */}
            }
            Log("Stopping and removing product update/agent tasks");
            RemoveProductTasks();
            System.Threading.Thread.Sleep(1500);
#if MANAGED
            string usbHelper=Path.Combine(Home,"usb_uninstall.py");
            if(!File.Exists(usbHelper))throw new Exception("The USB rollback helper is missing. Use the current managed-client cleanup package.");
            using(var rollback=Process.Start(new ProcessStartInfo(Path.Combine(Home,@"python\python.exe"),"-I \""+usbHelper+"\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true})) {
                var output=rollback.StandardOutput.ReadToEndAsync();var errors=rollback.StandardError.ReadToEndAsync();rollback.WaitForExit();
                if(rollback.ExitCode!=0)throw new Exception("USB rollback failed: "+errors.Result+output.Result);
            }
#endif
            Log("Removing program files: "+Home);RemoveTree(Home);
            Log("Removing definitions, settings and product data: "+Data);RemoveTree(Data);RemoveUserLogs();
#if !MANAGED
            string link=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),"Vorix Endpoint.lnk");if(File.Exists(link))File.Delete(link);
#endif
            #if MANAGED
            string managedLink=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),"Vorix Managed Protection.lnk");if(File.Exists(managedLink))File.Delete(managedLink);
#endif
            Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\"+RegistryName,false);
#if !MANAGED
            string maintenance=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),@"Vorix Standalone Maintenance\Uninstall.exe");
            if(!String.Equals(maintenance,Application.ExecutablePath,StringComparison.OrdinalIgnoreCase)&&File.Exists(maintenance)) {
                try{RemoveFile(maintenance);}catch(IOException){MoveFileEx(maintenance,null,4);}
            }
#endif
            RemovePendingProductDeletes();Log("Complete product removal finished; removing maintenance files after this process exits");FinishCleanup();
            if(!confirmed)MessageBox.Show(Product+" was removed, including definitions, settings and logs.\nThe remaining maintenance files will be deleted when this window closes.","Vorix Endpoint");return 0;
        } catch(Exception ex) {Log("ERROR: "+ex);string detail="Removal stopped: "+ex.Message+"\n\nLog: "+Path.Combine(Maintenance,"uninstall-last.log");if(fromSetup)Console.Error.WriteLine(detail);else MessageBox.Show(detail,"Vorix Endpoint");return 2;}
    }
}
