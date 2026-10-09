import json, tempfile, unittest, zlib, struct
from pathlib import Path
import evaluate

class EvaluationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.folder = Path(self.temp.name)
        def chunk(kind, data): return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))
        png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", 8, 8, 8, 2, 0, 0, 0)) + chunk(b"IDAT", zlib.compress((b"\0" + b"\0" * 24) * 8)) + chunk(b"IEND", b"")
        (self.folder / "image.png").write_bytes(png)
        self.result = dict(schemaVersion=1, id="test", wallSeconds=.2, status="completed", measuredFrames=10, captures=[dict(frame=3, image="image.png", sha256=evaluate.digest(self.folder / "image.png"))], commands=[], engine="test", renderer="test", os="test", processor="test", videoAdapter="test", resolution=[8, 8], logicalResolution=[8,8], vsync="Disabled", fixedVisualDelta=1/60, state=dict(digest="same"), averageFps=50, frameMs=dict(p50=10,p95=20,p99=22,max=25))
        evaluate.write(self.folder / "request.json", dict(id="test", outputDirectory="ignored", seed=1, measuredFrames=10, captureFrames=[3], commands=[]))
        evaluate.write(self.folder / "questions.json", [dict(id="q1", image="image.png", sha256=evaluate.digest(self.folder / "image.png"), choices=["house", "wolf"])])
        evaluate.write(self.folder / "answer-key.json", [dict(id="q1", label="house")])
        evaluate.write(self.folder / "provenance.json", dict(execution=dict(maxFps=0)))
        self.save_result()
    def save_result(self): evaluate.write(self.folder / "result.json", self.result)
    def tearDown(self): self.temp.cleanup()
    def test_missing_answers_are_wrong(self):
        self.assertEqual(evaluate.score(self.folder, [dict(id="q1", label="house")])["accuracy"], 1)
        self.assertEqual(evaluate.score(self.folder, [])["accuracy"], 0)
    def test_duplicate_and_unknown_predictions_rejected(self):
        for predictions in ([dict(id="q1", label="house")]*2, [dict(id="other", label="house")], [dict(id="q1", label="alien")]):
            with self.assertRaises(ValueError): evaluate.score(self.folder, predictions)
    def test_tampered_capture_rejected(self):
        (self.folder / "image.png").write_bytes(b"changed")
        with self.assertRaises(ValueError): evaluate.validate_artifacts(self.folder)
    def test_different_scenario_not_comparable(self):
        other = self.folder / "candidate"; other.mkdir()
        import shutil
        for path in self.folder.iterdir():
            if path.is_file(): shutil.copy2(path, other / path.name)
        self.assertTrue(evaluate.compare(self.folder, other)["comparable"])
        request = evaluate.read(other / "request.json"); request["seed"] = 2; evaluate.write(other / "request.json", request)
        self.assertFalse(evaluate.compare(self.folder, other)["comparable"])
    def test_render_quality_difference_rejected(self):
        other = self.folder / "quality"; other.mkdir()
        import shutil
        for path in self.folder.iterdir():
            if path.is_file(): shutil.copy2(path, other / path.name)
        candidate = evaluate.read(other / "result.json"); candidate["renderQuality"] = {"shadowMode": "changed"}; evaluate.write(other / "result.json", candidate)
        result = evaluate.compare(self.folder, other)
        self.assertFalse(result["comparable"]); self.assertIn("render quality differs", result["reasons"])
    def test_modified_answer_file_rejected_with_provenance(self):
        evaluate.write(self.folder / "provenance.json", dict(artifactHashes={"answer-key.json": evaluate.digest(self.folder / "answer-key.json")}))
        evaluate.write(self.folder / "answer-key.json", [dict(id="q1", label="wolf")])
        with self.assertRaises(ValueError): evaluate.validate_artifacts(self.folder)
    def test_missing_capture_and_wrong_budget_rejected(self):
        self.result["measuredFrames"] = 9; self.save_result()
        with self.assertRaisesRegex(ValueError, "frame budget"): evaluate.validate_artifacts(self.folder)
        self.result["measuredFrames"] = 10; self.result["captures"] = []; self.save_result()
        with self.assertRaisesRegex(ValueError, "schedule"): evaluate.validate_artifacts(self.folder)
    def test_truncated_png_rejected_even_when_file_hash_matches(self):
        raw = (self.folder / "image.png").read_bytes()[:33]; (self.folder / "image.png").write_bytes(raw)
        self.result["captures"][0]["sha256"] = evaluate.digest(self.folder / "image.png"); self.save_result()
        with self.assertRaisesRegex(ValueError, "PNG"): evaluate.validate_artifacts(self.folder)
    def test_nonfinite_result_rejected(self):
        self.result["averageFps"] = float("nan"); self.save_result()
        with self.assertRaises(ValueError): evaluate.validate_artifacts(self.folder)
    def test_explicit_missing_engine_does_not_fall_back(self):
        with self.assertRaises(ValueError): evaluate.engine_path(str(self.folder / "does-not-exist"))
    def test_build_identity_rejects_stale_source_and_replaced_binary(self):
        root=self.folder/'project';source=root/'src/SandBoxSim.Godot/game.cs';source.parent.mkdir(parents=True)
        source.write_text('source',encoding='utf-8');assembly=root/'binary/SandBoxSim.Godot.dll';assembly.parent.mkdir()
        assembly.write_bytes(b'game');assembly.with_name('SandBoxSim.Core.dll').write_bytes(b'core')
        with self.assertRaises(ValueError):evaluate.validate_build(root,assembly)
        stamp={'schemaVersion':1,'configuration':'Debug','sourceFiles':evaluate.compiled_source_hashes(root),
               'assemblySha256':evaluate.digest(assembly),'coreSha256':evaluate.digest(assembly.with_name('SandBoxSim.Core.dll'))}
        evaluate.write(assembly.with_name('build-source.json'),stamp);evaluate.validate_build(root,assembly)
        source.write_text('changed',encoding='utf-8')
        with self.assertRaises(ValueError):evaluate.validate_build(root,assembly)
        source.write_text('source',encoding='utf-8');assembly.write_bytes(b'replaced')
        with self.assertRaises(ValueError):evaluate.validate_build(root,assembly)
    def test_private_engine_layouts_for_all_platforms(self):
        files = {"win32": self.folder / "win/Godot_console.exe", "darwin": self.folder / "mac/Godot.app/Contents/MacOS/Godot", "linux": self.folder / "linux/Godot_v4.7.2-stable_mono_linux.x86_64"}
        for path in files.values(): path.parent.mkdir(parents=True); path.write_bytes(b"fixture"); path.chmod(0o700)
        for platform, path in files.items(): self.assertEqual(evaluate.installed_engines(self.folder, platform), [path])
    def test_json_cli_errors_and_malformed_predictions(self):
        import subprocess,sys
        predictions = self.folder / "bad.json"; evaluate.write(predictions, [dict(id=[],label="house")])
        command = [sys.executable,str(Path(evaluate.__file__)),"--json-errors","score",str(self.folder),str(predictions)]
        result = subprocess.run(command,capture_output=True,text=True,encoding="utf-8")
        self.assertEqual(result.returncode,2); self.assertEqual(json.loads(result.stderr)["error"]["code"],"invalid_evaluation")
        result = subprocess.run([sys.executable,str(Path(evaluate.__file__)),"--json-errors","unknown"],capture_output=True,text=True,encoding="utf-8")
        self.assertEqual(result.returncode,2); self.assertEqual(json.loads(result.stderr)["error"]["code"],"invalid_arguments")
    def test_failed_ui_and_incomplete_trace_rejected(self):
        event = dict(frame=3, command="settings.open", value="")
        request = evaluate.read(self.folder / "request.json"); request["commands"] = [event]; evaluate.write(self.folder / "request.json", request)
        with self.assertRaises(ValueError): evaluate.validate_artifacts(self.folder)
        self.result["commands"] = [event | dict(before={}, state=dict(settingsVisible=False), simulationUnchanged=True)]; self.save_result()
        with self.assertRaises(ValueError): evaluate.validate_artifacts(self.folder)

if __name__ == "__main__": unittest.main()
