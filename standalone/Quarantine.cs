using System;
using System.IO;
using System.Text;
using System.Drawing;
using System.Diagnostics;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Windows.Forms;

partial class Vorix {
    static string Vault {get{return Path.Combine(PrivatePath,"quarantine");}}
    class QuarantineSession {
        public string Folder; readonly object gate=new object();
        readonly Dictionary<string,string> threats=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        public QuarantineSession(){if(!Admin)throw new Exception("Administrator access is required for automatic quarantine.");SecurePrivate();Directory.CreateDirectory(Vault);Folder=Path.Combine(Vault,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Folder);}
        public string Arguments {get{return " --move="+Q(Folder)+" --exclude-dir="+Q("^"+Regex.Escape(Vault));}}
        public string Capture(string line) {try{return CaptureCore(line);}catch(Exception ex){return "QUARANTINE UNRESOLVED: "+ex.Message+" | "+line;}}
        string CaptureCore(string line) {
            lock(gate) {
                if(line.EndsWith(" FOUND")){int split=line.LastIndexOf(": ",StringComparison.Ordinal);if(split>0)threats[line.Substring(0,split)]=line.Substring(split+2,line.Length-split-8);}
                var match=Regex.Match(line,@"^(.*): moved to '(.*)'$");if(!match.Success)return line;
                string original=match.Groups[1].Value;var real=Regex.Match(original,@"^.* \(real path: '(.*)'\)$");if(real.Success)original=real.Groups[1].Value;
                string moved=Path.GetFullPath(match.Groups[2].Value);
                if(!moved.StartsWith(Folder+"\\",StringComparison.OrdinalIgnoreCase)||!File.Exists(moved)||(File.GetAttributes(moved)&FileAttributes.ReparsePoint)!=0)throw new Exception("Invalid quarantine move result.");
                string id=Guid.NewGuid().ToString("D"),target=Path.Combine(Vault,id+".blocked");
                // Store recovery metadata before renaming; a crash never silently discards evidence.
                string threat;threats.TryGetValue(original,out threat);
                string hash;using(var sha=SHA256.Create())using(var input=File.OpenRead(moved))hash=BitConverter.ToString(sha.ComputeHash(input)).Replace("-","").ToLowerInvariant();
                var record=new Dictionary<string,object>{{"id",id},{"original",original},{"threat",threat??"ClamAV detection"},{"sha256",hash},{"at",DateTime.Now.ToString("s")},{"state","quarantined"},{"stored",moved}};
                Report(Path.Combine(Vault,id+".json"),record);File.Move(moved,target);
                // Do not preserve a detected file's original permissive ACL in the vault.
                var acl=new FileSecurity();acl.SetAccessRuleProtection(true,false);foreach(string sid in new[]{"S-1-5-18","S-1-5-32-544"})acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid),FileSystemRights.FullControl,AccessControlType.Allow));acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier("S-1-1-0"),FileSystemRights.ExecuteFile,AccessControlType.Deny));new FileInfo(target).SetAccessControl(acl);
                record["stored"]=target;Report(Path.Combine(Vault,id+".json"),record);return "Quarantined: "+original+" (review ID "+id+")";
            }
        }
    }
    static string QuarantineRecordPath(string id){Guid parsed;if(!Guid.TryParseExact(id,"D",out parsed))throw new Exception("Invalid quarantine ID");return Path.Combine(Vault,parsed.ToString("D")+".json");}
    static void QuarantineAction(string id,bool restore) {
        if(!Admin)throw new Exception("Administrator access is required.");SecurePrivate();string recordPath=QuarantineRecordPath(id);var item=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(recordPath));if(Convert.ToString(item["state"])!="quarantined")throw new Exception("This item was already reviewed.");
        string stored=Path.GetFullPath(Convert.ToString(item["stored"]));if(!File.Exists(stored)&&File.Exists(Path.Combine(Vault,id+".blocked")))stored=Path.Combine(Vault,id+".blocked");if(!stored.StartsWith(Vault+"\\",StringComparison.OrdinalIgnoreCase)||(File.GetAttributes(stored)&FileAttributes.ReparsePoint)!=0)throw new Exception("Invalid vault item.");
        if(restore){
            string original=Convert.ToString(item["original"]);if(!Path.IsPathRooted(original)||original.StartsWith(@"\\")||original.IndexOf(':',3)>=0||original.StartsWith(Data+"\\",StringComparison.OrdinalIgnoreCase)||original.StartsWith(Home+"\\",StringComparison.OrdinalIgnoreCase))throw new Exception("Unsafe restore destination; use administrator recovery review.");
            string parent=Path.GetDirectoryName(original);if(!Directory.Exists(parent))throw new Exception("The original folder is missing. Restore refused.");
            for(var d=new DirectoryInfo(parent);d!=null;d=d.Parent)if((d.Attributes&FileAttributes.ReparsePoint)!=0)throw new Exception("Restore through linked folders is refused.");
            using(var sha=SHA256.Create())using(var input=File.OpenRead(stored))if(BitConverter.ToString(sha.ComputeHash(input)).Replace("-","").ToLowerInvariant()!=Convert.ToString(item["sha256"]))throw new Exception("Quarantine integrity check failed.");
            // CreateNew semantics: never overwrite an existing file. New file inherits destination ACL.
            using(var input=File.OpenRead(stored))using(var output=new FileStream(original,FileMode.CreateNew,FileAccess.Write,FileShare.None))input.CopyTo(output);
            File.Delete(stored);item["state"]="restored";
        }else {File.Delete(stored);item["state"]="deleted";}
        item["reviewed_at"]=DateTime.Now.ToString("s");Report(recordPath,item);
    }
    void ReviewQuarantine() {
        if(!Admin){try{Elevate("--review-quarantine");}catch(Exception ex){Append(ex.Message);}return;}
        SecurePrivate();Directory.CreateDirectory(Vault);
        using(var dialog=new Form{Text="Vorix quarantine review",ClientSize=new Size(940,510),StartPosition=FormStartPosition.CenterParent}) {
            var list=new ListView{Left=18,Top=18,Width=904,Height=360,View=View.Details,FullRowSelect=true,MultiSelect=false};list.Columns.Add("Date",145);list.Columns.Add("Detection",200);list.Columns.Add("Original file",410);list.Columns.Add("State",120);dialog.Controls.Add(list);
            Action refresh=delegate{list.Items.Clear();foreach(string file in Directory.GetFiles(Vault,"*.json")){try{var item=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(file));var row=new ListViewItem(new[]{Convert.ToString(item["at"]),Convert.ToString(item["threat"]),Convert.ToString(item["original"]),Convert.ToString(item["state"])});row.Tag=Convert.ToString(item["id"]);list.Items.Add(row);}catch{}}};refresh();
            dialog.Controls.Add(new Label{Left=18,Top=389,Width=904,Height=45,Text="Restore only after reviewing the detection. Restored files may be detected again; this does not add an exclusion.\nDelete permanently removes the selected quarantined file. No automatic expiry or deletion is used."});
            foreach(bool restore in new[]{true,false}){bool action=restore;var button=new Button{Left=restore?18:310,Top=448,Width=270,Height=38,Text=restore?"Restore — reviewed as safe":"Delete quarantined file"};dialog.Controls.Add(button);button.Click+=delegate{if(list.SelectedItems.Count!=1)return;if(MessageBox.Show(action?"Restore this file to its original path? Only continue if you have reviewed it as safe.":"Permanently delete this quarantined file?","Quarantine review",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;try{QuarantineAction(Convert.ToString(list.SelectedItems[0].Tag),action);refresh();}catch(Exception ex){MessageBox.Show(ex.Message);}};}
            dialog.ShowDialog(this);
        }
    }
}
