# RobloxPiano AI Transcription Worker (Basic Pitch 0.4.0)

이 디렉터리는 RobloxPiano v2의 **AI 음향-악보 변환(Audio → MIDI AI Transcription)** 을 담당하는 독립된 Python 3.11 서브프로세스 워커 환경입니다.

## 1. 개요 및 요구사항
- **대상 파이썬 버전**: CPython 3.11.x
- **핵심 라이브러리**: `basic-pitch==0.4.0`
- **통신 프로토콜**: 표준 입력(Stdin) 및 표준 출력(Stdout)을 통한 NDJSON (Newline Delimited JSON, UTF-8)

## 2. 가상환경 설정 (Windows)
```powershell
cd v2/python_worker
python -m venv .venv
.\.venv\Scripts\pip install --upgrade pip
.\.venv\Scripts\pip install -r requirements.txt
```

## 3. 테스트 실행
```powershell
.\.venv\Scripts\python.exe -m unittest discover -s tests
```
