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

namespace Heimdall.Core.SessionHealth;

/// <summary>
/// Probes a web console at its address. The production implementation is
/// <see cref="HttpHealthProbe"/>; tests substitute a stub.
/// </summary>
public interface IWebHealthProbe
{
    /// <summary>
    /// Asks <paramref name="address"/> for its page with a hard deadline of
    /// <paramref name="timeoutMs"/>. Implementations must never throw: failures are encoded in
    /// the returned <see cref="HealthState"/>.
    /// </summary>
    Task<HealthState> ProbeAsync(Uri address, int timeoutMs, CancellationToken ct);
}
