"""Fail without printing matching values. This is a heuristic, not a substitute for secret review."""

import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
patterns = [
    re.compile(rb"gsk_[a-zA-Z0-9]{30,}"),
    re.compile(rb"sk-(?:proj-)?[a-zA-Z0-9_-]{40,}"),
    re.compile(rb"__Secure-1PSID(?:TS)?\s*=\s*[a-zA-Z0-9_./%-]{40,}"),
    re.compile(rb"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----"),
]
result = subprocess.run(["git", "ls-files", "-z"], cwd=ROOT, capture_output=True, check=True)  # noqa: S603,S607 - fixed read-only Git command
violations = []
files = result.stdout.decode().split("\0")
for name in files:
    if not name:
        continue
    path = ROOT / name
    if not path.is_file():
        continue
    data = path.read_bytes()
    if any(pattern.search(data) for pattern in patterns):
        violations.append(name)
if violations:
    print("Potential credentials detected in tracked paths:", *violations, sep="\n", file=sys.stderr)
    sys.exit(1)
print(f"PASS credential pattern scan of {len([name for name in files if name])} tracked files (no values printed)")
