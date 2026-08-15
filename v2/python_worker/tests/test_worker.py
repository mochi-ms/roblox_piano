import io
import os
import shutil
import sys
import tempfile
import unittest
from pathlib import Path

# Add python_worker root to sys.path
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

import protocol
from basic_pitch_backend import FakeTranscriptionBackend


class TestWorkerBackend(unittest.TestCase):
    def setUp(self):
        self.temp_dir = tempfile.mkdtemp(prefix="rp_worker_test_")
        self.audio_path = os.path.join(self.temp_dir, "input.wav")
        with open(self.audio_path, "wb") as f:
            f.write(b"RIFF....WAVEfmt ")

    def tearDown(self):
        shutil.rmtree(self.temp_dir, ignore_errors=True)

    def test_fake_backend_success_writes_atomic_midi(self):
        backend = FakeTranscriptionBackend()
        out_dir = os.path.join(self.temp_dir, "out_job")

        statuses = []
        result = backend.transcribe(
            audio_path=self.audio_path,
            output_dir=out_dir,
            job_id="job_01",
            on_status=lambda phase, msg: statuses.append((phase, msg))
        )

        final_midi = os.path.join(out_dir, "transcription.mid")
        temp_midi = os.path.join(out_dir, "transcription.tmp.mid")

        self.assertTrue(os.path.exists(final_midi))
        self.assertFalse(os.path.exists(temp_midi))
        self.assertEqual(result["note_count"], 2)
        self.assertEqual(result["midi_path"], final_midi)
        self.assertTrue(any(s[0] == "transcribing" for s in statuses))
        self.assertTrue(any(s[0] == "writing_midi" for s in statuses))

    def test_fake_backend_failure_leaves_no_final_midi(self):
        backend = FakeTranscriptionBackend(should_fail=True, fail_stage="transcribe")
        out_dir = os.path.join(self.temp_dir, "out_fail")

        with self.assertRaises(RuntimeError):
            backend.transcribe(
                audio_path=self.audio_path,
                output_dir=out_dir,
                job_id="job_fail"
            )

        final_midi = os.path.join(out_dir, "transcription.mid")
        temp_midi = os.path.join(out_dir, "transcription.tmp.mid")
        self.assertFalse(os.path.exists(final_midi))
        self.assertFalse(os.path.exists(temp_midi))


if __name__ == "__main__":
    unittest.main()
