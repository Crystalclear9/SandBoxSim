"""Keep development dependency caches inside this checkout without changing user settings."""
import os
import shutil
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

def tool_environment(godot=False):
    env = os.environ.copy()
    for variable, folder in (("DOTNET_CLI_HOME", "dotnet-home"),
                             ("NUGET_PACKAGES", "nuget-packages"),
                             ("NUGET_HTTP_CACHE_PATH", "nuget-http-cache"),
                             ("NUGET_SCRATCH", "nuget-scratch")):
        env[variable] = str(ROOT / ".tools" / folder)
    temporary = ROOT / ".tools/tmp"
    temporary.mkdir(parents=True, exist_ok=True)
    for variable in ("TEMP", "TMP", "TMPDIR"):
        env[variable] = str(temporary)
    env["DOTNET_ADD_GLOBAL_TOOLS_TO_PATH"] = "0"
    env["DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE"] = "true"
    if godot:
        godot_user_environment(env, ROOT, sys.platform)
    return env


def godot_user_environment(env, root, platform):
    """Change only the child's cache/profile locations, never the caller's settings."""
    state = Path(root) / ".tools/godot-state"
    folders = {name: state / name for name in ("config", "data", "cache")}
    for folder in folders.values():
        folder.mkdir(parents=True, exist_ok=True)
    if platform == "win32":
        if not env.get("SANDBOXSIM_ORIGINAL_APPDATA") and env.get("APPDATA"):
            env["SANDBOXSIM_ORIGINAL_APPDATA"] = env["APPDATA"]
        env["APPDATA"] = str(folders["config"])
        env["LOCALAPPDATA"] = str(folders["cache"])
    elif platform.startswith("linux"):
        for variable, name in (("XDG_CONFIG_HOME", "config"), ("XDG_DATA_HOME", "data"), ("XDG_CACHE_HOME", "cache")):
            env[variable] = str(folders[name])
    # macOS uses native user directories; do not replace HOME or user profiles.
    env["SANDBOXSIM_GODOT_PROFILE_POLICY"] = "platform-user-v1" if platform == "darwin" else "project-local-v1"


def dotnet_executable():
    explicit = os.environ.get("SANDBOXSIM_DOTNET_ROOT")
    folder = Path(explicit) if explicit else ROOT / ".tools/net8"
    executable = folder / ("dotnet.exe" if os.name == "nt" else "dotnet")
    if executable.is_file():
        return str(executable.resolve())
    if explicit:
        raise ValueError("Explicit SANDBOXSIM_DOTNET_ROOT host does not exist")
    return shutil.which("dotnet")
