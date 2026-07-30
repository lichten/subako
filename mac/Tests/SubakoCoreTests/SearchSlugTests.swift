import Testing
@testable import SubakoCore

/// SearchSlug.from は Python 側 (sorsa_fetcher/fetcher.py の slugify_query) と
/// 同一規則。期待値は Python 実装の実出力 (両実装の同期規約 — SearchSlugTests.cs から転記)。
@Suite struct SearchSlugTests {
    @Test(arguments: [
        // python: slugify_query('(sts2 OR "slay the spire 2" OR スレスパ2) lang:ja')
        (#"(sts2 OR "slay the spire 2" OR スレスパ2) lang:ja"#,
         "(sts2_OR_slay_the_spire_2_OR_スレスパ2)_lang-3963e035"),
        // python: slugify_query('a/b\\c:d*e?f"g<h>i|j k')
        (#"a/b\c:d*e?f"g<h>i|j k"#, "a_b_c_d_e_f_g_h_i_j_k-91149389"),
        // python: slugify_query('   ') — 空 slug は "search" にフォールバック
        ("   ", "search-088fb1a4"),
    ])
    func matchesPythonImplementation(query: String, expected: String) {
        #expect(SearchSlug.from(query) == expected)
    }

    @Test func longQueryIsTruncatedTo40CharsBeforeHash() {
        let slug = SearchSlug.from(String(repeating: "x", count: 100))
        #expect(slug.count == 40 + 1 + 8)
        #expect(slug.hasPrefix(String(repeating: "x", count: 40) + "-"))
    }
}
