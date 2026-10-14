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

using System.Windows.Input;

namespace Heimdall.App.Services;

internal static class LowLevelGlobalShortcutRouter
{
    internal static bool ShouldRoute(Key key, ModifierKeys modifiers)
    {
        return (key == Key.Tab && modifiers == ModifierKeys.Control)
            || (key == Key.Tab && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            || (key == Key.O && modifiers == (ModifierKeys.Control | ModifierKeys.Shift));
    }

    /// <summary>
    /// Shortcuts taken from a focused terminal or tool surface but left to a focused RDP view.
    /// </summary>
    /// <remarks>
    /// Ctrl+F4 closes the tab. A shell has no use for it, so it is taken from the WebView2
    /// terminal, which the window's own key handling never sees. On a remote Windows desktop it
    /// closes the document or tab of the remote application, so an RDP view keeps it.
    /// Ctrl+PgUp and Ctrl+PgDn, with or without Shift, move between tabs and move a tab, for the
    /// same reasons: no use in a shell, the sheet or tab switch of a remote application.
    /// </remarks>
    internal static bool ShouldRouteUnlessRdpFocused(Key key, ModifierKeys modifiers)
        => (key == Key.F4 && modifiers == ModifierKeys.Control)
            || (key is Key.PageUp or Key.PageDown
                && modifiers is ModifierKeys.Control or (ModifierKeys.Control | ModifierKeys.Shift));
}
