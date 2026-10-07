import copy, json, tempfile, unittest
from pathlib import Path
import efficiency as e

class EfficiencyTests(unittest.TestCase):
    def grid(self):
        return {'width':3,'height':3,'tiles':[{'walkable':True,'moveCost':1.,'traversalHeight':0.} for _ in range(9)]}
    def test_independent_path_oracle(self):
        grid=self.grid(); q={'x':0,'y':0,'goalX':2,'goalY':2}
        e.check_paths(grid,[q],[{'success':True,'cost':2.8284,'points':[[0,0],[1,1],[2,2]]}])
        with self.assertRaises(ValueError): e.check_paths(grid,[q],[{'success':True,'cost':1.,'points':[[0,0],[2,2]]}])
        grid['tiles'][1]['walkable']=False
        with self.assertRaises(ValueError): e.check_paths(grid,[q],[{'success':True,'cost':2.8284,'points':[[0,0],[1,1],[2,2]]}])
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

    def report(self):
        suite={'schemaVersion':1,'split':'heldout','cases':[{'seed':i} for i in range(3)]}
        value={'digests':['a','b'],'mapSha256':'grid','queries':[],'stepMs':[1.],'pathMs':1.}
        samples=[{'case':i,'repeat':r,'referenceMs':2.,'candidateMs':2.,'reference':copy.deepcopy(value),'candidate':copy.deepcopy(value)} for i in range(3) for r in range(3)]
        return {'schemaVersion':1,'kind':'paired-efficiency-v1','correct':True,'failures':[],'samples':samples,'summary':e.paired_summary(samples),'suite':suite,
                'suiteSha256':e.hashlib.sha256(json.dumps(suite,sort_keys=True).encode()).hexdigest(),'evaluatorSha256':e.sha(e.__file__),
                'clientSha256':e.sha(Path(e.__file__).with_name('online.py')),'referenceIdentity':{'coreSha256':'baseline'},'candidateIdentity':{'coreSha256':'candidate'},'repeats':3}
    def test_report_cannot_hide_bad_state_or_forge_score(self):
        report=self.report(); e.validate_report(report)
        report['samples'][0]['candidate']['digests']=['different']
        with self.assertRaises(ValueError): e.validate_report(report)
        report=self.report(); report['summary']['medianSpeedup']=100
        with self.assertRaises(ValueError): e.validate_report(report)
        report=self.report(); report['samples'].pop()
        with self.assertRaises(ValueError): e.validate_report(report)

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
            broken=copy.deepcopy(manifest); broken['rounds'][1]['parentAgentSha256']='invalid'
            with self.assertRaises(ValueError): e.rounds(broken,root)
            broken=copy.deepcopy(manifest); broken['rounds'][1]['cost']['tokens']=101
            with self.assertRaises(ValueError): e.rounds(broken,root)
            broken=copy.deepcopy(manifest); broken['controlAgentSha256']='invalid'
            with self.assertRaises(ValueError): e.rounds(broken,root)

if __name__=='__main__': unittest.main()
