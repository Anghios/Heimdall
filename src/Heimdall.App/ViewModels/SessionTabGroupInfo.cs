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

namespace Heimdall.App.ViewModels;

/// <summary>
/// The folder a session tab is grouped under, as the session tree shows it.
/// </summary>
/// <param name="Key">The folder's full path, as the tree spells it. Compared ignoring case, like the tree.</param>
/// <param name="Label">The folder's own name, drawn on the group chip.</param>
/// <param name="Color">The folder's resolved colour as a palette hex string, or empty for the themed default.</param>
public sealed record SessionTabGroupInfo(string Key, string Label, string Color);
