import json, tempfile, unittest, zlib, struct
from pathlib import Path
import evaluate

class EvaluationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.folder = Path(self.temp.name)
        def chunk(kind, data): return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))
        png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", 8, 8, 8, 2, 0, 0, 0)) + chunk(b"IDAT", zlib.compress((b"\0" + b"\0" * 24) * 8)) + chunk(b"IEND", b"")
        (self.folder / "image.png").write_bytes(png)
        self.result = dict(schemaVersion=1, status="completed", measuredFrames=10, captures=[dict(image="image.png", sha256=evaluate.digest(self.folder / "image.png"))], commands=[], engine="test", renderer="test", os="test", processor="test", videoAdapter="test", resolution=[8, 8], logicalResolution=[8,8], vsync="Disabled", fixedVisualDelta=1/60, state=dict(digest="same"), averageFps=50, frameMs=dict(p95=20))
        evaluate.write(self.folder / "request.json", dict(id="test", outputDirectory="ignored", seed=1, commands=[]))
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
    def test_failed_ui_and_incomplete_trace_rejected(self):
        event = dict(frame=3, command="settings.open", value="")
        evaluate.write(self.folder / "request.json", dict(commands=[event]))
        with self.assertRaises(ValueError): evaluate.validate_artifacts(self.folder)
        self.result["commands"] = [event | dict(before={}, state=dict(settingsVisible=False), simulationUnchanged=True)]; self.save_result()
        with self.assertRaises(ValueError): evaluate.validate_artifacts(self.folder)

if __name__ == "__main__": unittest.main()
