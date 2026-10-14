/*
 * Copyright 2026 Nestor Gonzalez Medina
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

using Heimdall.App.Services;

namespace Heimdall.App.Tests;

/// <summary>
/// The pointer an embedded view runs with. Chromium's hide-while-typing feature is refused, so a
/// view closed while its pointer is hidden cannot leave the window without one.
/// </summary>
public sealed class WebView2PointerTests
{
    [Fact]
    public void AnEmbeddedView_AsksForThePointerToStayVisible()
    {
        Assert.Equal(
            "--disable-features=HideCursorWhileTyping",
            WebView2Helper.EmbeddedBrowserArguments(null));
    }

    [Fact]
    public void TheRoutesOwnArguments_FollowThePointerSwitch()
    {
        Assert.Equal(
            "--disable-features=HideCursorWhileTyping --proxy-server=socks5://127.0.0.1:41001",
            WebView2Helper.EmbeddedBrowserArguments(
                WebView2Helper.WebConsoleBrowserArguments(41001)));
    }
}
