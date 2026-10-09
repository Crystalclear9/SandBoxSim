import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
import sandbox_research_compare as compare


class ComparisonTests(unittest.TestCase):
    def test_failed_candidate_has_zero_gain(self):
        with tempfile.TemporaryDirectory() as temp:
            folder=Path(temp);compare.save(folder/'config.json',{'heldoutSuite':{'cases':[{},{}]}})
            self.assertEqual([0,0],compare.world_gains(folder,{'selectedAttempt':None,'heldoutCorrect':False}))

    def test_slowdown_and_paired_median_are_preserved(self):
        with tempfile.TemporaryDirectory() as temp:
            folder=Path(temp);(folder/'heldout').mkdir()
            report={'suite':{'cases':[{},{}]},'samples':[{'case':i,'reference':{'wallMs':10},'candidate':{'wallMs':ms}}
                for i,times in enumerate(([5,5,100],[20,20,1])) for ms in times]}
            compare.save(folder/'heldout/report.json',report)
            self.assertEqual([.5,-1],compare.world_gains(folder,{'selectedAttempt':0,'heldoutCorrect':True}))

    def test_condition_changes_and_duplicate_archives_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            folders={arm:Path(temp)/arm for arm in compare.ARMS}
            for arm,folder in folders.items():
                folder.mkdir();compare.save(folder/'source-manifest.json',{'source':'same'})
                compare.save(folder/'config.json',{'attempts':3 if arm=='child' else 2})
            with patch.object(compare,'verify',return_value={}):
                with self.assertRaisesRegex(ValueError,'attempts'):compare.analyze(folders)
            with self.assertRaisesRegex(ValueError,'distinct'):compare.analyze(dict.fromkeys(compare.ARMS,folders['parent']))

    def test_failed_compile_cost_is_charged_and_impossible_total_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            folder=Path(temp);path=folder/'attempt-0/logs/build-0';path.mkdir(parents=True)
            compare.save(path/'execution.json',{'exitCode':1,'wallSeconds':3})
            ledger=compare.cost_ledger(folder,{'wallSeconds':5})
            self.assertEqual(3,ledger['buildAndTestSeconds']);self.assertEqual(2,ledger['otherControllerSeconds'])
            with self.assertRaises(ValueError):compare.cost_ledger(folder,{'wallSeconds':1})


if __name__=='__main__':unittest.main()
