import importlib.util
import json
import os
from pathlib import Path
import subprocess
import tempfile
import time
from types import SimpleNamespace
import unittest
from unittest.mock import Mock,patch

spec=importlib.util.spec_from_file_location('security_monitor',Path(__file__).resolve().parents[1]/'standalone/windows_updates.py')
module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)


class SecurityMonitorTest(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup)
        self.base=Path(self.temp.name);self.downloads=self.base/'Downloads';self.downloads.mkdir()
        self.agent=SimpleNamespace(state=self.base,config={'download_paths':[str(self.downloads)]},active=False,scanner=Path('fixture-scanner'),call=Mock())
        self.monitor=module.SecurityMonitor(self.agent)

    def add_download(self,name='fixture.exe'):
        path=self.downloads/name;path.write_bytes(b'harmless fixture')
        os.utime(path,(time.time()-60,time.time()-60))
        return path

    def test_baseline_partial_and_completed_download(self):
        self.add_download('old.exe')
        with patch.object(self.monitor,'scan_download') as scan:
            self.monitor.check_downloads();scan.assert_not_called()
            self.add_download('unfinished.crdownload')
            fresh=self.add_download()
            self.monitor.check_downloads();scan.assert_not_called()
            self.assertEqual(self.monitor.check_downloads()['scanned'],1)
            self.assertEqual(scan.call_args.args[0],fresh)
            self.monitor.check_downloads();self.assertEqual(scan.call_count,1)

    def test_detection_persists_without_mutating_original(self):
        path=self.add_download();before=path.read_bytes();info=path.stat()
        result=subprocess.CompletedProcess([],1,b'snapshot: Fixture.Malware FOUND\n',b'')
        with patch.object(module.subprocess,'run',return_value=result):
            self.monitor.scan_download(path,[info.st_size,info.st_mtime_ns])
        self.assertEqual(path.read_bytes(),before)
        event=next(iter(self.monitor.pending.values()))
        self.assertEqual(event['engine'],'ClamAV download');self.assertEqual(event['status'],'unresolved')
        self.assertFalse(event['action_success'])
        self.assertEqual(len(module.SecurityMonitor(self.agent).pending),1)

    def test_retry_preserves_pending_events(self):
        event=dict(id='test',engine='Defender',updated_at=1)
        self.monitor.record(event)
        self.agent.call.side_effect=OSError('offline')
        with self.assertRaises(OSError):self.monitor.flush()
        self.assertEqual(len(self.monitor.pending),1)
        self.agent.call.side_effect=None;self.monitor.flush()
        self.assertEqual(self.monitor.pending,{})

    def test_changed_or_hardlinked_snapshot_rejected(self):
        path=self.add_download();info=path.stat()
        with self.assertRaisesRegex(ValueError,'changing'):
            module.snapshot_download(path,self.base/'snapshot',[info.st_size+1,info.st_mtime_ns],1000)
        linked=self.downloads/'linked.exe'
        try:os.link(path,linked)
        except OSError:self.skipTest('Hardlinks unavailable')
        with self.assertRaisesRegex(ValueError,'regular'):
            module.snapshot_download(linked,self.base/'snapshot',[info.st_size,info.st_mtime_ns],1000)

    def test_scanner_error_and_limit_not_reported_as_clean(self):
        path=self.add_download();info=path.stat()
        for result in [subprocess.CompletedProcess([],2,b'',b'ERROR fixture'),subprocess.CompletedProcess([],1,b'snapshot: Heuristics.Limits.Exceeded.MaxScanSize FOUND\n',b'')]:
            with patch.object(module.subprocess,'run',return_value=result):
                with self.assertRaises(RuntimeError):self.monitor.scan_download(path,[info.st_size,info.st_mtime_ns])
        self.assertEqual(self.monitor.pending,{})

    def test_defender_dedup_and_changed_action(self):
        raw=dict(id='defender-id',threat='Fixture',resources=['file:test.exe'],at=10,updated_at=11,status_id=1,action_success=False,additional_actions=0,error_code=0)
        def response(*args,**kwargs):return subprocess.CompletedProcess([],0,json.dumps({'events':[raw],'truncated':False}).encode(),b'')
        with patch.dict(os.environ,{'SystemRoot':'C:/Windows'}),patch.object(module.subprocess,'run',side_effect=response):
            self.monitor.check_defender();self.monitor.flush()
            self.monitor.check_defender();self.assertEqual(self.monitor.pending,{})
            raw.update(updated_at=12,status_id=3,action_success=True)
            self.monitor.check_defender()
            self.assertEqual(next(iter(self.monitor.pending.values()))['status'],'quarantined')


if __name__=='__main__':unittest.main()
