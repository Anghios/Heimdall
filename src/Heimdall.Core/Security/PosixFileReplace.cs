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
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Heimdall.Core.Security;

/// <summary>
/// Renames a file over an existing one with POSIX semantics: the replace succeeds while another
/// process holds the destination open, as long as that process allowed it to be deleted.
/// </summary>
/// <remarks>
/// <see cref="File.Move(string, string, bool)"/> goes through MoveFileEx, which refuses to replace a
/// destination that has ANY open handle, even one opened with every sharing flag, and reports it as
/// "Access to the path is denied". A virus scanner, an indexer, a backup or a sync client reading the
/// file at the wrong instant is enough to fail a save. With POSIX semantics the reader keeps the
/// content it opened, and the name points at the new file at once.
/// <para>
/// A holder that refused delete sharing still blocks the replace, and is reported as the sharing
/// violation it is rather than as an access denial. A read-only or delete-pending destination still
/// fails as access denied.
/// </para>
/// </remarks>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal static class PosixFileReplace
{
    private const uint DeleteAccess = 0x00010000;
    private const uint SynchronizeAccess = 0x00100000;
    private const uint OpenExisting = 3;
    private const int FileRenameInfoExClass = 22;
    private const uint ReplaceIfExistsFlag = 0x1;
    private const uint PosixSemanticsFlag = 0x2;

    private const int ErrorInvalidFunction = 1;
    private const int ErrorFileNotFound = 2;
    private const int ErrorPathNotFound = 3;
    private const int ErrorAccessDenied = 5;
    private const int ErrorNotSupported = 50;
    private const int ErrorInvalidParameter = 87;

    /// <summary>
    /// Renames <paramref name="sourcePath"/> over <paramref name="destinationPath"/>.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when the volume does not support a POSIX rename (older Windows, FAT,
    /// some network shares); nothing has been renamed then, and the caller uses the classic rename.
    /// </returns>
    /// <exception cref="UnauthorizedAccessException">The rename was denied.</exception>
    /// <exception cref="IOException">The rename failed, e.g. on a sharing violation.</exception>
    internal static bool TryReplace(string sourcePath, string destinationPath)
    {
        string source = Path.GetFullPath(sourcePath);
        string destination = Path.GetFullPath(destinationPath);

        using SafeFileHandle handle = CreateFileW(
            source,
            DeleteAccess | SynchronizeAccess,
            FileShare.ReadWrite | FileShare.Delete,
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            throw ErrorToException(Marshal.GetLastPInvokeError(), source);
        }

        byte[] name = Encoding.Unicode.GetBytes(destination);

        // FILE_RENAME_INFO: ULONG Flags, then HANDLE RootDirectory at pointer alignment, then
        // DWORD FileNameLength, then the WCHAR name; the size includes one terminating WCHAR.
        int rootOffset = IntPtr.Size;
        int lengthOffset = rootOffset + IntPtr.Size;
        int nameOffset = lengthOffset + sizeof(uint);
        int size = nameOffset + name.Length + sizeof(char);

        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.Copy(new byte[size], 0, buffer, size);
            Marshal.WriteInt32(buffer, 0, unchecked((int)(ReplaceIfExistsFlag | PosixSemanticsFlag)));
            Marshal.WriteIntPtr(buffer, rootOffset, IntPtr.Zero);
            Marshal.WriteInt32(buffer, lengthOffset, name.Length);
            Marshal.Copy(name, 0, buffer + nameOffset, name.Length);

            if (SetFileInformationByHandle(handle, FileRenameInfoExClass, buffer, (uint)size))
            {
                return true;
            }

            int error = Marshal.GetLastPInvokeError();
            if (error is ErrorInvalidParameter or ErrorNotSupported or ErrorInvalidFunction)
            {
                return false;
            }

            throw ErrorToException(error, destination);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static Exception ErrorToException(int error, string path)
    {
        string message = Marshal.GetPInvokeErrorMessage(error) + " : '" + path + "'";
        return error switch
        {
            ErrorAccessDenied => new UnauthorizedAccessException(message),
            ErrorFileNotFound => new FileNotFoundException(message, path),
            ErrorPathNotFound => new DirectoryNotFoundException(message),
            _ => new IOException(message, HResultFromWin32(error)),
        };
    }

    private static int HResultFromWin32(int error) => unchecked((int)0x80070000 | (error & 0xFFFF));

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        FileShare shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(
        SafeFileHandle file,
        int fileInformationClass,
        IntPtr fileInformation,
        uint bufferSize);
}
