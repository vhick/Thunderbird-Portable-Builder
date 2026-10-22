"""Download the *official* Mozilla signed Thunderbird Windows x64 installer.

For 'latest', Mozilla's maintained download endpoint resolves the stable channel.
For a pinned version, download directly from Mozilla's archive (no repack mirrors).
"""
import argparse
import hashlib
import pathlib
import re
import shutil
import urllib.parse
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[1]


def official_url(version, language):
    if version != "latest" and not re.fullmatch(r"\d+\.\d+(?:\.\d+)?(?:esr)?", version):
        raise ValueError("Version must be 'latest' or a stable version like 158.0.1")
    if not re.fullmatch(r"[a-zA-Z]{2,3}(?:-[a-zA-Z0-9]{2,8})?", language):
        raise ValueError("Invalid Mozilla language code; use en-US or en-GB.")
    if version == "latest":
        return "https://download.mozilla.org/?" + urllib.parse.urlencode({
            "product": "thunderbird-latest", "os": "win64", "lang": language
        })
    return ("https://archive.mozilla.org/pub/thunderbird/releases/"
            + urllib.parse.quote(version) + "/win64/" + urllib.parse.quote(language)
            + "/Thunderbird%20Setup%20" + urllib.parse.quote(version) + ".exe")


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--version", default="latest")
    p.add_argument("--language", default="en-US")
    args = p.parse_args()
    url = official_url(args.version, args.language)
    dest = ROOT / "work" / "download" / "ThunderbirdSetup.exe"
    dest.parent.mkdir(parents=True, exist_ok=True)
    print("Downloading official Mozilla installer:", url, flush=True)
    req = urllib.request.Request(url, headers={"User-Agent": "ThunderbirdPortableBuilder/1.0"})
    with urllib.request.urlopen(req, timeout=90) as response, dest.open("wb") as file:
        host = urllib.parse.urlsplit(response.geturl()).hostname or ""
        allowed = (host == "mozilla.org" or host.endswith(".mozilla.org")
                   or host == "thunderbird.net" or host.endswith(".thunderbird.net")
                   or host.endswith(".mozilla.net"))
        # Official Mozilla download endpoints redirect to their authorized CDN hosts.
        if not allowed:
            raise RuntimeError("Unexpected redirect host: " + host)
        shutil.copyfileobj(response, file, length=1024 * 1024)
    if dest.stat().st_size < 10_000_000:
        raise RuntimeError("Downloaded file is unexpectedly small; check language and release.")
    sha = hashlib.file_digest(dest.open("rb"), "sha256").hexdigest()
    (dest.parent / "SHA256.txt").write_text(f"{sha}  ThunderbirdSetup.exe\n")
    print(f"Downloaded {dest.stat().st_size:,} bytes from official CDN; SHA256={sha}")
    print("Windows Authenticode publisher signature is verified by the next CI step.")


if __name__ == "__main__":
    main()
