import copy
import importlib.util
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import rsi_benchmark as benchmark
import rsi_code
import rsi_stateful as streams

spec=importlib.util.spec_from_file_location('stateful_fixture',benchmark.ROOT/'benchmarks/agents/stateful_improver.py')
fixture=importlib.util.module_from_spec(spec);spec.loader.exec_module(fixture)


def solver(source):
    namespace={};exec(source,namespace);return namespace['solve']


class StatefulTests(unittest.TestCase):
    def test_depletion_refill_and_tie_break(self):
        payload={'family':'resource_ledger','items':[[0,0,1,2],[2,0,1,1],[0,0,0,1]],
                 'commands':[['query',0,0,1],['stock',0,0],['query',0,0,1],['stock',1,0],
                             ['query',0,0,1],['stock',0,3],['query',0,0,1]]}
        for source in (streams.REFERENCE,fixture.OPTIMIZED):self.assertEqual([0,1,-1,0],solver(source)(copy.deepcopy(payload)))

    def test_weighted_route_changes_and_blocked_endpoints(self):
        payload={'family':'dynamic_routes','size':2,'costs':[1,3,1,1],
                 'commands':[['route',0,3],['cost',2,0],['route',0,3],['cost',3,0],['route',0,3],
                             ['cost',3,4],['route',0,3],['route',0,0],['cost',0,0],['route',0,0]]}
        for source in (streams.REFERENCE,fixture.OPTIMIZED):self.assertEqual([2,4,-1,7,0,-1],solver(source)(copy.deepcopy(payload)))

    def test_entity_reuse_stale_deletion_and_duplicate_selection(self):
        payload={'family':'scene_lifecycle','entities':[[0,0,1]],
                 'commands':[['observe',[0,0]],['delete',0,0],['observe',[0]],['upsert',0,1,7],
                             ['delete',0,0],['upsert',0,0,99],['observe',[0,0]]]}
        geometry=lambda v:(v*2654435761 ^ (v>>3)) & 0xffffffff
        expected=[[[0,0,geometry(1)]],[],[[0,1,geometry(7)]]]
        for source in (streams.REFERENCE,fixture.OPTIMIZED):self.assertEqual(expected,solver(source)(copy.deepcopy(payload)))

    def test_curriculum_profiles_and_optimized_semantics(self):
        reference=solver(streams.REFERENCE);candidate=solver(fixture.OPTIMIZED)
        for phase in (0,1,4):
            tasks=benchmark.tasks_for('world-stream',6,'code',streams.SUITE,phase)
            for task in tasks:
                public=benchmark.public_task(task)
                self.assertNotIn('strategies',public);self.assertNotIn('heldoutSeed',public);self.assertNotIn('heldoutProfile',public)
                example=public['developmentExample']
                self.assertEqual(reference(copy.deepcopy(example['problem'])),example['expected'])
                private=copy.deepcopy(task);private['heldoutSeed']+=1;private['heldoutProfile']='changed-private-profile'
                self.assertEqual(public,benchmark.public_task(private))
                for seed in (task['developmentSeed'],task['heldoutSeed']):
                    payload=streams.problem(task,seed)
                    self.assertEqual(reference(copy.deepcopy(payload)),candidate(copy.deepcopy(payload)))
            self.assertEqual(tasks,benchmark.tasks_for('world-stream',6,'code',streams.SUITE,phase))
        for task in tasks:
            a=streams.problem(task,task['developmentSeed']);b=streams.problem(dict(task,_phase=0),task['developmentSeed'])
            self.assertNotEqual(a['commands'],b['commands'])

    def test_real_measurements_replay_and_wrong_cache_rejection(self):
        task=benchmark.tasks_for('measured-stream',3,'code',streams.SUITE)[0]
        proposal={'source':fixture.OPTIMIZED};value=rsi_code.evaluate(task,proposal,task['heldoutSeed'])
        self.assertTrue(value['correct']);self.assertEqual(7,len(value['codeReport']['candidate']))
        rsi_code.validate(task,proposal,task['heldoutSeed'],value)
        changed=copy.deepcopy(value);changed['codeReport']['candidate'][0]['digest']='fabricated'
        with self.assertRaises(ValueError):rsi_code.validate(task,proposal,task['heldoutSeed'],changed)
        bad={'source':fixture.OPTIMIZED.replace('cache.clear();continue','continue')}
        broken=rsi_code.evaluate(task,bad,task['heldoutSeed'])
        self.assertFalse(broken['correct']);self.assertEqual(0.,broken['gain'])

    def test_retention_cannot_hide_family_regression_in_average(self):
        parent=[{'taskId':str(i),'family':streams.FAMILIES[i%3],'searchCapability':.4} for i in range(12)]
        child=copy.deepcopy(parent)
        for row in child:row['searchCapability']=.34 if row['family']==streams.FAMILIES[0] else .8
        result=benchmark.retention_analysis(parent,child,.02)
        self.assertGreater(result['meanCapabilityChange'],0)
        self.assertFalse(result['nonRegressionPassed'])
        self.assertFalse(benchmark.apply_retention({'recursiveEvidenceSupported':True},{'parent':parent,'child':child},.02)['recursiveEvidenceSupported'])
        self.assertTrue(benchmark.retention_analysis(parent,parent,.02)['nonRegressionPassed'])

    def test_stream_backend_rejected_before_output_creation(self):
        with tempfile.TemporaryDirectory() as folder:
            path=Path(folder)/'run'
            config={'schemaVersion':2,'backend':'kernel','taskSuite':streams.SUITE,
                    'budget':{'attempts':1,'callSeconds':1,'taskSeconds':2}}
            with self.assertRaises(ValueError):benchmark.run(config,Path(folder),path)
            self.assertFalse(path.exists())

    def test_correctness_loss_cannot_hide_behind_zero_speed_gain(self):
        parent=[{'taskId':str(i),'family':streams.FAMILIES[i%3],'searchCapability':0.,
                 'heldoutResults':[{'correct':True}]} for i in range(12)]
        child=copy.deepcopy(parent);child[0]['heldoutResults']=[None]
        result=benchmark.retention_analysis(parent,child,.02)
        self.assertEqual(0.,result['meanCapabilityChange'])
        self.assertEqual(1,len(result['correctnessRegressions']))
        self.assertFalse(result['retainedCorrectnessPassed']);self.assertFalse(result['nonRegressionPassed'])

    def test_workload_scale_is_fixed_and_bounded(self):
        small=benchmark.tasks_for('scale',3,'code',streams.SUITE,0,1)
        large=benchmark.tasks_for('scale',3,'code',streams.SUITE,0,4)
        for a,b in zip(small,large):
            self.assertEqual(a['id'],b['id']);self.assertEqual(a['n']*4,b['n']);self.assertEqual(a['q']*4,b['q'])
        with self.assertRaises(ValueError):benchmark.tasks_for('scale',3,'code',streams.SUITE,0,5)

    def test_report_verifies_before_writing_and_never_overwrites(self):
        from rsi_report import report
        with tempfile.TemporaryDirectory() as folder:
            path=Path(folder)/'report.html'
            with patch('rsi_benchmark.verify',side_effect=ValueError('tampered evidence')):
                with self.assertRaises(ValueError):report(Path(folder),path)
            self.assertFalse(path.exists())


if __name__=='__main__':unittest.main()
