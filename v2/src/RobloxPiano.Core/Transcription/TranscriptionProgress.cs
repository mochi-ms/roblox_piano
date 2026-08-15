namespace RobloxPiano.Core.Transcription;

public enum TranscriptionPhase
{
    WorkerStarting,
    ModelLoading,
    ModelReady,
    Transcribing,
    WritingMidi,
    ValidatingResult,
    Completed,
    Failed,
    Cancelled
}

public record TranscriptionProgress(
    TranscriptionPhase Phase,
    string Message,
    bool IsIndeterminate = true,
    double? ProgressFraction = null
)
{
    public static TranscriptionProgress Starting(string msg = "AI 엔진 시작 중...") =>
        new(TranscriptionPhase.WorkerStarting, msg, true);

    public static TranscriptionProgress LoadingModel(string msg = "Basic Pitch AI 모델 로딩 중...") =>
        new(TranscriptionPhase.ModelLoading, msg, true);

    public static TranscriptionProgress ModelReady(string msg = "AI 모델 준비 완료") =>
        new(TranscriptionPhase.ModelReady, msg, true);

    public static TranscriptionProgress Transcribing(string msg = "오디오 피치 및 타이밍 분석 중...") =>
        new(TranscriptionPhase.Transcribing, msg, true);

    public static TranscriptionProgress WritingMidi(string msg = "MIDI 파일 생성 중...") =>
        new(TranscriptionPhase.WritingMidi, msg, true);

    public static TranscriptionProgress Validating(string msg = "변환된 악보 검증 중...") =>
        new(TranscriptionPhase.ValidatingResult, msg, true);

    public static TranscriptionProgress Completed(string msg = "AI 변환 완료") =>
        new(TranscriptionPhase.Completed, msg, false, 1.0);
}
