namespace RobloxPiano.Core.Importing;

public static class ImportError
{
    public const string UnsupportedFormat = "지원하지 않는 파일 형식입니다.";
    public const string CorruptMidi = "MIDI 파일이 손상되었거나 읽을 수 없습니다.";
    public const string InvalidMml = "MML 구문을 해석할 수 없습니다.";
    public const string FileTooLarge = "파일이 너무 큽니다.";
    public const string FileNotFound = "파일을 찾을 수 없습니다.";
    public const string AlreadyImported = "이미 가져온 파일입니다.";
    public const string NoPlayableNotes = "연주할 노트가 없습니다.";
    public const string Cancelled = "가져오기가 취소되었습니다.";
    public const string EmptyFile = "빈 파일은 악보로 등록할 수 없습니다.";
    public const string CorruptTiming = "악보 타이밍 데이터가 손상되었습니다.";
}
