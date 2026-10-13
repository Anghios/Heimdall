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

using System.Windows.Controls;

namespace Heimdall.App.Controls;

/// <summary>
/// The read-only properties of one session: protocol, state, endpoint, authentication and the
/// profile's descriptive fields.
/// </summary>
/// <remarks>
/// Its DataContext is a <see cref="ViewModels.ServerItemViewModel"/>. The detail pane of the
/// session area and the properties panel under the tree both host it, so a field added here
/// appears in both and neither can drift from the other.
/// </remarks>
public partial class SessionPropertiesView : UserControl
{
    public SessionPropertiesView()
    {
        InitializeComponent();
    }
}
