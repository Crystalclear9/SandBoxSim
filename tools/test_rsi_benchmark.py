import copy,json,tempfile,unittest
from pathlib import Path
import rsi_benchmark as r
from rsi_tasks import CHOICES,evaluate

class ImproverTests(unittest.TestCase):
    def arms(self,values):
        values.setdefault('mechanism_reverted_offspring',values['parent_offspring'])
        return {name:[{'taskId':str(i),'family':list(CHOICES)[i%3],'searchCapability':value,
            'checkpoints':[value],'success':value>=.15,'agentWallSeconds':1.,'calls':[]} for i in range(12)] for name,value in values.items()}
    def test_program_and_agent_gains_are_not_second_order_improvement(self):
        arms=self.arms({'parent':.2,'child':.4,'reverted_parent':.2,'frozen':.1,'parent_offspring':.4,'child_offspring':.5})
        result=r.analyze(arms)
        self.assertTrue(result['heldoutCapabilityGain']['supported'])
        self.assertFalse(result['offspringYieldAdvantage']['supported'])
        self.assertFalse(result['recursiveEvidenceSupported'])
    def test_improved_successor_production_and_reversion(self):
        arms=self.arms({'parent':.2,'child':.4,'reverted_parent':.2,'frozen':.1,'parent_offspring':.4,'child_offspring':.8})
        self.assertTrue(r.analyze(arms)['recursiveEvidenceSupported'])
        self.assertFalse(r.analyze(arms,mechanism_changed=False)['recursiveEvidenceSupported'])
        with_same_mechanism=copy.deepcopy(arms);with_same_mechanism['mechanism_reverted_offspring']=copy.deepcopy(arms['child_offspring'])
        self.assertFalse(r.analyze(with_same_mechanism)['recursiveEvidenceSupported'])
        arms['reverted_parent']=copy.deepcopy(arms['child'])
        self.assertFalse(r.analyze(arms)['recursiveEvidenceSupported'])
    def test_mismatched_tasks_and_small_samples_rejected(self):
        arms=self.arms(dict.fromkeys(('parent','child','reverted_parent','frozen','parent_offspring','child_offspring'),.2))
        arms['child'][0]['taskId']='different'
        with self.assertRaises(ValueError):r.analyze(arms)
        self.assertFalse(r.effect([1.]*3,['resource']*3)['supported'])
    def test_evaluator_owned_work_and_correctness_across_seeds(self):
        for family,choices in CHOICES.items():
            task={'family':family,'n':200,'q':10}
            for seed in (1,17,907):
                slow=evaluate(task,{'strategy':choices[0]},seed);fast=evaluate(task,{'strategy':choices[1]},seed)
                self.assertEqual(slow['digest'],fast['digest']);self.assertTrue(fast['correct'])
                with self.assertRaises(ValueError):evaluate(task,{'strategy':'fabricated'},seed)
    def test_self_edit_is_executed_and_unchanged_source_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);agent=root/'original.py'
            agent.write_text("import json,sys\nr=json.load(sys.stdin)\nprint(json.dumps({'successorSource':r['source']}))\n")
            runner=r.Runner(root,{'attempts':1,'callSeconds':2,'taskSeconds':3})
            with self.assertRaises(ValueError):r.successor(runner,agent,[],'child.py')
            self.assertEqual(1,runner.calls)
    def test_timeout_is_a_measured_failure(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);agent=root/'timeout.py';agent.write_text('while True: pass\n')
            runner=r.Runner(root,{'attempts':1,'callSeconds':.15,'taskSeconds':.2})
            output,record=runner.invoke(agent,{'mode':'propose'})
            self.assertIsNone(output);self.assertIn('budget',record['error'])

if __name__=='__main__':unittest.main()
