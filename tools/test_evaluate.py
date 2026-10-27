import json, tempfile, unittest, zlib, struct, os, signal, sys, io, shutil
from contextlib import redirect_stdout
from pathlib import Path
from types import SimpleNamespace
from unittest import mock
import evaluate

class EvaluationTests(unittest.TestCase):
    def test_phase_timings_keep_valid_records_among_mixed_logs(self):
        first = dict(phase="world_initialized", elapsedMs=15, frame=0)
        last = dict(phase="capture_complete", elapsedMs=1650, frame=4)
        logs = (b"Godot Engine\n\xff driver diagnostic\n" +
                ("EVALUATION_PHASE " + json.dumps(first) + "\r\n").encode() +
                b"ERROR: unrelated diagnostic\nEVALUATION_PHASE {truncated\n" +
                ("EVALUATION_PHASE " + json.dumps(last) + "\nEVALUATION_COMPLETE output\n").encode())
        self.assertEqual(evaluate.phase_timings(logs), [first, last])

    def test_phase_timings_ignore_malformed_records(self):
        invalid = [None, [], "scene_ready", {},
                   dict(phase=[], elapsedMs=10, frame=0),
                   dict(phase="invalid phase", elapsedMs=10, frame=0),
                   dict(phase="scene_ready", elapsedMs=-1, frame=0),
                   dict(phase="scene_ready", elapsedMs=True, frame=0),
                   dict(phase="scene_ready", elapsedMs=float("nan"), frame=0),
                   dict(phase="scene_ready", elapsedMs=10, frame=False),
                   dict(phase="scene_ready", elapsedMs=10, frame=-1),
                   dict(phase="scene_ready", elapsedMs=10, frame="0")]
        logs = "\n".join("EVALUATION_PHASE " + json.dumps(value) for value in invalid)
        logs += '\nnoise EVALUATION_PHASE {"phase":"scene_ready","elapsedMs":10,"frame":0}'
        logs += '\nEVALUATION_PHASE ' + '[' * 2000 + ']' * 2000
        self.assertEqual(evaluate.phase_timings(logs.encode()), [])

    def test_run_real_timeout_with_complete_artifacts_stays_124(self):
        root = self.folder / "runner-project"
        assembly = root / "src/SandBoxSim.Godot/.godot/mono/temp/bin/Debug/SandBoxSim.Godot.dll"
        assembly.parent.mkdir(parents=True)
        assembly.write_bytes(b"game")
        assembly.with_name("SandBoxSim.Core.dll").write_bytes(b"core")
        evaluate.write(assembly.with_name("build-source.json"), dict(
            schemaVersion=1, configuration="Debug", sourceFiles=evaluate.compiled_source_hashes(root),
            assemblySha256=evaluate.digest(assembly), coreSha256=evaluate.digest(assembly.with_name("SandBoxSim.Core.dll"))))
        output = root / "result"
        last = dict(phase="quit_requested", elapsedMs=50, frame=13)
        original_wait = evaluate.wait_for_engine
        def completed_but_running(command, cwd, env, timeout):
            # The engine stand-in has completed every requested artifact but stays alive.
            request = next(arg.split("=", 1)[1] for arg in command if arg.startswith("--evaluation="))
            Path(command[command.index("--log-file")+1]).write_text("native engine log", encoding="utf-8")
            shutil.copy2(request, output / "request.json")
            for name in ("image.png", "result.json", "questions.json", "answer-key.json"):
                shutil.copy2(self.folder / name, output / name)
            script = "import time; print(" + repr("EVALUATION_PHASE " + json.dumps(last)) + ",flush=True); time.sleep(30)"
            return original_wait([sys.executable, "-c", script], cwd, env, timeout)
        args = SimpleNamespace(scenario=self.folder / "request.json", output=output, engine=sys.executable,
                               timeout=1, max_fps=60, resolution="1440x900", audio_driver="Dummy", label="test")
        captured = io.StringIO()
        with mock.patch.object(evaluate, "ROOT", root), \
             mock.patch.object(evaluate, "assert_dotnet_engine"), \
             mock.patch.object(evaluate, "tool_environment", return_value=os.environ.copy()), \
             mock.patch.object(evaluate, "dotnet_executable", return_value=None), \
             mock.patch.object(evaluate, "git", side_effect=lambda *args: "1" * 40 if args[0] == "rev-parse" else ""), \
             mock.patch.object(evaluate, "wait_for_engine", side_effect=completed_but_running), redirect_stdout(captured):
            code = evaluate.run(args)
        summary = json.loads(captured.getvalue())
        provenance = evaluate.read(output / "provenance.json")
        self.assertEqual(code, 124)
        self.assertEqual(summary["exitCode"], 124)
        self.assertNotEqual(summary["engineExitCode"], 0)
        self.assertTrue(summary["timedOut"])
        self.assertEqual(summary["artifactValidation"], "passed")
        self.assertEqual(summary["lastPhase"], last)
        self.assertEqual(provenance["phaseTimings"], [last])
        self.assertEqual(provenance["exitCode"], 124)
        self.assertEqual(provenance["execution"], dict(maxFps=60, resolution="1440x900", disableVsync=True, audioDriver="Dummy", userDataPolicy="external"))
        self.assertEqual(evaluate.validate_artifacts(output)["status"], "completed")

    def test_locked_native_log_preserves_real_timeout_and_phase_evidence(self):
        with mock.patch.object(Path, "unlink", side_effect=PermissionError("log still held by native process")):
            self.test_run_real_timeout_with_complete_artifacts_stays_124()
        output=self.folder / "runner-project/result"
        provenance=evaluate.read(output / "provenance.json")
        self.assertEqual(provenance["nativeLogCleanupError"], "PermissionError")
        retained=Path(provenance["nativeLogRetained"])
        self.assertEqual(retained.read_text(encoding="utf-8"), "native engine log")
        self.assertIn("quit_requested", (output / "engine.log").read_text(encoding="utf-8"))

    def test_window_restoration_count_is_bounded_and_typed(self):
        for invalid in (True,-1,2,.5,"1",None):
            with self.subTest(invalid=invalid):
                self.result["captureWindowRestores"]=invalid;self.save_result()
                with self.assertRaisesRegex(ValueError,"window restoration"):evaluate.validate_artifacts(self.folder)
        self.result["captureWindowRestores"]=1;self.save_result()
        self.assertEqual(evaluate.validate_artifacts(self.folder)["captureWindowRestores"],1)

    def test_restored_capture_is_valid_for_vision_but_not_timing_comparison(self):
        with tempfile.TemporaryDirectory(dir=self.folder) as temporary:
            other=Path(temporary)
            for path in self.folder.iterdir():
                if path.is_file():shutil.copy2(path,other/path.name)
            candidate=evaluate.read(other/"result.json");candidate["captureWindowRestores"]=1
            evaluate.write(other/"result.json",candidate)
            result=evaluate.compare(self.folder,other)
            self.assertFalse(result["comparable"]);self.assertIsNone(result["fpsRatio"])
            self.assertIn("capture window was restored; recapture a stable window for timing",result["reasons"])

    def test_engine_exit_does_not_wait_for_inherited_log_handles(self):
        # A real descendant retains both handles after the engine stand-in exits.
        # communicate(PIPE) would time out; the engine itself exits successfully.
        child_pid = None
        try:
            script = "import subprocess,sys; p=subprocess.Popen([sys.executable,'-c','import time; time.sleep(30)']); print(p.pid,flush=True)"
            native, timed_out, logs = evaluate.wait_for_engine([sys.executable,'-c',script], self.folder, os.environ.copy(), 3)
            child_pid = int(logs.strip())
            self.assertEqual(native, 0)
            self.assertFalse(timed_out)
        finally:
            if child_pid is not None:
                try: os.kill(child_pid, signal.SIGTERM)
                except ProcessLookupError: pass

    def test_engine_real_timeout_remains_failure_and_keeps_logs(self):
        script = "import sys,time; print('started',flush=True); print('diagnostic',file=sys.stderr,flush=True); time.sleep(30)"
        native, timed_out, logs = evaluate.wait_for_engine([sys.executable,'-c',script], self.folder, os.environ.copy(), 1)
        self.assertTrue(timed_out)
        self.assertNotEqual(native, 0)
        self.assertIn(b'started', logs)
        self.assertIn(b'diagnostic', logs)

    def test_engine_failure_preserves_exit_code_and_stderr(self):
        native, timed_out, logs = evaluate.wait_for_engine([sys.executable,'-c',"import sys; print('failed',file=sys.stderr); sys.exit(2)"], self.folder, os.environ.copy(), 3)
        self.assertEqual(native, 2)
        self.assertFalse(timed_out)
        self.assertIn(b'failed', logs)

    def test_zero_exit_complete_artifacts_still_reject_runtime_errors(self):
        root = self.folder / "error-runner"
        assembly = root / "src/SandBoxSim.Godot/.godot/mono/temp/bin/Debug/SandBoxSim.Godot.dll"
        assembly.parent.mkdir(parents=True)
        assembly.write_bytes(b"game"); assembly.with_name("SandBoxSim.Core.dll").write_bytes(b"core")
        evaluate.write(assembly.with_name("build-source.json"), dict(schemaVersion=1, configuration="Debug",
            sourceFiles=evaluate.compiled_source_hashes(root), assemblySha256=evaluate.digest(assembly),
            coreSha256=evaluate.digest(assembly.with_name("SandBoxSim.Core.dll"))))
        for i, error in enumerate((b"ERROR: Failed to read the root certificate store.", b"ERROR: Can't create shader cache folder")):
            with self.subTest(error=error):
                output = root / ("result-" + str(i))
                def failed_log(command, cwd, env, timeout):
                    request = next(arg.split("=", 1)[1] for arg in command if arg.startswith("--evaluation="))
                    shutil.copy2(request, output / "request.json")
                    for name in ("image.png", "result.json", "questions.json", "answer-key.json"):
                        shutil.copy2(self.folder / name, output / name)
                    return 0, False, error
                args = SimpleNamespace(scenario=self.folder / "request.json", output=output, engine=sys.executable,
                    timeout=3, max_fps=60, resolution="1440x900", audio_driver="Dummy", label="error-test")
                with mock.patch.object(evaluate, "ROOT", root), mock.patch.object(evaluate, "assert_dotnet_engine"), \
                     mock.patch.object(evaluate, "tool_environment", return_value=os.environ.copy()), \
                     mock.patch.object(evaluate, "dotnet_executable", return_value=None), \
                     mock.patch.object(evaluate, "git", side_effect=lambda *a: "1" * 40 if a[0] == "rev-parse" else ""), \
                     mock.patch.object(evaluate, "wait_for_engine", side_effect=failed_log), redirect_stdout(io.StringIO()):
                    self.assertEqual(evaluate.run(args), 2)
                provenance = evaluate.read(output / "provenance.json")
                self.assertEqual(provenance["engineExitCode"], 0)
                self.assertFalse(provenance["timedOut"])
                self.assertEqual(provenance["artifactValidation"], "Engine reported an error")

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
    def test_portrait_window_commands_and_escape_precedence(self):
        request=evaluate.read(self.folder/'request.json')
        base=dict(journalVisible=True,settingsVisible=False,portraitExpanded=False)
        events=[dict(frame=3,command='portrait.expand',value=''),dict(frame=4,command='escape',value='')]
        request['commands']=events;evaluate.write(self.folder/'request.json',request)
        expanded=base|dict(portraitExpanded=True)
        self.result['commands']=[events[0]|dict(before=base,state=expanded,simulationUnchanged=True),events[1]|dict(before=expanded,state=base,simulationUnchanged=True)]
        self.save_result();evaluate.validate_artifacts(self.folder)
        self.result['commands'][1]['state']=base|dict(journalVisible=False)
        self.save_result()
        with self.assertRaisesRegex(ValueError,'Portrait Escape'):evaluate.validate_artifacts(self.folder)
    def test_portrait_window_rejects_unperformed_transition(self):
        request=evaluate.read(self.folder/'request.json')
        event=dict(frame=3,command='portrait.expand',value='');request['commands']=[event]
        evaluate.write(self.folder/'request.json',request)
        state=dict(journalVisible=True,settingsVisible=False,portraitExpanded=False)
        self.result['commands']=[event|dict(before=state,state=state,simulationUnchanged=True)]
        self.save_result()
        with self.assertRaisesRegex(ValueError,'window transition'):evaluate.validate_artifacts(self.folder)
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
    def test_package_configuration_is_part_of_canonical_build_identity(self):
        root=self.folder/'configured-project';root.mkdir()
        package_config=root/'NuGet.config';package_config.write_text('<clear/>',encoding='utf-8')
        assembly=root/'SandBoxSim.Godot.dll';assembly.write_bytes(b'game')
        assembly.with_name('SandBoxSim.Core.dll').write_bytes(b'core')
        sources=evaluate.compiled_source_hashes(root)
        self.assertIn('NuGet.config',sources)
        self.assertNotIn('NuGet.Config',sources)
        evaluate.write(assembly.with_name('build-source.json'),{
            'schemaVersion':1,'configuration':'Debug','sourceFiles':sources,
            'assemblySha256':evaluate.digest(assembly),'coreSha256':evaluate.digest(assembly.with_name('SandBoxSim.Core.dll'))})
        evaluate.validate_build(root,assembly)
        package_config.write_text('<changed-source/>',encoding='utf-8')
        with self.assertRaisesRegex(ValueError,'build identity'):evaluate.validate_build(root,assembly)
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
