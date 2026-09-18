import importlib.util
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location("godot_log", Path(__file__).with_name("check-godot-log.py"))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class LogGateTests(unittest.TestCase):
    def test_only_pinned_post_export_shutdown_is_classified(self):
        header = "Godot Engine v4.6.2.stable.mono.official.71f334935\n"
        complete = "\x1b[92m[ DONE ]\x1b[39m \x1b[1mexport\x1b[22m\n"
        diagnostic = module.ANDROID_SHUTDOWN
        self.assertEqual(module.errors(header + complete + diagnostic, True), ([], [diagnostic]))
        for text, exported in [(header + complete + diagnostic, False),
                               (header + diagnostic + "\n" + complete, True),
                               (complete + diagnostic, True),
                               (header.replace("4.6.2", "4.7.0") + complete + diagnostic, True)]:
            self.assertEqual(module.errors(text, exported)[0], [diagnostic])
        self.assertEqual(module.errors(header + complete + diagnostic + "\nSCRIPT ERROR: failed\nERROR: missing assembly", True)[0],
                         ["SCRIPT ERROR: failed", "ERROR: missing assembly"])


if __name__ == "__main__":
    unittest.main()
