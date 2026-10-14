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

using Heimdall.Core.Configuration;

namespace Heimdall.App.ViewModels;

/// <summary>
/// What a session row needs to say which gateway it goes through: the configured gateways, to
/// name one, and the folder defaults, to find one the profile inherits.
/// </summary>
/// <remarks>
/// One value rather than two parameters, so a row can never be handed the gateways without the
/// folder defaults. The row asked only for the gateways once, and a profile whose gateway came
/// from its folder showed no badge although every connection went through that gateway.
/// </remarks>
public sealed class SessionGatewayContext
{
    private static readonly IReadOnlyDictionary<string, GroupDefaultsDto> NoGroupDefaults =
        new Dictionary<string, GroupDefaultsDto>();

    public SessionGatewayContext(
        IReadOnlyDictionary<string, SshGatewayDto> gateways,
        IReadOnlyDictionary<string, GroupDefaultsDto>? groupDefaults = null)
    {
        ArgumentNullException.ThrowIfNull(gateways);
        Gateways = gateways;
        GroupDefaults = groupDefaults ?? NoGroupDefaults;
    }

    /// <summary>The configured gateways by identifier, compared without case.</summary>
    public IReadOnlyDictionary<string, SshGatewayDto> Gateways { get; }

    /// <summary>The folder defaults by folder path.</summary>
    public IReadOnlyDictionary<string, GroupDefaultsDto> GroupDefaults { get; }

    /// <summary>
    /// Builds the context from the settings, keeping the last of several gateways that share an
    /// identifier, as the connection does.
    /// </summary>
    public static SessionGatewayContext FromSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Dictionary<string, SshGatewayDto> gateways = settings.SshGateways
            .Where(gw => !string.IsNullOrWhiteSpace(gw.Id))
            .GroupBy(gw => gw.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
        return new SessionGatewayContext(gateways, settings.GroupDefaults);
    }
}
