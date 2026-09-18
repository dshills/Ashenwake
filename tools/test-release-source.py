import importlib.util
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


spec = importlib.util.spec_from_file_location("release_source", Path(__file__).with_name("check-release-source.py"))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class SourceGateTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="ashenwake-source-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name) / "repo"
        self.root.mkdir()
        self.git("init", "--quiet")
        self.git("config", "user.name", "Local fixture")
        self.git("config", "user.email", "fixture@example.invalid")
        self.write(".gitignore", "*.cs\n*.props\n.tools/\nartifacts/\n")
        self.write("game/Ashenwake.Client/Main.cs", "class Main {}\n")
        self.write("game/Ashenwake.Client/Main.cs.uid", "uid://reviewed\n")
        self.write("game/Ashenwake.Client/Other.gd", "extends Node\n")
        self.write("content/combat.json", "{}\n")
        self.write("tools/build.sh", "#!/bin/sh\nexit 0\n")
        (self.root / "tools/build.sh").chmod(0o755)
        os.symlink("combat.json", self.root / "content/current.json")
        self.git("add", "--force", ".")
        self.git("commit", "--quiet", "-m", "fixture")
        self.commit = self.git("rev-parse", "HEAD")
        self.tree = self.git("rev-parse", "HEAD^{tree}")

    def git(self, *args):
        return subprocess.check_output(["git", "-C", str(self.root), *args], stderr=subprocess.DEVNULL).decode().strip()

    def write(self, relative, text, root=None):
        path = (root or self.root) / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text)
        return path

    def verify(self, root=None, source=None):
        return module.verify(root or self.root, self.root, source or self.commit)

    def test_commit_and_tree_verify_without_mutating_git_index(self):
        index = (self.root / ".git/index").read_bytes()
        self.assertEqual(self.verify()["treeId"], self.tree)
        self.assertEqual(self.verify(source=self.commit.upper())["treeId"], self.tree)
        self.assertEqual(self.verify(source=self.tree)["trackedFiles"], 7)
        self.assertEqual((self.root / ".git/index").read_bytes(), index)

    def test_changed_deleted_executable_and_symlink_entries_reject(self):
        cases = [
            lambda: self.write("content/combat.json", '{"changed":true}\n'),
            lambda: (self.root / "content/combat.json").unlink(),
            lambda: (self.root / "tools/build.sh").chmod(0o644),
            lambda: (self.root / "content/current.json").unlink(),
        ]
        for mutate in cases:
            with self.subTest(mutation=mutate):
                self.git("reset", "--hard", "--quiet", self.commit)
                mutate()
                with self.assertRaises(module.SourceMismatch):
                    self.verify()

    def test_isolated_snapshot_uses_explicit_read_only_repository(self):
        snapshot = Path(self.temporary.name) / "snapshot"
        shutil.copytree(self.root, snapshot, symlinks=True, ignore=shutil.ignore_patterns(".git"))
        result = subprocess.run(["python3", str(Path(module.__file__).resolve()), self.tree,
                                 "--source-directory", str(snapshot)], capture_output=True, text=True,
                                env={**os.environ, "ASHENWAKE_SOURCE_REPOSITORY": str(self.root)})
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn(self.tree, result.stdout)
        self.write("content/combat.json", "{ }\n", snapshot)
        with self.assertRaises(module.SourceMismatch):
            self.verify(snapshot)

    def test_unknown_blob_abbreviated_and_unrelated_hashes_reject(self):
        for source in ("f" * 40, self.commit[:12], self.git("rev-parse", "HEAD:content/combat.json"), "HEAD"):
            with self.subTest(source=source), self.assertRaises(module.SourceMismatch):
                self.verify(source=source)
        self.write("content/combat.json", '{"second":true}\n')
        self.git("add", ".")
        self.git("commit", "--quiet", "-m", "different")
        with self.assertRaises(module.SourceMismatch):
            self.verify()

    def test_ignored_extra_compiler_and_content_inputs_reject(self):
        for relative in ("game/Ashenwake.Client/Hidden.cs", "Directory.Build.props", "content/extra.json",
                         "game/Ashenwake.Client/unreviewed.json", "tools/extra.sh"):
            with self.subTest(path=relative):
                extra = self.write(relative, "unreviewed")
                with self.assertRaisesRegex(module.SourceMismatch, "undeclared file"):
                    self.verify()
                extra.unlink()

    def test_generated_outputs_and_caches_are_not_scanned_but_tracked_uid_is_checked(self):
        for directory in (".tools", "artifacts", "game/Ashenwake.Client/bin", "game/Ashenwake.Client/obj",
                          "game/Ashenwake.Client/.godot", "tools/__pycache__"):
            self.write(directory + "/unreviewed.cs", "cache")
        self.write("game/Ashenwake.Client/combat.json", "generated")
        self.write("game/Ashenwake.Client/content.bundle.json", "generated")
        self.write("game/Ashenwake.Client/Other.gd.uid", "uid://generated\n")
        self.verify()
        self.write("game/Ashenwake.Client/Main.cs.uid", "uid://changed\n")
        with self.assertRaisesRegex(module.SourceMismatch, "changed bytes or mode"):
            self.verify()

    def test_symlink_parent_and_generated_symlink_cannot_hide_other_source(self):
        target = Path(self.temporary.name) / "external"
        shutil.move(self.root / "content", target)
        os.symlink(target, self.root / "content")
        with self.assertRaisesRegex(module.SourceMismatch, "symlink parent"):
            self.verify()
        (self.root / "content").unlink()
        shutil.move(target, self.root / "content")
        os.symlink(target, self.root / "game/Ashenwake.Client/combat.json")
        with self.assertRaisesRegex(module.SourceMismatch, "undeclared file"):
            self.verify()

    def test_changed_symlink_target_and_unsupported_submodule_reject(self):
        (self.root / "content/current.json").unlink()
        os.symlink("../.gitignore", self.root / "content/current.json")
        with self.assertRaisesRegex(module.SourceMismatch, "changed bytes or mode"):
            self.verify()
        self.git("update-index", "--add", "--cacheinfo", "160000," + self.commit + ",vendor")
        with self.assertRaisesRegex(module.SourceMismatch, "including submodules"):
            self.verify(source=self.git("write-tree"))


if __name__ == "__main__":
    unittest.main()
