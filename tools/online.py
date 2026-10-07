"""Standard-library client and launcher for the caller-driven simulation service."""
from __future__ import annotations
import argparse, json, os, shutil, subprocess, sys, time, urllib.error, urllib.parse, urllib.request
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]

class Client:
    def __init__(self, url, token=None, timeout=60):
        parsed = urllib.parse.urlparse(url)
        if parsed.scheme != 'http' or parsed.hostname != '127.0.0.1' or not parsed.port or parsed.path not in ('', '/'):
            raise ValueError('Use an explicit http://127.0.0.1:port loopback endpoint')
        self.url, self.timeout = url.rstrip('/'), timeout
        self.calls = 0
        self.wall_seconds = 0.
        self.response_bytes = 0
        self.token = token or os.environ.get('SANDBOXSIM_API_TOKEN', '')
        if len(self.token) < 16: raise ValueError('SANDBOXSIM_API_TOKEN must contain at least 16 characters')

    def request(self, method, path, body=None):
        data = None if body is None else json.dumps(body, allow_nan=False).encode('utf-8')
        request = urllib.request.Request(self.url + path, data=data, method=method,
            headers={'Authorization': 'Bearer ' + self.token, 'Content-Type': 'application/json'})
        # Never send a local service token through an HTTP proxy or follow redirects.
        class NoRedirect(urllib.request.HTTPRedirectHandler):
            def redirect_request(self, *args): return None
        opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())
        started = time.perf_counter()
        self.calls += 1
        try:
            with opener.open(request, timeout=self.timeout) as response:
                raw = response.read(16*1024*1024+1)
                if len(raw)>16*1024*1024: raise ValueError('Response size limit exceeded')
                self.response_bytes += len(raw)
                return json.loads(raw)
        finally: self.wall_seconds += time.perf_counter()-started

def launch(assembly, port, token, log):
    private = Path.home() / '.sandboxsim-tool/net8' / ('dotnet.exe' if os.name == 'nt' else 'dotnet')
    dotnet = str(private) if private.is_file() else shutil.which('dotnet')
    if not dotnet or not Path(assembly).is_file(): raise ValueError('Build the console assembly with tools/build.ps1 first')
    env = os.environ.copy(); env['SANDBOXSIM_API_TOKEN'] = token
    return subprocess.Popen([dotnet, str(Path(assembly).resolve()), '--serve', '--port', str(port)], env=env,
        stdout=log, stderr=subprocess.STDOUT, creationflags=subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0)

def ready(client, process, timeout=30):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if process.poll() is not None: raise ValueError('Service exited during startup; inspect its log')
        try: return client.request('GET', '/v1/health')
        except (OSError, urllib.error.URLError): time.sleep(.1)
    raise ValueError('Service startup timed out')

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--assembly', type=Path, default=ROOT/'artifacts/Release/SandBoxSim.Console.dll')
    parser.add_argument('--port', type=int, default=8765)
    args = parser.parse_args()
    token = os.environ.get('SANDBOXSIM_API_TOKEN', '')
    if len(token) < 16: raise ValueError('Set SANDBOXSIM_API_TOKEN to at least 16 characters')
    process = launch(args.assembly, args.port, token, None)
    try: return process.wait()
    except KeyboardInterrupt:
        process.terminate(); process.wait(timeout=10); return 0

if __name__ == '__main__':
    try: sys.exit(main())
    except (ValueError, OSError) as e: print(str(e), file=sys.stderr); sys.exit(2)
