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

namespace Heimdall.App.Tests;

/// <summary>
/// Apps and Shift+F10 open a context menu. F10 reaches WPF as a system key, which is why
/// Shift+F10 opened nothing until the system key was read.
/// </summary>
public sealed class MainWindowContextMenuKeyTests
{
    [Theory]
    [InlineData(Key.System, Key.F10, ModifierKeys.Shift, true)]
    [InlineData(Key.F10, Key.None, ModifierKeys.Shift, true)]
    [InlineData(Key.Apps, Key.None, ModifierKeys.None, true)]
    [InlineData(Key.System, Key.F10, ModifierKeys.None, false)]
    [InlineData(Key.System, Key.F10, ModifierKeys.Control | ModifierKeys.Shift, false)]
    [InlineData(Key.System, Key.F4, ModifierKeys.Alt, false)]
    [InlineData(Key.F9, Key.None, ModifierKeys.Shift, false)]
    public void IsContextMenuKey_ReadsTheSystemKey(Key key, Key systemKey, ModifierKeys modifiers, bool expected)
    {
        Assert.Equal(expected, MainWindow.IsContextMenuKey(key, systemKey, modifiers));
    }
}
