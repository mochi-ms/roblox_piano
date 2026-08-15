"""Spotify Basic Pitch backend integration with atomic MIDI output and model caching."""

import os
import sys
import time
from abc import ABC, abstractmethod
from pathlib import Path
from typing import Any, Callable, Dict, Optional, Tuple

import protocol


class ITranscriptionBackend(ABC):
    """Abstract interface for AI transcription backends."""

    @abstractmethod
    def check_environment(self) -> Tuple[bool, str, str, str]:
        """Returns (is_available, python_version, basic_pitch_version, status_message)."""
        pass

    @abstractmethod
    def load_model(self, on_status: Optional[Callable[[str, str], None]] = None) -> None:
        """Load neural network model into memory."""
        pass

    @abstractmethod
    def transcribe(
        self,
        audio_path: str,
        output_dir: str,
        job_id: str,
        onset_threshold: float,
        frame_threshold: float,
        minimum_note_length_ms: float,
        on_status: Optional[Callable[[str, str], None]] = None
    ) -> Dict[str, Any]:
        """Perform inference on audio_path and write atomic transcription.mid in output_dir."""
        pass


class BasicPitchBackend(ITranscriptionBackend):
    """Production backend executing Spotify Basic Pitch 0.4.0."""

    def __init__(self) -> None:
        self._model: Any = None
        self._basic_pitch_version = "unknown"

    def check_environment(self) -> Tuple[bool, str, str, str]:
        py_ver = f"{sys.version_info[0]}.{sys.version_info[1]}.{sys.version_info[2]}"
        if sys.version_info[0] != 3 or sys.version_info[1] != 11:
            return False, py_ver, "", f"CPython 3.11이 필요하지만 현재 {py_ver}입니다."

        try:
            import importlib.metadata
            bp_ver = importlib.metadata.version("basic-pitch")
            self._basic_pitch_version = bp_ver
            if bp_ver != "0.4.0":
                return False, py_ver, bp_ver, f"Basic Pitch 0.4.0이 필요하지만 현재 {bp_ver}가 설치되어 있습니다."
            return True, py_ver, bp_ver, "정상"
        except Exception as ex:
            return False, py_ver, "", f"Basic Pitch 패키지를 로드할 수 없습니다: {ex}"

    def load_model(self, on_status: Optional[Callable[[str, str], None]] = None) -> None:
        if self._model is not None:
            return

        if on_status:
            on_status("model_loading", "Basic Pitch AI 모델 로딩 중...")

        try:
            from basic_pitch import ICASSP_2022_MODEL_PATH
            from basic_pitch.inference import Model
            self._model = Model(ICASSP_2022_MODEL_PATH)
            if on_status:
                on_status("model_ready", "AI 모델 준비 완료")
        except Exception as ex:
            raise RuntimeError(f"Basic Pitch 모델 초기화 실패: {ex}") from ex

    def transcribe(
        self,
        audio_path: str,
        output_dir: str,
        job_id: str,
        onset_threshold: float = 0.5,
        frame_threshold: float = 0.3,
        minimum_note_length_ms: float = 127.7,
        on_status: Optional[Callable[[str, str], None]] = None
    ) -> Dict[str, Any]:
        if not protocol.validate_job_id(job_id):
            raise ValueError(f"유효하지 않거나 안전하지 않은 작업 ID입니다: {job_id}")

        audio_file = Path(audio_path).resolve()
        if not audio_file.is_file():
            raise FileNotFoundError(f"입력 오디오 파일을 찾을 수 없습니다: {audio_path}")

        out_path = Path(output_dir).resolve()
        out_path.mkdir(parents=True, exist_ok=True)

        temp_midi = out_path / "transcription.tmp.mid"
        final_midi = out_path / "transcription.mid"

        if temp_midi.exists():
            temp_midi.unlink(missing_ok=True)

        self.load_model(on_status)

        if on_status:
            on_status("transcribing", "오디오 피치 및 타이밍 분석 중...")

        start_time = time.perf_counter()

        try:
            from basic_pitch.inference import predict
            # Predict returns (model_output, midi_data, note_events)
            model_output, midi_data, note_events = predict(
                str(audio_file),
                model_or_model_path=self._model,
                onset_threshold=onset_threshold,
                frame_threshold=frame_threshold,
                minimum_note_length=minimum_note_length_ms
            )

            if on_status:
                on_status("writing_midi", "MIDI 파일 생성 중...")

            # midi_data is a pretty_midi.PrettyMIDI instance
            midi_data.write(str(temp_midi))

            if not temp_midi.exists() or temp_midi.stat().st_size == 0:
                raise RuntimeError("생성된 임시 MIDI 파일이 비어있거나 생성되지 않았습니다.")

            # Atomic replace
            os.replace(temp_midi, final_midi)

            duration_seconds = float(midi_data.get_end_time())
            all_notes = []
            for instrument in midi_data.instruments:
                for note in instrument.notes:
                    all_notes.append(note.pitch)

            note_count = len(all_notes)
            min_pitch = min(all_notes) if all_notes else None
            max_pitch = max(all_notes) if all_notes else None

            runtime_seconds = time.perf_counter() - start_time

            return {
                "midi_path": str(final_midi),
                "note_count": note_count,
                "duration_seconds": duration_seconds,
                "min_pitch": min_pitch,
                "max_pitch": max_pitch,
                "runtime_seconds": runtime_seconds,
                "engine_version": self._basic_pitch_version or "0.4.0"
            }
        except Exception:
            if temp_midi.exists():
                temp_midi.unlink(missing_ok=True)
            raise


class FakeTranscriptionBackend(ITranscriptionBackend):
    """Fast lightweight mock backend for testing without TensorFlow/Basic Pitch."""

    def __init__(self, should_fail: bool = False, fail_stage: str = "") -> None:
        self.should_fail = should_fail
        self.fail_stage = fail_stage
        self.load_model_called = False

    def check_environment(self) -> Tuple[bool, str, str, str]:
        return True, "3.11.0", "0.4.0", "정상 (Fake)"

    def load_model(self, on_status: Optional[Callable[[str, str], None]] = None) -> None:
        self.load_model_called = True
        if self.should_fail and self.fail_stage == "load_model":
            raise RuntimeError("Fake model loading failure")
        if on_status:
            on_status("model_loading", "Fake model loading...")
            on_status("model_ready", "Fake model ready")

    def transcribe(
        self,
        audio_path: str,
        output_dir: str,
        job_id: str,
        onset_threshold: float = 0.5,
        frame_threshold: float = 0.3,
        minimum_note_length_ms: float = 127.7,
        on_status: Optional[Callable[[str, str], None]] = None
    ) -> Dict[str, Any]:
        if not protocol.validate_job_id(job_id):
            raise ValueError(f"유효하지 않거나 안전하지 않은 작업 ID입니다: {job_id}")

        if not Path(audio_path).exists():
            raise FileNotFoundError(f"입력 오디오 파일을 찾을 수 없습니다: {audio_path}")

        out_path = Path(output_dir).resolve()
        out_path.mkdir(parents=True, exist_ok=True)

        temp_midi = out_path / "transcription.tmp.mid"
        final_midi = out_path / "transcription.mid"

        if on_status:
            on_status("transcribing", "Fake transcribing...")

        if self.should_fail and self.fail_stage == "transcribe":
            raise RuntimeError("Fake transcription inference failure")

        if on_status:
            on_status("writing_midi", "Fake writing MIDI...")

        # Create a valid minimal standard Type 0 MIDI file
        # MThd header (14 bytes) + MTrk header + NoteOn/Off events + EndOfTrack
        raw_midi = bytearray([
            0x4D, 0x54, 0x68, 0x64,  # "MThd"
            0x00, 0x00, 0x00, 0x06,  # Chunk size: 6
            0x00, 0x00,              # Format: 0 (single track)
            0x00, 0x01,              # Number of tracks: 1
            0x01, 0xE0,              # Ticks per quarter note: 480
            # MTrk chunk
            0x4D, 0x54, 0x72, 0x6B,  # "MTrk"
            0x00, 0x00, 0x00, 0x16,  # Chunk length: 22 bytes
            # Events:
            0x00, 0x90, 0x3C, 0x60,  # delta=0, NoteOn ch=0, pitch=60 (C4), vel=96
            0x83, 0x60, 0x80, 0x3C, 0x40, # delta=480, NoteOff ch=0, pitch=60, vel=64
            0x00, 0x90, 0x40, 0x60,  # delta=0, NoteOn ch=0, pitch=64 (E4), vel=96
            0x83, 0x60, 0x80, 0x40, 0x40, # delta=480, NoteOff ch=0, pitch=64, vel=64
            0x00, 0xFF, 0x2F, 0x00   # delta=0, End of track
        ])

        with open(temp_midi, "wb") as f:
            f.write(raw_midi)

        # Atomic move
        os.replace(temp_midi, final_midi)

        return {
            "midi_path": str(final_midi),
            "note_count": 2,
            "duration_seconds": 2.0,
            "min_pitch": 60,
            "max_pitch": 64,
            "runtime_seconds": 0.05,
            "engine_version": "0.4.0"
        }
