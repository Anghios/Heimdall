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
/// The zoom each web console was last left at, per profile, so a console that is only readable
/// zoomed in or out opens that way next time.
/// </summary>
/// <remarks>
/// Kept per profile rather than per address: two profiles on one address can be two people's
/// habits, and the browser engine's own zoom memory is per origin and lost with its profile
/// folder. The default zoom is not stored, so a console put back to 100% leaves no entry.
/// </remarks>
public sealed class WebZoomMemory
{
    /// <summary>The smallest zoom the embedded browser accepts.</summary>
    public const double MinZoom = 0.25;

    /// <summary>The largest zoom the embedded browser accepts.</summary>
    public const double MaxZoom = 5.0;

    /// <summary>The browser's own zoom, which is not stored.</summary>
    public const double DefaultZoom = 1.0;

    /// <summary>
    /// Kept zooms are rounded to the percent: the browser steps zoom by multiplication, so a step
    /// in then a step out lands near 1 rather than on it, and the rounding brings it back.
    /// </summary>
    private const int ZoomDecimals = 2;

    private readonly object _lock = new();
    private readonly Dictionary<string, double> _zoomByProfile = new(StringComparer.Ordinal);

    /// <summary>
    /// Raised when a profile's zoom changes; the app persists it. Parameters: the profile id and
    /// the zoom, or null when the profile is back at the default.
    /// </summary>
    public event Action<string, double?>? ZoomChanged;

    /// <summary>Loads the zooms kept in the settings, dropping any the browser would refuse.</summary>
    public void LoadFromConfig(IEnumerable<KeyValuePair<string, double>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        lock (_lock)
        {
            _zoomByProfile.Clear();
            foreach ((string profileId, double zoom) in entries)
            {
                if (!string.IsNullOrEmpty(profileId) && Normalize(zoom) is double kept)
                {
                    _zoomByProfile[profileId] = kept;
                }
            }
        }
    }

    /// <summary>The zoom to open <paramref name="profileId"/> at, or null for the default.</summary>
    public double? Get(string? profileId)
    {
        if (string.IsNullOrEmpty(profileId))
        {
            return null;
        }

        lock (_lock)
        {
            return _zoomByProfile.TryGetValue(profileId, out double zoom) ? zoom : null;
        }
    }

    /// <summary>
    /// Records the zoom a console was just set to.
    /// </summary>
    /// <param name="profileId">The profile the console was opened from.</param>
    /// <param name="zoom">The browser's zoom factor.</param>
    public void Remember(string? profileId, double zoom)
    {
        if (string.IsNullOrEmpty(profileId) || double.IsNaN(zoom) || double.IsInfinity(zoom))
        {
            return;
        }

        double? kept = Normalize(Math.Clamp(zoom, MinZoom, MaxZoom));
        lock (_lock)
        {
            bool had = _zoomByProfile.TryGetValue(profileId, out double previous);
            if (kept is double value)
            {
                if (had && previous == value)
                {
                    return;
                }

                _zoomByProfile[profileId] = value;
            }
            else
            {
                if (!had)
                {
                    return;
                }

                _zoomByProfile.Remove(profileId);
            }
        }

        ZoomChanged?.Invoke(profileId, kept);
    }

    /// <summary>
    /// A zoom worth keeping, rounded to the percent the user can tell apart; null for the
    /// default or for a value the browser would refuse.
    /// </summary>
    internal static double? Normalize(double zoom)
    {
        if (double.IsNaN(zoom) || zoom < MinZoom || zoom > MaxZoom)
        {
            return null;
        }

        double rounded = Math.Round(zoom, ZoomDecimals, MidpointRounding.AwayFromZero);
        return rounded == DefaultZoom ? null : rounded;
    }
}
