"""Release helper for the Release workflow. Stdlib only.

  release.py check-youtubeexplode  -> bumps the csproj if NuGet has a newer stable YoutubeExplode;
                                      prints the changelog line, or nothing if already current.
  release.py next-version          -> prints the next plugin version (patch bump of manifest.json's newest).
  release.py add-version VERSION ZIP URL CHANGELOG -> adds the release to manifest.json.
"""
import datetime
import hashlib
import json
import re
import sys
import urllib.request

CSPROJ = "Jellyfin.Plugin.JellyTheme/Jellyfin.Plugin.JellyTheme.csproj"
MANIFEST = "manifest.json"
TARGET_ABI = "12.1.0.0"


def check_youtubeexplode():
    with open(CSPROJ) as f:
        csproj = f.read()
    current = re.search(r'Include="YoutubeExplode" Version="([^"]+)"', csproj).group(1)
    with urllib.request.urlopen("https://api.nuget.org/v3-flatcontainer/youtubeexplode/index.json") as r:
        stable = [v for v in json.load(r)["versions"] if "-" not in v]
    latest = max(stable, key=lambda v: tuple(int(p) for p in v.split(".")))
    if tuple(int(p) for p in latest.split(".")) <= tuple(int(p) for p in current.split(".")):
        return
    with open(CSPROJ, "w") as f:
        f.write(csproj.replace(f'Include="YoutubeExplode" Version="{current}"', f'Include="YoutubeExplode" Version="{latest}"'))
    print(f"Update YoutubeExplode {current} -> {latest}, to keep YouTube downloads working.")


def load():
    with open(MANIFEST) as f:
        return json.load(f)


def next_version():
    versions = load()[0]["versions"]
    if not versions:
        print("1.0.0.0")
        return
    major, minor, patch, _ = (int(p) for p in versions[0]["version"].split("."))
    print(f"{major}.{minor}.{patch + 1}.0")


def add_version(version, zip_path, url, changelog):
    with open(zip_path, "rb") as f:
        checksum = hashlib.md5(f.read()).hexdigest()
    manifest = load()
    manifest[0]["versions"].insert(0, {
        "version": version,
        "changelog": changelog,
        "targetAbi": TARGET_ABI,
        "sourceUrl": url,
        "checksum": checksum,
        "timestamp": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
    })
    with open(MANIFEST, "w") as f:
        json.dump(manifest, f, indent=2)
        f.write("\n")


if __name__ == "__main__":
    command, *args = sys.argv[1:]
    {"check-youtubeexplode": check_youtubeexplode, "next-version": next_version, "add-version": add_version}[command](*args)
