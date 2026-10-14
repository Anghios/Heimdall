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

using System.Reflection;
using Heimdall.App.Services;

namespace Heimdall.App.Tests;

/// <summary>
/// The pointer switch reaches every WebView2 environment the application creates.
/// </summary>
/// <remarks>
/// <see cref="WebView2PointerTests"/> pins the text of the switch. It stays green when the switch
/// is composed and then never handed to WebView2: dropping <c>options: options</c> from
/// <c>CreateEnvironmentAsync</c> compiles to a <c>null</c> options argument, which brings the
/// hidden pointer back to SSH, VNC and the editors, and passing the route's <c>arguments</c>
/// instead of <c>EmbeddedBrowserArguments(arguments)</c> brings it back to the web consoles. A
/// build carrying the first mutation was measured: the browser process started without the
/// switch and the pointer vanished while typing. Both mutations turn this file red, and so does
/// an environment created outside the helper. It reads the compiled methods, not the source.
/// </remarks>
public sealed class WebView2PointerWiringTests
{
    private const byte LdNull = 0x14;

    // By name: this project holds no reference to the WebView2 assembly, the application does.
    private static readonly Type WebView2Environment = Type.GetType(
        "Microsoft.Web.WebView2.Core.CoreWebView2Environment, Microsoft.Web.WebView2.Core",
        throwOnError: true)!;

    private static readonly MethodInfo CreateAsync = WebView2Environment.GetMethods()
        .Single(method => method.Name == "CreateAsync" && method.GetParameters().Length == 3);

    private static readonly MethodInfo Compose = typeof(WebView2Helper).GetMethod(
        nameof(WebView2Helper.EmbeddedBrowserArguments),
        BindingFlags.NonPublic | BindingFlags.Static)!;

    [Fact]
    public void EveryEnvironment_IsCreatedWithOptionsComposedFromTheEmbeddedArguments()
    {
        List<CallSite> creations = CallSitesOf(CreateAsync, HelperMethods()).ToList();

        // Two today: the shared environment and the web console's own. Fewer means the sweep
        // stopped seeing a call, and checking nothing reads as a pass.
        Assert.True(creations.Count >= 2, $"Found {creations.Count} environment creation(s) in WebView2Helper.");
        Assert.All(creations, site =>
        {
            Assert.NotEqual(LdNull, site.PrecedingOpcode);
            Assert.True(
                CallSitesOf(Compose, [site.Method]).Any(),
                $"{site.Method.DeclaringType?.Name}.{site.Method.Name} creates an environment without composing the embedded arguments.");
        });
    }

    [Fact]
    public void NoEnvironment_IsCreatedOutsideTheHelper()
    {
        IEnumerable<MethodBase> elsewhere = typeof(WebView2Helper).Assembly.GetTypes()
            .Where(type => !IsHelperOrNestedInIt(type))
            .SelectMany(MethodsOf);

        string[] callers = CallSitesOf(CreateAsync, elsewhere)
            .Select(site => $"{site.Method.DeclaringType?.FullName}.{site.Method.Name}")
            .Distinct()
            .ToArray();

        Assert.Empty(callers);
    }

    private static IEnumerable<MethodBase> HelperMethods() =>
        typeof(WebView2Helper).Assembly.GetTypes().Where(IsHelperOrNestedInIt).SelectMany(MethodsOf);

    private static bool IsHelperOrNestedInIt(Type type)
    {
        for (Type? current = type; current is not null; current = current.DeclaringType)
        {
            if (current == typeof(WebView2Helper))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<MethodBase> MethodsOf(Type type)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            | BindingFlags.Static | BindingFlags.DeclaredOnly;
        return type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all));
    }

    /// <summary>
    /// Every call or callvirt to <paramref name="target"/> in <paramref name="methods"/>, with the
    /// opcode right before it: <c>ldnull</c> there is a literal null passed as the last argument.
    /// </summary>
    private static IEnumerable<CallSite> CallSitesOf(MethodInfo target, IEnumerable<MethodBase> methods)
    {
        foreach (MethodBase method in methods)
        {
            byte[]? il = method.GetMethodBody()?.GetILAsByteArray();
            if (il is null)
            {
                continue;
            }

            for (int index = 1; index + 4 < il.Length; index++)
            {
                if (il[index] is not (0x28 or 0x6F))
                {
                    continue;
                }

                MethodBase? called;
                try
                {
                    called = method.Module.ResolveMethod(
                        BitConverter.ToInt32(il, index + 1),
                        method.DeclaringType?.GetGenericArguments(),
                        method.IsGenericMethod ? method.GetGenericArguments() : null);
                }
                catch (Exception ex) when (ex is ArgumentException or BadImageFormatException)
                {
                    // An operand byte that only looks like a call opcode resolves to nothing.
                    continue;
                }

                if (called == target)
                {
                    yield return new CallSite(method, il[index - 1]);
                }
            }
        }
    }

    private sealed record CallSite(MethodBase Method, byte PrecedingOpcode);
}
