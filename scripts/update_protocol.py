"""Regenerate both protocol models and run all protocol checks."""
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]


def main():
    steps = [
        ("Generating Python and C# models", "generate_protocol.py", []),
        ("Checking generated files", "generate_protocol.py", ["--check"]),
        ("Running protocol tests", "check_protocol.py", []),
    ]
    for title, script, arguments in steps:
        print(f"\n{title}...", flush=True)
        result = subprocess.run(
            [sys.executable, str(ROOT / "scripts" / script), *arguments], cwd=ROOT,
        )
        if result.returncode:
            print(f"Stopped: {script} failed (exit {result.returncode}).", file=sys.stderr)
            return result.returncode
    print("\nProtocol updated and verified. Refresh Unity and reconnect MCP.", flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
