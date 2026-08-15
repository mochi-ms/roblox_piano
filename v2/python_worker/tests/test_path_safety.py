import unittest
import sys
from pathlib import Path

# Add python_worker root to sys.path
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

import protocol
from basic_pitch_backend import FakeTranscriptionBackend


class TestPathSafety(unittest.TestCase):
    def test_valid_job_ids(self):
        valid_ids = [
            "c2e5661b36bb489fae0daecbca1dbeea",
            "c2e5661b-36bb-489f-ae0d-aecbca1dbeea",
            "job_123",
            "JOB-456_TEST"
        ]
        for jid in valid_ids:
            self.assertTrue(protocol.validate_job_id(jid), f"Should accept: {jid}")

    def test_invalid_job_ids(self):
        invalid_ids = [
            "../../outside",
            "../outside",
            "job/subfolder",
            "job\\subfolder",
            "C:\\Windows",
            "\\\\server\\share",
            ".",
            "..",
            "",
            None,
            "job with spaces",
            "job$bad!char"
        ]
        for jid in invalid_ids:
            self.assertFalse(protocol.validate_job_id(jid), f"Should reject: {jid}")

    def test_backend_rejects_malformed_job_id(self):
        backend = FakeTranscriptionBackend()
        with self.assertRaises(ValueError):
            backend.transcribe(
                audio_path="some.wav",
                output_dir="some_dir",
                job_id="../../traversal"
            )


if __name__ == "__main__":
    unittest.main()
