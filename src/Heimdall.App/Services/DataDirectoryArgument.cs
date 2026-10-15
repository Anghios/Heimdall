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

using System.IO;

namespace Heimdall.App.Services;

/// <summary>
/// Reads <c>--data-dir &lt;path&gt;</c> or <c>--data-dir=&lt;path&gt;</c> from the command line: the
/// directory that holds the profiles, settings and logs instead of the user's own.
/// </summary>
internal static class DataDirectoryArgument
{
    /// <summary>The option's name, as Heimdall-rs spells it.</summary>
    internal const string OptionName = "--data-dir";

    /// <summary>
    /// The data directory named on the command line, made absolute against the current directory,
    /// or null when the option is absent or has no value. The last occurrence wins.
    /// </summary>
    internal static string? Find(IReadOnlyList<string>? args)
    {
        if (args is null)
        {
            return null;
        }

        string? found = null;
        for (int index = 0; index < args.Count; index++)
        {
            string arg = args[index];
            if (string.Equals(arg, OptionName, StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 < args.Count && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    found = args[index + 1];
                    index++;
                }

                continue;
            }

            string prefix = OptionName + "=";
            if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                found = arg[prefix.Length..];
            }
        }

        return string.IsNullOrWhiteSpace(found) ? null : Path.GetFullPath(found.Trim().Trim('"'));
    }
}
