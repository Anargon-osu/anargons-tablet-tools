#!/usr/bin/env python3
"""Check that CI helpers reject failed processes and invalid firmware oracles."""
import importlib.util
import contextlib
import io
import json
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

HERE = Path(__file__).resolve().parent


def load(name, filename):
    spec = importlib.util.spec_from_file_location(name, HERE / filename)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


RUNNER = load("runner", "run-tests.py")
DUMP = load("dump", "dumptool.py")
REGRESSIONS = load("regressions", "run-regressions.py")


def record(address, kind, data):
    values = bytes((len(data), address >> 8, address & 255, kind)) + data
    return ":" + (values + bytes((-sum(values) & 255,))).hex().upper() + "\n"


class InfrastructureTests(unittest.TestCase):
    def test_process_exit_code_overrides_success_looking_output(self):
        suite = RUNNER.Suite(sys.executable)
        with contextlib.redirect_stdout(io.StringIO()):
            suite.run("false-success", ["-c", "print('RESULT selftest done'); raise SystemExit(3)"])
        self.assertEqual(suite.failed, 1)

    def test_expected_failure_must_actually_fail(self):
        suite = RUNNER.Suite(sys.executable)
        with contextlib.redirect_stdout(io.StringIO()):
            suite.run("false-failure", ["-c", "print('refused')"], expected="failure")
        self.assertEqual(suite.failed, 1)

    def test_hex_oracle_requires_complete_valid_data(self):
        header = record(0, 4, bytes.fromhex("0800"))
        data = record(0x3800, 0, bytes.fromhex("01020304"))
        end = record(0, 1, b"")
        with tempfile.TemporaryDirectory(prefix="hex test ") as folder:
            path = Path(folder) / "fixture.hex"
            path.write_text(header + data + end, encoding="ascii")
            self.assertEqual(DUMP.hex_crc(path, 0x08003804), 0xB63CFBCD)
            for invalid in (header + data, header + data + data + end,
                            header + data + end + data,
                            header + data[:-3] + "00\n" + end,
                            header + data + end):
                path.write_text(invalid, encoding="ascii")
                # The final case asks for one more byte than the fixture contains.
                bound = 0x08003805 if invalid == header + data + end else 0x08003804
                with self.assertRaises(ValueError):
                    DUMP.hex_crc(path, bound)

    def test_stock_dump_validation_includes_size_and_code_crc(self):
        self.assertIsNone(DUMP.build_of(bytes(65536)))
        self.assertIsNone(DUMP.build_of(bytes(65535)))

    def test_default_regression_work_is_unique_and_explicit_work_must_be_fresh(self):
        with tempfile.TemporaryDirectory(prefix="regression isolation ") as temporary:
            with patch.object(REGRESSIONS, "ROOT", Path(temporary)):
                first = REGRESSIONS.prepare_work(None)
                second = REGRESSIONS.prepare_work(None)
                self.assertNotEqual(first, second)
                self.assertTrue(first.is_dir() and second.is_dir())
                with self.assertRaises(FileExistsError):
                    REGRESSIONS.prepare_work(first)

    def test_firmware_harness_receipts_belong_to_each_suite_invocation(self):
        with tempfile.TemporaryDirectory(prefix="firmware harness isolation ") as temporary:
            first = RUNNER.Suite(sys.executable, Path(temporary) / "first", "first-invocation")
            second = RUNNER.Suite(sys.executable, Path(temporary) / "second", "second-invocation")
            completed = SimpleNamespace(returncode=0, stdout="captured harness output\n")
            with patch.object(RUNNER.subprocess, "run", return_value=completed) as execute:
                with contextlib.redirect_stdout(io.StringIO()):
                    first.run_regressions("feb", Path(temporary) / "fixture.bin")
                    second.run_regressions("feb", Path(temporary) / "fixture.bin")
            commands = [call.args[0] for call in execute.call_args_list]
            for suite, command in zip((first, second), commands):
                self.assertEqual(command[command.index("--work") + 1], str(suite.work / "regressions-feb"))
                self.assertEqual(command[command.index("--run-id") + 1], suite.run_id + "-feb")
                suite.write_receipt()
                receipt = json.loads((suite.work / "result.json").read_text(encoding="utf-8"))
                self.assertEqual(receipt["run_id"], suite.run_id)
                self.assertEqual(receipt["regression_runs"][0]["work"], str(suite.work / "regressions-feb"))
                self.assertEqual(Path(receipt["regression_runs"][0]["output"]).read_text(), completed.stdout)
            self.assertNotEqual(first.regression_runs[0]["work"], second.regression_runs[0]["work"])

    def test_integration_refuses_reusing_a_work_directory(self):
        with tempfile.TemporaryDirectory(prefix="integration reuse ") as temporary:
            work = Path(temporary) / "run"
            RUNNER.Suite(sys.executable, work)
            with self.assertRaises(FileExistsError):
                RUNNER.Suite(sys.executable, work)


if __name__ == "__main__":
    unittest.main()
