using System;
using System.IO;
using System.Drawing;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using System.Runtime.InteropServices;

class StandaloneStatus : ApplicationContext {
    [DllImport("user32.dll")]static extern bool DestroyIcon(IntPtr icon);
    NotifyIcon icon;Icon image;System.Windows.Forms.Timer timer;string message="Checking local monitoring";int lastAlertCount=0;
    string home=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Vorix Endpoint");
    string status=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),@"Vorix Endpoint\status\agent.json");
    StandaloneStatus() {
        var menu=new ContextMenuStrip();menu.Items.Add("View monitoring status",null,delegate{Refresh();MessageBox.Show(message,"Vorix Standalone");});
        menu.Items.Add("Security alerts",null,delegate{Open("--security-alerts");});menu.Items.Add("Open Vorix Endpoint",null,delegate{Open("");});
        icon=new NotifyIcon{Visible=true,ContextMenuStrip=menu};icon.DoubleClick+=delegate{Open("--security-alerts");};
        timer=new System.Windows.Forms.Timer{Interval=10000};timer.Tick+=delegate{Refresh();};Refresh();timer.Start();
    }
    public static bool IsMonitorProcessPresent(int pid) {
        foreach(var process in Process.GetProcessesByName("python"))using(process){if(process.Id==pid)return true;}
        return false;
    }
    void Open(string args){try{Process.Start(new ProcessStartInfo(Path.Combine(home,"Vorix Endpoint.exe"),args){UseShellExecute=true});}catch(Exception ex){MessageBox.Show(ex.Message);}}
    void Refresh() {
        int alertCount=0;Color color=Color.DarkOrange;string label="Installed; monitoring unverified";
        message="Standalone client installed. No fresh monitoring report is available.\nThe status icon starts automatically at sign-in.";
        try {
            if(File.Exists(status)) {
                var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(status));
                bool alive=IsMonitorProcessPresent(Convert.ToInt32(data["pid"]));
                double age=DateTimeOffset.UtcNow.ToUnixTimeSeconds()-Convert.ToDouble(data["updated_at"]);
                int count=data.ContainsKey("unresolved")?Convert.ToInt32(data["unresolved"]):0;alertCount=count;
                label=!alive?"Monitor not running":age>180||age< -30?"Monitoring report stale":count>0?count+" threats need review":"Monitoring active";
                color=!alive||count>0?Color.Firebrick:Color.SeaGreen;
                string detail="";
                object monitoring;if(data.TryGetValue("monitoring",out monitoring)) {
                    var reports=(Dictionary<string,object>)monitoring;
                    foreach(string key in new[]{"defender","downloads"}) {
                        var report=(Dictionary<string,object>)reports[key];string state=Convert.ToString(report["state"]);
                        bool stale=DateTimeOffset.UtcNow.ToUnixTimeSeconds()-Convert.ToDouble(report["checked_at"])>600;
                        detail+="\n"+(key=="defender"?"Defender alerts":"Download scans")+": "+(stale?"stale / unverified":state);
                        if(report.ContainsKey("detail"))detail+="\n"+report["detail"];
                        if(state!="ok"||stale){if(color!=Color.Firebrick)color=Color.DarkOrange;}
                    }
                }
                string error=data.ContainsKey("error")?Convert.ToString(data["error"]):"";
                if(error.Length>0){if(color!=Color.Firebrick)color=Color.DarkOrange;detail+="\n"+error;}
                if(age>180||age< -30)color=Color.DarkOrange;
                message="Vorix Standalone 1.2.1\n"+label+detail+"\n\nLocal monitoring only. Defender provides real-time blocking.\nThe status icon remains available while you are signed in.";
            }
        } catch {label="Installed; monitoring unverified";color=Color.DarkOrange;}
        if(color==Color.DarkOrange&&label=="Monitoring active")label="Monitoring needs attention";
        icon.Text=("Vorix Standalone: "+label).Substring(0,Math.Min(63,("Vorix Standalone: "+label).Length));
        using(var bitmap=new Bitmap(32,32)) {
            using(var g=Graphics.FromImage(bitmap)) {
                g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;g.Clear(Color.Transparent);
                using(var b=new SolidBrush(Color.FromArgb(18,43,62)))g.FillRectangle(b,1,1,30,30);
                using(var b=new SolidBrush(Color.FromArgb(255,150,132)))g.FillPolygon(b,new[]{new Point(6,5),new Point(15,11),new Point(6,17)});
                using(var b=new SolidBrush(Color.FromArgb(164,211,240)))g.FillPolygon(b,new[]{new Point(26,5),new Point(26,20),new Point(16,27),new Point(16,12)});
                using(var b=new SolidBrush(Color.FromArgb(101,170,213)))g.FillPolygon(b,new[]{new Point(7,20),new Point(16,14),new Point(16,27)});
                g.FillEllipse(Brushes.White,21,21,10,10);using(var b=new SolidBrush(color))g.FillEllipse(b,22.5f,22.5f,7,7);
            }
            IntPtr handle=bitmap.GetHicon();var next=(Icon)Icon.FromHandle(handle).Clone();DestroyIcon(handle);icon.Icon=next;var old=image;image=next;if(old!=null)old.Dispose();
        }
        NotifyAlerts(alertCount);
    }
    void NotifyAlerts(int count){if(count>lastAlertCount)icon.ShowBalloonTip(8000,"Vorix security alert",count+" local detections need review. Open Security alerts for details.",ToolTipIcon.Warning);lastAlertCount=count;}
    protected override void ExitThreadCore(){timer.Dispose();icon.Visible=false;icon.Dispose();if(image!=null)image.Dispose();base.ExitThreadCore();}
    [STAThread]static void Main(){bool created;using(var mutex=new Mutex(true,@"Local\VorixStandaloneStatus",out created)){if(!created)return;Application.EnableVisualStyles();Application.Run(new StandaloneStatus());}}
}
