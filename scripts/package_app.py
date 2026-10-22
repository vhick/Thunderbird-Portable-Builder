"""Package official Thunderbird binary payload with a separately compiled launcher.

Never read/copy a host Thunderbird profile. Package always begins with EMPTY Data.
"""
import argparse
import configparser
import json
import pathlib
import re
import shutil
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[1]
APP_NAME = "ThunderbirdPortable"


def get_version(source):
    ini = source / "application.ini"
    parser = configparser.ConfigParser(interpolation=None)
    parser.read(ini, encoding="utf-8")
    version = parser.get("App", "Version", fallback="").strip()
    if not re.fullmatch(r"\d+(?:\.\d+){1,3}(?:esr)?", version):
        raise ValueError("Unexpected version in Mozilla application.ini: " + version)
    return version


def safe_package_version(version):
    # PortableApps uses up to 4 numeric version components, including ESR builds.
    nums = re.findall(r"\d+", version)
    return ".".join((nums + ["0", "0", "0", "0"])[:4])


def copy_upstream(source, dest):
    if not (source / "thunderbird.exe").is_file():
        raise ValueError("The extracted upstream source is missing thunderbird.exe")
    for entry in source.iterdir():
        if entry.is_symlink():
            raise ValueError("Symlink found in upstream program payload: " + str(entry))
        # Mozilla's installer includes updater/uninstaller helpers. Only skip the
        # uninstaller: we must retain actual Thunderbird application libraries.
        if entry.name.lower() in {"uninstall", "uninstall.exe"}:
            continue
        output = dest / entry.name
        if entry.is_dir():
            shutil.copytree(entry, output, symlinks=False)
        elif entry.is_file():
            shutil.copy2(entry, output)
    if not (dest / "omni.ja").is_file() or not (dest / "application.ini").is_file():
        raise ValueError("Upstream payload is incomplete (omni.ja/application.ini missing)")


def make_appinfo(version, language):
    return f"""[Format]
Type=PortableApps.comFormat
Version=3.8

[Details]
Name=Mozilla Thunderbird Portable
AppID=ThunderbirdPortable
Publisher=Mozilla Thunderbird contributors
Homepage=https://www.thunderbird.net/
Category=Internet
Description=Mozilla Thunderbird mail client with its profile stored under Data
Language={language}

[License]
Shareable=true
OpenSource=true
Freeware=true
CommercialUse=true

[Version]
PackageVersion={safe_package_version(version)}
DisplayVersion={version}

[Control]
Icons=1
Start=ThunderbirdPortable.exe
"""


def build(source, work, outdir, language="en-US"):
    version = get_version(source)
    root = work / APP_NAME
    if root.exists():
        shutil.rmtree(root)
    app = root / "App" / "Thunderbird"
    info = root / "App" / "AppInfo"
    other = root / "Other"
    source_dir = other / "Source"
    for folder in (app, info, source_dir, root / "Data" / "profile"):
        folder.mkdir(parents=True, exist_ok=True)
    copy_upstream(source, app)
    # Keep official binaries unmodified; policies is a supported Thunderbird
    # deployment mechanism rather than an executable patch.
    policy_dir = app / "distribution"
    policy_dir.mkdir(exist_ok=True)
    (policy_dir / "policies.json").write_text(
        json.dumps({"policies": {"DisableAppUpdate": True}}, indent=2) + "\n", encoding="utf-8")
    launcher = work / (APP_NAME + ".exe")
    icon = work / "appicon.ico"
    if not launcher.is_file() or not icon.is_file():
        raise FileNotFoundError("Compiled launcher or official icon missing")
    shutil.copy2(launcher, root / launcher.name)
    shutil.copy2(icon, info / "appicon.ico")
    for size in [16, 32, 128]:
        shutil.copy2(work / "icons" / f"appicon_{size}.png", info / f"appicon_{size}.png")
    (info / "appinfo.ini").write_text(make_appinfo(version, language), encoding="utf-8")
    shutil.copy2(ROOT / "launcher" / "ThunderbirdPortable.cs", source_dir / "ThunderbirdPortable.cs")
    (source_dir / "SOURCE-NOTICE.txt").write_text(
        "Mozilla Thunderbird original binaries: https://www.thunderbird.net/\n"
        "Mozilla Thunderbird source: https://hg.mozilla.org/comm-central/\n"
        "Original Mozilla code is subject to applicable upstream licenses, including MPL 2.0.\n"
        "Builder-specific Windows launcher source is included in this folder (MIT license).\n",
        encoding="utf-8")
    (other / "PORTABLE-README.txt").write_text(
        f"Mozilla Thunderbird Portable {version} (Windows x64, {language})\n\n"
        "START: Run ThunderbirdPortable.exe from the top folder. Do not run the\n"
        "App\\Thunderbird\\thunderbird.exe file directly.\n\n"
        "PROFILE: All normal Thunderbird accounts, messages, extensions, calendars,\n"
        "address books and settings use Data\\profile via -no-remote -profile.\n"
        "The launcher does not touch an existing installed Thunderbird profile.\n\n"
        "UPDATING: Fully close Thunderbird. Make a backup of Data first.\n"
        "Replace only App, Other and ThunderbirdPortable.exe using a new ZIP.\n"
        "NEVER overwrite or delete your existing Data folder.\n"
        "In-app updates are disabled via App\\Thunderbird\\distribution\\policies.json.\n"
        "Run a new GitHub Actions build to get security updates.\n\n"
        "MOVE TO NEW PC: Move the complete folder. External folders or attachment\n"
        "paths set up in Thunderbird might use old drive letters; adjust them.\n"
        "OAuth logins may require signing in again. Do not run the same profile\n"
        "on two computers simultaneously or from a live-synced cloud directory.\n\n"
        "HOST LIMITATIONS: Windows caches, default-mail associations, file dialog\n"
        "history, browser OAuth and notifications may leave OS traces.\n"
        "Do not enable default-mail integration if you want to reduce such changes.\n"
        "This launcher is not a Windows sandbox or a data-erasure tool.\n\n"
        "SECURITY: Data\\profile can contain private messages, access tokens,\n"
        "account credentials and keys. Store and back it up securely.\n",
        encoding="utf-8")
    outdir.mkdir(parents=True, exist_ok=True)
    zip_file = outdir / f"Thunderbird-Portable-{version}-Windows-x64.zip"
    with zipfile.ZipFile(zip_file, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=6, allowZip64=True) as archive:
        for path in sorted(root.rglob("*")):
            relative = path.relative_to(root.parent).as_posix()
            if path.is_dir():
                archive.writestr(relative + "/", b"")
            else:
                archive.write(path, relative)
    print("Created", zip_file, "size", zip_file.stat().st_size)
    return zip_file


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--language", default="en-US")
    args = p.parse_args()
    executable = pathlib.Path((ROOT / "work" / "source_path.txt").read_text(encoding="utf-8").strip())
    if not executable.is_file():
        raise FileNotFoundError(executable)
    build(executable.parent, ROOT / "work", ROOT / "dist", args.language)


if __name__ == "__main__":
    main()
