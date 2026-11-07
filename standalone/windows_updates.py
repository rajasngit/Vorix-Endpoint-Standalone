"""Background, report-only Windows Update applicability inventory."""
import base64
from contextlib import nullcontext
import hashlib
import json
import os
from pathlib import Path
import stat
import subprocess
import tempfile
import threading
import time

QUERY = r"""
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[System.Text.UTF8Encoding]::new($false)
$session=New-Object -ComObject Microsoft.Update.Session
$session.ClientApplicationID='Vorix patch inventory'
$searcher=$session.CreateUpdateSearcher()
$searcher.Online=$true
$result=$searcher.Search("IsInstalled=0 and Type='Software' and IsHidden=0")
if ([int]$result.ResultCode -ne 2) { throw "Incomplete update search: $($result.ResultCode)" }
$items=@()
foreach ($u in $result.Updates) {
  $security=$false
  foreach ($c in $u.Categories) {
    if ($c.CategoryID -eq '0fa1201d-4330-4fa8-8ae9-b877473b6441') {$security=$true}
  }
  if ($security -or $u.MsrcSeverity) {
    $items+=@{title=([string]$u.Title).Substring(0,[Math]::Min(180,$u.Title.Length));kb=@($u.KBArticleIDs | Select-Object -First 5 | ForEach-Object {'KB'+$_});severity=[string]$u.MsrcSeverity;released=$u.LastDeploymentChangeTime.ToUniversalTime().ToString('yyyy-MM-dd')}
  }
}
$info=New-Object -ComObject Microsoft.Update.SystemInfo
@{state='ok';checked_at=[DateTimeOffset]::UtcNow.ToUnixTimeSeconds();missing_count=$items.Count;updates=@($items | Select-Object -First 15);truncated=($items.Count -gt 15);reboot_required=[bool]$info.RebootRequired} | ConvertTo-Json -Depth 6 -Compress
"""


class Monitor:
    def __init__(self):
        self.lock = threading.Lock()
        self.report = {'state': 'checking', 'checked_at': 0}
        self.running = False
        self.next_check = 0

    def snapshot(self):
        with self.lock:
            if not self.running and time.monotonic() >= self.next_check:
                self.running = True
                threading.Thread(target=self.check, daemon=True).start()
            return dict(self.report)

    def check(self):
        try:
            command = base64.b64encode(QUERY.encode('utf-16le')).decode('ascii')
            result = subprocess.run([str(Path(os.environ['SystemRoot']) / 'System32/WindowsPowerShell/v1.0/powershell.exe'), '-NoProfile', '-NonInteractive', '-EncodedCommand', command], capture_output=True, timeout=900, creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0))
            if result.returncode:
                raise RuntimeError(result.stderr.decode('utf-8', errors='replace')[-300:])
            report = json.loads(result.stdout.decode('utf-8-sig'))
            if report.get('state') != 'ok':
                raise ValueError('Invalid update response')
            delay = 86400
        except Exception as exc:
            report = {'state': 'error', 'checked_at': time.time(), 'error': str(exc)[:300]}
            delay = 3600
        with self.lock:
            self.report = report
            self.next_check = time.monotonic() + delay
            self.running = False


DEFENDER_QUERY = r"""
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[System.Text.UTF8Encoding]::new($false)
$names=@{}
Get-MpThreat | ForEach-Object {$names[[string]$_.ThreatID]=[string]$_.ThreatName}
$all=@(Get-MpThreatDetection | Sort-Object LastThreatStatusChangeTime -Descending)
$events=@($all | Select-Object -First 100 | ForEach-Object {
  $when=$_.LastThreatStatusChangeTime
  if(!$when){$when=$_.InitialDetectionTime}
  $name=$names[[string]$_.ThreatID]
  if(!$name){$name='Defender threat '+[string]$_.ThreatID}
  @{id=[string]$_.DetectionID;engine='Defender';threat=$name;resources=@($_.Resources | Select-Object -First 5);at=([DateTimeOffset]$_.InitialDetectionTime).ToUnixTimeSeconds();updated_at=([DateTimeOffset]$when).ToUnixTimeSeconds();status_id=[int]$_.ThreatStatusID;action_success=[bool]$_.ActionSuccess;additional_actions=[int]$_.AdditionalActionsBitMask;error_code=[int]$_.ThreatStatusErrorCode}
})
@{events=$events;truncated=($all.Count -gt 100)} | ConvertTo-Json -Compress -Depth 6
"""


def defender_event(raw):
    status = {2:'cleaned',3:'quarantined',4:'removed',5:'allowed',6:'blocked'}.get(raw['status_id'],'unresolved')
    return dict(id=raw['id'][:100],engine='Defender',threat=raw['threat'][:240],
                resources=[str(p)[:1024] for p in raw['resources'][:5]],at=raw['at'],updated_at=raw['updated_at'],
                status=status,action_success=raw['action_success'],additional_actions=raw['additional_actions'],
                detail=('Defender error '+str(raw['error_code'])) if raw['error_code'] else '')


def atomic_json(path, value):
    temporary=path.with_suffix('.tmp')
    with temporary.open('w',encoding='utf-8') as stream:
        json.dump(value,stream);stream.flush();os.fsync(stream.fileno())
    temporary.replace(path)


def download_directories(config):
    """Loaded-user redirected Downloads, otherwise the standard per-profile folder."""
    explicit=config.get('download_paths')
    if explicit is not None:
        if not isinstance(explicit,list) or len(explicit)>50:raise ValueError('Invalid download paths')
        return [Path(p) for p in explicit]
    import winreg
    folders=[]
    with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE,r'SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList') as profiles:
        for index in range(winreg.QueryInfoKey(profiles)[0]):
            sid=winreg.EnumKey(profiles,index)
            if not sid.startswith('S-1-5-21-') and not sid.startswith('S-1-12-1-'):continue
            with winreg.OpenKey(profiles,sid) as entry:
                profile=Path(os.path.expandvars(winreg.QueryValueEx(entry,'ProfileImagePath')[0]))
            path=str(profile/'Downloads')
            try:
                with winreg.OpenKey(winreg.HKEY_USERS,sid+r'\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders') as shell:
                    path=winreg.QueryValueEx(shell,'{374DE290-123F-4565-9164-39C4925E467B}')[0]
                    path=path.replace('%USERPROFILE%',str(profile)).replace('%userprofile%',str(profile))
            except FileNotFoundError:pass
            if '%' in path:raise ValueError('Cannot resolve redirected Downloads folder for a user')
            folders.append(Path(path))
    return list(dict.fromkeys(folders))


def snapshot_download(path, destination, expected, max_bytes):
    """Read only a verified regular file; never mutate a user-writable source."""
    if any(p.is_symlink() or (hasattr(p,'is_junction') and p.is_junction()) for p in [path,*path.parents]):
        raise ValueError('Linked download paths are not scanned')
    flags=os.O_RDONLY|getattr(os,'O_BINARY',0)
    with os.fdopen(os.open(path,flags),'rb') as source:
        info=os.fstat(source.fileno())
        if not stat.S_ISREG(info.st_mode) or info.st_nlink!=1 or info.st_size>max_bytes:
            raise ValueError('Not a supported regular download or file exceeds 500 MiB limit')
        if os.name=='nt':
            import ctypes,msvcrt
            from ctypes import wintypes
            get_final=ctypes.windll.kernel32.GetFinalPathNameByHandleW
            get_final.argtypes=[wintypes.HANDLE,wintypes.LPWSTR,wintypes.DWORD,wintypes.DWORD]
            get_final.restype=wintypes.DWORD
            buffer=ctypes.create_unicode_buffer(32768)
            length=get_final(msvcrt.get_osfhandle(source.fileno()),buffer,len(buffer),0)
            # The Win32 extended prefix has two initial backslashes.
            actual=buffer.value[4:] if buffer.value.startswith('\\\\?\\') else buffer.value
            if not length or length>=len(buffer) or os.path.normcase(actual)!=os.path.normcase(os.path.abspath(path)):
                raise ValueError('Download path changed or resolves through a link')
        fingerprint=[info.st_size,info.st_mtime_ns]
        if fingerprint!=expected:raise ValueError('Download is still changing')
        digest=hashlib.sha256()
        with destination.open('xb') as target:
            copied=0
            while True:
                block=source.read(1024*1024)
                if not block:break
                copied+=len(block)
                if copied>max_bytes:raise ValueError('Download grew beyond the scan size limit')
                target.write(block);digest.update(block)
        after=os.fstat(source.fileno())
        if [after.st_size,after.st_mtime_ns]!=fingerprint:raise ValueError('Download changed during snapshot')
        return digest.hexdigest()


class SecurityMonitor:
    """Bounded persistent outbox and background checks; no network on worker threads."""
    def __init__(self, agent):
        self.agent=agent
        self.folder=agent.state/'security-monitor'
        self.folder.mkdir(exist_ok=True)
        self.lock=threading.Lock()
        self.running=False
        self.next_check=0
        self.next_defender=0
        self.defender={'state':'checking','checked_at':0}
        self.downloads={'state':'checking','checked_at':0,'scanned':0,'skipped':0}
        self.pending_path=self.folder/'pending.json'
        self.known_path=self.folder/'downloads.json'
        self.pending=json.loads(self.pending_path.read_text(encoding='utf-8')) if self.pending_path.exists() else {}
        self.known=json.loads(self.known_path.read_text(encoding='utf-8')) if self.known_path.exists() else {}
        self.seen_path=self.folder/'defender-seen.json'
        self.seen=json.loads(self.seen_path.read_text(encoding='utf-8')) if self.seen_path.exists() else {}
        self.suspended=False

    def snapshot(self):
        with self.lock:
            if not self.suspended and not self.running and time.monotonic()>=self.next_check:
                self.running=True
                threading.Thread(target=self.check,daemon=True).start()
            return dict(defender=dict(self.defender),downloads=dict(self.downloads),pending_events=len(self.pending))

    def record(self,event):
        key=event['engine']+':'+event['id']
        with self.lock:
            if len(self.pending)>=500 and key not in self.pending:raise RuntimeError('Security event outbox full; reporting backlog must clear')
            self.pending[key]=event
            atomic_json(self.pending_path,self.pending)

    def flush(self):
        with self.lock:batch=list(self.pending.values())[:20]
        if not batch:return
        self.agent.call('/api/agent/security-events',{'events':batch})
        with self.lock:
            for event in batch:
                key=event['engine']+':'+event['id']
                if self.pending.get(key)==event:self.pending.pop(key)
            atomic_json(self.pending_path,self.pending)

    def check_defender(self):
        command=base64.b64encode(DEFENDER_QUERY.encode('utf-16le')).decode('ascii')
        result=subprocess.run([str(Path(os.environ['SystemRoot'])/'System32/WindowsPowerShell/v1.0/powershell.exe'),'-NoProfile','-NonInteractive','-EncodedCommand',command],capture_output=True,timeout=60,creationflags=getattr(subprocess,'CREATE_NO_WINDOW',0))
        if result.returncode:raise RuntimeError(result.stderr.decode('utf-8',errors='replace')[-240:])
        report=json.loads(result.stdout.decode('utf-8-sig'))
        for raw in report['events']:
            event=defender_event(raw)
            signature=hashlib.sha256(json.dumps(event,sort_keys=True).encode()).hexdigest()
            if self.seen.get(event['id'])!=signature:
                self.record(event);self.seen[event['id']]=signature
        self.seen={raw['id'][:100]:self.seen[raw['id'][:100]] for raw in report['events']}
        atomic_json(self.seen_path,self.seen)
        return {'state':'limited' if report['truncated'] else 'ok','checked_at':time.time(),'detail':'Latest 100 Defender detections' if report['truncated'] else 'Defender detection history checked'}

    def scan_download(self,path,fingerprint):
        with getattr(self.agent,'scan_gate',nullcontext()):
            return self._scan_download(path,fingerprint)

    def _scan_download(self,path,fingerprint):
        with tempfile.TemporaryDirectory(prefix='download-',dir=self.folder) as folder:
            snapshot=Path(folder)/('sample'+path.suffix[:20])
            digest=snapshot_download(path,snapshot,fingerprint,500*1024*1024)
            command=[str(self.agent.scanner),'--no-summary','--infected','--max-filesize=500M','--max-scansize=500M','--max-files=10000','--max-recursion=16','--alert-exceeds-max=yes',str(snapshot)]
            if self.agent.config.get('database'):command.insert(1,'--database='+str(self.agent.config['database']))
            result=subprocess.run(command,capture_output=True,timeout=180,creationflags=getattr(subprocess,'CREATE_NO_WINDOW',0))
            output=(result.stdout+result.stderr).decode('utf-8',errors='replace')
            if result.returncode not in [0,1] or 'WARNING' in output or 'ERROR' in output:
                raise RuntimeError('ClamAV download scan incomplete: '+output[-240:])
            if result.returncode==1:
                findings=[line.rsplit(': ',1)[-1].removesuffix(' FOUND') for line in output.splitlines() if line.endswith(' FOUND')]
                if any(name.startswith('Heuristics.Limits.Exceeded') for name in findings):raise RuntimeError('ClamAV archive or scan limit exceeded; coverage incomplete')
                now=time.time()
                self.record(dict(id=hashlib.sha256((str(path)+digest).encode()).hexdigest(),engine='ClamAV download',threat='; '.join(findings)[:240] or 'Malware detected',resources=[str(path)[:1024]],at=now,updated_at=now,status='unresolved',action_success=False,additional_actions=0,detail='Detected in a completed-download snapshot. Original file requires review; Defender provides real-time blocking. SHA-256: '+digest))

    def check_downloads(self):
        directories=download_directories(self.agent.config)
        errors=[];visited=0;scanned=0;skipped=0;work=0;available=0
        for root in directories[:50]:
            key=str(root)
            if not root.is_absolute() or str(root).startswith('\\\\'):
                errors.append('Downloads path is not a local absolute path');continue
            if not root.exists():continue
            if any(p.is_symlink() or (hasattr(p,'is_junction') and p.is_junction()) for p in [root,*root.parents]):
                errors.append('Linked Downloads folder is not monitored');continue
            available+=1
            baseline=key not in self.known
            history=self.known.setdefault(key,{})
            found=set()
            def walk_error(error):errors.append('Cannot enumerate a Downloads folder')
            for directory,subdirs,files in os.walk(root,followlinks=False,onerror=walk_error):
                folder=Path(directory)
                subdirs[:]=[name for name in subdirs if not (folder/name).is_symlink() and not (hasattr(folder/name,'is_junction') and (folder/name).is_junction())]
                if len(folder.relative_to(root).parts)>=8:subdirs[:]=[]
                for name in files:
                    visited+=1
                    if visited>5000:break
                    path=folder/name
                    if path.suffix.lower() in ['.crdownload','.part','.partial','.tmp','.download']:continue
                    relative=str(path.relative_to(root));found.add(relative)
                    try:
                        info=path.lstat()
                        if not stat.S_ISREG(info.st_mode):continue
                        fingerprint=[info.st_size,info.st_mtime_ns]
                        old=history.get(relative)
                        if old and old['fingerprint']==fingerprint and old.get('done'):continue
                        if baseline:
                            history[relative]={'fingerprint':fingerprint,'done':True};continue
                        if not old or old['fingerprint']!=fingerprint:
                            history[relative]={'fingerprint':fingerprint,'done':False};continue
                        if time.time()-info.st_mtime<30 or work>=3 or self.agent.active or self.suspended:continue
                        work+=1
                        if info.st_size>500*1024*1024:
                            skipped+=1;errors.append('A download exceeds the 500 MiB scan limit')
                        else:
                            self.scan_download(path,fingerprint);scanned+=1
                        history[relative]['done']=True
                    except FileNotFoundError:pass  # Defender or the user may already have removed it.
                    except Exception as exc:
                        errors.append(str(exc)[:240])
                if visited>5000:break
            if visited>5000:
                errors.append('Downloads inventory capped at 5000 files; coverage incomplete');break
            self.known[key]={name:value for name,value in history.items() if name in found}
        atomic_json(self.known_path,self.known)
        return dict(state='limited' if errors or not available else 'ok',checked_at=time.time(),scanned=self.downloads.get('scanned',0)+scanned,skipped=self.downloads.get('skipped',0)+skipped,folders=available,detail=errors[0] if errors else 'New or changed completed downloads only; existing files are baseline. Additional report-only ClamAV scan.')

    def check(self):
        active=self.folder/'active.json'
        try:
            atomic_json(active,{'at':time.time()})
            if time.monotonic()>=self.next_defender:
                try:report=self.check_defender()
                except Exception as exc:report={'state':'error','checked_at':time.time(),'detail':str(exc)[:240]}
                with self.lock:self.defender=report
                self.next_defender=time.monotonic()+120
            try:report=self.check_downloads()
            except Exception as exc:report={'state':'error','checked_at':time.time(),'detail':str(exc)[:240]}
            with self.lock:self.downloads=report
        finally:
            active.unlink(missing_ok=True)
            with self.lock:
                self.next_check=time.monotonic()+30
                self.running=False
