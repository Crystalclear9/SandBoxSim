"""Keep development dependency caches inside this checkout without changing user settings."""
import os
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

def tool_environment():
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
    return env


def dotnet_executable():
    explicit = os.environ.get("SANDBOXSIM_DOTNET_ROOT")
    folder = Path(explicit) if explicit else ROOT / ".tools/net8"
    executable = folder / ("dotnet.exe" if os.name == "nt" else "dotnet")
    if executable.is_file():
        return str(executable.resolve())
    if explicit:
        raise ValueError("Explicit SANDBOXSIM_DOTNET_ROOT host does not exist")
    return shutil.which("dotnet")
