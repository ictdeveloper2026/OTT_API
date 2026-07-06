using OTT.Application.Services;
using Xunit;

namespace OTT.Tests;

// Covers VideoService.ExtractYouTubeId / ExtractVimeoId — accept anything an admin pastes.
public class VideoIdExtractionTests
{
    [Theory]
    [InlineData("b68HETiNO98", "b68HETiNO98")]                                                        // bare id
    [InlineData("https://www.youtube.com/watch?v=b68HETiNO98", "b68HETiNO98")]                        // watch url
    [InlineData("https://www.youtube.com/watch?v=b68HETiNO98&list=RDb68HETiNO98&start_radio=1", "b68HETiNO98")] // watch + params
    [InlineData("https://youtu.be/b68HETiNO98", "b68HETiNO98")]                                       // short url
    [InlineData("https://www.youtube.com/embed/b68HETiNO98?list=RDb68HETiNO98", "b68HETiNO98")]       // embed url
    [InlineData("https://www.youtube.com/shorts/b68HETiNO98", "b68HETiNO98")]                         // shorts url
    [InlineData("<iframe width=\"1128\" src=\"https://www.youtube.com/embed/b68HETiNO98?list=RD\"></iframe>", "b68HETiNO98")] // iframe embed
    public void ExtractYouTubeId_AcceptsAnyForm(string input, string expected)
        => Assert.Equal(expected, VideoService.ExtractYouTubeId(input));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a youtube link")]
    public void ExtractYouTubeId_ReturnsNullForGarbage(string input)
        => Assert.Null(VideoService.ExtractYouTubeId(input));

    [Theory]
    [InlineData("123456789", "123456789")]
    [InlineData("https://vimeo.com/123456789", "123456789")]
    [InlineData("https://player.vimeo.com/video/123456789?h=abc", "123456789")]
    public void ExtractVimeoId_AcceptsUrlOrId(string input, string expected)
        => Assert.Equal(expected, VideoService.ExtractVimeoId(input));
}
