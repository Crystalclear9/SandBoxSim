import copy, unittest
from rsi_code import REFERENCE, evaluate, validate
from rsi_benchmark import public_task, tasks_for

class CodeProbeTests(unittest.TestCase):
    def test_real_code_correctness_and_independent_measurements(self):
        for task in tasks_for('code-check',3,'code'):
            proposal={'source':REFERENCE}
            report=evaluate(task,proposal,task['heldoutSeed'])
            self.assertTrue(report['correct'])
            self.assertEqual(7,len(report['codeReport']['candidate']))
            self.assertTrue(all(row['cpuNanoseconds']>=0 and row['wallNanoseconds']>0 for row in report['codeReport']['candidate']))
            validate(task,proposal,task['heldoutSeed'],report)
            modified=copy.deepcopy(report);modified['codeReport']['candidate'][0]['digest']='fabricated'
            with self.assertRaises(ValueError):validate(task,proposal,task['heldoutSeed'],modified)
            self.assertNotIn('strategies',public_task(task));self.assertNotIn('heldoutProfile',public_task(task))
    def test_wrong_result_and_invalid_source_cannot_receive_gain(self):
        task=tasks_for('bad-code',3,'code')[0]
        proposal={'source':'def solve(problem): return []'}
        report=evaluate(task,proposal,task['heldoutSeed'])
        self.assertFalse(report['correct']);self.assertEqual(0.,report['gain'])
        validate(task,proposal,task['heldoutSeed'],report)
        with self.assertRaises(ValueError):evaluate(task,{'source':'invalid python !'},task['developmentSeed'])
    def test_candidate_source_and_cost_summary_are_bound(self):
        task=tasks_for('identity',3,'code')[0];proposal={'source':REFERENCE}
        report=evaluate(task,proposal,task['developmentSeed'])
        with self.assertRaises(ValueError):validate(task,{'source':REFERENCE+'\n#changed'},task['developmentSeed'],report)
        report['codeReport']['pairedSpeedups'][0]=999
        with self.assertRaises(ValueError):validate(task,proposal,task['developmentSeed'],report)

if __name__=='__main__':unittest.main()
