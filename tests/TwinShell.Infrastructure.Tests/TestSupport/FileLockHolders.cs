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
using System.Runtime.InteropServices.ComTypes;

namespace TwinShell.Infrastructure.Tests;

/// <summary>
/// Names the processes that hold a file open, through the Windows Restart Manager.
/// Used only to enrich a test failure with its cause; it reports user-mode handles,
/// so a kernel-mode scanner holding the file shows as no holder at all.
/// </summary>
internal static class FileLockHolders
{
    private const int Success = 0;
    private const int ErrorMoreData = 234;
    private const int AppNameLength = 256;
    private const int ServiceNameLength = 64;

    internal static IReadOnlyList<string> Describe(string filePath)
    {
        if (!File.Exists(filePath)
            || RmStartSession(out uint session, 0, Guid.NewGuid().ToString("N")) != Success)
        {
            return [];
        }

        try
        {
            if (RmRegisterResources(session, 1, [filePath], 0, null, 0, null) != Success)
            {
                return [];
            }

            uint count = 0;
            uint rebootReasons = 0;
            int result = RmGetList(session, out uint needed, ref count, null, ref rebootReasons);
            if (result != ErrorMoreData || needed == 0)
            {
                return [];
            }

            RmProcessInfo[] processes = new RmProcessInfo[needed];
            count = needed;
            if (RmGetList(session, out _, ref count, processes, ref rebootReasons) != Success)
            {
                return [];
            }

            return processes
                .Take((int)count)
                .Select(process => process.AppName + "(" + process.Process.ProcessId + ")")
                .ToList();
        }
        finally
        {
            _ = RmEndSession(session);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RmUniqueProcess
    {
        public int ProcessId;
        public FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RmProcessInfo
    {
        public RmUniqueProcess Process;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = AppNameLength)]
        public string AppName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = ServiceNameLength)]
        public string ServiceShortName;

        public int ApplicationType;
        public uint AppStatus;
        public uint TerminalServicesSessionId;

        [MarshalAs(UnmanagedType.Bool)]
        public bool Restartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint sessionHandle, int sessionFlags, string sessionKey);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint sessionHandle);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(
        uint sessionHandle,
        uint fileCount,
        string[] fileNames,
        uint applicationCount,
        RmUniqueProcess[]? applications,
        uint serviceCount,
        string[]? serviceNames);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(
        uint sessionHandle,
        out uint processInfoNeeded,
        ref uint processInfoCount,
        [In, Out] RmProcessInfo[]? processInfo,
        ref uint rebootReasons);
}
