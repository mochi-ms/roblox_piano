namespace RobloxPiano.Core.YouTube;

public static class YouTubeError
{
    public const string InvalidUrl = "유효한 YouTube 영상 링크가 아닙니다.";
    public const string UnsupportedHost = "지원되지 않는 웹사이트 링크입니다. YouTube 영상 링크만 지원합니다.";
    public const string PlaylistUnsupported = "플레이리스트 가져오기는 현재 지원하지 않습니다.";
    public const string YtDlpNotFound = "yt-dlp 실행 파일을 찾을 수 없습니다.";
    public const string YtDlpUnavailable = "yt-dlp 도구를 사용할 수 없습니다.";
    public const string FfmpegUnavailable = "FFmpeg 도구를 사용할 수 없습니다.";
    public const string MetadataFailed = "영상 정보를 불러올 수 없습니다.";
    public const string MetadataTimeout = "영상 정보 확인 시간이 초과되었습니다.";
    public const string VideoIdMismatch = "요청한 영상 ID와 메타데이터가 일치하지 않습니다.";
    public const string LiveUnsupported = "라이브 방송은 현재 지원하지 않습니다.";
    public const string DurationUnknown = "영상 길이를 확인할 수 없습니다.";
    public const string TooLong = "영상 길이가 너무 깁니다. 최대 30분까지 지원합니다.";
    public const string FileTooLarge = "오디오 파일 크기가 허용치(500MB)를 초과했습니다.";
    public const string LoginRequired = "로그인 또는 추가 권한이 필요한 영상은 현재 지원하지 않습니다.";
    public const string DownloadFailed = "YouTube 오디오 다운로드에 실패했습니다.";
    public const string DownloadOutputMissing = "다운로드된 오디오 파일을 찾을 수 없습니다.";
    public const string AudioPreparationFailed = "오디오 정규화 처리에 실패했습니다.";
    public const string Cancelled = "YouTube 오디오 가져오기가 취소되었습니다.";
}
