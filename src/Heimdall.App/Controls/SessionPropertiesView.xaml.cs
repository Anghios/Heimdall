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

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Threading;
using Heimdall.App.Localization;
using Heimdall.App.Services;

namespace Heimdall.App.Controls;

/// <summary>
/// The read-only properties of one session: protocol, state, endpoint, authentication and the
/// profile's descriptive fields.
/// </summary>
/// <remarks>
/// Its DataContext is a <see cref="ViewModels.ServerItemViewModel"/>. The detail pane of the
/// session area and the properties panel under the tree both host it, so a field added here
/// appears in both and neither can drift from the other. Each value copies itself on a click or
/// Enter: reading an address off the panel is usually the first step of pasting it elsewhere.
/// </remarks>
public partial class SessionPropertiesView : UserControl
{
    /// <summary>How long the "copied" line stays under the values.</summary>
    internal static readonly TimeSpan CopyNoticeDuration = TimeSpan.FromSeconds(2);

    private readonly DispatcherTimer _noticeTimer;

    public SessionPropertiesView()
    {
        InitializeComponent();
        _noticeTimer = new DispatcherTimer { Interval = CopyNoticeDuration };
        _noticeTimer.Tick += (_, _) =>
        {
            _noticeTimer.Stop();
            Spv_CopyNotice.Visibility = Visibility.Collapsed;
        };
    }

    /// <summary>Where copied values go; the system clipboard unless a test supplies another.</summary>
    internal IClipboardService Clipboard { get; set; } = new WpfClipboardService();

    /// <summary>What a copy attempt came to.</summary>
    internal enum CopyOutcome
    {
        Copied,
        NothingToCopy,
        ClipboardBusy,
    }

    /// <summary>Copies <paramref name="value"/>, and says how it went.</summary>
    /// <remarks>
    /// Another program holding the clipboard open makes the write throw; that is a busy
    /// clipboard to report, not a failure to let escape a click handler.
    /// </remarks>
    internal static CopyOutcome TryCopy(IClipboardService clipboard, string? value)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        if (string.IsNullOrWhiteSpace(value))
        {
            return CopyOutcome.NothingToCopy;
        }

        try
        {
            clipboard.SetText(value);
            return CopyOutcome.Copied;
        }
        catch (ExternalException)
        {
            return CopyOutcome.ClipboardBusy;
        }
    }

    private void OnCopyValueClick(object sender, RoutedEventArgs e)
    {
        string? value = (sender as FrameworkElement)?.Tag as string;
        CopyOutcome outcome = TryCopy(Clipboard, value);
        if (outcome == CopyOutcome.NothingToCopy)
        {
            return;
        }

        Spv_CopyNotice.Text = outcome == CopyOutcome.Copied
            ? string.Format(System.Globalization.CultureInfo.CurrentCulture, LocalizationSource.Instance["PropertiesCopied"], value)
            : LocalizationSource.Instance["PropertiesCopyFailed"];
        Spv_CopyNotice.Visibility = Visibility.Visible;
        UIElementAutomationPeer.FromElement(Spv_CopyNotice)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);

        _noticeTimer.Stop();
        _noticeTimer.Start();
    }
}
