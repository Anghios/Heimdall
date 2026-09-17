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

namespace Heimdall.Core.Models;

/// <summary>
/// Why a tool refused to close, so the shell can say the right thing or say
/// nothing.
/// </summary>
/// <remarks>
/// <para><see cref="IToolView.CanClose"/> answers a single bool, and the shell
/// used to read every false the same way: "the tool is busy and cannot be closed
/// right now". That is true of most tools, which refuse while a scan or an export
/// runs. It is false of the two that ask the user something first. Cancelling the
/// diagram editor's save prompt was reported as the tool being busy, when the user
/// had just declined; a note that could not be written to disk was reported the
/// same way, and the failure itself was never mentioned.</para>
/// <para>A tool sets this on every <see cref="IToolView.CanClose"/> call that
/// returns false, so the shell reading it straight afterwards cannot pick up an
/// answer to an older question.</para>
/// </remarks>
public enum ToolCloseRefusal
{
    /// <summary>
    /// Work is in progress. The shell explains why the tab stayed open, because
    /// nobody asked the user anything.
    /// </summary>
    Busy = 0,

    /// <summary>
    /// The tool asked and the user declined. The shell says nothing: they answered
    /// the question a moment ago and a second message would only contradict it.
    /// </summary>
    UserDeclined = 1,

    /// <summary>
    /// The tool could not write what closing required. The shell says so, because
    /// this one is neither expected nor already on screen.
    /// </summary>
    SaveFailed = 2,
}
