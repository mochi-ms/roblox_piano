import unittest
import sys
from pathlib import Path

# Add python_worker root to sys.path
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

import protocol


class TestProtocol(unittest.TestCase):
    def test_protocol_valid_transcribe_request(self):
        line = '{"type":"transcribe","protocol":1,"request_id":"req_01","job_id":"job_123","audio_path":"C:/test.wav","output_dir":"C:/out"}'
        parsed = protocol.parse_line(line)
        self.assertEqual(parsed["type"], "transcribe")
        self.assertEqual(parsed["protocol"], 1)
        self.assertEqual(parsed["request_id"], "req_01")
        self.assertEqual(parsed["job_id"], "job_123")

    def test_protocol_rejects_bad_version(self):
        line = '{"type":"transcribe","protocol":2,"request_id":"req_01"}'
        with self.assertRaises(protocol.ProtocolError) as ctx:
            protocol.parse_line(line)
        self.assertIn("지원되지 않는 프로토콜 버전", str(ctx.exception))

    def test_protocol_rejects_malformed_json(self):
        line = '{"type":"transcribe", malformed}'
        with self.assertRaises(protocol.ProtocolError) as ctx:
            protocol.parse_line(line)
        self.assertIn("JSON 파싱 실패", str(ctx.exception))

    def test_protocol_rejects_missing_type(self):
        line = '{"protocol":1,"request_id":"req_01"}'
        with self.assertRaises(protocol.ProtocolError) as ctx:
            protocol.parse_line(line)
        self.assertIn("타입('type')", str(ctx.exception))

    def test_protocol_preserves_unicode_paths(self):
        payload = {
            "type": "result",
            "protocol": 1,
            "request_id": "req_uni",
            "job_id": "job_한글_01",
            "midi_path": "C:\\음악 폴더\\쇼팽_피아노.mid"
        }
        line = protocol.format_line(payload)
        parsed = protocol.parse_line(line)
        self.assertEqual(parsed["job_id"], "job_한글_01")
        self.assertEqual(parsed["midi_path"], "C:\\음악 폴더\\쇼팽_피아노.mid")


if __name__ == "__main__":
    unittest.main()
