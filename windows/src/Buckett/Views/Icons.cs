// Buckett's icon set. Every glyph is authored as vector path data on a 24x24
// grid, so the app has no dependency on an icon font being present on the
// machine. Names mirror the SF Symbol names used by the macOS build, which
// keeps the two code bases readable side by side.

using System.Collections.Generic;

namespace Buckett.Views;

/// One icon: an optional stroked outline plus an optional filled shape.
/// Filled paths use the even-odd rule so inner detail is knocked out and the
/// icon reads correctly on any background.
public readonly record struct IconData(string? Stroke, string? Fill);

public static class Icons
{
    public const double DesignSize = 24.0;
    public const double StrokeWidth = 1.7;

    public static readonly IReadOnlyDictionary<string, IconData> All =
        new Dictionary<string, IconData>
        {
            ["chevron.up"] = new IconData("M6 15 L12 9 L18 15", null),
            ["chevron.down"] = new IconData("M6 9 L12 15 L18 9", null),
            ["chevron.right"] = new IconData("M9.5 5.5 L16 12 L9.5 18.5", null),
            ["chevron.left"] = new IconData("M14.5 5.5 L8 12 L14.5 18.5", null),
            ["chevron.up.chevron.down"] = new IconData("M8 10 L12 6 L16 10 M8 14 L12 18 L16 14", null),
            ["plus"] = new IconData("M12 4.8 V19.2 M4.8 12 H19.2", null),
            ["minus"] = new IconData("M4.8 12 H19.2", null),
            ["xmark"] = new IconData("M6.2 6.2 L17.8 17.8 M17.8 6.2 L6.2 17.8", null),
            ["checkmark"] = new IconData("M5.2 12.6 L10 17.4 L18.8 7.2", null),
            ["ellipsis.circle"] = new IconData("M12 2.2 A9.8 9.8 0 1 1 11.99 2.2 Z", "M7.4 10.6 A1.4 1.4 0 1 0 7.41 10.6 Z M12 10.6 A1.4 1.4 0 1 0 12.01 10.6 Z M16.6 10.6 A1.4 1.4 0 1 0 16.61 10.6 Z"),
            ["checkmark.circle.fill"] = new IconData(null, "M12 2.2 A9.8 9.8 0 1 0 12.01 2.2 Z M7.0 12.0 L10.6 15.6 L16.9 9.0 L18.4 10.5 L10.6 18.6 L5.5 13.5 Z"),
            ["xmark.circle.fill"] = new IconData(null, "M12 2.2 A9.8 9.8 0 1 0 12.01 2.2 Z M8.1 6.7 L12 10.6 L15.9 6.7 L17.3 8.1 L13.4 12 L17.3 15.9 L15.9 17.3 L12 13.4 L8.1 17.3 L6.7 15.9 L10.6 12 L6.7 8.1 Z"),
            ["info.circle.fill"] = new IconData(null, "M12 2.2 A9.8 9.8 0 1 0 12.01 2.2 Z M12 5.9 A1.35 1.35 0 1 1 11.99 5.9 Z M10.85 10.0 H13.15 V18.0 H10.85 Z"),
            ["arrow.down.circle.fill"] = new IconData(null, "M12 2.2 A9.8 9.8 0 1 0 12.01 2.2 Z M10.9 6.2 H13.1 V12.6 H10.9 Z M8.2 11.8 H15.8 L12 17.4 Z"),
            ["arrow.up.circle.fill"] = new IconData(null, "M12 2.2 A9.8 9.8 0 1 0 12.01 2.2 Z M10.9 17.8 H13.1 V11.4 H10.9 Z M8.2 12.2 H15.8 L12 6.6 Z"),
            ["arrow.clockwise.circle.fill"] = new IconData(null, "M12 2.2 A9.8 9.8 0 1 0 12.01 2.2 Z M12 6.4 A5.6 5.6 0 1 0 17.4 13.4 H15.3 A3.6 3.6 0 1 1 12 8.4 Z M11.2 4.0 H13.2 V9.2 H16.6 L12.2 12.4 L7.8 9.2 H11.2 Z"),
            ["link.circle.fill"] = new IconData(null, "M12 2.2 A9.8 9.8 0 1 0 12.01 2.2 Z M9.7 15.7 A3.6 3.6 0 0 1 9.7 10.6 L11.6 8.7 A3.6 3.6 0 0 1 16.7 13.8 L15.5 15 L14.1 13.6 L15.3 12.4 A1.6 1.6 0 0 0 13 10.1 L11.1 12 A1.6 1.6 0 0 0 11.1 14.3 Z M8.5 9 L9.9 10.4 L8.7 11.6 A1.6 1.6 0 0 0 11 13.9 L9.6 15.3 A3.6 3.6 0 0 1 7.3 10.2 Z"),
            ["magnifyingglass.circle.fill"] = new IconData(null, "M12 2.2 A9.8 9.8 0 1 0 12.01 2.2 Z M11.1 6.6 A4.5 4.5 0 1 0 11.11 6.6 Z M11.1 8.4 A2.7 2.7 0 1 1 11.09 8.4 Z M13.6 14.2 L15 12.8 L17.9 15.7 L16.5 17.1 Z"),
            ["trash.circle.fill"] = new IconData(null, "M12 2.2 A9.8 9.8 0 1 0 12.01 2.2 Z M7.6 8.6 H16.4 V10.1 H7.6 Z M10.2 6.4 H13.8 V8.1 H10.2 Z M9 11 H15 L14.4 17.6 H9.6 Z"),
            ["dollarsign.circle.fill"] = new IconData("M12 5.6 V18.4 M15.2 9.2 C15.2 7.9 13.8 7.1 12 7.1 C10.2 7.1 8.8 7.9 8.8 9.3 C8.8 12.7 15.2 11.3 15.2 14.7 C15.2 16.1 13.8 16.9 12 16.9 C10.2 16.9 8.8 16.1 8.8 14.8", "M12 1.4 A10.6 10.6 0 1 0 12.01 1.4 Z M12 3.2 A8.8 8.8 0 1 1 11.99 3.2 Z"),
            ["arrow.up.arrow.down.circle.fill"] = new IconData(null, "M12 2.2 A9.8 9.8 0 1 0 12.01 2.2 Z M8.5 10.4 H10.3 V17.2 H8.5 Z M6.1 11.2 H12.7 L9.4 6.8 Z M13.7 6.8 H15.5 V13.6 H13.7 Z M11.3 12.8 H17.9 L14.6 17.2 Z"),
            ["arrow.up.circle"] = new IconData("M21 12 A9 9 0 1 1 3 12 A9 9 0 1 1 21 12 M12 17 V7.6 M8.4 11.2 L12 7.6 L15.6 11.2", null),
            ["arrow.down.circle"] = new IconData("M21 12 A9 9 0 1 1 3 12 A9 9 0 1 1 21 12 M12 7 V16.4 M8.4 12.8 L12 16.4 L15.6 12.8", null),
            ["info.circle"] = new IconData("M21 12 A9 9 0 1 1 3 12 A9 9 0 1 1 21 12 M12 11 V16.6", "M12 6.6 A1.25 1.25 0 1 0 12.01 6.6 Z"),
            ["exclamationmark.triangle"] = new IconData("M12 3.6 L21.4 19.6 H2.6 Z M12 9.6 V14.2", "M12 16.2 A1.2 1.2 0 1 0 12.01 16.2 Z"),
            ["exclamationmark.triangle.fill"] = new IconData(null, "M12 3.2 L21.8 19.9 H2.2 Z M10.9 8.6 H13.1 V14.4 H10.9 Z M12 15.6 A1.25 1.25 0 1 0 12.01 15.6 Z"),
            ["eye.slash"] = new IconData("M4 12 C6.4 8.2 9 6.3 12 6.3 C15 6.3 17.6 8.2 20 12 C17.6 15.8 15 17.7 12 17.7 C9 17.7 6.4 15.8 4 12 Z M9.6 12 A2.4 2.4 0 1 1 14.4 12 A2.4 2.4 0 1 1 9.6 12 M4.4 4.4 L19.6 19.6", null),
            ["arrow.clockwise"] = new IconData("M19.6 12 A7.6 7.6 0 1 1 17 6.2 M17.4 2.6 V6.8 H13.2", null),
            ["arrow.triangle.2.circlepath"] = new IconData("M5 10.4 A7.2 7.2 0 0 1 17.6 7 M19 13.6 A7.2 7.2 0 0 1 6.4 17 M18.2 3.6 V7.4 H14.4 M5.8 20.4 V16.6 H9.6", null),
            ["arrow.up.arrow.down"] = new IconData("M8 20 V4.6 M4.6 8 L8 4.6 L11.4 8 M16 4 V19.4 M12.6 16 L16 19.4 L19.4 16", null),
            ["arrow.up.doc"] = new IconData("M17.6 9.4 V19.4 A1.8 1.8 0 0 1 15.8 21.2 H8.2 A1.8 1.8 0 0 1 6.4 19.4 V9.4 M12 15.6 V3.4 M8.4 7 L12 3.4 L15.6 7", null),
            ["arrow.up.right.square"] = new IconData("M20.2 6.4 V4.4 A1.6 1.6 0 0 0 18.6 2.8 H5.4 A1.6 1.6 0 0 0 3.8 4.4 V18.6 A1.6 1.6 0 0 0 5.4 20.2 H18.6 A1.6 1.6 0 0 0 20.2 18.6 V13.6 M11 13 L20 4 M14.6 3.6 H20.4 V9.4", null),
            ["arrow.down.and.line.horizontal.and.arrow.up"] = new IconData("M7 3 V9.6 M4.4 7 L7 9.6 L9.6 7 M3 12.8 H21 M17 21 V14.4 M14.4 17 L17 14.4 L19.6 17", null),
            ["square.and.arrow.up"] = new IconData("M6 11.4 V19.4 A1.8 1.8 0 0 0 7.8 21.2 H16.2 A1.8 1.8 0 0 0 18 19.4 V11.4 M12 15 V3.2 M8.4 6.8 L12 3.2 L15.6 6.8", null),
            ["square.and.arrow.down"] = new IconData("M6 11.4 V19.4 A1.8 1.8 0 0 0 7.8 21.2 H16.2 A1.8 1.8 0 0 0 18 19.4 V11.4 M12 3.2 V15 M8.4 11.4 L12 15 L15.6 11.4", null),
            ["doc"] = new IconData("M14 2.8 H7.2 A2 2 0 0 0 5.2 4.8 V19.2 A2 2 0 0 0 7.2 21.2 H16.8 A2 2 0 0 0 18.8 19.2 V7.6 Z M13.9 2.9 V7.7 H18.7", null),
            ["doc.text"] = new IconData("M14 2.8 H7.2 A2 2 0 0 0 5.2 4.8 V19.2 A2 2 0 0 0 7.2 21.2 H16.8 A2 2 0 0 0 18.8 19.2 V7.6 Z M13.9 2.9 V7.7 H18.7 M8.2 11.4 H15.8 M8.2 14.6 H15.8 M8.2 17.8 H13", null),
            ["doc.richtext"] = new IconData("M14 2.8 H7.2 A2 2 0 0 0 5.2 4.8 V19.2 A2 2 0 0 0 7.2 21.2 H16.8 A2 2 0 0 0 18.8 19.2 V7.6 Z M13.9 2.9 V7.7 H18.7 M8.2 14.6 H15.8 M8.2 17.8 H13", "M8.2 9.8 H15.8 V12.4 H8.2 Z"),
            ["doc.zipper"] = new IconData("M14 2.8 H7.2 A2 2 0 0 0 5.2 4.8 V19.2 A2 2 0 0 0 7.2 21.2 H16.8 A2 2 0 0 0 18.8 19.2 V7.6 Z M13.9 2.9 V7.7 H18.7", "M11 3 H13 V5 H11 Z M11 6.4 H13 V8.4 H11 Z M11 9.8 H13 V11.8 H11 Z M11 13.2 H13 V15.2 H11 Z M10.4 16.6 H13.6 V20.2 H10.4 Z"),
            ["doc.on.doc"] = new IconData("M8.6 7.4 A1.8 1.8 0 0 1 10.4 5.6 H15.2 L19.4 9.8 V17.6 A1.8 1.8 0 0 1 17.6 19.4 H10.4 A1.8 1.8 0 0 1 8.6 17.6 Z M15.1 5.7 V9.9 H19.3 M5.6 16.4 A1.8 1.8 0 0 1 4.6 14.8 V6.4 A1.8 1.8 0 0 1 6.4 4.6 H13.4", null),
            ["doc.on.doc.fill"] = new IconData(null, "M8.6 7.4 A1.8 1.8 0 0 1 10.4 5.6 H14.6 V10.4 H19.4 V17.6 A1.8 1.8 0 0 1 17.6 19.4 H10.4 A1.8 1.8 0 0 1 8.6 17.6 Z M15.8 5.8 L19.2 9.2 H15.8 Z M4.6 6.4 A1.8 1.8 0 0 1 6.4 4.6 H13.4 V6.2 H10.4 A3.4 3.4 0 0 0 7 9.6 V16.5 A1.8 1.8 0 0 1 4.6 14.8 Z"),
            ["folder"] = new IconData("M3.2 6.6 A1.8 1.8 0 0 1 5 4.8 H9.3 L11.3 7.4 H19 A1.8 1.8 0 0 1 20.8 9.2 V17.6 A1.8 1.8 0 0 1 19 19.4 H5 A1.8 1.8 0 0 1 3.2 17.6 Z", null),
            ["folder.fill"] = new IconData(null, "M3.2 6.6 A1.8 1.8 0 0 1 5 4.8 H9.3 L11.3 7.4 H19 A1.8 1.8 0 0 1 20.8 9.2 V17.6 A1.8 1.8 0 0 1 19 19.4 H5 A1.8 1.8 0 0 1 3.2 17.6 Z"),
            ["folder.badge.plus"] = new IconData("M3.2 6.6 A1.8 1.8 0 0 1 5 4.8 H9.3 L11.3 7.4 H19 A1.8 1.8 0 0 1 20.8 9.2 V17.6 A1.8 1.8 0 0 1 19 19.4 H5 A1.8 1.8 0 0 1 3.2 17.6 Z M12 11.4 V16.4 M9.5 13.9 H14.5", null),
            ["photo"] = new IconData("M3.4 6.6 A1.8 1.8 0 0 1 5.2 4.8 H18.8 A1.8 1.8 0 0 1 20.6 6.6 V17.4 A1.8 1.8 0 0 1 18.8 19.2 H5.2 A1.8 1.8 0 0 1 3.4 17.4 Z M3.6 16.4 L9 11.2 L13.4 15.4 L16 13 L20.4 17", "M16.2 9.4 A1.7 1.7 0 1 0 16.21 9.4 Z"),
            ["film"] = new IconData("M3.4 6.4 A1.6 1.6 0 0 1 5 4.8 H19 A1.6 1.6 0 0 1 20.6 6.4 V17.6 A1.6 1.6 0 0 1 19 19.2 H5 A1.6 1.6 0 0 1 3.4 17.6 Z M7.6 4.9 V19.1 M16.4 4.9 V19.1 M3.5 12 H20.5", "M4.6 6.6 H6.4 V8.4 H4.6 Z M4.6 15.6 H6.4 V17.4 H4.6 Z M17.6 6.6 H19.4 V8.4 H17.6 Z M17.6 15.6 H19.4 V17.4 H17.6 Z"),
            ["waveform"] = new IconData("M3.2 12 H5.2 M7.2 7 V17 M11.2 3.6 V20.4 M15.2 6.4 V17.6 M19 9.4 V14.6 M21 11.4 V12.6", null),
            ["chevron.left.forwardslash.chevron.right"] = new IconData("M8.4 7.6 L3.6 12 L8.4 16.4 M15.6 7.6 L20.4 12 L15.6 16.4 M13.6 4.4 L10.4 19.6", null),
            ["tray.full"] = new IconData("M3.4 13.6 H8.2 L9.6 16.2 H14.4 L15.8 13.6 H20.6 M3.4 13.6 L5.9 6.2 A1.6 1.6 0 0 1 7.4 5.1 H16.6 A1.6 1.6 0 0 1 18.1 6.2 L20.6 13.6 V17.4 A1.6 1.6 0 0 1 19 19 H5 A1.6 1.6 0 0 1 3.4 17.4 Z M6.6 9.4 H17.4 M7.6 6.4 H16.4", null),
            ["tray.full.fill"] = new IconData(null, "M3.4 13.6 H8.2 L9.6 16.2 H14.4 L15.8 13.6 H20.6 M3.4 13.6 L5.9 6.2 A1.6 1.6 0 0 1 7.4 5.1 H16.6 A1.6 1.6 0 0 1 18.1 6.2 L20.6 13.6 V17.4 A1.6 1.6 0 0 1 19 19 H5 A1.6 1.6 0 0 1 3.4 17.4 Z M6.9 8.6 H17.1 V10.1 H6.9 Z M8.1 5.6 H15.9 V7.1 H8.1 Z"),
            ["tray.2.fill"] = new IconData(null, "M3.4 10.6 H7.6 L8.8 12.6 H15.2 L16.4 10.6 H20.6 V14 A1.6 1.6 0 0 1 19 15.6 H5 A1.6 1.6 0 0 1 3.4 14 Z M3.4 10.6 L5.8 4.8 A1.6 1.6 0 0 1 7.3 3.8 H16.7 A1.6 1.6 0 0 1 18.2 4.8 L20.6 10.6 H16.4 L15.2 12.6 H8.8 L7.6 10.6 Z M4.6 17 H8.2 L9.4 19 H14.6 L15.8 17 H19.4 V18.6 A1.6 1.6 0 0 1 17.8 20.2 H6.2 A1.6 1.6 0 0 1 4.6 18.6 Z"),
            ["archivebox.fill"] = new IconData(null, "M3.2 4.6 H20.8 V8.4 H3.2 Z M4.6 9.8 H19.4 V18 A1.8 1.8 0 0 1 17.6 19.8 H6.4 A1.8 1.8 0 0 1 4.6 18 Z M9.4 12.2 H14.6 V13.9 H9.4 Z"),
            ["basket.fill"] = new IconData(null, "M3 9.6 H21 L19 19 A1.7 1.7 0 0 1 17.3 20.3 H6.7 A1.7 1.7 0 0 1 5 19 Z M8.6 12.4 H10.2 V17.6 H8.6 Z M13.8 12.4 H15.4 V17.6 H13.8 Z"),
            ["shippingbox.fill"] = new IconData(null, "M12 2.6 L21 6.8 V17.2 L12 21.4 L3 17.2 V6.8 Z M11.1 10.6 L3.3 7 L4 5.5 L11.8 9.1 Z M12.2 9.1 L20 5.5 L20.7 7 L12.9 10.6 Z M11.1 10.3 H12.9 V20.8 H11.1 Z"),
            ["internaldrive"] = new IconData("M3.4 7.4 A2 2 0 0 1 5.4 5.4 H18.6 A2 2 0 0 1 20.6 7.4 V16.6 A2 2 0 0 1 18.6 18.6 H5.4 A2 2 0 0 1 3.4 16.6 Z M3.5 12.6 H20.5", "M16.6 15.2 A1.3 1.3 0 1 0 16.61 15.2 Z"),
            ["internaldrive.fill"] = new IconData(null, "M3.4 7.4 A2 2 0 0 1 5.4 5.4 H18.6 A2 2 0 0 1 20.6 7.4 V16.6 A2 2 0 0 1 18.6 18.6 H5.4 A2 2 0 0 1 3.4 16.6 Z M4.6 11.6 H19.4 V13.2 H4.6 Z M16.6 15.4 A1.25 1.25 0 1 0 16.61 15.4 Z"),
            ["externaldrive.fill"] = new IconData(null, "M3.4 7.4 A2 2 0 0 1 5.4 5.4 H18.6 A2 2 0 0 1 20.6 7.4 V16.6 A2 2 0 0 1 18.6 18.6 H5.4 A2 2 0 0 1 3.4 16.6 Z M4.6 11.6 H19.4 V13.2 H4.6 Z M6.4 15.2 H12 V16.8 H6.4 Z M16.4 15.2 A1.2 1.2 0 1 0 16.41 15.2 Z"),
            ["cloud"] = new IconData("M7.4 18.6 A4.4 4.4 0 0 1 7.2 9.9 A5.4 5.4 0 0 1 17.4 10.2 A4.2 4.2 0 0 1 17 18.6 Z", null),
            ["cloud.fill"] = new IconData(null, "M7.4 18.8 A4.5 4.5 0 0 1 7.2 9.8 A5.5 5.5 0 0 1 17.5 10.1 A4.3 4.3 0 0 1 17.1 18.8 Z"),
            ["flame.fill"] = new IconData(null, "M12 2.4 C15.6 6.6 18.8 8.6 18.8 13.4 A6.8 6.8 0 0 1 5.2 13.4 C5.2 10.6 6.6 9 8.2 7.2 C8.4 9.4 9.4 10.6 10.6 11 C10.2 7.6 10.6 4.8 12 2.4 Z"),
            ["square.grid.2x2"] = new IconData("M4.4 5.6 A1.2 1.2 0 0 1 5.6 4.4 H9.6 A1.2 1.2 0 0 1 10.8 5.6 V9.6 A1.2 1.2 0 0 1 9.6 10.8 H5.6 A1.2 1.2 0 0 1 4.4 9.6 Z M13.2 5.6 A1.2 1.2 0 0 1 14.4 4.4 H18.4 A1.2 1.2 0 0 1 19.6 5.6 V9.6 A1.2 1.2 0 0 1 18.4 10.8 H14.4 A1.2 1.2 0 0 1 13.2 9.6 Z M4.4 14.4 A1.2 1.2 0 0 1 5.6 13.2 H9.6 A1.2 1.2 0 0 1 10.8 14.4 V18.4 A1.2 1.2 0 0 1 9.6 19.6 H5.6 A1.2 1.2 0 0 1 4.4 18.4 Z M13.2 14.4 A1.2 1.2 0 0 1 14.4 13.2 H18.4 A1.2 1.2 0 0 1 19.6 14.4 V18.4 A1.2 1.2 0 0 1 18.4 19.6 H14.4 A1.2 1.2 0 0 1 13.2 18.4 Z", null),
            ["square.grid.2x2.fill"] = new IconData(null, "M4.4 5.6 A1.2 1.2 0 0 1 5.6 4.4 H9.6 A1.2 1.2 0 0 1 10.8 5.6 V9.6 A1.2 1.2 0 0 1 9.6 10.8 H5.6 A1.2 1.2 0 0 1 4.4 9.6 Z M13.2 5.6 A1.2 1.2 0 0 1 14.4 4.4 H18.4 A1.2 1.2 0 0 1 19.6 5.6 V9.6 A1.2 1.2 0 0 1 18.4 10.8 H14.4 A1.2 1.2 0 0 1 13.2 9.6 Z M4.4 14.4 A1.2 1.2 0 0 1 5.6 13.2 H9.6 A1.2 1.2 0 0 1 10.8 14.4 V18.4 A1.2 1.2 0 0 1 9.6 19.6 H5.6 A1.2 1.2 0 0 1 4.4 18.4 Z M13.2 14.4 A1.2 1.2 0 0 1 14.4 13.2 H18.4 A1.2 1.2 0 0 1 19.6 14.4 V18.4 A1.2 1.2 0 0 1 18.4 19.6 H14.4 A1.2 1.2 0 0 1 13.2 18.4 Z"),
            ["list.bullet"] = new IconData("M8.6 6.4 H20.4 M8.6 12 H20.4 M8.6 17.6 H20.4", "M4.6 6.4 A1.3 1.3 0 1 0 4.61 6.4 Z M4.6 12 A1.3 1.3 0 1 0 4.61 12 Z M4.6 17.6 A1.3 1.3 0 1 0 4.61 17.6 Z"),
            ["square.stack.3d.up"] = new IconData("M12 3.2 L20.6 7.4 L12 11.6 L3.4 7.4 Z M3.4 12 L12 16.2 L20.6 12 M3.4 16.6 L12 20.8 L20.6 16.6", null),
            ["magnifyingglass"] = new IconData("M17.2 11 A6.2 6.2 0 1 1 4.8 11 A6.2 6.2 0 1 1 17.2 11 M15.6 15.6 L20.4 20.4", null),
            ["trash"] = new IconData("M4.6 6.8 H19.4 M9 6.8 V4.6 A0.9 0.9 0 0 1 9.9 3.7 H14.1 A0.9 0.9 0 0 1 15 4.6 V6.8 M6.6 6.8 L7.6 19.6 A1.6 1.6 0 0 0 9.2 21 H14.8 A1.6 1.6 0 0 0 16.4 19.6 L17.4 6.8 M10.4 10.2 V17.6 M13.6 10.2 V17.6", null),
            ["pencil"] = new IconData("M4 20 L4.9 16.2 L16.3 4.8 A1.9 1.9 0 0 1 19 4.8 L19.2 5 A1.9 1.9 0 0 1 19.2 7.7 L7.8 19.1 Z M14.6 6.6 L17.4 9.4", null),
            ["link"] = new IconData("M10 13.6 A3.4 3.4 0 0 1 10 8.8 L13.2 5.6 A3.4 3.4 0 0 1 18 10.4 L16.4 12 M14 10.4 A3.4 3.4 0 0 1 14 15.2 L10.8 18.4 A3.4 3.4 0 0 1 6 13.6 L7.6 12", null),
            ["tag.fill"] = new IconData(null, "M3.2 5.6 A2.4 2.4 0 0 1 5.6 3.2 H11.6 L20.8 12.4 A1.6 1.6 0 0 1 20.8 14.6 L14.6 20.8 A1.6 1.6 0 0 1 12.4 20.8 L3.2 11.6 Z M7.6 6.4 A1.5 1.5 0 1 0 7.61 6.4 Z"),
            ["gearshape"] = new IconData("M9.9 3.6 H14.1 L14.6 6.2 L16.4 7 L18.6 5.6 L21 9.2 L18.9 10.8 V13.2 L21 14.8 L18.6 18.4 L16.4 17 L14.6 17.8 L14.1 20.4 H9.9 L9.4 17.8 L7.6 17 L5.4 18.4 L3 14.8 L5.1 13.2 V10.8 L3 9.2 L5.4 5.6 L7.6 7 L9.4 6.2 Z M9 12 A3 3 0 1 1 15 12 A3 3 0 1 1 9 12", null),
            ["key"] = new IconData("M15.4 4.2 A4.6 4.6 0 1 1 11.2 10.8 L4 18 V20.6 H7 V18.4 H9.2 V16.2 H11.4 L13.1 14.5", "M16.3 7.9 A1.35 1.35 0 1 0 16.31 7.9 Z"),
            ["key.slash"] = new IconData("M15.4 4.2 A4.6 4.6 0 1 1 11.2 10.8 L4 18 V20.6 H7 V18.4 H9.2 V16.2 H11.4 L13.1 14.5 M3.4 3.4 L20.6 20.6", null),
            ["lock.shield"] = new IconData("M12 3 L19.8 6 V12.2 C19.8 16.6 16.6 19.8 12 21.4 C7.4 19.8 4.2 16.6 4.2 12.2 V6 Z M9.6 12.2 V10.4 A2.4 2.4 0 0 1 14.4 10.4 V12.2", "M9.2 12.2 H14.8 V16.6 H9.2 Z"),
            ["lock.shield.fill"] = new IconData(null, "M12 2.8 L20 5.9 V12.2 C20 16.8 16.7 20.1 12 21.7 C7.3 20.1 4 16.8 4 12.2 V5.9 Z M9.6 11.4 H14.4 V9.9 A2.4 2.4 0 0 0 9.6 9.9 Z"),
            ["paintpalette"] = new IconData("M12 3.4 C17.2 3.4 21 6.8 21 11.2 C21 14 19 15.4 17 15.4 H15.2 A1.6 1.6 0 0 0 14 18 A1.7 1.7 0 0 1 12.6 20.6 C6.9 20.6 3 17 3 12 C3 7 7 3.4 12 3.4 Z", "M8 9.4 A1.3 1.3 0 1 0 8.01 9.4 Z M12.4 7.2 A1.3 1.3 0 1 0 12.41 7.2 Z M16.6 9.6 A1.3 1.3 0 1 0 16.61 9.6 Z M7.4 14.4 A1.3 1.3 0 1 0 7.41 14.4 Z"),
            ["bell.badge"] = new IconData("M17.4 15.6 V10.8 A5.4 5.4 0 0 0 6.6 10.8 V15.6 L4.8 18 H19.2 Z M9.8 18.4 A2.3 2.3 0 0 0 14.2 18.4", "M18.4 6.6 A2.4 2.4 0 1 0 18.41 6.6 Z"),
            ["clock.fill"] = new IconData(null, "M12 2.2 A9.8 9.8 0 1 0 12.01 2.2 Z M11.1 6 H12.9 V12.5 H11.1 Z M12 11.1 L16.4 13.7 L15.5 15.2 L11.1 12.6 Z"),
            ["star.fill"] = new IconData(null, "M12 2.8 L14.9 9.1 L21.6 9.9 L16.6 14.4 L18 21 L12 17.7 L6 21 L7.4 14.4 L2.4 9.9 L9.1 9.1 Z"),
            ["cup.and.saucer.fill"] = new IconData(null, "M4.6 5.6 H16.4 V12 A5 5 0 0 1 6.4 12 Z M17.2 6.8 A2.6 2.6 0 0 1 17.2 11.6 Z M3 17.6 H19 A2.4 2.4 0 0 1 16.6 20 H5.4 A2.4 2.4 0 0 1 3 17.6 Z"),
            ["person.crop.circle"] = new IconData("M21 12 A9 9 0 1 1 3 12 A9 9 0 1 1 21 12 M9.2 9.6 A2.8 2.8 0 1 1 14.8 9.6 A2.8 2.8 0 1 1 9.2 9.6 M5.6 18.6 C6.8 16 9.2 14.6 12 14.6 C14.8 14.6 17.2 16 18.4 18.6", null),
            ["person.crop.circle.badge.plus"] = new IconData("M20.2 8.8 A9 9 0 1 0 15.4 20.4 M8.6 9.4 A2.7 2.7 0 1 1 14 9.4 A2.7 2.7 0 1 1 8.6 9.4 M5.2 18.4 C6.4 16 8.6 14.6 11.3 14.6 M18.4 13.4 V21 M14.6 17.2 H22.2", null),
            ["person.crop.circle.badge.questionmark"] = new IconData("M20.2 8.8 A9 9 0 1 0 15.4 20.4 M8.6 9.4 A2.7 2.7 0 1 1 14 9.4 A2.7 2.7 0 1 1 8.6 9.4 M5.2 18.4 C6.4 16 8.6 14.6 11.3 14.6 M16.4 15.6 A2 2 0 0 1 20.4 15.9 C20.4 17.4 18.4 17.4 18.4 19", "M18.4 20.4 A1 1 0 1 0 18.41 20.4 Z"),
            ["chart.bar"] = new IconData("M4.6 19.4 H20.4", "M5.6 12 H8.6 V18 H5.6 Z M10.5 7.6 H13.5 V18 H10.5 Z M15.4 10 H18.4 V18 H15.4 Z"),
            ["chart.bar.xaxis"] = new IconData("M4.6 20 H20.4 M6 20 V21.4 M12 20 V21.4 M18 20 V21.4", "M5 5.6 H12.6 V8.2 H5 Z M5 10.2 H17 V12.8 H5 Z M5 14.8 H10.4 V17.4 H5 Z"),
            ["chart.bar.doc.horizontal"] = new IconData("M14 2.8 H7.2 A2 2 0 0 0 5.2 4.8 V19.2 A2 2 0 0 0 7.2 21.2 H16.8 A2 2 0 0 0 18.8 19.2 V7.6 Z M13.9 2.9 V7.7 H18.7", "M8.2 9.6 H15 V11.6 H8.2 Z M8.2 13 H12.4 V15 H8.2 Z M8.2 16.4 H16 V18.4 H8.2 Z"),
            ["checkmark.seal"] = new IconData("M12 2.6 L14.4 4.8 L17.6 4.6 L18.6 7.7 L21.4 9.4 L20.4 12.5 L21.4 15.6 L18.6 17.3 L17.6 20.4 L14.4 20.2 L12 22.4 L9.6 20.2 L6.4 20.4 L5.4 17.3 L2.6 15.6 L3.6 12.5 L2.6 9.4 L5.4 7.7 L6.4 4.6 L9.6 4.8 Z M8.2 12.5 L11 15.3 L16 10.1", null),
        };

    /// Falls back to a neutral document glyph so an unknown name never renders
    /// as an empty hole in the layout.
    public static IconData Get(string? name)
    {
        if (name != null && All.TryGetValue(name, out var icon)) return icon;
        return All["doc"];
    }

    public static bool Contains(string name) => All.ContainsKey(name);
}
