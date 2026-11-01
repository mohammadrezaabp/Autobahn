"""Fail CI if private state, credentials or generated dependencies enter Git."""
from pathlib import PurePosixPath
import subprocess
import sys

BLOCKED_DIRS = {"node_modules", "bin", "obj", ".secrets", "data", "TestResults", "artifacts", "__pycache__"}
BLOCKED_SUFFIXES = {".db", ".enc", ".pem", ".key", ".pfx", ".p12", ".log"}
files = subprocess.check_output(["git", "ls-files", "-z"]).decode("utf-8").split("\0")
blocked = []
for name in filter(None, files):
    path = PurePosixPath(name)
    if (set(path.parts) & BLOCKED_DIRS
        or path.suffix.lower() in BLOCKED_SUFFIXES
        or (path.name.startswith(".env") and path.name != ".env.example")
        or path.name == "secrets.json"
        or path.name.endswith(".local.json")
        or ".db-" in path.name
        or name.startswith("src/Autobahn.Host/wwwroot/admin/")):
        blocked.append(name)
if blocked:
    print("Private or generated files must be removed from Git:")
    print("\n".join(blocked))
    sys.exit(1)
print(f"Publication file check passed ({len(list(filter(None, files)))} files).")
