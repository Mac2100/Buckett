// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "Buckett",
    platforms: [
        .macOS(.v14)
    ],
    targets: [
        .executableTarget(
            name: "Buckett",
            path: "Sources/Buckett"
        )
    ]
)
