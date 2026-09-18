#!/usr/bin/env python3
"""Reject Godot errors, with one pinned editor-shutdown diagnostic exception."""
import re
import sys
from pathlib import Path

ANDROID_SHUTDOWN = 'ERROR: EditorSettings not instantiated yet when getting setting "export/android/android_sdk_path".'


def errors(text, exported=False):
    lines = re.sub(r"\x1b\[[0-9;]*m", "", text).splitlines()
    pinned = any(line.startswith("Godot Engine v4.6.2.stable.mono.official.") for line in lines)
    completed = -1
    failures, warnings = [], []
    for index, line in enumerate(lines):
        if re.fullmatch(r"\[\s*DONE\s*\]\s+export\s*", line):
            completed = index
        if "ERROR:" not in line:
            continue
        # The Android device-poll thread can outlive EditorSettings on desktop exit.
        # Never waive this in a running client, before export completion, on another
        # engine version, or for a different diagnostic. Keep the raw log intact.
        if exported and pinned and completed >= 0 and line == ANDROID_SHUTDOWN:
            warnings.append(line)
        else:
            failures.append(line)
    return failures, warnings


if __name__ == "__main__":
    args = sys.argv[1:]
    export_mode = bool(args and args[0] == "--export")
    if export_mode:
        args = args[1:]
    if len(args) != 1:
        sys.exit("Usage: check-godot-log.py [--export] <log>")
    failures, warnings = errors(Path(args[0]).read_text(), export_mode)
    for warning in warnings:
        print("Known Godot 4.6.2 editor shutdown diagnostic (package smoke remains required): " + warning, file=sys.stderr)
    for failure in failures:
        print(failure, file=sys.stderr)
    sys.exit(bool(failures))
