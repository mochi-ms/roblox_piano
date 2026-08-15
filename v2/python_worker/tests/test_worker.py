import io
import os
import shutil
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import MagicMock, patch

# Add python_worker root to sys.path
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

import protocol
from basic_pitch_backend import BasicPitchBackend, FakeTranscriptionBackend


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

    def test_basic_pitch_backend_constructs_model_with_official_model_path(self):
        backend = BasicPitchBackend()
        mock_model_cls = MagicMock()
        mock_model_instance = MagicMock()
        mock_model_cls.return_value = mock_model_instance

        with patch.dict("sys.modules", {
            "basic_pitch": MagicMock(ICASSP_2022_MODEL_PATH="/mock/path/to/icassp_model"),
            "basic_pitch.inference": MagicMock(Model=mock_model_cls)
        }):
            backend.load_model()

            mock_model_cls.assert_called_once_with("/mock/path/to/icassp_model")
            self.assertIs(backend._model, mock_model_instance)

    def test_environment_exact_040_available(self):
        backend = BasicPitchBackend()
        with patch("sys.version_info", (3, 11, 2, "final", 0)), \
             patch("importlib.metadata.version", return_value="0.4.0"):
            avail, py_ver, bp_ver, msg = backend.check_environment()
            self.assertTrue(avail)
            self.assertEqual(bp_ver, "0.4.0")
            self.assertIn("정상", msg)

    def test_environment_wrong_basic_pitch_version_unavailable(self):
        backend = BasicPitchBackend()
        with patch("sys.version_info", (3, 11, 2, "final", 0)), \
             patch("importlib.metadata.version", return_value="0.5.0"):
            avail, py_ver, bp_ver, msg = backend.check_environment()
            self.assertFalse(avail)
            self.assertEqual(bp_ver, "0.5.0")
            self.assertIn("0.4.0", msg)

    def test_environment_missing_basic_pitch_unavailable(self):
        backend = BasicPitchBackend()
        with patch("sys.version_info", (3, 11, 2, "final", 0)), \
             patch("importlib.metadata.version", side_effect=Exception("Package not found")):
            avail, py_ver, bp_ver, msg = backend.check_environment()
            self.assertFalse(avail)
            self.assertIn("패키지를 로드할 수 없습니다", msg)

    def test_environment_wrong_python_unavailable(self):
        backend = BasicPitchBackend()
        with patch("sys.version_info", (3, 10, 5, "final", 0)):
            avail, py_ver, bp_ver, msg = backend.check_environment()
            self.assertFalse(avail)
            self.assertIn("3.11", msg)


if __name__ == "__main__":
    unittest.main()
