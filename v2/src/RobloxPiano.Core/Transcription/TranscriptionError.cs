namespace RobloxPiano.Core.Transcription;

public static class TranscriptionError
{
    public const string EngineUnavailable = "AI 변환 엔진을 사용할 수 없습니다.";
    public const string PythonNotFound = "Python 3.11 환경을 찾을 수 없습니다.";
    public const string UnsupportedPythonVersion = "지원되지 않는 Python 버전입니다. CPython 3.11이 필요합니다.";
    public const string BasicPitchMissing = "Basic Pitch 0.4.0 패키지가 설치되어 있지 않습니다.";
    public const string WorkerStartupFailed = "AI 워커 프로세스를 시작하지 못했습니다.";
    public const string ProtocolError = "AI 워커와의 통신 중 프로토콜 오류가 발생했습니다.";
    public const string WorkerCrashed = "AI 워커 프로세스가 비정상 종료되었습니다.";
    public const string InferenceFailed = "AI 음악 분석 및 음표 추출에 실패했습니다.";
    public const string MidiWriteFailed = "변환된 MIDI 파일 저장에 실패했습니다.";
    public const string InvalidGeneratedMidi = "생성된 MIDI 파일이 올바르지 않거나 손상되었습니다.";
    public const string NoNotes = "오디오에서 감지된 음표(Note)가 없습니다.";
    public const string Cancelled = "AI 악보 변환 작업이 취소되었습니다.";
    public const string Timeout = "AI 악보 변환 작업 시간이 초과되었습니다.";
    public const string InvalidInputAudio = "유효하지 않은 입력 오디오 파일입니다.";
}
