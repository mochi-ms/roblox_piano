using RobloxPiano.Core.Importing;
using Xunit;

namespace RobloxPiano.Core.Tests;

public class SmartMmlPreprocessorTests
{
    [Fact]
    public void Process_StandardMml_ReturnsExactMml()
    {
        string input = "MML@T120L4CDEF,L4GAB>C,L4C<BAG;";
        var result = SmartMmlPreprocessor.Process(input);

        Assert.True(result.Success);
        Assert.Equal("MML@T120L4CDEF,L4GAB>C,L4C<BAG;", result.ProcessedMml);
        Assert.Equal(3, result.TrackCount);
    }

    [Fact]
    public void Process_KoreanSectionHeaders_ConvertsToMultiTrackMml()
    {
        string input = @"
곡명: 나의 아름다운 노래
멜로디
T150L4CDEF
화음1
L4GAB>C
화음2
L4C<BAG
";
        var result = SmartMmlPreprocessor.Process(input);

        Assert.True(result.Success);
        Assert.Equal("나의 아름다운 노래", result.ExtractedTitle);
        Assert.Equal("MML@T150L4CDEF,L4GAB>C,L4C<BAG;", result.ProcessedMml);
        Assert.Equal(3, result.TrackCount);
    }

    [Fact]
    public void Process_JapaneseAndEnglishSectionHeaders_ConvertsCorrectly()
    {
        string input = @"
Title: Anime Theme
[Melody]
T130L8CDEFCDEF
[Chord 1]
L8GAB>CGAB>C
[Accompaniment]
L8C<BAGC<BAG
";
        var result = SmartMmlPreprocessor.Process(input);

        Assert.True(result.Success);
        Assert.Equal("Anime Theme", result.ExtractedTitle);
        Assert.Equal("MML@T130L8CDEFCDEF,L8GAB>CGAB>C,L8C<BAGC<BAG;", result.ProcessedMml);
        Assert.Equal(3, result.TrackCount);
    }

    [Fact]
    public void Process_MarkdownCodeFences_StripsFencesCleanly()
    {
        string input = @"```mml
MML@T120L4CDEF,L4GAB>C;
```";
        var result = SmartMmlPreprocessor.Process(input);

        Assert.True(result.Success);
        Assert.Equal("MML@T120L4CDEF,L4GAB>C;", result.ProcessedMml);
        Assert.Equal(2, result.TrackCount);
    }

    [Fact]
    public void Process_InlineSectionLabels_ExtractsProperly()
    {
        string input = @"
멜로디: T120L4CDEF
화음1: L4GAB>C
화음2: L4C<BAG
";
        var result = SmartMmlPreprocessor.Process(input);

        Assert.True(result.Success);
        Assert.Equal("MML@T120L4CDEF,L4GAB>C,L4C<BAG;", result.ProcessedMml);
        Assert.Equal(3, result.TrackCount);
    }
}
