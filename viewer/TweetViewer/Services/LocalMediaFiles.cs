using System.IO;

namespace TweetViewer.Services;

/// <summary>images/&lt;tweet_id&gt;_&lt;idx&gt;.&lt;ext&gt; のローカルパス解決 (拡張子不一致に耐える)。</summary>
public static class LocalMediaFiles
{
    private static readonly string[] ProbeExtensions = { "jpg", "png", "webp", "gif", "jpeg" };

    public static string? Resolve(string imagesDir, string tweetId, int index, string ext)
    {
        var expected = Path.Combine(imagesDir, $"{tweetId}_{index}.{ext}");
        if (File.Exists(expected))
            return expected;
        return ProbeExtensions
            .Select(e => Path.Combine(imagesDir, $"{tweetId}_{index}.{e}"))
            .FirstOrDefault(File.Exists);
    }
}
