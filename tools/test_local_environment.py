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


import tempfile
from unittest import mock
import local_environment

class GodotEnvironmentTests(unittest.TestCase):
    def test_platform_profiles_stay_local_and_keep_home_and_trust_settings(self):
        for platform in ("win32", "linux", "darwin"):
            with self.subTest(platform=platform), tempfile.TemporaryDirectory() as folder:
                original = dict(HOME="original-home", USERPROFILE="original-profile", SSL_CERT_FILE="private-ca", APPDATA="original-appdata")
                child = original.copy()
                local_environment.godot_user_environment(child, Path(folder), platform)
                self.assertEqual(original["APPDATA"], "original-appdata")
                for name in ("HOME", "USERPROFILE", "SSL_CERT_FILE"):
                    self.assertEqual(child[name], original[name])
                if platform == "win32":
                    self.assertEqual(child["SANDBOXSIM_ORIGINAL_APPDATA"], "original-appdata")
                    self.assertEqual(Path(child["APPDATA"]), Path(folder) / ".tools/godot-state/config")
                    self.assertTrue(Path(child["LOCALAPPDATA"]).is_dir())
                elif platform == "linux":
                    self.assertEqual(Path(child["XDG_DATA_HOME"]), Path(folder) / ".tools/godot-state/data")
                else:
                    self.assertEqual(child["APPDATA"], "original-appdata")

    def test_normal_tools_do_not_redirect_game_profile(self):
        with tempfile.TemporaryDirectory() as folder, mock.patch.object(local_environment, "ROOT", Path(folder)):
            env = local_environment.tool_environment()
            self.assertEqual(env.get("APPDATA"), os.environ.get("APPDATA"))
            self.assertNotIn("SANDBOXSIM_GODOT_PROFILE_POLICY", env.keys() - os.environ.keys())

    def test_game_environment_is_a_copy(self):
        before = os.environ.copy()
        with tempfile.TemporaryDirectory() as folder, mock.patch.object(local_environment, "ROOT", Path(folder)):
            child = local_environment.tool_environment(godot=True)
            self.assertIn("SANDBOXSIM_GODOT_PROFILE_POLICY", child)
        self.assertEqual(os.environ.copy(), before)



if __name__ == "__main__": unittest.main()
