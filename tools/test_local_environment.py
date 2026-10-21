"""Regression checks for repository-local dependency storage."""
import os
from pathlib import Path
import unittest
from unittest.mock import patch
from local_environment import ROOT, tool_environment, dotnet_executable
from online import ready
import urllib.error

class LocalEnvironmentTests(unittest.TestCase):
    def test_child_caches_stay_local_without_mutating_user_environment(self):
        with patch.dict(os.environ, {"NUGET_PACKAGES": "/shared/packages", "KEEP_ME": "value"}):
            before = dict(os.environ)
            child = tool_environment()
            self.assertEqual(dict(os.environ), before)
            self.assertEqual(child["KEEP_ME"], "value")
            for variable in ("DOTNET_CLI_HOME", "NUGET_PACKAGES", "NUGET_HTTP_CACHE_PATH", "NUGET_SCRATCH", "TEMP", "TMP", "TMPDIR"):
                self.assertTrue(Path(child[variable]).is_relative_to(ROOT / ".tools"))
            self.assertEqual(child["NUGET_PACKAGES"], str(ROOT / ".tools/nuget-packages"))
            self.assertEqual(child["DOTNET_ADD_GLOBAL_TOOLS_TO_PATH"], "0")

    def test_explicit_missing_dotnet_does_not_silently_use_system_host(self):
        with patch.dict(os.environ, {"SANDBOXSIM_DOTNET_ROOT": str(ROOT / ".tools/missing-host")}):
            with self.assertRaisesRegex(ValueError, "Explicit SANDBOXSIM_DOTNET_ROOT"):
                dotnet_executable()

    def test_service_readiness_reports_authentication_errors_immediately(self):
        from unittest.mock import Mock
        client, process = Mock(), Mock()
        process.poll.return_value = None
        client.request.side_effect = urllib.error.HTTPError('http://127.0.0.1:8765/v1/health', 401, 'Unauthorized', {}, None)
        with self.assertRaises(urllib.error.HTTPError):
            ready(client, process)
        self.assertEqual(client.request.call_count, 1)

if __name__ == "__main__": unittest.main()
