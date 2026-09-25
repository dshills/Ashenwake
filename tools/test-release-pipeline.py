"""Bounded release-script regressions; fake engine/build helpers, no real export."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


TOOLS = Path(__file__).resolve().parent


class ReleasePipelineTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="ashenwake-pipeline-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.write("tools/env.sh", 'AW_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"\nexport AW_ROOT\nexport PATH="$AW_ROOT/fake-bin:$PATH"\nexport GODOT="$AW_ROOT/fake-bin/godot"\n')
        self.write("fake-bin/uname", "#!/bin/bash\necho Darwin\n", executable=True)
        self.write("tools/check-release-source.py", "import os, pathlib\np=pathlib.Path(os.environ['AW_ROOT'])/'events'\nwith p.open('a') as f: f.write('source\\n')\n")
        self.write("tools/check-godot-log.py", "")
        self.write("assets/credits.json", '{"assets":[]}')
        self.write("docs/release_readiness.md", "Unaccepted local release candidate.")
        self.write("game/Ashenwake.Client/project.godot", 'config_version=5\n[application]\nconfig/name="Ashenwake"\nconfig/version="1.0.0-rc.1"\n')
        (self.root / "content").mkdir()
        (self.root / "tools/aw").mkdir()
        for name in ("combat", "adventure", "progression", "text.en", "campaign", "campaign-combat", "endgame", "endgame-combat", "experiments"):
            self.write(f"content/{name}.json", "{}")

    def write(self, name, text, executable=False):
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text)
        if executable:
            path.chmod(0o755)
        return path

    def run_script(self, name, *arguments):
        return subprocess.run(["bash", str(self.root / name), *arguments], cwd=self.root, text=True, capture_output=True, timeout=15)

    def package_stubs(self):
        shutil.copyfile(TOOLS / "release-package.sh", self.root / "tools/release-package.sh")
        self.write("fake-bin/dotnet", '#!/bin/bash\n[[ "$*" == *"-t:Rebuild"* ]] || exit 8\necho dotnet >> "$AW_ROOT/events"\ntouch "$AW_ROOT/dotnet-built"\n', executable=True)
        self.write("fake-bin/go", '#!/bin/bash\n[[ "$PWD" == "$AW_ROOT/tools/aw" ]] || exit 8\necho go >> "$AW_ROOT/events"\ntouch "$AW_ROOT/go-built"\n', executable=True)
        self.write("tools/export.sh", '''#!/bin/bash
set -eu
[[ -f "$AW_ROOT/dotnet-built" && -f "$AW_ROOT/go-built" ]]
echo export >> "$AW_ROOT/events"
mkdir -p artifacts/export artifacts/package/current/pets-client artifacts/package/current/macos/Ashenwake.app
printf zip > artifacts/export/Ashenwake.zip
printf smoke > artifacts/export/package.log
printf export > artifacts/export/export.log
printf '{"schemaVersion":1,"completed":true,"packageOutput":"artifacts/package/current"}' > artifacts/export/package-run.json
printf details > artifacts/package/current/pets-client/smoke.log
printf '{"passed":true}' > artifacts/package/current/pets-client/pets-review.json
printf '{"passed":true}' > artifacts/package/current/pets-client/performance-report.json
printf private > artifacts/package/current/pets-client/character.save
printf replay > artifacts/package/current/pets-client/pets.awendgame
printf excluded > artifacts/package/current/macos/Ashenwake.app/engine.log
''')
        self.write("tools/collect-notices.sh", '#!/bin/bash\necho notices >> "$AW_ROOT/events"\nmkdir -p "$1"\n')
        self.write("fake-bin/aw", '#!/bin/bash\necho "$2" >> "$AW_ROOT/events"\nif [[ "$2" == manifest ]]; then echo \'{}\' > "${@: -1}"; fi\n', executable=True)

    def test_source_check_precedes_fresh_helpers_and_version_is_recorded(self):
        self.package_stubs()
        result = self.run_script("tools/release-package.sh", "a" * 40)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual((self.root / "events").read_text().splitlines(), ["source", "dotnet", "go", "export", "source", "notices", "manifest", "verify"])
        identity = json.loads(next(self.root.glob("artifacts/release/candidate.*/package/build-identity.json")).read_text())
        self.assertEqual(identity["applicationVersion"], "1.0.0-rc.1")
        self.assertEqual(identity["sourceId"], "a" * 40)
        self.assertFalse(identity["publicReleaseAccepted"])
        self.assertEqual(len(identity["contentFiles"]), 9)
        candidate = next(self.root.glob("artifacts/release/candidate.*"))
        evidence = candidate / "evidence"
        self.assertEqual(sorted(p.relative_to(evidence).as_posix() for p in evidence.rglob("*") if p.is_file()), [
            "export.log", "package-run.json", "package-smoke.log", "pets-client/performance-report.json",
            "pets-client/pets-review.json", "pets-client/smoke.log"])
        self.assertEqual((evidence / "pets-client/smoke.log").read_text(), "details")

    def test_helper_build_failure_stops_export(self):
        self.package_stubs()
        self.write("fake-bin/dotnet", "#!/bin/bash\nexit 7\n", executable=True)
        result = self.run_script("tools/release-package.sh", "a" * 40)
        self.assertEqual(result.returncode, 7)
        self.assertEqual((self.root / "events").read_text().splitlines(), ["source"])
        self.assertFalse((self.root / "artifacts/export/Ashenwake.zip").exists())

    def test_missing_version_does_not_create_manifest(self):
        self.package_stubs()
        self.write("game/Ashenwake.Client/project.godot", '[application]\nconfig/name="Ashenwake"\n')
        result = self.run_script("tools/release-package.sh", "a" * 40)
        self.assertNotEqual(result.returncode, 0)
        self.assertNotIn("manifest", (self.root / "events").read_text().splitlines())

    def test_failed_export_invalidates_previous_success_record(self):
        script = (TOOLS / "export.sh").read_text()
        self.write("tools/export.sh", script)
        self.write("artifacts/export/package-run.json", '{"schemaVersion":1,"completed":true}')
        self.write("fake-bin/aw", "#!/bin/bash\nexit 9\n", executable=True)
        result = self.run_script("tools/export.sh")
        self.assertEqual(result.returncode, 9)
        self.assertFalse((self.root / "artifacts/export/package-run.json").exists())

    def test_completed_export_writes_current_run_record(self):
        script = (TOOLS / "export.sh").read_text().split("# Publish the evidence location only after every packaged diagnostic has passed.", 1)[1]
        self.write("tools/export-finish.sh", 'source "$(dirname "$0")/env.sh"\ncd "$AW_ROOT"\npackage_output="$AW_ROOT/artifacts/package/current"\n' + script)
        (self.root / "artifacts/package/current").mkdir(parents=True)
        (self.root / "artifacts/export").mkdir(parents=True)
        result = self.run_script("tools/export-finish.sh")
        self.assertEqual(result.returncode, 0, result.stderr)
        record = json.loads((self.root / "artifacts/export/package-run.json").read_text())
        self.assertEqual(record, {"schemaVersion": 1, "completed": True, "packageOutput": "artifacts/package/current"})

    def test_missing_success_record_blocks_manifest(self):
        self.package_stubs()
        with (self.root / "tools/export.sh").open("a") as script:
            script.write("rm artifacts/export/package-run.json\n")
        result = self.run_script("tools/release-package.sh", "a" * 40)
        self.assertNotEqual(result.returncode, 0)
        self.assertNotIn("manifest", (self.root / "events").read_text().splitlines())

    def test_mac_smoke_uses_fresh_extraction_and_exact_named_executable(self):
        # Run the actual export-script prefix through its first packaged smoke.
        script = (TOOLS / "export.sh").read_text().split("python3 tools/check-godot-log.py artifacts/export/package.log", 1)[0]
        self.write("tools/export-prefix.sh", script)
        self.write("fake-bin/aw", "#!/bin/bash\nexit 0\n", executable=True)
        self.write("artifacts/export/macos/Ashenwake.app/Contents/MacOS/Stale", "#!/bin/bash\nexit 88\n", executable=True)
        self.write("fake-bin/godot", '''#!/usr/bin/env python3
import os, pathlib, sys, zipfile
args=sys.argv
output=pathlib.Path(args[args.index('--export-debug')+2])
info=zipfile.ZipInfo('Ashenwake.app/Contents/MacOS/Ashenwake')
info.external_attr=(0o100755 << 16)
with zipfile.ZipFile(output,'w') as archive:
    archive.writestr(info, '#!/bin/bash\\nprintf "%s" "$0" > "$AW_ROOT/tested-binary"\\n')
pathlib.Path(args[args.index('--log-file')+1]).write_text('Export complete')
''', executable=True)
        result = self.run_script("tools/export-prefix.sh")
        self.assertEqual(result.returncode, 0, result.stderr)
        tested = Path((self.root / "tested-binary").read_text())
        self.assertEqual(tested.name, "Ashenwake")
        self.assertTrue(tested.is_relative_to(self.root / "artifacts/package"))
        self.assertFalse((tested.parent / "Stale").exists())
        self.assertTrue((self.root / "artifacts/export/macos/Ashenwake.app/Contents/MacOS/Stale").exists())
        self.assertTrue((self.root / "artifacts/export/Ashenwake.zip").exists())
        self.assertFalse((self.root / "artifacts/export/package-run.json").exists())


if __name__ == "__main__":
    unittest.main()
