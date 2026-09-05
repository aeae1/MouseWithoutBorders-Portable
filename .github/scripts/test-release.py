import importlib.util
import pathlib
import unittest

spec = importlib.util.spec_from_file_location("verify_release", pathlib.Path(__file__).with_name("verify-release.py"))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class ReleaseValidationTests(unittest.TestCase):
    def test_stable(self):
        module.validate("mwb-v1.0.1", "false", "1.0.1")

    def test_prerelease(self):
        module.validate("mwb-v1.0.1-rc.1", "true", "1.0.1-rc.1")

    def test_reject_mismatch(self):
        for tag, flag, version in [
            ("mwb-v1.0.0", "false", "1.0.1"),
            ("mwb-v1.0.1", "true", "1.0.1"),
            ("mwb-v1.0.1-rc.1", "false", "1.0.1-rc.1"),
            ("invalid", "false", "1.0.1"),
        ]:
            with self.subTest(tag=tag, flag=flag):
                with self.assertRaises(ValueError):
                    module.validate(tag, flag, version)


if __name__ == "__main__":
    unittest.main()
