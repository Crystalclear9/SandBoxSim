"""Local evaluation runner, artifact verifier, vision scorer and paired comparison."""
from __future__ import annotations
import argparse, hashlib, json, os, platform, re, shutil, signal, struct, subprocess, sys, time
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]

def read(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))

def write(path, value):
    Path(path).write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()

def git(*args):
    result = subprocess.run(["git", *args], cwd=ROOT, text=True, capture_output=True, check=True)
    return result.stdout.strip()

def engine_path(value):
    if value:
        path = Path(value)
        if path.is_file(): return str(path.resolve())
        found = shutil.which(value)
        if found: return found
    for name in ("godot-mono", "godot", "godot4"):
        found = shutil.which(name)
        if found: return found
    home = Path.home() / ".sandboxsim-tool" / "godot-4.7.2"
    matches = list(home.glob("**/*console.exe"))
    if matches: return str(sorted(matches)[0])
    raise ValueError("Godot .NET executable missing; use --engine or GODOT_EXE")

def validate_artifacts(folder):
    folder = Path(folder)
    if (folder / "provenance.json").exists():
        for name, expected in read(folder / "provenance.json").get("artifactHashes", {}).items():
            if Path(name).name != name or digest(folder / name) != expected: raise ValueError("Recorded artifact checksum mismatch")
    result = read(folder / "result.json")
    if result.get("schemaVersion") != 1 or result.get("measuredFrames", 0) < 1:
        raise ValueError("Missing or unsupported completed result")
    for sample in result["captures"]:
        name = sample["image"]
        if Path(name).name != name or digest(folder / name) != sample["sha256"]:
            raise ValueError("Capture checksum mismatch")
    request = read(folder / "request.json")
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
    ids = [q["id"] for q in questions]
    if len(set(ids)) != len(ids) or set(ids) != {a["id"] for a in keys} or len(keys) != len(ids):
        raise ValueError("Question/answer IDs disagree")
    for q in questions:
        name = q["image"]
        if Path(name).name != name: raise ValueError("Invalid image path")
        raw = (folder / name).read_bytes()
        if digest(folder / name) != q["sha256"]: raise ValueError("Visual sample checksum mismatch")
        if raw[:8] != b"\x89PNG\r\n\x1a\n" or min(struct.unpack(">II", raw[16:24])) < 8:
            raise ValueError("Invalid visual sample")
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
    if old["state"]["digest"] != new["state"]["digest"]: reasons.append("simulation digest differs")
    return {"schemaVersion": 1, "comparable": not reasons, "reasons": reasons,
            "fpsRatio": new["averageFps"] / old["averageFps"] if not reasons and old["averageFps"] > 0 else None,
            "p95MsChange": new["frameMs"]["p95"] - old["frameMs"]["p95"] if not reasons else None,
            "baselineFps": old["averageFps"], "candidateFps": new["averageFps"]}

def source_hashes():
    files = git("ls-files", "--cached", "--others", "--exclude-standard").splitlines()
    suffixes = {".cs", ".csproj", ".godot", ".py", ".ps1", ".json", ".props"}
    return {name: digest(ROOT / name) for name in sorted(set(files)) if Path(name).suffix in suffixes and (ROOT / name).is_file()}

def run(args):
    spec = read(args.scenario)
    if not isinstance(spec.get("id"), str) or not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]{0,63}", spec["id"]): raise ValueError("Scenario ID must be a safe 1-64 character identifier")
    if args.timeout <= 0 or args.max_fps < 0: raise ValueError("Timeout must be positive and max FPS nonnegative")
    stamp = time.strftime("%Y%m%d-%H%M%S")
    output = Path(args.output or ROOT / "runs" / "evaluation" / (spec["id"] + "-" + stamp)).resolve()
    executable = engine_path(args.engine or os.environ.get("GODOT_EXE"))
    output.mkdir(parents=True, exist_ok=False)
    # Engine refuses nonempty outputs. Stage the immutable input beside this output.
    staging = output.parent / (output.name + ".request.json")
    if staging.exists(): raise ValueError("Request staging path already exists")
    spec["outputDirectory"] = str(output); write(staging, spec)
    env = os.environ.copy(); dotnet = Path.home() / ".sandboxsim-tool" / "net8"
    if dotnet.exists(): env["PATH"] = str(dotnet) + os.pathsep + env.get("PATH", "")
    command = [executable, "--path", str(ROOT / "src/SandBoxSim.Godot"), "--resolution", args.resolution, "--max-fps", str(args.max_fps), "--disable-vsync", "--", "--evaluation=" + str(staging)]
    provenance = {"schemaVersion": 1, "sourceCommit": git("rev-parse", "HEAD"), "sourceDirty": bool(git("status", "--porcelain")),
                  "runnerSha256": digest(__file__), "scenarioSha256": digest(args.scenario), "engineSha256": digest(executable),
                  "sourceFiles": source_hashes(), "assemblySha256": digest(ROOT / "src/SandBoxSim.Godot/.godot/mono/temp/bin/Debug/SandBoxSim.Godot.dll"), "execution": {"maxFps": args.max_fps, "resolution": args.resolution, "disableVsync": True}, "candidateLabel": args.label, "python": platform.python_version(), "command": command, "startedUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())}
    started = time.monotonic()
    process = subprocess.Popen(command, cwd=ROOT, env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                               creationflags=subprocess.CREATE_NEW_PROCESS_GROUP if os.name == "nt" else 0,
                               start_new_session=os.name != "nt")
    try:
        stdout, stderr = process.communicate(timeout=args.timeout)
        code = process.returncode; logs = stdout + stderr
    except subprocess.TimeoutExpired:
        if os.name == "nt": subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"], capture_output=True)
        else: os.killpg(process.pid, signal.SIGKILL)
        stdout, stderr = process.communicate(); code = 124; logs = stdout + stderr
    (output / "engine.log").write_bytes(logs)
    provenance.update(engineExitCode=code,exitCode=code, wallSeconds=time.monotonic() - started, sourceChangedDuringRun=source_hashes() != provenance["sourceFiles"])
    if provenance["sourceChangedDuringRun"] and code == 0: code = 2
    try:
        validate_artifacts(output)
        if provenance["sourceChangedDuringRun"]: raise ValueError("Source changed during evaluation")
        if b"ERROR:" in logs: raise ValueError("Engine reported an error")
        provenance["artifactValidation"] = "passed"
        provenance["artifactHashes"] = {p.name: digest(p) for p in sorted(output.iterdir()) if p.is_file()}
    except (ValueError, KeyError, OSError, json.JSONDecodeError) as exc:
        provenance["artifactValidation"] = str(exc)
        if code == 0: code = 2
    provenance["exitCode"] = code
    write(output / "provenance.json", provenance)
    print(json.dumps({"output": str(output), "exitCode": code, "artifactValidation": provenance["artifactValidation"]}))
    return code

def main():
    parser = argparse.ArgumentParser(description=__doc__); commands = parser.add_subparsers(dest="command", required=True)
    runner = commands.add_parser("run"); runner.add_argument("scenario", type=Path); runner.add_argument("--engine"); runner.add_argument("--output"); runner.add_argument("--resolution", default="1440x900"); runner.add_argument("--max-fps", type=int, default=0); runner.add_argument("--timeout", type=int, default=180); runner.add_argument("--label", default="unlabelled")
    scorer = commands.add_parser("score"); scorer.add_argument("folder"); scorer.add_argument("predictions", type=Path)
    comparer = commands.add_parser("compare"); comparer.add_argument("baseline"); comparer.add_argument("candidate")
    verifier = commands.add_parser("verify"); verifier.add_argument("folder")
    args = parser.parse_args()
    try:
        if args.command == "run": return run(args)
        result = score(args.folder, read(args.predictions)) if args.command == "score" else compare(args.baseline, args.candidate) if args.command == "compare" else {"status": validate_artifacts(args.folder)["status"]}
        print(json.dumps(result, ensure_ascii=False, indent=2)); return 2 if result.get("comparable") is False else 0
    except (ValueError, OSError, KeyError, json.JSONDecodeError, subprocess.CalledProcessError) as exc:
        print("EVALUATION_ERROR: " + str(exc), file=sys.stderr); return 2

if __name__ == "__main__": sys.exit(main())
