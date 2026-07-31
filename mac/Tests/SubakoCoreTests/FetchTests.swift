import Foundation
import Testing
@testable import SubakoCore

/// 完了ログ「APIリクエスト=N回」の解析 (C# FetchBudgetTests.cs 相当)。
@Suite struct FetchBudgetTests {
    @Test func 完了ログから消費数を読む() {
        let lines = [
            "INFO: ページ 1 を取得",
            "完了: 新規保存=12件 / 総保存=100件 / APIリクエスト=5回 / 保存先=data/alice",
        ]
        #expect(FetchBudget.parseConsumedRequests(lines) == 5)
    }

    @Test func 複数行あれば最後の値を採用() {
        let lines = [
            "完了: 新規保存=1件 / 総保存=1件 / APIリクエスト=3回 / 保存先=x",
            "完了: 新規保存=2件 / 総保存=2件 / APIリクエスト=7回 / 保存先=x",
        ]
        #expect(FetchBudget.parseConsumedRequests(lines) == 7)
    }

    @Test func 見つからなければnilで割当全消費とみなす() {
        #expect(FetchBudget.parseConsumedRequests(["ログなし"]) == nil)
        #expect(FetchBudget.consumedOrGranted(parsedConsumed: nil, granted: 50) == 50)
        #expect(FetchBudget.consumedOrGranted(parsedConsumed: 3, granted: 50) == 3)
        // 実消費が割当を上回ることもある (リトライは 1 回ずつカウント) — 丸めない
        #expect(FetchBudget.consumedOrGranted(parsedConsumed: 60, granted: 50) == 60)
    }
}

/// exit code 契約 (docs/fetcher-cli.md §3) のメッセージ分岐 (C# FetchOutcomeTests.cs 相当)。
@Suite struct FetchOutcomeTests {
    @Test func 正常終了() {
        let (message, hasIssues) = FetchOutcome.describeSingle(
            FetchResult(exitCode: 0, cancelled: false), mode: .update)
        #expect(message == "取得完了")
        #expect(!hasIssues)
    }

    @Test func ユーザー中断はexitCodeに関係なく中断扱い() {
        let (message, hasIssues) = FetchOutcome.describeSingle(
            FetchResult(exitCode: 0, cancelled: true), mode: .update)
        #expect(message.contains("中断"))
        #expect(hasIssues)
    }

    @Test func 上限到達はモード別の案内() {
        let search = FetchOutcome.describeSingle(
            FetchResult(exitCode: 10, cancelled: false), mode: .searchUpdate)
        #expect(search.message.contains("続きから再開"))
        #expect(search.hasIssues)

        // フォロー一覧のみ再開不可 (docs/data-layer.md §1.7)
        let followings = FetchOutcome.describeSingle(
            FetchResult(exitCode: 10, cancelled: false), mode: .followings)
        #expect(followings.message.contains("上限を増やして"))
        #expect(!followings.message.contains("続きから再開"))

        let backfill = FetchOutcome.describeSingle(
            FetchResult(exitCode: 10, cancelled: false), mode: .backfill)
        #expect(backfill.message.contains("バックフィル"))
    }

    @Test func エラー終了() {
        let (message, hasIssues) = FetchOutcome.describeSingle(
            FetchResult(exitCode: 1, cancelled: false), mode: .update)
        #expect(message.contains("exit code 1"))
        #expect(hasIssues)
    }

    @Test func 環境不備ヒント() {
        #expect(FetchOutcome.environmentHint(
            ["Traceback...", "ModuleNotFoundError: No module named 'requests'"])!
            .contains("pip install"))
        #expect(FetchOutcome.environmentHint(
            ["RuntimeError: SORSA_API_KEY が未設定"])!
            .contains(".env"))
        #expect(FetchOutcome.environmentHint(["正常ログ"]) == nil)
    }

    @Test func バッチサマリ() {
        let ok = FetchOutcome.describeBatch(
            succeeded: 3, total: 3, consumedTotal: 12, failed: [], stopReason: nil)
        #expect(ok.summary == "完了 3/3 件 / 消費 12 リクエスト")
        #expect(!ok.hasIssues)

        let stopped = FetchOutcome.describeBatch(
            succeeded: 2, total: 5, consumedTotal: 100, failed: ["bob"],
            stopReason: "合計リクエスト上限に達しました")
        #expect(stopped.summary.contains("失敗 1 件"))
        #expect(stopped.summary.contains("合計リクエスト上限"))
        #expect(stopped.hasIssues)
    }
}

/// 引数生成の契約 (docs/fetcher-cli.md §2 の表)。
@Suite struct FetchArgumentsTests {
    @Test func 各モードの引数() {
        #expect(FetchArguments.build(
            username: "alice", mode: .update, dataDir: "/data", maxRequests: nil)
            == ["main.py", "alice", "--output-dir", "/data", "--update"])
        #expect(FetchArguments.build(
            username: "alice", mode: .backfill, dataDir: "/data", maxRequests: 500)
            == ["main.py", "alice", "--output-dir", "/data", "--backfill", "--max-requests", "500"])
        #expect(FetchArguments.build(
            username: "searches/cat-12345678", mode: .search, dataDir: "/data",
            maxRequests: 50, searchQuery: "猫")
            == ["main.py", "--output-dir", "/data", "--search", "猫",
                "--search-name", "cat-12345678", "--max-requests", "50"])
        #expect(FetchArguments.build(
            username: "searches/cat-12345678", mode: .searchUpdate, dataDir: "/data",
            maxRequests: 500, searchQuery: "猫")
            == ["main.py", "--output-dir", "/data", "--search", "猫",
                "--search-name", "cat-12345678", "--update", "--max-requests", "500"])
        #expect(FetchArguments.build(
            username: "searches/cat-12345678", mode: .searchBackfill, dataDir: "/data",
            maxRequests: 500, searchQuery: "猫", backfillSince: "2014-01-01")
            == ["main.py", "--output-dir", "/data", "--search", "猫",
                "--search-name", "cat-12345678", "--backfill",
                "--backfill-since", "2014-01-01", "--max-requests", "500"])
        #expect(FetchArguments.build(
            username: "alice", mode: .imagesOnly, dataDir: "/data", maxRequests: nil)
            == ["main.py", "alice", "--output-dir", "/data", "--images-only"])
        // 検索バケットの画像のみ取得は slug だけ渡す
        #expect(FetchArguments.build(
            username: "searches/cat-12345678", mode: .imagesOnly, dataDir: "/data", maxRequests: nil)
            == ["main.py", "--search-name", "cat-12345678", "--output-dir", "/data", "--images-only"])
        #expect(FetchArguments.build(
            username: "alice", mode: .followings, dataDir: "/data", maxRequests: 50)
            == ["main.py", "alice", "--output-dir", "/data", "--followings", "--max-requests", "50"])
    }
}

/// _trash/ への退避 (docs/data-layer.md §1.6)。
@Suite struct ArchiveTrashTests {
    @Test func 退避と同名衝突() throws {
        let dataDir = try makeTempDir()
        defer { try? FileManager.default.removeItem(atPath: dataDir) }
        let userDir = dataDir + "/alice"
        try FileManager.default.createDirectory(atPath: userDir, withIntermediateDirectories: true)
        FileManager.default.createFile(atPath: userDir + "/tweets.jsonl", contents: Data())

        let dest = try ArchiveTrash.moveToTrash(dataDir: dataDir, username: "alice")
        #expect(dest == dataDir + "/_trash/alice")
        #expect(!FileManager.default.fileExists(atPath: userDir))
        #expect(FileManager.default.fileExists(atPath: dest! + "/tweets.jsonl"))

        // 同名が既にあるときは日時サフィックス
        try FileManager.default.createDirectory(atPath: userDir, withIntermediateDirectories: true)
        let dest2 = try ArchiveTrash.moveToTrash(dataDir: dataDir, username: "alice")
        #expect(dest2 != dest)
        #expect(dest2!.hasPrefix(dataDir + "/_trash/alice_"))

        // 元フォルダが無ければ nil
        #expect(try ArchiveTrash.moveToTrash(dataDir: dataDir, username: "ghost") == nil)
    }

    @Test func 検索バケットは階層を保って退避() throws {
        let dataDir = try makeTempDir()
        defer { try? FileManager.default.removeItem(atPath: dataDir) }
        let bucketDir = dataDir + "/searches/kw-12345678"
        try FileManager.default.createDirectory(atPath: bucketDir, withIntermediateDirectories: true)

        let dest = try ArchiveTrash.moveToTrash(dataDir: dataDir, username: "searches/kw-12345678")
        #expect(dest == dataDir + "/_trash/searches/kw-12345678")
    }
}
