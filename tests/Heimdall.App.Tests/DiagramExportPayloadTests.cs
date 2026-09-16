/*
 * Copyright 2026 Julien Bombled
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System.Text;
using Heimdall.App.Services;

namespace Heimdall.App.Tests;

/// <summary>
/// Covers how an export coming back from the embedded editor is turned into a file.
/// </summary>
/// <remarks>
/// The shapes asserted here were measured against the vendored draw.io 31.4.5
/// bundle, driving the same embed protocol the tool uses: an SVG export arrives
/// as <c>data:image/svg+xml;base64,PHN2ZyB4bWxucz0i...</c> and a PNG export as
/// <c>data:image/png;base64,iVBORw0KGgo...</c>. The shipped code branched on the
/// PNG prefix alone and wrote everything else through as text, so an SVG file
/// would have contained the characters of a data URI. No control reached that
/// path, which is why it was never seen.
/// </remarks>
public sealed class DiagramExportPayloadTests
{
    /// <summary>The first bytes of a PNG file, so a decode can be checked rather than counted.</summary>
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [Fact]
    public void Parse_SvgDataUri_DecodesToMarkup()
    {
        const string Markup = "<svg xmlns=\"http://www.w3.org/2000/svg\"><rect/></svg>";
        string data = "data:image/svg+xml;base64,"
            + Convert.ToBase64String(Encoding.UTF8.GetBytes(Markup));

        var payload = DiagramExportPayload.Parse(data);

        Assert.NotNull(payload);
        Assert.Equal(DiagramExportKind.Svg, payload!.Kind);
        Assert.Equal(Markup, Encoding.UTF8.GetString(payload.Bytes));

        // The defect this replaces: the data URI itself reaching the file.
        Assert.DoesNotContain("base64", Encoding.UTF8.GetString(payload.Bytes), StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_PngDataUri_DecodesToImageBytes()
    {
        string data = "data:image/png;base64," + Convert.ToBase64String(PngSignature);

        var payload = DiagramExportPayload.Parse(data);

        Assert.NotNull(payload);
        Assert.Equal(DiagramExportKind.Png, payload!.Kind);
        Assert.Equal(PngSignature, payload.Bytes);
    }

    /// <summary>
    /// The real prefixes, so a future bundle that changes them is noticed here
    /// rather than in a file nobody can open.
    /// </summary>
    [Theory]
    [InlineData("data:image/svg+xml;base64,", DiagramExportKind.Svg)]
    [InlineData("data:image/png;base64,", DiagramExportKind.Png)]
    [InlineData("DATA:IMAGE/PNG;BASE64,", DiagramExportKind.Png)]
    internal void Parse_RecognizesTheKindFromTheMediaType(string prefix, DiagramExportKind expected)
    {
        var payload = DiagramExportPayload.Parse(prefix + Convert.ToBase64String("payload"u8.ToArray()));

        Assert.NotNull(payload);
        Assert.Equal(expected, payload!.Kind);
        Assert.Equal("payload", Encoding.UTF8.GetString(payload.Bytes));
    }

    [Fact]
    public void Parse_RawSvgMarkup_IsKeptAsIs()
    {
        const string Markup = "<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>";

        var payload = DiagramExportPayload.Parse(Markup);

        Assert.NotNull(payload);
        Assert.Equal(DiagramExportKind.Svg, payload!.Kind);
        Assert.Equal(Markup, Encoding.UTF8.GetString(payload.Bytes));
    }

    [Fact]
    public void Parse_PlainDataUri_DecodesPercentEncodedText()
    {
        var payload = DiagramExportPayload.Parse("data:image/svg+xml,%3Csvg%3E%3C%2Fsvg%3E");

        Assert.NotNull(payload);
        Assert.Equal(DiagramExportKind.Svg, payload!.Kind);
        Assert.Equal("<svg></svg>", Encoding.UTF8.GetString(payload.Bytes));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("data:image/png;base64")]
    [InlineData("data:image/png;base64,not valid base64 !!")]
    [InlineData("whatever the editor meant by this")]
    public void Parse_UnusablePayload_ReturnsNull(string? data)
    {
        Assert.Null(DiagramExportPayload.Parse(data));
    }

    /// <summary>
    /// An unknown media type is still written, because the bytes are the export;
    /// only the proposed file name depends on knowing the kind.
    /// </summary>
    [Fact]
    public void Parse_UnknownMediaType_StillYieldsTheBytes()
    {
        var payload = DiagramExportPayload.Parse(
            "data:application/octet-stream;base64," + Convert.ToBase64String("bytes"u8.ToArray()));

        Assert.NotNull(payload);
        Assert.Equal(DiagramExportKind.Unknown, payload!.Kind);
        Assert.Equal("bytes", Encoding.UTF8.GetString(payload.Bytes));
    }
}
