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
/// How far the tab strip scrolls on one drag-over event while a dragged tab is held near one of
/// its ends.
/// </summary>
internal static class TabStripDragAutoScroll
{
    /// <summary>Width, in device-independent pixels, of the zone at each end that scrolls.</summary>
    internal const double EdgeZoneWidth = 32d;

    /// <summary>Distance scrolled per drag-over event inside an edge zone.</summary>
    internal const double StepWidth = 16d;

    /// <summary>
    /// The horizontal offset change for a pointer at <paramref name="pointerX"/> in a viewport of
    /// <paramref name="viewportWidth"/>: negative near the left end, positive near the right end,
    /// zero elsewhere and for a viewport too narrow to hold two zones.
    /// </summary>
    internal static double Step(double pointerX, double viewportWidth)
    {
        if (viewportWidth < EdgeZoneWidth * 2d || pointerX < 0d || pointerX > viewportWidth)
        {
            return 0d;
        }

        if (pointerX < EdgeZoneWidth)
        {
            return -StepWidth;
        }

        return pointerX > viewportWidth - EdgeZoneWidth ? StepWidth : 0d;
    }
}
