"""Local-only standalone security monitor. No management-console transport."""
import ctypes
from ctypes import wintypes
import json
import os
from pathlib import Path
import time

from windows_updates import SecurityMonitor, atomic_json


class ScanGate:
    def __init__(self,name=None):
        self.api=ctypes.windll.kernel32
        self.api.CreateMutexW.argtypes=[ctypes.c_void_p,wintypes.BOOL,wintypes.LPCWSTR]
        self.api.CreateMutexW.restype=wintypes.HANDLE
        self.api.WaitForSingleObject.argtypes=[wintypes.HANDLE,wintypes.DWORD]
        self.api.ReleaseMutex.argtypes=[wintypes.HANDLE]
        self.api.CloseHandle.argtypes=[wintypes.HANDLE]
        class Attributes(ctypes.Structure):
            _fields_=[('length',wintypes.DWORD),('descriptor',ctypes.c_void_p),('inherit',wintypes.BOOL)]
        descriptor=ctypes.c_void_p()
        convert=ctypes.windll.advapi32.ConvertStringSecurityDescriptorToSecurityDescriptorW
        convert.argtypes=[wintypes.LPCWSTR,wintypes.DWORD,ctypes.POINTER(ctypes.c_void_p),ctypes.c_void_p]
        if not convert('D:(A;;GA;;;SY)(A;;GA;;;BA)(A;;0x00100001;;;BU)',1,ctypes.byref(descriptor),None):raise ctypes.WinError()
        try:
            attributes=Attributes(ctypes.sizeof(Attributes),descriptor,False)
            self.handle=self.api.CreateMutexW(ctypes.byref(attributes),False,name or ('Global'+chr(92)+'VorixStandaloneScan'))
        finally:
            ctypes.windll.kernel32.LocalFree.argtypes=[ctypes.c_void_p]
            ctypes.windll.kernel32.LocalFree(descriptor)
        if not self.handle:raise ctypes.WinError()

    def busy(self):
        result=self.api.WaitForSingleObject(self.handle,0)
        if result in [0,128]:self.api.ReleaseMutex(self.handle);return False
        if result==258:return True
        raise ctypes.WinError()

    def __enter__(self):
        if self.api.WaitForSingleObject(self.handle,1000) not in [0,128]:raise RuntimeError('A manual or scheduled scan is active; download check deferred')
        return self

    def __exit__(self,*args):self.api.ReleaseMutex(self.handle)


class LocalClient:
    def __init__(self,home,data,gate):
        self.state=data/'private'/'monitor'
        self.state.mkdir(parents=True,exist_ok=True)
        self.history=self.state/'history.json'
        self.scanner=home/'clamav'/'clamscan.exe'
        self.config={'database':str(data/'database')}
        self.scan_gate=gate

    @property
    def active(self):return self.scan_gate.busy()

    def call(self,path,payload):
        if path!='/api/agent/security-events':raise ValueError('Standalone supports local security events only')
        history=json.loads(self.history.read_text(encoding='utf-8')) if self.history.exists() else {}
        for event in payload['events']:
            key=event['engine']+':'+event['id']
            old=history.get(key)
            if old and old['updated_at']>event['updated_at']:continue
            resolved=event['engine']=='Defender' and event['status'] in ['cleaned','quarantined','removed','blocked'] and event['action_success'] and not event['additional_actions']
            history[key]=dict(event,needs_review=not resolved)
        if len(history)>1000:
            resolved=sorted((key for key,value in history.items() if not value['needs_review']),key=lambda key:history[key]['updated_at'])
            for key in resolved[:len(history)-1000]:history.pop(key)
            if len(history)>1000:raise RuntimeError('Local alert history is full of unresolved events; administrator review required')
        atomic_json(self.history,history)
        return {'ok':True}


def main():
    import msvcrt
    home=Path(__file__).resolve().parent
    data=Path(os.environ['ProgramData'])/'Vorix Endpoint'
    client=LocalClient(home,data,ScanGate())
    status=data/'status';status.mkdir(exist_ok=True)
    with (client.state/'worker.lock').open('a+b') as lock:
        lock.seek(0);lock.write(b'0');lock.flush();lock.seek(0)
        try:msvcrt.locking(lock.fileno(),msvcrt.LK_NBLCK,1)
        except OSError:return
        monitor=SecurityMonitor(client)
        while True:
            report=monitor.snapshot()
            error=''
            try:monitor.flush()
            except Exception as exc:error=str(exc)[:240]
            try:
                events=json.loads(client.history.read_text(encoding='utf-8')) if client.history.exists() else {}
                unresolved=sum(bool(event['needs_review']) for event in events.values())
                atomic_json(status/'agent.json',dict(pid=os.getpid(),updated_at=time.time(),version='1.2.1',unresolved=unresolved,event_count=len(events),monitoring=report,error=error))
            except Exception as exc:
                atomic_json(status/'agent.json',dict(pid=os.getpid(),updated_at=time.time(),version='1.2.1',error=str(exc)[:240]))
            time.sleep(10)


if __name__=='__main__':main()
