"""Fail fast if ZIP does not contain a usable PortableApps-style application."""
import configparser
import io
import json
import pathlib
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[1]
PREFIX = "ThunderbirdPortable/"
REQUIRED = [
    "ThunderbirdPortable.exe",
    "App/Thunderbird/thunderbird.exe",
    "App/Thunderbird/application.ini",
    "App/Thunderbird/omni.ja",
    "App/Thunderbird/distribution/policies.json",
    "App/AppInfo/appinfo.ini",
    "App/AppInfo/appicon.ico",
    "App/AppInfo/appicon_16.png",
    "App/AppInfo/appicon_32.png",
    "App/AppInfo/appicon_128.png",
    "Other/PORTABLE-README.txt",
    "Other/Source/ThunderbirdPortable.cs",
    "Other/Source/TraceGuard.cs",
    "Data/",
    "Data/profile/",
    "Data/TraceGuard/",
]


def inspect_package(path):
    with zipfile.ZipFile(path) as archive:
        names = archive.namelist()
        items = set(names)
        missing = [p for p in REQUIRED if PREFIX + p not in items]
        if missing:
            raise RuntimeError("Missing required portable entries: " + repr(missing))
        if len(items) != len(names):
            raise RuntimeError("Duplicate ZIP entry names")
        if any(not n.startswith(PREFIX) or ".." in pathlib.PurePosixPath(n).parts for n in names):
            raise RuntimeError("Unsafe path outside ThunderbirdPortable folder")
        forbidden = [n for n in names if n.startswith(PREFIX + "Data/profile/") and not n.endswith("/")]
        if forbidden:
            raise RuntimeError("Builder ZIP accidentally contains user profile data: " + repr(forbidden))
        policies = json.loads(archive.read(PREFIX + "App/Thunderbird/distribution/policies.json"))
        if policies.get("policies", {}).get("DisableAppUpdate") is not True:
            raise RuntimeError("DisableAppUpdate deployment policy missing")
        for policy, expected in (("DisableTelemetry", True), ("BackgroundAppUpdate", False)):
            if policies.get("policies", {}).get(policy) is not expected:
                raise RuntimeError("Missing host-trace reduction policy: " + policy)
        config = configparser.ConfigParser(interpolation=None)
        config.read_string(archive.read(PREFIX + "App/AppInfo/appinfo.ini").decode("utf-8"))
        if config.get("Control", "Start") != "ThunderbirdPortable.exe":
            raise RuntimeError("PortableApps menu launcher target is wrong")
        if not config.get("Version", "DisplayVersion"):
            raise RuntimeError("Missing application version")
    return len(items)


def main():
    files = sorted((ROOT / "dist").glob("Thunderbird-Portable-*-Windows-x64.zip"))
    if len(files) != 1:
        raise RuntimeError("Expected exactly one portable release ZIP in dist")
    print("PASS: validated", inspect_package(files[0]), "ZIP entries in", files[0].name)


if __name__ == "__main__":
    main()
