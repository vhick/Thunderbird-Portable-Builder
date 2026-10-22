import configparser
import json
import pathlib
import tempfile
import unittest
import zipfile

from scripts.download_official import official_url
from scripts.locate_thunderbird import find_app_dir
from scripts.package_app import build, get_version, safe_package_version, make_appinfo
from scripts.verify_package import inspect_package

ROOT = pathlib.Path(__file__).resolve().parents[1]


class BuilderTests(unittest.TestCase):
    def test_latest_official_url(self):
        url = official_url("latest", "en-US")
        self.assertTrue(url.startswith("https://download.mozilla.org/?"))
        self.assertIn("thunderbird-latest", url)
        self.assertIn("win64", url)

    def test_pinned_version_download_url(self):
        self.assertIn("158.0.1", official_url("158.0.1", "en-GB"))
        self.assertIn("archive.mozilla.org", official_url("158.0.1", "en-GB"))

    def test_rejects_invalid_version_and_language(self):
        for bad in ["../evil", "158.0beta", "", "latest&os=win32"]:
            with self.assertRaises(ValueError): official_url(bad, "en-US")
        with self.assertRaises(ValueError): official_url("latest", "../../unsafe")

    def test_version_metadata(self):
        self.assertEqual(safe_package_version("158.0.1"), "158.0.1.0")
        self.assertEqual(safe_package_version("140.17.0esr"), "140.17.0.0")
        self.assertIn("Start=ThunderbirdPortable.exe", make_appinfo("158.0.1", "en-US"))

    def test_locates_only_complete_binary(self):
        with tempfile.TemporaryDirectory() as d:
            extracted = pathlib.Path(d)
            (extracted / "fake").mkdir()
            (extracted / "fake" / "thunderbird.exe").write_bytes(b"fake")
            (extracted / "core").mkdir()
            for name in ("thunderbird.exe", "application.ini", "omni.ja"):
                (extracted / "core" / name).write_bytes(b"mock")
            self.assertEqual(find_app_dir(extracted), extracted / "core")

    def test_missing_app_fails(self):
        with tempfile.TemporaryDirectory() as d:
            with self.assertRaises(FileNotFoundError): find_app_dir(pathlib.Path(d))

    def test_version_is_from_official_application_ini(self):
        with tempfile.TemporaryDirectory() as d:
            root = pathlib.Path(d)
            (root / "application.ini").write_text("[App]\nVersion=158.0.1\nName=Thunderbird\n")
            self.assertEqual(get_version(root), "158.0.1")

    def test_compiler_uses_absolute_paths_and_headless_timeout(self):
        workflow = (ROOT / ".github/workflows/build-portable.yml").read_text(encoding="utf-8")
        self.assertIn("Resolve-Path -LiteralPath 'launcher/ThunderbirdPortable.cs'", workflow)
        self.assertIn("WaitForExit(15000)", workflow)
        self.assertIn("retention-days: 1", workflow)
        source = (ROOT / "launcher/ThunderbirdPortable.cs").read_text(encoding="utf-8")
        self.assertLess(source.index('if (args.Length == 1 && args[0] == "--portable-self-test")'), source.index('if (!File.Exists(exe))'))

    def test_profile_routed_without_touching_host(self):
        source = (ROOT / "launcher/ThunderbirdPortable.cs").read_text(encoding="utf-8")
        self.assertIn('"Data", "profile"', source)
        self.assertIn('"-no-remote -profile "', source)
        self.assertIn("Environment.SpecialFolder.ApplicationData", source)  # read-only trace snapshot
        self.assertIn("TraceGuard traceGuard", source)
        self.assertIn("traceGuard.Cleanup()", source)
        self.assertNotIn("Registry.SetValue", source)

    def test_traceguard_guards_old_state_and_dynamic_hashes(self):
        source = (ROOT / "launcher/TraceGuard.cs").read_text(encoding="utf-8")
        self.assertIn('oldAppDirs.Contains', source)
        self.assertIn('oldUpdateDirs.Contains', source)
        self.assertIn('oldLockFiles.Contains', source)
        self.assertIn('oldUpdatesParents.Contains', source)
        self.assertIn('IsHash(name.Substring', source)
        self.assertIn('IsMozillaRunning()', source)
        self.assertIn('IsLink(', source)
        self.assertIn('Directory.Delete(path, false)', source)
        self.assertNotIn('Directory.Delete(path, true)', source)
        self.assertNotIn('Registry.', source)
        self.assertIn('TraceGuard-Test-', source)

    def test_windows_workflow_compiles_and_tests_traceguard(self):
        workflow = (ROOT / ".github/workflows/build-portable.yml").read_text(encoding="utf-8")
        self.assertIn("'launcher/TraceGuard.cs'", workflow)
        self.assertIn(' $source $traceGuardSource', workflow)
        self.assertIn('--traceguard-self-test', workflow)
        self.assertIn('WaitForExit(15000)', workflow)

    def test_updater_policy_reduces_host_traces(self):
        with tempfile.TemporaryDirectory() as d:
            place = pathlib.Path(d)
            src = place / 'core'
            src.mkdir()
            for name, content in {"thunderbird.exe": b"exe", "omni.ja": b"runtime", "application.ini": b"[App]\nVersion=158.0.1\n"}.items():
                (src / name).write_bytes(content)
            work = place / 'work'
            (work / 'icons').mkdir(parents=True)
            (work / 'ThunderbirdPortable.exe').write_bytes(b'launcher')
            (work / 'appicon.ico').write_bytes(b'icon')
            for size in (16, 32, 128):
                (work / 'icons' / f'appicon_{size}.png').write_bytes(b'icon png')
            z = build(src, work, place / 'dist')
            with zipfile.ZipFile(z) as archive:
                policy = json.loads(archive.read('ThunderbirdPortable/App/Thunderbird/distribution/policies.json'))['policies']
                self.assertEqual(policy['DisableAppUpdate'], True)
                self.assertEqual(policy['DisableTelemetry'], True)
                self.assertEqual(policy['BackgroundAppUpdate'], False)
                self.assertIn('ThunderbirdPortable/Other/Source/TraceGuard.cs', archive.namelist())

    def test_full_fake_package_and_profile_is_empty(self):
        with tempfile.TemporaryDirectory() as d:
            place = pathlib.Path(d)
            extracted = place / "core"
            extracted.mkdir()
            for name, content in {
                "thunderbird.exe": b"test executable",
                "omni.ja": b"test runtime",
                "application.ini": b"[App]\nVersion=158.0.1\n",
                "mozglue.dll": b"test library",
            }.items(): (extracted / name).write_bytes(content)
            work = place / "work"
            (work / "icons").mkdir(parents=True)
            (work / "ThunderbirdPortable.exe").write_bytes(b"fake compiled launcher")
            (work / "appicon.ico").write_bytes(b"fake official icon")
            for size in (16, 32, 128):
                (work / "icons" / f"appicon_{size}.png").write_bytes(b"fake icon image")
            z = build(extracted, work, place / "dist")
            self.assertGreater(inspect_package(z), 10)
            with zipfile.ZipFile(z) as archive:
                names = archive.namelist()
                self.assertFalse(any(n.startswith("ThunderbirdPortable/Data/profile/") and not n.endswith("/") for n in names))
                self.assertIn("ThunderbirdPortable/App/Thunderbird/mozglue.dll", names)
                self.assertTrue(json.loads(archive.read("ThunderbirdPortable/App/Thunderbird/distribution/policies.json"))["policies"]["DisableAppUpdate"])


if __name__ == "__main__":
    unittest.main()
