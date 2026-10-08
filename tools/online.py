"""Standard-library client and launcher for the caller-driven simulation service."""
from __future__ import annotations
import argparse, http.client, io, json, os, shutil, subprocess, sys, threading, time, urllib.error, urllib.parse
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]

class Client:
    def __init__(self, url, token=None, timeout=60):
        parsed = urllib.parse.urlparse(url)
        if parsed.scheme != 'http' or parsed.hostname != '127.0.0.1' or not parsed.port or parsed.path not in ('', '/') or parsed.username or parsed.password or parsed.query or parsed.fragment:
            raise ValueError('Use an explicit http://127.0.0.1:port loopback endpoint')
        self.url, self.timeout = url.rstrip('/'), timeout
        self._port=parsed.port; self._transport=threading.local(); self._stats=threading.Lock()
        self.calls = 0
        self.wall_seconds = 0.
        self.response_bytes = 0
        self.token = token or os.environ.get('SANDBOXSIM_API_TOKEN', '')
        if len(self.token) < 16: raise ValueError('SANDBOXSIM_API_TOKEN must contain at least 16 characters')

    def request(self, method, path, body=None):
        data = None if body is None else json.dumps(body, allow_nan=False).encode('utf-8')
        if not path.startswith('/') or path.startswith('//'):raise ValueError('Use an absolute service path')
        connection=getattr(self._transport,'connection',None)
        if connection is None:
            connection=http.client.HTTPConnection('127.0.0.1',self._port,timeout=self.timeout); self._transport.connection=connection
        # Per-thread keep-alive connections preserve concurrency. No proxies, redirects or ambiguous auto-retries.
        started = time.perf_counter()
        with self._stats:self.calls += 1
        try:
            connection.request(method,path,body=data,headers={'Authorization':'Bearer '+self.token,'Content-Type':'application/json'})
            with connection.getresponse() as response:
                raw = response.read(16*1024*1024+1)
                if len(raw)>16*1024*1024: raise ValueError('Response size limit exceeded')
                with self._stats:self.response_bytes += len(raw)
                if response.status>=300:raise urllib.error.HTTPError(self.url+path,response.status,response.reason,response.headers,io.BytesIO(raw))
                return json.loads(raw)
        except (OSError,http.client.HTTPException,ValueError):
            connection.close();self._transport.connection=None;raise
        finally:
            with self._stats:self.wall_seconds += time.perf_counter()-started

    def close(self):
        connection=getattr(self._transport,'connection',None)
        if connection is not None:connection.close();self._transport.connection=None

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
