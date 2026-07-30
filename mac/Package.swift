// swift-tools-version: 6.0
import PackageDescription

// SubakoCore は UI 非依存の共有契約層 (パーサ・DB・取込・fetcher 連携)。
// アプリ本体 (SubakoApp) も同一パッケージの executable ターゲットとして持ち、
// .app バンドルは Scripts/make-app.sh で組み立てる (Xcode プロジェクト不要)。
let package = Package(
    name: "Subako",
    defaultLocalization: "ja",
    platforms: [.macOS(.v15)],
    products: [
        .library(name: "SubakoCore", targets: ["SubakoCore"]),
        .executable(name: "SubakoApp", targets: ["SubakoApp"]),
        .executable(name: "subako-smoke", targets: ["subako-smoke"]),
    ],
    dependencies: [
        .package(url: "https://github.com/groue/GRDB.swift.git", from: "7.0.0"),
    ],
    targets: [
        .target(
            name: "SubakoCore",
            dependencies: [.product(name: "GRDB", package: "GRDB.swift")]
        ),
        .executableTarget(
            name: "SubakoApp",
            dependencies: ["SubakoCore"]
        ),
        // 実データフォルダとの疎通確認 CLI (docs/mac-port-notes.md §6.3)
        .executableTarget(
            name: "subako-smoke",
            dependencies: ["SubakoCore"]
        ),
        .testTarget(
            name: "SubakoCoreTests",
            dependencies: ["SubakoCore"]
        ),
    ]
)
