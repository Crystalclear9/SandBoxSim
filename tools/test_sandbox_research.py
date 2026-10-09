import hashlib
import tempfile
import subprocess
from unittest.mock import patch
import unittest
from pathlib import Path
import sandbox_research as research


class ResearchTests(unittest.TestCase):
    def test_source_path_restrictions(self):
        for name in ('../src/SandBoxSim.Core/a.cs','C:/a.cs','src/SandBoxSim.Core/../../a.cs',
                     'src/SandBoxSim.Tests/a.cs','src/SandBoxSim.Core/a.csproj','src\\SandBoxSim.Core\\a.cs'):
            with self.assertRaises(ValueError):research.editable_path(name)

    def test_edits_hash_and_scope_are_transactional(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);name='src/SandBoxSim.Core/a.cs';p=root/name;p.parent.mkdir(parents=True);p.write_text('old',encoding='utf-8')
            edit={'path':name,'baseSha256':research.sha(p),'content':'new'}
            with self.assertRaises(ValueError):research.apply_edits(root,[edit,edit],[name])
            self.assertEqual('old',p.read_text())
            bad=dict(edit,baseSha256='wrong')
            with self.assertRaises(ValueError):research.apply_edits(root,[bad],[name])
            with self.assertRaises(ValueError):research.apply_edits(root,[edit],[])
            self.assertEqual('old',p.read_text())
            self.assertEqual(hashlib.sha256(b'new').hexdigest(),research.apply_edits(root,[edit],[name])[name])

    def test_noop_and_new_file_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);name='src/SandBoxSim.Core/a.cs';p=root/name;p.parent.mkdir(parents=True);p.write_text('old')
            with self.assertRaises(ValueError):research.apply_edits(root,[{'path':name,'baseSha256':research.sha(p),'content':'old'}],[name])
            with self.assertRaises(ValueError):research.apply_edits(root,[{'path':name+'x','baseSha256':'x','content':'new'}],[name+'x'])

    def test_seeds_are_disjoint(self):
        config={'developmentSuite':{'split':'development','cases':[{'seed':1}]},'heldoutSuite':{'split':'heldout','cases':[{'seed':1}]}}
        with self.assertRaises(ValueError):research.suites(config)
        config['heldoutSuite']['cases'][0]['seed']=2
        self.assertEqual(config['heldoutSuite'],research.suites(config)[1])

    def test_manifest_ignores_only_compiled_directories(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);(root/'bin').mkdir();(root/'bin/a.dll').write_bytes(b'code');(root/'source.cs').write_text('source')
            self.assertEqual(['source.cs'],list(research.source_manifest(root)))

    def test_snapshot_includes_new_working_sources_and_excludes_ignored_output(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp)/'repository';root.mkdir();destination=Path(temp)/'snapshot';destination.mkdir()
            subprocess.run(['git','init','-q'],cwd=root,check=True)
            core=root/'src/SandBoxSim.Core';core.mkdir(parents=True)
            (core/'tracked.cs').write_text('tracked',encoding='utf-8')
            subprocess.run(['git','add','src/SandBoxSim.Core/tracked.cs'],cwd=root,check=True)
            (core/'new.cs').write_text('new module',encoding='utf-8');(root/'.gitignore').write_text('bin/\n',encoding='utf-8')
            (core/'bin').mkdir();(core/'bin/ignored.cs').write_text('build output')
            (root/'outside.txt').write_text('outside source scope')
            with patch.object(research,'ROOT',root):manifest=research.snapshot(destination)
            self.assertEqual(['src/SandBoxSim.Core/new.cs','src/SandBoxSim.Core/tracked.cs'],list(manifest))
            self.assertEqual('new module',(destination/'src/SandBoxSim.Core/new.cs').read_text())


if __name__=='__main__':unittest.main()
