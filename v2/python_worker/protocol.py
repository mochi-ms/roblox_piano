"""NDJSON Protocol implementation for RobloxPiano AI Transcription Worker (v1)."""

import json
from typing import Any, Dict, Optional

PROTOCOL_VERSION = 1


class ProtocolError(Exception):
    """Raised when an incoming or outgoing protocol message violates specification."""
    pass


def validate_job_id(job_id: Optional[str]) -> bool:
    """Validate that a job_id only contains safe alphanumeric characters, hyphens, and underscores."""
    if not job_id or not isinstance(job_id, str):
        return False
    if len(job_id) > 128:
        return False
    return all(c.isalnum() or c in ('-', '_') for c in job_id)


def parse_line(line_str: str) -> Dict[str, Any]:
    """Parse a single NDJSON line and validate basic protocol envelope."""
    if not line_str or not line_str.strip():
        raise ProtocolError("빈 메시지 라인입니다.")

    if len(line_str) > 1_048_576:  # 1 MB protection
        raise ProtocolError("메시지 크기가 최대 허용치(1MB)를 초과했습니다.")

    try:
        data = json.loads(line_str.strip())
    except Exception as ex:
        raise ProtocolError(f"JSON 파싱 실패: {ex}")

    if not isinstance(data, dict):
        raise ProtocolError("JSON 최상위 객체가 딕셔너리(Object)가 아닙니다.")

    msg_protocol = data.get("protocol")
    if msg_protocol != PROTOCOL_VERSION:
        raise ProtocolError(f"지원되지 않는 프로토콜 버전입니다: {msg_protocol} (기대치: {PROTOCOL_VERSION})")

    msg_type = data.get("type")
    if not msg_type or not isinstance(msg_type, str):
        raise ProtocolError("메시지 타입('type') 필드가 누락되었거나 문자열이 아닙니다.")

    request_id = data.get("request_id")
    if not request_id or not isinstance(request_id, str):
        raise ProtocolError("요청 ID('request_id') 필드가 누락되었거나 문자열이 아닙니다.")

    return data


def format_line(payload: Dict[str, Any]) -> str:
    """Format a dictionary into an NDJSON string with newline."""
    if "protocol" not in payload:
        payload["protocol"] = PROTOCOL_VERSION
    return json.dumps(payload, ensure_ascii=False) + "\n"


def create_hello(
    worker_version: str,
    python_version: str,
    basic_pitch_version: str,
    engine_available: bool = True,
    status_message: str = "정상",
    request_id: str = "init"
) -> Dict[str, Any]:
    return {
        "type": "hello",
        "protocol": PROTOCOL_VERSION,
        "request_id": request_id,
        "worker_version": worker_version,
        "python_version": python_version,
        "basic_pitch_version": basic_pitch_version,
        "engine_available": engine_available,
        "status_message": status_message
    }


def create_status(
    request_id: str,
    job_id: str,
    phase: str,
    message: str
) -> Dict[str, Any]:
    return {
        "type": "status",
        "protocol": PROTOCOL_VERSION,
        "request_id": request_id,
        "job_id": job_id,
        "phase": phase,
        "message": message
    }


def create_result(
    request_id: str,
    job_id: str,
    midi_path: str,
    note_count: int,
    duration_seconds: float,
    min_pitch: Optional[int],
    max_pitch: Optional[int],
    runtime_seconds: float,
    engine_version: str = "0.4.0"
) -> Dict[str, Any]:
    return {
        "type": "result",
        "protocol": PROTOCOL_VERSION,
        "request_id": request_id,
        "job_id": job_id,
        "midi_path": midi_path,
        "note_count": note_count,
        "duration_seconds": round(duration_seconds, 3),
        "min_pitch": min_pitch,
        "max_pitch": max_pitch,
        "runtime_seconds": round(runtime_seconds, 3),
        "engine_name": "Basic Pitch",
        "engine_version": engine_version
    }


def create_error(
    request_id: str,
    error_code: str,
    error_message: str,
    job_id: Optional[str] = None
) -> Dict[str, Any]:
    payload = {
        "type": "error",
        "protocol": PROTOCOL_VERSION,
        "request_id": request_id,
        "error_code": error_code,
        "error_message": error_message
    }
    if job_id:
        payload["job_id"] = job_id
    return payload


def create_pong(request_id: str) -> Dict[str, Any]:
    return {
        "type": "pong",
        "protocol": PROTOCOL_VERSION,
        "request_id": request_id
    }
