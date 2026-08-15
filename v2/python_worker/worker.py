#!/usr/bin/env python3
"""RobloxPiano AI Transcription Worker Process.

Communicates with C# host over NDJSON stdin/stdout.
CRITICAL: All regular print/stdout is redirected to stderr so that only strict
NDJSON protocol messages are emitted on the original stdout descriptor.
"""

import argparse
import sys
import traceback
from typing import Optional

import protocol
from basic_pitch_backend import BasicPitchBackend, FakeTranscriptionBackend, ITranscriptionBackend

# Save unpolluted original stdout before redirection
_PROTOCOL_STDOUT = sys.__stdout__

# Redirect standard stdout to stderr to prevent any third-party library from polluting NDJSON protocol
sys.stdout = sys.stderr


def send_protocol(msg_dict: dict) -> None:
    """Send a strictly formatted NDJSON message to the host over original stdout."""
    line = protocol.format_line(msg_dict)
    _PROTOCOL_STDOUT.write(line)
    _PROTOCOL_STDOUT.flush()


def run_worker(backend: Optional[ITranscriptionBackend] = None) -> int:
    parser = argparse.ArgumentParser(description="RobloxPiano AI Transcription Worker")
    parser.add_argument("--backend", choices=["basic_pitch", "fake"], default="basic_pitch", help="Inference backend to use")
    args, _ = parser.parse_known_args()

    if backend is None:
        if args.backend == "fake":
            backend = FakeTranscriptionBackend()
        else:
            backend = BasicPitchBackend()

    # 1. Environment check
    is_available, py_ver, bp_ver, status_msg = backend.check_environment()

    # 2. Emit initial hello handshake
    send_protocol(protocol.create_hello(
        worker_version="1.0.0",
        python_version=py_ver,
        basic_pitch_version=bp_ver if is_available else "none",
        request_id="startup"
    ))

    # 3. Main NDJSON request processing loop
    while True:
        try:
            line = sys.stdin.readline()
            if not line:
                # EOF reached: C# closed stdin or process is terminating
                break

            line = line.strip()
            if not line:
                continue

            try:
                request = protocol.parse_line(line)
            except protocol.ProtocolError as p_err:
                send_protocol(protocol.create_error(
                    request_id="unknown",
                    error_code="PROTOCOL_ERROR",
                    error_message=str(p_err)
                ))
                continue

            req_type = request.get("type")
            req_id = request.get("request_id", "unknown")

            if req_type == "ping":
                send_protocol(protocol.create_pong(req_id))
                continue

            if req_type == "shutdown":
                break

            if req_type == "transcribe":
                job_id = request.get("job_id", "")
                audio_path = request.get("audio_path", "")
                output_dir = request.get("output_dir", "")
                options = request.get("options", {}) or {}

                onset_threshold = float(options.get("onset_threshold", 0.5))
                frame_threshold = float(options.get("frame_threshold", 0.3))
                min_note_len_ms = float(options.get("minimum_note_length_ms", 127.7))

                if not protocol.validate_job_id(job_id):
                    send_protocol(protocol.create_error(
                        request_id=req_id,
                        error_code="INVALID_JOB_ID",
                        error_message=f"유효하지 않거나 안전하지 않은 작업 ID입니다: '{job_id}'",
                        job_id=job_id
                    ))
                    continue

                def on_status(phase: str, message: str) -> None:
                    send_protocol(protocol.create_status(
                        request_id=req_id,
                        job_id=job_id,
                        phase=phase,
                        message=message
                    ))

                try:
                    res = backend.transcribe(
                        audio_path=audio_path,
                        output_dir=output_dir,
                        job_id=job_id,
                        onset_threshold=onset_threshold,
                        frame_threshold=frame_threshold,
                        minimum_note_length_ms=min_note_len_ms,
                        on_status=on_status
                    )

                    send_protocol(protocol.create_result(
                        request_id=req_id,
                        job_id=job_id,
                        midi_path=res["midi_path"],
                        note_count=res["note_count"],
                        duration_seconds=res["duration_seconds"],
                        min_pitch=res["min_pitch"],
                        max_pitch=res["max_pitch"],
                        runtime_seconds=res["runtime_seconds"],
                        engine_version=res.get("engine_version", "0.4.0")
                    ))
                except Exception as ex:
                    sys.stderr.write(f"Inference exception: {traceback.format_exc()}\n")
                    sys.stderr.flush()
                    send_protocol(protocol.create_error(
                        request_id=req_id,
                        error_code="INFERENCE_FAILED",
                        error_message=str(ex),
                        job_id=job_id
                    ))
                continue

            # Unknown request type
            send_protocol(protocol.create_error(
                request_id=req_id,
                error_code="UNKNOWN_TYPE",
                error_message=f"지원되지 않는 요청 타입입니다: '{req_type}'"
            ))

        except Exception as top_ex:
            sys.stderr.write(f"Worker unhandled top-level exception: {traceback.format_exc()}\n")
            sys.stderr.flush()

    return 0


if __name__ == "__main__":
    sys.exit(run_worker())
