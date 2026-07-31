// swift-tools-version: 6.2
import PackageDescription

// 全ターゲット共通の厳格化設定:
// Swift 7 で必須になる予定の upcoming feature を先行 opt-in する。
let strictSwiftSettings: [SwiftSetting] = [
    .enableUpcomingFeature("ExistentialAny"),          // SE-0335: 存在型に any を必須化
]

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
        // macOS 標準の SQLite は「開いているファイルへのハードリンク作成」を API 違反として
        // 接続を無効化するガードを持ち、Google Drive の同期 (アップロード用ハードリンク) と
        // 衝突する。Windows 版 (SQLitePCLRaw) と同様に自前の SQLite を同梱して回避する。
        .package(url: "https://github.com/swiftlang/swift-toolchain-sqlite.git", from: "1.0.0"),
    ],
    targets: [
        .target(
            name: "SubakoCore",
            dependencies: [
                .product(name: "SwiftToolchainCSQLite", package: "swift-toolchain-sqlite"),
            ],
            swiftSettings: strictSwiftSettings
        ),
        .executableTarget(
            name: "SubakoApp",
            dependencies: ["SubakoCore"],
            swiftSettings: strictSwiftSettings
        ),
        // 実データフォルダとの疎通確認 CLI (docs/mac-port-notes.md §6.3)
        .executableTarget(
            name: "subako-smoke",
            dependencies: ["SubakoCore"],
            swiftSettings: strictSwiftSettings
        ),
        // アプリケーションアイコンの生成 (tools/icongen の移植。Scripts/make-icon.sh から使う)
        .executableTarget(name: "subako-icongen", swiftSettings: strictSwiftSettings),
        .testTarget(
            name: "SubakoCoreTests",
            dependencies: ["SubakoCore"],
            swiftSettings: strictSwiftSettings
        ),
    ]
)
