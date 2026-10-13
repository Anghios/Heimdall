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
using Heimdall.App.Services;

namespace Heimdall.App.Tests;

/// <summary>
/// Ctrl+W stays the shell's inside a terminal; Ctrl+F4 closes the tab from anywhere but an RDP view.
/// </summary>
public sealed class MainWindowCloseTabShortcutTests
{
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public void CtrlW_ClosesTheTab_OnlyOutsideATerminal(bool terminalFocused, int expectedCloses)
    {
        KeyboardShortcutService service = new();
        int closes = 0;
        MainWindow.RegisterCloseTabShortcuts(service, () => terminalFocused, () => closes++);

        service.TryHandle(Key.W, ModifierKeys.Control);

        Assert.Equal(expectedCloses, closes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CtrlF4_ClosesTheTab_FromATerminalToo(bool terminalFocused)
    {
        KeyboardShortcutService service = new();
        int closes = 0;
        MainWindow.RegisterCloseTabShortcuts(service, () => terminalFocused, () => closes++);

        bool handled = service.TryHandle(Key.F4, ModifierKeys.Control);

        Assert.True(handled);
        Assert.Equal(1, closes);
    }

    [Fact]
    public void CtrlF4_IsRoutedFromASurface_ButNotThroughTheUnconditionalList()
    {
        // Unconditional routing would take it from a focused RDP view too, where it closes the
        // remote application's document.
        Assert.True(LowLevelGlobalShortcutRouter.ShouldRouteUnlessRdpFocused(Key.F4, ModifierKeys.Control));
        Assert.False(LowLevelGlobalShortcutRouter.ShouldRoute(Key.F4, ModifierKeys.Control));
    }

    [Theory]
    [InlineData(Key.F4, ModifierKeys.None)]
    [InlineData(Key.F4, ModifierKeys.Alt)]
    [InlineData(Key.F4, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData(Key.W, ModifierKeys.Control)]
    public void OnlyCtrlF4_IsRoutedUnlessRdpFocused(Key key, ModifierKeys modifiers)
    {
        Assert.False(LowLevelGlobalShortcutRouter.ShouldRouteUnlessRdpFocused(key, modifiers));
    }
}
