using System;
using System.IO;
using System.Drawing;
using System.Diagnostics;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;
using Microsoft.Win32;

partial class Vorix {
    static void InstallSecurityMonitor() {
        SecurePrivate();
        string statusFolder=Path.Combine(Data,"status");Directory.CreateDirectory(statusFolder);
        string python=Path.Combine(Home,@"python\python.exe"),script=Path.Combine(Home,"security_worker.py");
        PS("$a=New-ScheduledTaskAction -Execute "+PSQ(python)+" -Argument "+PSQ("-u "+Q(script))+"; $t=New-ScheduledTaskTrigger -AtStartup; $p=New-ScheduledTaskPrincipal -UserId SYSTEM -LogonType ServiceAccount -RunLevel Highest; $s=New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 3) -MultipleInstances IgnoreNew -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries; Register-ScheduledTask -TaskName VorixStandaloneMonitor -Action $a -Trigger $t -Principal $p -Settings $s -Force | Out-Null; Start-ScheduledTask -TaskName VorixStandaloneMonitor");
        using(var run=Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"))run.SetValue("VorixStandaloneStatus",Q(Path.Combine(Home,"VorixStatus.exe")));
        PS("$w=New-Object -ComObject WScript.Shell; $l=$w.CreateShortcut([IO.Path]::Combine([Environment]::GetFolderPath('CommonPrograms'),'Vorix Endpoint status.lnk')); $l.TargetPath="+PSQ(Path.Combine(Home,"VorixStatus.exe"))+"; $l.Save()");
    }
    static void ShowSecurityAlerts() {
        if(!Admin){Elevate("--security-alerts");return;}
        using(var dialog=new Form{Text="Vorix Standalone — local security alerts",ClientSize=new Size(1000,570),StartPosition=FormStartPosition.CenterScreen}) {
            var note=new Label{Left=16,Top=14,Width=965,Height=52,Text="Defender supplies real-time blocking. Additional ClamAV download detections require review.\nThese are local alerts; no management-console connection or mailbox access is used."};dialog.Controls.Add(note);
            var grid=new DataGridView{Left=16,Top=74,Width=965,Height=425,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,RowHeadersVisible=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect};
            foreach(string column in new[]{"Time","Engine","Threat","Action","Review","Affected resource / details"})grid.Columns.Add(column,column);
            dialog.Controls.Add(grid);
            Action refresh=delegate {
                grid.Rows.Clear();string path=Path.Combine(PrivatePath,@"monitor\history.json");
                if(!File.Exists(path)){note.Text="No local alerts reported yet. Check the status icon to confirm monitoring is running.";return;}
                var history=new JavaScriptSerializer{MaxJsonLength=8388608}.Deserialize<Dictionary<string,Dictionary<string,object>>>(File.ReadAllText(path));
                foreach(var value in history.Values.OrderByDescending(v=>Convert.ToBoolean(v["needs_review"])).ThenByDescending(v=>Convert.ToDouble(v["updated_at"]))) {
                    var resources=value["resources"] as System.Collections.IEnumerable;var paths=new List<string>();if(resources!=null)foreach(var item in resources)paths.Add(Convert.ToString(item));
                    grid.Rows.Add(DateTimeOffset.FromUnixTimeSeconds((long)Convert.ToDouble(value["at"])).LocalDateTime.ToString(),value["engine"],value["threat"],value["status"],Convert.ToBoolean(value["needs_review"])?"Needs review":"Action completed",String.Join("; ",paths)+" "+Convert.ToString(value["detail"])+(Convert.ToInt64(value["additional_actions"])!=0?" | Additional Defender actions: "+value["additional_actions"]:""));
                }
            };
            var button=new Button{Left=16,Top=515,Width=140,Height=36,Text="Refresh alerts"};button.Click+=delegate{try{refresh();}catch(Exception ex){MessageBox.Show(ex.Message);}};dialog.Controls.Add(button);
            refresh();dialog.ShowDialog();
        }
    }
}
