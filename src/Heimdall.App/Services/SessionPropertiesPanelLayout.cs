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

namespace Heimdall.App.Services;

/// <summary>
/// Where the properties panel under the session tree sits, decided apart from the window so the
/// decision can be tested without one.
/// </summary>
internal static class SessionPropertiesPanelLayout
{
    /// <summary>Height of the panel before the user ever drags its splitter.</summary>
    public const double DefaultHeight = 240d;

    /// <summary>Below this the panel shows less than the authentication line and the endpoint.</summary>
    public const double MinHeight = 120d;

    /// <summary>The bound the settings file accepts; the window clamps further at layout time.</summary>
    public const double MaxHeight = 2000d;

    /// <summary>Height the tree keeps whatever the panel asks for.</summary>
    public const double TreeMinHeight = 120d;

    /// <summary>Height of the splitter above the panel: 4 left a target hard to catch.</summary>
    public const double SplitterHeight = 6d;

    /// <summary>
    /// Whether Ctrl+Shift+B shows the panel rather than toggling it: while the Tools tab is up or
    /// the sidebar is hidden the panel cannot be seen, and a toggle there turned it off unseen,
    /// so the second press of a user looking for it put it back where it started.
    /// </summary>
    public static bool ShortcutReveals(bool toolsTabActive, bool sidebarHidden) => toolsTabActive || sidebarHidden;

    /// <summary>
    /// Whether the panel is on screen: the user asked for it and the sidebar shows the sessions,
    /// not the tools, whose list has no session to describe.
    /// </summary>
    public static bool IsShown(bool enabled, bool toolsTabActive) => enabled && !toolsTabActive;

    /// <summary>A saved or dragged height brought back inside the accepted range.</summary>
    public static double ClampHeight(double height) =>
        double.IsFinite(height) ? Math.Clamp(height, MinHeight, MaxHeight) : DefaultHeight;

    /// <summary>
    /// The panel height to lay out in a sidebar of the given height, leaving the tree at least
    /// <see cref="TreeMinHeight"/>. A height saved on a taller window would otherwise push the
    /// tree off the bottom of a shorter one.
    /// </summary>
    public static double FitHeight(double savedHeight, double availableHeight)
    {
        double height = ClampHeight(savedHeight);
        if (!double.IsFinite(availableHeight) || availableHeight <= 0d)
        {
            return height;
        }

        double room = availableHeight - TreeMinHeight;
        return Math.Max(MinHeight, Math.Min(height, room));
    }
}
