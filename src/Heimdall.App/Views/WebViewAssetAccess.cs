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

using Microsoft.Web.WebView2.Core;

namespace Heimdall.App.Views;

/// <summary>
/// How each WebView2 surface exposes the local folder it serves its page from.
/// </summary>
/// <remarks>
/// <para><c>Allow</c> lets any origin reach the mapped folder. <c>DenyCors</c> serves
/// it only to a document of the same origin, which is what every one of these pages
/// is: each is loaded FROM the host it then fetches from. So the hardened setting
/// should be invisible, and the only way to know is to run it.</para>
/// <para><b>Measured 2026-09-17</b> on a French Release build, per surface:</para>
/// <list type="bullet">
/// <item>Diagram editor, <c>DenyCors</c>: the vendored draw.io loads whole, shapes
/// and panels included, a shape drops on the canvas and a PNG export writes a valid
/// file. The bundle is framed from the same virtual host, so it never crosses an
/// origin.</item>
/// <item>Markdown editor, <c>DenyCors</c>: this was the one in doubt, because
/// <c>milkdown/index.html</c> carries <c>&lt;script type="module" crossorigin&gt;</c>
/// and a module script with that attribute is fetched in CORS mode even same-origin.
/// It loads and the document renders, so WebView2 compares origins rather than
/// keying on the request mode.</item>
/// <item>VNC, <b>left on <c>Allow</c></b>: not measured. No VNC target was reachable
/// on the day, the lab containers were down, and there is no path into that surface
/// without one. The reasoning that carried the other two applies to it unchanged, but
/// reasoning is what this file exists to replace. It stays as it was until someone
/// brings a VNC target up and drives a session against <c>DenyCors</c>.</item>
/// </list>
/// <para>The three are named rather than spelled at the call sites so the next reader
/// finds the measurement instead of repeating the question, and so a change to one of
/// them is a change to a value under test.</para>
/// </remarks>
internal static class WebViewAssetAccess
{
    /// <summary>The diagram editor's vendored draw.io. Measured.</summary>
    internal const CoreWebView2HostResourceAccessKind Diagram =
        CoreWebView2HostResourceAccessKind.DenyCors;

    /// <summary>The Markdown editor's bundle. Measured.</summary>
    internal const CoreWebView2HostResourceAccessKind MarkdownEditor =
        CoreWebView2HostResourceAccessKind.DenyCors;

    /// <summary>noVNC. Not measured, so not changed.</summary>
    internal const CoreWebView2HostResourceAccessKind Vnc =
        CoreWebView2HostResourceAccessKind.Allow;
}
