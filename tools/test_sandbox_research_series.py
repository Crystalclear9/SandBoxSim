import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
import sandbox_research_series as series


class SeriesTests(unittest.TestCase):
    def fixtures(self, root, count=3, effect=.1, worlds=None, correct=True):
        blocks=[];results=[];arms=list(series.comparison.ARMS)
        for i in range(count):
            block=Path(root)/str(i);(block/'parent').mkdir(parents=True)
            series.comparison.save(block/'parent/source-manifest.json',{'source':'same'})
            series.comparison.save(block/'parent/config.json',{'attempts':2})
            series.comparison.save(block/'comparison.json',{'executionOrder':arms[i%3:]+arms[:i%3]})
            row={'arms':{a:{'heldoutCorrect':correct,'controllerWallSeconds':10,'failedAttempts':1} for a in arms}}
            for control in ('Parent','Reverted'):row['childMinus'+control]={'meanDifference':effect,'researchEfficiencyDifference':effect/10,'pairedWorldSavingsDifference':worlds or [effect,effect]}
            blocks.append(block);results.append(row)
        return blocks,results

    def test_null_effect_never_supports_rsi(self):
        with tempfile.TemporaryDirectory() as root:
            blocks,rows=self.fixtures(root,effect=0)
            with patch.object(series.comparison,'analyze',side_effect=rows):r=series.aggregate(blocks)
            self.assertFalse(r['frozenMethodGainSupported']);self.assertFalse(r['recursiveEvidenceSupported'])
            self.assertEqual(9,r['failedAttempts']);self.assertEqual(90,r['totalControllerSeconds'])

    def test_world_regression_is_not_hidden_by_positive_mean(self):
        with tempfile.TemporaryDirectory() as root:
            blocks,rows=self.fixtures(root,worlds=[.4,-.2])
            with patch.object(series.comparison,'analyze',side_effect=rows):r=series.aggregate(blocks)
            self.assertEqual(6,len(r['regressions']));self.assertFalse(r['frozenMethodGainSupported'])

    def test_positive_frozen_effect_is_not_recursive_evidence(self):
        with tempfile.TemporaryDirectory() as root:
            blocks,rows=self.fixtures(root)
            with patch.object(series.comparison,'analyze',side_effect=rows):r=series.aggregate(blocks)
            self.assertTrue(r['frozenMethodGainSupported']);self.assertFalse(r['recursiveEvidenceSupported'])

    def test_insufficient_runs_and_failed_child_do_not_pass(self):
        for count,correct in ((1,True),(3,False)):
            with tempfile.TemporaryDirectory() as root:
                blocks,rows=self.fixtures(root,count=count,correct=correct)
                with patch.object(series.comparison,'analyze',side_effect=rows):self.assertFalse(series.aggregate(blocks)['frozenMethodGainSupported'])

    def test_duplicate_and_changed_conditions_rejected(self):
        with tempfile.TemporaryDirectory() as root:
            blocks,rows=self.fixtures(root)
            with self.assertRaises(ValueError):series.aggregate([blocks[0],blocks[0]])
            (blocks[1]/'parent/config.json').write_text('{"attempts":3}',encoding='utf-8')
            with patch.object(series.comparison,'analyze',side_effect=rows):
                with self.assertRaisesRegex(ValueError,'conditions'):series.aggregate(blocks)

    def test_bootstrap_is_deterministic_and_runs_are_units(self):
        self.assertIsNone(series.interval([1,2]))
        self.assertEqual(series.interval([-.1,.1,.3]),series.interval([-.1,.1,.3]))
        self.assertEqual(.2,series.interval([.2,.2,.2])['low'])

    def test_unbalanced_order_and_negative_efficiency_do_not_pass(self):
        with tempfile.TemporaryDirectory() as root:
            blocks,rows=self.fixtures(root)
            for block in blocks:(block/'comparison.json').write_text('{"executionOrder":["parent","child","reverted"]}',encoding='utf-8')
            with patch.object(series.comparison,'analyze',side_effect=rows):self.assertFalse(series.aggregate(blocks)['frozenMethodGainSupported'])
        with tempfile.TemporaryDirectory() as root:
            blocks,rows=self.fixtures(root)
            for row in rows:row['childMinusParent']['researchEfficiencyDifference']=-.1
            with patch.object(series.comparison,'analyze',side_effect=rows):self.assertFalse(series.aggregate(blocks)['frozenMethodGainSupported'])

    def test_nonfinite_contrast_rejected(self):
        with tempfile.TemporaryDirectory() as root:
            blocks,rows=self.fixtures(root);rows[0]['childMinusParent']['meanDifference']=float('nan')
            with patch.object(series.comparison,'analyze',side_effect=rows):
                with self.assertRaisesRegex(ValueError,'contrast'):series.aggregate(blocks)


if __name__=='__main__':unittest.main()
