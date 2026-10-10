namespace EasySearch.Tests;

public sealed class VideoByteRangeTests
{
    [Theory]
    [InlineData(null, 8000000000, 0, VideoByteRange.MaxBytes)]
    [InlineData("bytes=7000000000-", 8000000000, 7000000000, VideoByteRange.MaxBytes)]
    [InlineData("bytes=10-19", 100, 10, 10)]
    [InlineData("bytes=90-999", 100, 90, 10)]
    [InlineData("bytes=-10", 100, 90, 10)]
    [InlineData("bytes=-200", 100, 0, 100)]
    [InlineData("bytes=99-", 100, 99, 1)]
    public void ReadsOnlyTheRequestedBoundedSlice(string? header, long length, long start, int count)
    {
        var range = VideoByteRange.Parse(header, length);
        Assert.NotNull(range);
        Assert.Equal(start, range.Value.Start);
        Assert.Equal(count, range.Value.Count);
        Assert.Equal(start + count - 1, range.Value.End);
    }

    [Theory]
    [InlineData("bytes=100-", 100)]
    [InlineData("bytes=20-10", 100)]
    [InlineData("bytes=-0", 100)]
    [InlineData("bytes=0-1,10-20", 100)]
    [InlineData("bytes=bogus-", 100)]
    [InlineData("items=0-10", 100)]
    [InlineData("bytes=9223372036854775808-", 100)]
    [InlineData(null, 0)]
    public void RejectsInvalidOrUnsatisfiableRequests(string? header, long length) =>
        Assert.Null(VideoByteRange.Parse(header, length));
}
