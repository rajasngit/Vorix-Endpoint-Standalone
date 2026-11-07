import importlib.util
import json
import os
from pathlib import Path
import sys
import tempfile
import threading
import unittest
from unittest.mock import patch

sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'standalone'))
spec=importlib.util.spec_from_file_location('standalone_worker',Path(__file__).resolve().parents[1]/'standalone/security_worker.py')
module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)


class FakeGate:
    def busy(self):return False


class StandaloneMonitorTest(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup)
        self.client=module.LocalClient(Path(self.temp.name),Path(self.temp.name),FakeGate())

    def test_local_history_no_network_and_resolution(self):
        event=dict(id='fixture',engine='Defender',at=1,updated_at=2,status='quarantined',action_success=True,additional_actions=8)
        with patch('socket.socket',side_effect=AssertionError('Standalone event reporting must stay local')):
            self.client.call('/api/agent/security-events',{'events':[event]})
            history=json.loads(self.client.history.read_text())
            self.assertTrue(history['Defender:fixture']['needs_review'])
            event.update(additional_actions=0,updated_at=3)
            self.client.call('/api/agent/security-events',{'events':[event]})
        self.assertFalse(json.loads(self.client.history.read_text())['Defender:fixture']['needs_review'])
        event.update(updated_at=2,status='unresolved')
        self.client.call('/api/agent/security-events',{'events':[event]})
        self.assertFalse(json.loads(self.client.history.read_text())['Defender:fixture']['needs_review'])

    def test_download_detections_are_not_claimed_as_remediated(self):
        event=dict(id='fixture',engine='ClamAV download',at=1,updated_at=2,status='unresolved',action_success=False,additional_actions=0)
        self.client.call('/api/agent/security-events',{'events':[event]})
        self.assertTrue(json.loads(self.client.history.read_text())['ClamAV download:fixture']['needs_review'])
        with self.assertRaises(ValueError):self.client.call('/api/login',{})

    @unittest.skipUnless(os.name=='nt','Windows named mutex')
    def test_downloads_share_scan_mutex_with_other_threads(self):
        name='Local'+chr(92)+'VorixStandaloneTest-'+str(os.getpid())
        first=module.ScanGate(name);second=module.ScanGate(name)
        try:
            result=[]
            with first:
                thread=threading.Thread(target=lambda:result.append(second.busy()));thread.start();thread.join()
            self.assertEqual(result,[True])
            self.assertFalse(second.busy())
        finally:
            first.api.CloseHandle(first.handle);second.api.CloseHandle(second.handle)
