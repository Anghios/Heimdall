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

namespace Heimdall.App.Services;

/// <summary>Which file an export belongs in.</summary>
internal enum DiagramExportKind
{
    /// <summary>The editor did not say which format it produced.</summary>
    Unknown,

    /// <summary>A raster image.</summary>
    Png,

    /// <summary>A vector image.</summary>
    Svg
}

/// <summary>
/// What the diagram editor handed back for an export, decoded into the bytes to
/// write and the kind of file they are.
/// </summary>
/// <param name="Bytes">Exactly what belongs in the file.</param>
/// <param name="Kind">Which format the editor produced.</param>
internal sealed record DiagramExportPayload(byte[] Bytes, DiagramExportKind Kind)
{
    private const string DataUriPrefix = "data:";
    private const string Base64Marker = "base64";

    /// <summary>
    /// Reads an export message from the embedded editor.
    /// </summary>
    /// <remarks>
    /// <para>draw.io answers an export request with a data URI, and it does so for
    /// SVG as well as for PNG: measured against the vendored 31.4.5 bundle, an
    /// SVG export arrives as <c>data:image/svg+xml;base64,...</c> rather than as
    /// markup. Writing that string into a .svg file produces a file whose content
    /// is the text of a data URI, which nothing can open.</para>
    /// <para>Raw markup is still accepted: it costs one branch, and the embed
    /// protocol has carried SVG both ways across versions.</para>
    /// </remarks>
    /// <param name="data">The editor's export payload.</param>
    /// <returns>The payload, or null when <paramref name="data"/> carries nothing usable.</returns>
    public static DiagramExportPayload? Parse(string? data)
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            return null;
        }

        if (!data.StartsWith(DataUriPrefix, StringComparison.OrdinalIgnoreCase))
        {
            // Raw markup: only SVG ever arrives this way.
            return data.Contains("<svg", StringComparison.OrdinalIgnoreCase)
                ? new DiagramExportPayload(Encoding.UTF8.GetBytes(data), DiagramExportKind.Svg)
                : null;
        }

        int separator = data.IndexOf(',', StringComparison.Ordinal);
        if (separator < 0)
        {
            return null;
        }

        string header = data[..separator];
        string payload = data[(separator + 1)..];
        DiagramExportKind kind = KindFromHeader(header);

        if (!header.Contains(Base64Marker, StringComparison.OrdinalIgnoreCase))
        {
            // A data URI without base64 carries percent-encoded text.
            return new DiagramExportPayload(
                Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload)), kind);
        }

        try
        {
            return new DiagramExportPayload(Convert.FromBase64String(payload), kind);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static DiagramExportKind KindFromHeader(string header)
    {
        if (header.Contains("svg", StringComparison.OrdinalIgnoreCase))
        {
            return DiagramExportKind.Svg;
        }

        return header.Contains("png", StringComparison.OrdinalIgnoreCase)
            ? DiagramExportKind.Png
            : DiagramExportKind.Unknown;
    }
}
