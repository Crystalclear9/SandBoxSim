import copy, json, tempfile, unittest
from pathlib import Path
import efficiency as e
from unittest.mock import patch

class EfficiencyTests(unittest.TestCase):
    def grid(self):
        return {'width':3,'height':3,'tiles':[{'walkable':True,'moveCost':1.,'traversalHeight':0.} for _ in range(9)]}
    def test_independent_path_oracle(self):
        grid=self.grid(); q={'x':0,'y':0,'goalX':2,'goalY':2}
        e.check_paths(grid,[q],[{'success':True,'cost':2.8284,'points':[[0,0],[1,1],[2,2]]}])
        with self.assertRaises(ValueError): e.check_paths(grid,[q],[{'success':True,'cost':1.,'points':[[0,0],[2,2]]}])
        grid['tiles'][1]['walkable']=False
        with self.assertRaises(ValueError): e.check_paths(grid,[q],[{'success':True,'cost':2.8284,'points':[[0,0],[1,1],[2,2]]}])
    def test_untrusted_map_rejected_before_oracle(self):
        # A negative-cost cycle used to keep Dijkstra relaxing forever.
        invalid=[]
        for value in (-1., 0., float('nan'), float('inf'), 1e308, 10**400, True):
            grid=self.grid();grid['tiles'][0]['moveCost']=value;invalid.append(grid)
        for value in (float('nan'), float('inf'), 1e308, -1e308, 10**400, True):
            grid=self.grid();grid['tiles'][0]['traversalHeight']=value;invalid.append(grid)
        for field,value in [('width',True),('width',129),('height',0),('tiles',[])]:
            grid=self.grid();grid[field]=value;invalid.append(grid)
        grid=self.grid();grid['tiles'][0]['walkable']=1;invalid.append(grid)
        q={'x':0,'y':0,'goalX':2,'goalY':2}
        path={'success':True,'cost':2.8284,'points':[[0,0],[1,1],[2,2]]}
        for grid in invalid:
            with self.subTest(grid=grid),patch.object(e,'_optimal_cost') as oracle:
                with self.assertRaises(ValueError):e.check_paths(grid,[q],[path])
                oracle.assert_not_called()

    def test_untrusted_query_and_path_types_rejected(self):
        q={'x':0,'y':0,'goalX':2,'goalY':2}
        path={'success':True,'cost':2.8284,'points':[[0,0],[1,1],[2,2]]}
        for value in (True, -1, 3, 1.5):
            bad=dict(q,x=value)
            with self.subTest(query=bad),self.assertRaises(ValueError):e.check_paths(self.grid(),[bad],[path])
        for field,value in [('success',1),('cost',True),('cost',float('nan')),('cost',float('inf')),
                            ('points',[[False,0],[1,1],[2,2]]),('points',[[0,0],[1.,1],[2,2]]),
                            ('points',[[0,0],[3,1],[2,2]]),('points',[[0,0]]*10)]:
            with self.subTest(field=field,value=value),self.assertRaises(ValueError):
                e.check_paths(self.grid(),[q],[dict(path,**{field:value})])

    def test_valid_unreachable_and_same_cell_paths(self):
        grid=self.grid();q={'x':0,'y':0,'goalX':2,'goalY':2}
        grid['tiles'][-1]['walkable']=False
        e.check_paths(grid,[q],[{'success':False,'cost':0.,'points':[]}])
        q['goalX']=q['goalY']=0
        e.check_paths(grid,[q],[{'success':True,'cost':0.,'points':[[0,0]]}])

    def test_shorter_road_route_beats_direct_grass(self):
        grid=self.grid()
        for t in grid['tiles'][3:6]: t['moveCost']=.6
        grid['tiles'][1]['moveCost']=1.5
        q={'x':0,'y':0,'goalX':2,'goalY':0}
        with self.assertRaises(ValueError): e.check_paths(grid,[q],[{'success':True,'cost':2.5,'points':[[0,0],[1,0],[2,0]]}])
    def test_summary_no_false_gain_for_identity_and_insufficient_pairs(self):
        self.assertFalse(e.paired_summary([{'referenceMs':2,'candidateMs':2}]*9)['improvementDetected'])
        self.assertFalse(e.paired_summary([{'referenceMs':2,'candidateMs':1}]*3)['improvementDetected'])
        self.assertTrue(e.paired_summary([{'referenceMs':2,'candidateMs':1}]*9)['improvementDetected'])
        identity={'coreSha256':'same','assemblySha256':'same'}
        self.assertFalse(e.report_summary([{'referenceMs':2,'candidateMs':1}]*9,identity,identity)['improvementDetected'])

    def report(self):
        suite={'schemaVersion':1,'split':'heldout','cases':[{'seed':i,'width':16,'height':16,'agents':4,'pathQueries':1,'batches':1,'ticksPerBatch':10} for i in range(3)]}
        value={'digests':['a','b'],'mapSha256':'grid','queries':[{'x':0,'y':0,'goalX':1,'goalY':1}],
               'stepMs':[1.],'pathMs':1.,'setupAndReleaseMs':0.,'wallMs':2.,'measuredTicks':10,
               'finalObservation':{'tick':15,'revision':2,'digest':'b','faulted':False}}
        cold=[{key:copy.deepcopy(value) for key in ('reference','candidate')} for _ in range(3)]
        samples=[{'case':i,'repeat':r,'reference':copy.deepcopy(value),'candidate':copy.deepcopy(value)} for i in range(3) for r in range(3)]
        report={'schemaVersion':e.REPORT_SCHEMA,'kind':e.REPORT_KIND,'correct':True,'failures':[],'samples':samples,'suite':suite,'coldEpisodes':cold,
                'suiteSha256':e.hashlib.sha256(json.dumps(suite,sort_keys=True).encode()).hexdigest(),'evaluatorSha256':e.sha(e.__file__),
                'clientSha256':e.sha(Path(e.__file__).with_name('online.py')),'referenceIdentity':{'coreSha256':'baseline'},'candidateIdentity':{'coreSha256':'candidate'},'repeats':3}
        self.retime(report);return report

    def retime(self,report):
        for pair in report['coldEpisodes']+report['samples']:
            for key in ('reference','candidate'):
                value=pair[key];value['wallMs']=sum(value['stepMs'])+value['pathMs']+value['setupAndReleaseMs']
        for sample in report['samples']:
            for key in ('reference','candidate'):
                sample[key+'Ms']=sample[key]['wallMs']+report['coldEpisodes'][sample['case']][key]['wallMs']/report['repeats']
        report['summary']=e.report_summary(report['samples'],report['referenceIdentity'],report['candidateIdentity'])

    def test_report_cannot_hide_bad_state_or_forge_score(self):
        report=self.report(); e.validate_report(report)
        report['samples'][0]['candidate']['digests']=['different']
        with self.assertRaises(ValueError): e.validate_report(report)

    def test_cold_and_setup_work_cannot_be_hidden(self):
        report=self.report()
        for pair in report['coldEpisodes']+report['samples']:
            for key in ('reference','candidate'):pair[key]['setupAndReleaseMs']=1000.
        self.retime(report);e.validate_report(report)
        report['coldEpisodes'][0]['candidate']['wallMs']=.001
        with self.assertRaises(ValueError): e.validate_report(report)
        for operation in ('remove','empty','incomplete'):
            report=self.report()
            if operation=='remove':report.pop('coldEpisodes')
            elif operation=='empty':report['coldEpisodes']=[]
            else:report['coldEpisodes'].pop()
            with self.subTest(operation=operation),self.assertRaises(ValueError):e.validate_report(report)
        for group in ('samples','coldEpisodes'):
            report=self.report();report[group][0]['candidate'].pop('setupAndReleaseMs')
            with self.subTest(group=group),self.assertRaises(ValueError):e.validate_report(report)
        report=self.report();report['summary']['medianSpeedup']=100
        with self.assertRaises(ValueError): e.validate_report(report)
        report=self.report();report['samples'].pop()
        with self.assertRaises(ValueError): e.validate_report(report)

    def test_legacy_report_requires_explicit_rerun(self):
        report=self.report();report.update(schemaVersion=1,kind='paired-efficiency-v1')
        with self.assertRaisesRegex(ValueError,'rerun legacy'):e.validate_report(report)

    def test_full_cost_ledger_rejects_missing_work_even_when_totals_match(self):
        for field,value in [('stepMs',[]),('digests',['b']),('queries',[]),('measuredTicks',0),
                            ('finalObservation',{'tick':15,'revision':2,'digest':'b','faulted':True})]:
            report=self.report();report['samples'][0]['candidate'][field]=value;self.retime(report)
            with self.subTest(field=field),self.assertRaises(ValueError):e.validate_report(report)

    def test_round_lineage_budget_and_control(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder)
            for name,text in [('agent0','v0'),('agent1','v1'),('patch','example patch')]: (root/name).write_text(text)
            for index in range(2):
                report=self.report(); report['candidateIdentity']['coreSha256']='candidate'+str(index)
                (root/('report'+str(index)+'.json')).write_text(json.dumps(report))
            (root/'control.json').write_text(json.dumps(self.report()))
            manifest={'schemaVersion':1,'budget':{'tokens':100,'wallSeconds':10,'attempts':1},'controlAgentArtifact':'agent0','controlAgentSha256':e.sha(root/'agent0'),'rounds':[]}
            for index in range(2):
                name='report'+str(index)+'.json'
                manifest['rounds'].append({'report':name,'reportSha256':e.sha(root/name),'controlReport':'control.json','controlReportSha256':e.sha(root/'control.json'),
                    'agentArtifact':'agent'+str(index),'agentSha256':e.sha(root/('agent'+str(index))),'parentAgentSha256':None if index==0 else e.sha(root/'agent0'),
                    'patch':'patch','patchSha256':e.sha(root/'patch'),'cost':{'tokens':20,'wallSeconds':1,'attempts':1},'controlCost':{'tokens':20,'wallSeconds':1,'attempts':1}})
            self.assertEqual(0,e.rounds(manifest,root)['improvingRounds'])
            # Synthetic timings test the decision rule, not measured model performance.
            for index in range(2):
                report=self.report(); report['candidateIdentity']['coreSha256']='candidate'+str(index)
                for sample in report['samples']:
                    sample['reference']['stepMs']=[2.]; sample['reference']['pathMs']=2.; sample['referenceMs']=4.
                    sample['candidate']['stepMs']=[.5/(2**index)]; sample['candidate']['pathMs']=.5/(2**index); sample['candidateMs']=1./(2**index)
                self.retime(report)
                name='report'+str(index)+'.json'; (root/name).write_text(json.dumps(report)); manifest['rounds'][index]['reportSha256']=e.sha(root/name)
            evidence=e.rounds(manifest,root)
            self.assertEqual(2,evidence['improvingRounds']); self.assertEqual(1,evidence['sequentialRuntimeImprovingRounds']); self.assertFalse(evidence['rsiClaim'])
            plateau=json.loads((root/'report0.json').read_text()); plateau['candidateIdentity']['coreSha256']='candidate1'
            (root/'report1.json').write_text(json.dumps(plateau)); manifest['rounds'][1]['reportSha256']=e.sha(root/'report1.json')
            self.assertEqual(0,e.rounds(manifest,root)['sequentialRuntimeImprovingRounds'])
            broken=copy.deepcopy(manifest); broken['rounds'][1]['parentAgentSha256']='invalid'
            with self.assertRaises(ValueError): e.rounds(broken,root)
            broken=copy.deepcopy(manifest); broken['rounds'][1]['cost']['tokens']=101
            with self.assertRaises(ValueError): e.rounds(broken,root)
            broken=copy.deepcopy(manifest); broken['controlAgentSha256']='invalid'
            with self.assertRaises(ValueError): e.rounds(broken,root)

if __name__=='__main__': unittest.main()
