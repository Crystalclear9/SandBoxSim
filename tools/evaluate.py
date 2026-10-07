"""Local evaluation runner, artifact verifier, vision scorer and paired comparison."""
from __future__ import annotations
import argparse, hashlib, json, os, platform, re, shutil, signal, struct, zlib, subprocess, sys, time
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]

def read(path):
    def invalid_constant(value): raise ValueError("Nonfinite JSON constant: " + value)
    return json.loads(Path(path).read_text(encoding="utf-8-sig"), parse_constant=invalid_constant)

def write(path, value):
    Path(path).write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()

def git(*args):
    result = subprocess.run(["git", *args], cwd=ROOT, text=True, encoding="utf-8", capture_output=True, check=True)
    return result.stdout.strip()

def engine_path(value):
    if value:
        path = Path(value)
        if sys.platform == "darwin" and path.is_dir(): path = path / "Contents/MacOS/Godot"
        if path.is_file(): return str(path.resolve())
        found = shutil.which(value)
        if found: return found
        raise ValueError("Explicit Godot executable does not exist: " + str(value))
    home = Path.home() / ".sandboxsim-tool" / "godot-4.7.2"
    matches = installed_engines(home, sys.platform)
    if matches: return str(matches[0])
    for name in ("godot-mono", "godot", "godot4"):
        found = shutil.which(name)
        if found: return found
    raise ValueError("Godot .NET executable missing; use --engine or GODOT_EXE")

def installed_engines(home, platform_name):
    if platform_name == "win32": matches = list(home.glob("**/*console.exe"))
    elif platform_name == "darwin": matches = list(home.glob("**/Contents/MacOS/Godot"))
    else: matches = [p for p in home.glob("**/Godot*") if p.is_file() and re.fullmatch(r"Godot.*(?:x86_64|arm64)", p.name)]
    return sorted(p for p in matches if os.access(p, os.X_OK))

def validate_png(raw):
    if len(raw) < 33 or raw[:8] != b"\x89PNG\r\n\x1a\n": raise ValueError("Invalid PNG header")
    offset = 8; has_image = False; first = True
    while offset + 12 <= len(raw):
        length = struct.unpack(">I", raw[offset:offset+4])[0]; end = offset+12+length
        if end > len(raw): raise ValueError("Truncated PNG chunk")
        kind = raw[offset+4:offset+8]; payload = raw[offset+8:offset+8+length]
        crc = struct.unpack(">I", raw[end-4:end])[0]
        if zlib.crc32(kind+payload) != crc: raise ValueError("PNG checksum invalid")
        if first:
            if kind != b"IHDR" or length != 13 or min(struct.unpack(">II", payload[:8])) < 8: raise ValueError("Invalid PNG dimensions")
            first = False
        if kind == b"IDAT": has_image = True
        if kind == b"IEND":
            if length or not has_image or end != len(raw): raise ValueError("Invalid PNG end")
            return
        offset = end
    raise ValueError("PNG missing image or end")

def object_value(value, label):
    if not isinstance(value, dict): raise ValueError(label + " must be an object")
    return value

def portable_name(value):
    if not isinstance(value, str) or not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]*", value):
        raise ValueError("Artifact name must be a portable basename")
    return value

def finite_number(value, label, minimum=0):
    import math
    if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value) or value < minimum:
        raise ValueError(label + " must be a finite number")
    return value

def validate_artifacts(folder):
    folder = Path(folder)
    if (folder / "provenance.json").exists():
        for name, expected in read(folder / "provenance.json").get("artifactHashes", {}).items():
            if Path(name).name != name or digest(folder / name) != expected: raise ValueError("Recorded artifact checksum mismatch")
    result = object_value(read(folder / "result.json"), "Result")
    if result.get("schemaVersion") != 1 or result.get("measuredFrames", 0) < 1:
        raise ValueError("Missing or unsupported completed result")
    request = object_value(read(folder / "request.json"), "Request")
    if result.get("id") != request.get("id") or result["measuredFrames"] != request.get("measuredFrames"):
        raise ValueError("Result does not match requested identity or frame budget")
    fps = finite_number(result["averageFps"], "FPS")
    finite_number(result["wallSeconds"], "Wall time", .000001)
    for name in ("p50", "p95", "p99", "max"): finite_number(result["frameMs"][name], name)
    threshold = finite_number(request.get("minimumFps", 0), "FPS threshold")
    expected_status = "completed" if fps >= threshold else "performance_threshold_failed"
    if result.get("status") != expected_status: raise ValueError("Result status disagrees with FPS threshold")
    captures = result["captures"]
    if not isinstance(captures, list): raise ValueError("Captures must be an array")
    frames = [object_value(c, "Capture")["frame"] for c in captures]
    if len(frames) != len(set(frames)) or set(frames) != set(request.get("captureFrames", [])):
        raise ValueError("Capture schedule incomplete or duplicated")
    for sample in captures:
        name = portable_name(sample["image"])
        if digest(folder / name) != sample["sha256"]: raise ValueError("Capture checksum mismatch")
        validate_png((folder / name).read_bytes())
    expected_commands = request.get("commands", [])
    actual = result["commands"]
    if len(expected_commands) != len(actual): raise ValueError("Command trace incomplete")
    for expected, observed in zip(sorted(expected_commands, key=lambda e: e["frame"]), actual):
        if any(expected[k] != observed[k] for k in ("frame", "command", "value")) or not observed["simulationUnchanged"]:
            raise ValueError("Command trace or observation purity failed")
        before, after, command = observed["before"], observed["state"], observed["command"]
        if command == "settings.open" and not after["settingsVisible"]: raise ValueError("Settings failed to open")
        if command == "journal.open" and not after["journalVisible"]: raise ValueError("Journal failed to open")
        if command == "escape":
            if before["settingsVisible"] and (after["settingsVisible"] or after["journalVisible"] != before["journalVisible"]): raise ValueError("Escape precedence failed")
            if not before["settingsVisible"] and before["journalVisible"] and after["journalVisible"]: raise ValueError("Journal did not close")
        if command in ("portrait.face", "portrait.hands", "portrait.body") and after["portrait"]["framing"] != command.split(".")[1]: raise ValueError("Portrait framing failed")
        if command == "portrait.reset" and (after["portrait"]["pitch"] != 0 or after["portrait"]["dragging"] or abs(after["portrait"]["yaw"] + .28) > .001): raise ValueError("Portrait reset failed")
        if command == "portrait.drag" and after["portrait"]["dragging"]: raise ValueError("Portrait drag not released")
    questions = read(folder / "questions.json"); keys = read(folder / "answer-key.json")
    if not isinstance(questions, list) or not isinstance(keys, list): raise ValueError("Questions and answers must be arrays")
    ids = [object_value(q, "Question")["id"] for q in questions]
    if len(set(ids)) != len(ids) or set(ids) != {a["id"] for a in keys} or len(keys) != len(ids):
        raise ValueError("Question/answer IDs disagree")
    for q in questions:
        name = portable_name(q["image"])
        raw = (folder / name).read_bytes()
        if digest(folder / name) != q["sha256"]: raise ValueError("Visual sample checksum mismatch")
        validate_png(raw)
    return result

def score(folder, predictions):
    validate_artifacts(folder)
    answers = read(Path(folder) / "answer-key.json")
    expected = {a["id"]: a["label"] for a in answers}
    questions = {q["id"]: q for q in read(Path(folder) / "questions.json")}
    guesses = {}
    if not isinstance(predictions, list): raise ValueError("Predictions must be an array")
    for item in predictions:
        if not isinstance(item, dict) or set(item) != {"id", "label"}: raise ValueError("Expected id and label")
        if not isinstance(item["id"], str) or not isinstance(item["label"], str): raise ValueError("Prediction ID and label must be strings")
        if item["id"] in guesses or item["id"] not in expected: raise ValueError("Duplicate or unknown prediction ID")
        if item["label"] not in questions[item["id"]]["choices"]: raise ValueError("Prediction outside allowed labels")
        guesses[item["id"]] = item["label"]
    correct = sum(guesses.get(k) == v for k, v in expected.items())
    return {"schemaVersion": 1, "total": len(expected), "answered": len(guesses), "correct": correct,
            "accuracy": correct / len(expected) if expected else None, "missing": sorted(set(expected) - set(guesses))}

def compare(baseline, candidate):
    old = validate_artifacts(baseline); new = validate_artifacts(candidate)
    def spec(folder):
        value = read(Path(folder) / "request.json")
        return {k: v for k, v in value.items() if k not in ("id", "outputDirectory")}
    reasons = []
    if spec(baseline) != spec(candidate): reasons.append("scenario configuration differs")
    for key in ("engine", "renderer", "os", "processor", "videoAdapter", "resolution", "logicalResolution", "vsync", "fixedVisualDelta", "measuredFrames"):
        if old[key] != new[key]: reasons.append(key + " differs")
    if read(Path(baseline) / "provenance.json")["execution"] != read(Path(candidate) / "provenance.json")["execution"]: reasons.append("runner execution options differ")
    if old.get("renderQuality") != new.get("renderQuality"): reasons.append("render quality differs")
    if old["state"]["digest"] != new["state"]["digest"]: reasons.append("simulation digest differs")
    return {"schemaVersion": 1, "comparable": not reasons, "reasons": reasons,
            "fpsRatio": new["averageFps"] / old["averageFps"] if not reasons and old["averageFps"] > 0 else None,
            "p95MsChange": new["frameMs"]["p95"] - old["frameMs"]["p95"] if not reasons else None,
            "baselineFps": old["averageFps"], "candidateFps": new["averageFps"]}

def source_hashes():
    files = git("ls-files", "--cached", "--others", "--exclude-standard").splitlines()
    suffixes = {".cs", ".csproj", ".godot", ".py", ".ps1", ".json", ".props"}
    return {name: digest(ROOT / name) for name in sorted(set(files)) if Path(name).suffix in suffixes and (ROOT / name).is_file()}

def assert_dotnet_engine(executable, env):
    probe = subprocess.run([executable, "--headless", "--version"], env=env, capture_output=True, timeout=15)
    version = (probe.stdout + probe.stderr).decode("utf-8", errors="replace")
    if probe.returncode != 0 or not re.search(r"\b4\.7\.2[.-].*mono", version):
        raise ValueError("Godot 4.7.2 .NET editor required; executable version does not match")

def run(args):
    spec = object_value(read(args.scenario), "Scenario")
    spec.setdefault("id", "sample")
    if not isinstance(spec.get("id"), str) or not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]{0,63}", spec["id"]): raise ValueError("Scenario ID must be a safe 1-64 character identifier")
    if args.timeout <= 0 or args.max_fps < 0: raise ValueError("Timeout must be positive and max FPS nonnegative")
    stamp = time.strftime("%Y%m%d-%H%M%S")
    output = Path(args.output or ROOT / "runs" / "evaluation" / (spec["id"] + "-" + stamp)).resolve()
    executable = engine_path(args.engine or os.environ.get("GODOT_EXE"))
    assembly = ROOT / "src/SandBoxSim.Godot/.godot/mono/temp/bin/Debug/SandBoxSim.Godot.dll"
    if not assembly.is_file(): raise ValueError("Debug client assembly missing; run tools/godot.ps1 -Mode build first")
    env = os.environ.copy(); dotnet = Path.home() / ".sandboxsim-tool" / "net8"
    if dotnet.exists(): env["PATH"] = str(dotnet) + os.pathsep + env.get("PATH", "")
    assert_dotnet_engine(executable, env)
    output.mkdir(parents=True, exist_ok=False)
    # Engine refuses nonempty outputs. Stage the immutable input beside this output.
    staging = output.parent / (output.name + ".request.json")
    if staging.exists(): raise ValueError("Request staging path already exists")
    spec["outputDirectory"] = str(output); write(staging, spec)
    command = [executable, "--path", str(ROOT / "src/SandBoxSim.Godot"), "--resolution", args.resolution, "--max-fps", str(args.max_fps), "--disable-vsync", "--", "--evaluation=" + str(staging)]
    if args.audio_driver: command[command.index("--"):command.index("--")] = ["--audio-driver", args.audio_driver]
    provenance = {"schemaVersion": 1, "sourceCommit": git("rev-parse", "HEAD"), "sourceDirty": bool(git("status", "--porcelain")),
                  "runnerSha256": digest(__file__), "scenarioSha256": digest(args.scenario), "engineSha256": digest(executable),
                  "sourceFiles": source_hashes(), "assemblySha256": digest(assembly), "execution": {"maxFps": args.max_fps, "resolution": args.resolution, "disableVsync": True, "audioDriver": args.audio_driver or "system-default"}, "candidateLabel": args.label, "python": platform.python_version(), "command": command, "startedUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())}
    started = time.monotonic()
    process = subprocess.Popen(command, cwd=ROOT, env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                               creationflags=subprocess.CREATE_NEW_PROCESS_GROUP if os.name == "nt" else 0,
                               start_new_session=os.name != "nt")
    try:
        stdout, stderr = process.communicate(timeout=args.timeout)
        native_code = process.returncode; code = native_code if native_code in (0,1,2) else 2; logs = stdout + stderr
    except subprocess.TimeoutExpired:
        if os.name == "nt": subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"], capture_output=True)
        else:
            try: os.killpg(process.pid, signal.SIGKILL)
            except ProcessLookupError: pass
        stdout, stderr = process.communicate(); native_code = process.returncode; code = 124; logs = stdout + stderr
    (output / "engine.log").write_bytes(logs)
    provenance.update(engineExitCode=native_code,exitCode=code, wallSeconds=time.monotonic() - started, sourceChangedDuringRun=source_hashes() != provenance["sourceFiles"])
    if provenance["sourceChangedDuringRun"] and code == 0: code = 2
    try:
        result = validate_artifacts(output)
        if code != 124 and (native_code not in (0,1) or (native_code == 0) != (result["status"] == "completed")):
            raise ValueError("Engine exit disagrees with result status")
        if provenance["sourceChangedDuringRun"]: raise ValueError("Source changed during evaluation")
        if b"ERROR:" in logs: raise ValueError("Engine reported an error")
        provenance["artifactValidation"] = "passed"
        provenance["artifactHashes"] = {p.name: digest(p) for p in sorted(output.iterdir()) if p.is_file()}
    except (ValueError, KeyError, TypeError, AttributeError, OSError, struct.error, json.JSONDecodeError) as exc:
        provenance["artifactValidation"] = str(exc)
        if code != 124: code = 2
    provenance["exitCode"] = code
    write(output / "provenance.json", provenance)
    print(json.dumps({"output": str(output), "exitCode": code, "artifactValidation": provenance["artifactValidation"]}))
    return code

class ProtocolParser(argparse.ArgumentParser):
    def error(self, message):
        if "--json-errors" in sys.argv:
            print(json.dumps({"schemaVersion": 1, "error": {"code": "invalid_arguments", "message": message}}), file=sys.stderr)
            raise SystemExit(2)
        super().error(message)

def main():
    parser = ProtocolParser(description=__doc__); parser.add_argument("--json-errors", action="store_true"); commands = parser.add_subparsers(dest="command", required=True)
    runner = commands.add_parser("run"); runner.add_argument("scenario", type=Path); runner.add_argument("--engine"); runner.add_argument("--output"); runner.add_argument("--resolution", default="1440x900"); runner.add_argument("--max-fps", type=int, default=0); runner.add_argument("--timeout", type=int, default=180); runner.add_argument("--label", default="unlabelled"); runner.add_argument("--audio-driver")
    scorer = commands.add_parser("score"); scorer.add_argument("folder"); scorer.add_argument("predictions", type=Path)
    comparer = commands.add_parser("compare"); comparer.add_argument("baseline"); comparer.add_argument("candidate")
    verifier = commands.add_parser("verify"); verifier.add_argument("folder")
    commands.add_parser("describe")
    args = parser.parse_args()
    try:
        if args.command == "run": return run(args)
        if args.command == "describe":
            print(json.dumps(read(ROOT / "benchmarks/interface.json"), ensure_ascii=False, indent=2)); return 0
        result = score(args.folder, read(args.predictions)) if args.command == "score" else compare(args.baseline, args.candidate) if args.command == "compare" else {"status": validate_artifacts(args.folder)["status"]}
        print(json.dumps(result, ensure_ascii=False, indent=2)); return 2 if result.get("comparable") is False else 0
    except (ValueError, OSError, KeyError, TypeError, AttributeError, struct.error, json.JSONDecodeError, subprocess.SubprocessError) as exc:
        if args.json_errors: print(json.dumps({"schemaVersion": 1, "error": {"code": "invalid_evaluation", "message": str(exc)}}), file=sys.stderr)
        else: print("EVALUATION_ERROR: " + str(exc), file=sys.stderr)
        return 2

if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8"); sys.stderr.reconfigure(encoding="utf-8")
    sys.exit(main())
