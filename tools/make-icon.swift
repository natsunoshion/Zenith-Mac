#!/usr/bin/swift
// Repackage the original Windows artwork; no redrawing or generated artwork.
// Run from the repository root: swift tools/make-icon.swift
import Foundation
import CoreGraphics
import ImageIO
import UniformTypeIdentifiers

let root = URL(fileURLWithPath: FileManager.default.currentDirectoryPath)
let sourceIco = root.appendingPathComponent("upstream/Black-Midi-Render/icon.ico")
let sourcePng = root.appendingPathComponent("upstream/Black-Midi-Render/icon.png")
let resourceFolder = root.appendingPathComponent("src/Zenith.Mac/Assets")
let iconset = root.appendingPathComponent("artifacts/icon-review/Zenith.iconset")
let outputIcns = resourceFolder.appendingPathComponent("Zenith.icns")

func require<T>(_ value: T?, _ message: String) throws -> T {
    guard let value else { throw NSError(domain: "ZenithIcon", code: 1,
        userInfo: [NSLocalizedDescriptionKey: message]) }
    return value
}

func writePng(_ image: CGImage, to url: URL) throws {
    let destination = try require(CGImageDestinationCreateWithURL(url as CFURL,
        UTType.png.identifier as CFString, 1, nil), "Cannot create PNG: \(url.path)")
    CGImageDestinationAddImage(destination, image, nil)
    guard CGImageDestinationFinalize(destination) else {
        throw NSError(domain: "ZenithIcon", code: 2,
            userInfo: [NSLocalizedDescriptionKey: "Cannot write PNG: \(url.path)"])
    }
}

func centeredOriginal(_ image: CGImage, size: Int) throws -> CGImage {
    let space = CGColorSpace(name: CGColorSpace.sRGB)!
    let bitmap = try require(CGContext(data: nil, width: size, height: size,
        bitsPerComponent: 8, bytesPerRow: size * 4, space: space,
        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue), "Cannot create icon canvas")
    bitmap.clear(CGRect(x: 0, y: 0, width: size, height: size))
    bitmap.interpolationQuality = .high
    let scale = Double(size) / Double(max(image.width, image.height))
    let width = Double(image.width) * scale
    let height = Double(image.height) * scale
    bitmap.draw(image, in: CGRect(x: (Double(size) - width) / 2,
        y: (Double(size) - height) / 2, width: width, height: height))
    return try require(bitmap.makeImage(), "Cannot finish icon canvas")
}

do {
    try FileManager.default.createDirectory(at: resourceFolder, withIntermediateDirectories: true)
    try FileManager.default.createDirectory(at: iconset, withIntermediateDirectories: true)
    let ico = try require(CGImageSourceCreateWithURL(sourceIco as CFURL, nil), "Cannot read upstream ICO")
    var originals: [Int: CGImage] = [:]
    for index in 0..<CGImageSourceGetCount(ico) {
        let image = try require(CGImageSourceCreateImageAtIndex(ico, index, nil), "Cannot decode ICO entry")
        guard image.width == image.height else { continue }
        originals[image.width] = image
    }
    let png = try require(CGImageSourceCreateWithURL(sourcePng as CFURL, nil), "Cannot read upstream PNG")
    let artwork = try require(CGImageSourceCreateImageAtIndex(png, 0, nil), "Cannot decode upstream PNG")
    print("Original ICO sizes: \(originals.keys.sorted()); original PNG: \(artwork.width)×\(artwork.height)")
    let entries = [(16, "16x16", "icp4"), (32, "16x16@2x", "ic11"),
        (32, "32x32", "icp5"), (64, "32x32@2x", "ic12"),
        (128, "128x128", "ic07"), (256, "128x128@2x", "ic13"),
        (256, "256x256", "ic08"), (512, "256x256@2x", "ic14"),
        (512, "512x512", "ic09"), (1024, "512x512@2x", "ic10")]
    // PNG-backed ICNS entries preserve the original straight-alpha samples.
    // iconutil's legacy 16/32px RGB+mask encoding alters their translucent RGB.
    func chunk(_ type: String, _ payload: Data) -> Data {
        var data = Data(type.utf8)
        var length = UInt32(payload.count + 8).bigEndian
        withUnsafeBytes(of: &length) { data.append(contentsOf: $0) }
        data.append(payload)
        return data
    }
    var contents = Data()
    for (size, name, type) in entries {
        let image: CGImage
        if size <= 256 {
            image = try require(originals[size], "Missing original \(size)px ICO entry")
        } else {
            image = try centeredOriginal(artwork, size: size)
        }
        let outputPng = iconset.appendingPathComponent("icon_\(name).png")
        try writePng(image, to: outputPng)
        contents.append(chunk(type, try Data(contentsOf: outputPng)))
        if size == 1024 { try writePng(image, to: resourceFolder.appendingPathComponent("Zenith.png")) }
    }
    try chunk("icns", contents).write(to: outputIcns, options: .atomic)
    print("Created \(outputIcns.path)")
} catch {
    fputs("\(error.localizedDescription)\n", stderr)
    exit(1)
}
