// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "Deskify",
    platforms: [
        .macOS(.v13) // Ventura+ — covers Apple Silicon and every Intel Mac still receiving macOS updates.
    ],
    targets: [
        // Platform-agnostic business logic: models, persistence, launch/close
        // orchestration. Talks to the OS only through the protocols in
        // Platform.swift, so the Windows (WPF) and macOS apps can share the
        // same behavior while their platform layers evolve independently.
        .target(
            name: "DeskifyCore",
            path: "Sources/DeskifyCore"
        ),
        // The macOS app: SwiftUI interface + native platform services
        // (Accessibility window control, NSWorkspace launching, NSScreen
        // monitor mapping, Carbon global hotkey).
        .executableTarget(
            name: "Deskify",
            dependencies: ["DeskifyCore"],
            path: "Sources/Deskify"
        ),
    ]
)
