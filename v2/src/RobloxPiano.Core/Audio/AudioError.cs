namespace RobloxPiano.Core.Audio;

public static class AudioError
{
    public const string FileNotFound = "오디오 파일을 찾을 수 없습니다.";
    public const string UnsupportedExtension = "지원하지 않는 오디오 확장자입니다. (MP3, WAV, M4A, FLAC, AAC, OGG 지원)";
    public const string FileTooLarge = "파일 크기가 너무 큽니다. (최대 500MB까지 지원)";
    public const string FfmpegNotFound = "FFmpeg를 찾을 수 없습니다. 설정에서 FFmpeg 경로를 지정해주세요.";
    public const string FfprobeNotFound = "FFprobe를 찾을 수 없습니다. 설정에서 FFmpeg/FFprobe 경로를 지정해주세요.";
    public const string InvalidMedia = "손상되었거나 올바르지 않은 미디어 파일입니다.";
    public const string NoAudioStream = "오디오 스트림을 찾을 수 없습니다.";
    public const string TooLong = "오디오가 너무 깁니다. 최대 30분까지 지원합니다.";
    public const string ProbeFailed = "오디오 메타데이터 분석에 실패했습니다.";
    public const string ConversionFailed = "오디오 변환에 실패했습니다.";
    public const string OutputValidationFailed = "변환된 오디오 파일 검증에 실패했습니다.";
    public const string Cancelled = "사용자에 의해 취소되었습니다.";
}
