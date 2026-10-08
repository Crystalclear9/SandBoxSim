import copy
import unittest
from unittest.mock import patch
import rsi_efficiency_study as study


def row(gains=(.2,.6), costs=(1.,1.), family='resource'):
    return {'taskId':'a','family':family,'searchCapability':sum(gains)/len(gains),
            'checkpoints':list(gains),'calls':[{'wallSeconds':v} for v in costs],
            'developmentResults':[], 'heldoutResults':[]}


def stage():
    parent=[dict(row((.1,),(.1,),f),taskId=str(i)) for i,f in enumerate(['resource','routing','visual_update']*4)]
    child=copy.deepcopy(parent)
    for r in child:r['checkpoints']=[.2];r['searchCapability']=.2
    forks=[]
    for _ in range(3):
        branch={}
        for key,edge,base,gain in (('parentProbes','parentFork',parent,.2),('childProbes','childFork',child,.5),('mechanismProbes','mechanismFork',child,.3)):
            branch[key]=copy.deepcopy(base)
            for r in branch[key]:r['searchCapability']=gain;r['checkpoints']=[gain]
            branch[edge]={'execution':{'wallSeconds':.1},'accepted':True}
        forks.append(branch)
    return {'stage':0,'cohort':[{}]*12,'arms':{'reverted_parent':parent,'child':child},'forks':forks,
            'lineage':{'execution':{'wallSeconds':1.}},'analysis':{'recursiveEvidenceSupported':True}}


class CostStudyTests(unittest.TestCase):
    def test_result_available_only_after_call_completes(self):
        self.assertEqual(0,study.trajectory(row(),4,.2)['gain'])
        self.assertEqual(.2,study.trajectory(row(),4,.25)['gain'])

    def test_time_auc_weights_waiting_and_idle_tail(self):
        self.assertAlmostEqual(.35,study.trajectory(row(),4)['timeAuc'])

    def test_no_hindsight_maximum(self):
        value=study.trajectory(row((.8,.1)),4)
        self.assertEqual(.1,value['gain']);self.assertFalse(value['success'])

    def test_unsolved_is_censored_at_budget(self):
        result=study.trajectory(row((0.,0.)),4)
        self.assertEqual(4,result['restrictedTimeToSuccess']);self.assertTrue(result['censored'])

    def test_timeout_cannot_return_late_gain(self):
        self.assertEqual(0,study.trajectory(row((1.,),(5.,)),4)['gain'])

    def test_failure_without_calls_zero(self):
        self.assertEqual(0,study.trajectory(row((0.,),()),4)['gain'])

    def test_invalid_times_cannot_create_efficiency(self):
        for v in (-1.,0.,float('inf'),float('nan'),True):
            with self.assertRaises(ValueError):study.trajectory(row((1.,),(v,)),4)

    def test_cost_ledger_includes_solve_and_agent(self):
        r=row((0.,),(.1,));r['developmentResults']=[{'codeReport':{'candidate':[{'wallNanoseconds':100000000}], 'reference':[{'wallNanoseconds':200000000}]}}]
        self.assertAlmostEqual(.4,study.lower_bound_search_cost(r))

    def test_higher_yield_at_similar_cost_supported(self):
        result=study.second_order(stage())
        self.assertTrue(result['parent']['supported']);self.assertTrue(result['reverted']['supported'])

    def test_expensive_generation_reverses_efficiency(self):
        s=stage()
        for f in s['forks']:f['childFork']['execution']['wallSeconds']=10000.
        self.assertLess(study.second_order(s)['parent']['meanEffect'],0.)

    def test_failed_offspring_cost_not_dropped(self):
        s=stage()
        for f in s['forks']:
            f['childFork']['accepted']=False
            for r in f['childProbes']:r['searchCapability']=0.;r['calls']=[]
        self.assertFalse(study.second_order(s)['parent']['supported'])

    def test_original_evidence_required_even_when_cost_signals_pass(self):
        s=stage();s['analysis']['recursiveEvidenceSupported']=False
        self.assertFalse(study.stage_study(s,6)['costSensitiveEvidenceSupported'])

    def test_no_code_backend_no_invented_payback(self):
        self.assertEqual(0,study.payback(stage(),6)['confirmedSavingTasks'])
        self.assertIsNone(study.payback(stage(),6)['tasks'][0]['optimisticBreakEvenExecutions'])

    def test_payback_rejects_unpaired_or_wrong_or_noisy(self):
        s=stage()
        def report(cpu,wall):
            return {'expectedDigests':['same']*7,'memoryBudgetPassed':True,'reference':[{'wallNanoseconds':1000}]*7,'candidate':[
                {'cpuNanoseconds':cpu,'cpuQuantumNanoseconds':10,'wallNanoseconds':wall} for _ in range(7)]}
        for r in s['arms']['reverted_parent']:r['heldoutResults']=[{'correct':True,'codeReport':report(1000,1000)}]
        for r in s['arms']['child']:r['heldoutResults']=[{'correct':True,'codeReport':report(100,100)}]
        self.assertEqual(12,study.payback(s,6)['confirmedSavingTasks'])
        s['arms']['child'][0]['heldoutResults'][0]['correct']=False
        self.assertEqual(11,study.payback(s,6)['confirmedSavingTasks'])
        s['arms']['child'][1]['heldoutResults'][0]['codeReport']['candidate'][0]['cpuNanoseconds']=999
        self.assertEqual(10,study.payback(s,6)['confirmedSavingTasks'])
        s['arms']['child'][2]['heldoutResults'][0]['codeReport']['expectedDigests'][0]='other'
        with self.assertRaisesRegex(ValueError,'not paired'):study.payback(s,6)

    def test_archive_verification_must_precede_analysis(self):
        with patch('rsi_benchmark.verify',side_effect=ValueError('tampered')):
            with self.assertRaisesRegex(ValueError,'tampered'):study.study('missing-folder')


if __name__=='__main__':unittest.main()
