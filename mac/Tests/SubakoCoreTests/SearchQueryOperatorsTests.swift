import Testing
@testable import SubakoCore

/// min_retweets: / min_faves: の合成・分解の検証 (C# SearchQueryOperatorsTests.cs の移植)。
@Suite struct SearchQueryOperatorsTests {
    @Test func compose_下限なしはそのまま() {
        #expect(SearchQueryOperators.compose(
            baseQuery: "sts2 OR スレスパ2", minRetweets: nil, minFaves: nil) == "sts2 OR スレスパ2")
    }

    @Test func compose_下限ありは括弧で包んで演算子を付与() {
        #expect(SearchQueryOperators.compose(baseQuery: "q", minRetweets: 5, minFaves: nil)
            == "(q) min_retweets:5")
        #expect(SearchQueryOperators.compose(baseQuery: "q", minRetweets: nil, minFaves: 10)
            == "(q) min_faves:10")
        #expect(SearchQueryOperators.compose(baseQuery: "q", minRetweets: 5, minFaves: 10)
            == "(q) min_retweets:5 min_faves:10")
    }

    @Test(arguments: [
        (#"(sts2 OR "slay the spire 2") min_faves:10"#, #"sts2 OR "slay the spire 2""#,
         Int64?.none, Int64?.some(10)),
        ("(a OR b) min_retweets:3 min_faves:7", "a OR b", Int64?.some(3), Int64?.some(7)),
        ("plain query", "plain query", Int64?.none, Int64?.none),
    ])
    func split_基本形(full: String, expectedBase: String, expectedRt: Int64?, expectedFav: Int64?) {
        let (baseQuery, minRt, minFav) = SearchQueryOperators.split(full)
        #expect(baseQuery == expectedBase)
        #expect(minRt == expectedRt)
        #expect(minFav == expectedFav)
    }

    @Test func splitとComposeで往復できる() {
        let original = SearchQueryOperators.compose(
            baseQuery: #"sts2 OR "slay the spire 2" OR スレスパ2"#, minRetweets: nil, minFaves: 10)
        let (baseQuery, minRt, minFav) = SearchQueryOperators.split(original)
        #expect(SearchQueryOperators.compose(
            baseQuery: baseQuery, minRetweets: minRt, minFaves: minFav) == original)
    }

    @Test func split_並列括弧は外さない() {
        let (baseQuery, _, minFav) = SearchQueryOperators.split("((a OR b) (c OR d)) min_faves:1")
        #expect(baseQuery == "(a OR b) (c OR d)")
        #expect(minFav == 1)
    }

    @Test func split_演算子なしの括弧クエリは括弧を維持() {
        // 意図して書かれた括弧を外すと保存時に文字列が変わり不要な状態リセットになる
        let (baseQuery, minRt, minFav) = SearchQueryOperators.split("(a OR b)")
        #expect(baseQuery == "(a OR b)")
        #expect(minRt == nil)
        #expect(minFav == nil)
    }

    @Test func split_手書きの中間演算子も拾う() {
        let (baseQuery, _, minFav) = SearchQueryOperators.split("foo min_faves:10 bar")
        #expect(baseQuery == "foo bar")
        #expect(minFav == 10)
    }

    @Test func split_重複トークンは最後の値を採用() {
        let (_, _, minFav) = SearchQueryOperators.split("q min_faves:5 min_faves:20")
        #expect(minFav == 20)
    }
}
