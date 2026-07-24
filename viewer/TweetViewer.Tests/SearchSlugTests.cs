using TweetViewer.Data;

namespace TweetViewer.Tests;

/// <summary>
/// SearchSlug.From は Python 側 (sorsa_fetcher/fetcher.py の slugify_query) と
/// 同一規則。期待値は Python 実装の実出力 (両実装の同期規約)。
/// </summary>
public class SearchSlugTests
{
    [Theory]
    // python: slugify_query('(sts2 OR "slay the spire 2" OR スレスパ2) lang:ja')
    [InlineData("""(sts2 OR "slay the spire 2" OR スレスパ2) lang:ja""",
        "(sts2_OR_slay_the_spire_2_OR_スレスパ2)_lang-3963e035")]
    // python: slugify_query('a/b\\c:d*e?f"g<h>i|j k')
    [InlineData("""a/b\c:d*e?f"g<h>i|j k""", "a_b_c_d_e_f_g_h_i_j_k-91149389")]
    // python: slugify_query('   ') — 空 slug は "search" にフォールバック
    [InlineData("   ", "search-088fb1a4")]
    public void MatchesPythonImplementation(string query, string expected) =>
        Assert.Equal(expected, SearchSlug.From(query));

    [Fact]
    public void LongQueryIsTruncatedTo40CharsBeforeHash()
    {
        var slug = SearchSlug.From(new string('x', 100));
        Assert.Equal(40 + 1 + 8, slug.Length);
        Assert.StartsWith(new string('x', 40) + "-", slug);
    }
}
