using Microsoft.Win32.SafeHandles;
using System;
using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace ClashDetector
{
    public static class NamedPipeHelper
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_FLAG_OVERLAPPED = 0x40000000;

        public static bool IsPipeBusy(string pipeName)
        {
            string fullPipeName = @"\\.\pipe\" + pipeName;
            SafeFileHandle handle = CreateFile(
                fullPipeName,
                GENERIC_READ | GENERIC_WRITE,
                0,
                IntPtr.Zero,
                OPEN_EXISTING,
                FILE_FLAG_OVERLAPPED,
                IntPtr.Zero);

            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                if (error == 231) // ERROR_PIPE_BUSY
                {
                    return true;
                }
            }
            else
            {
                CloseHandle(handle.DangerousGetHandle());
            }

            return false;
        }

        public static void TerminatePipe(string pipeName)
        {
            string fullPipeName = @"\\.\pipe\" + pipeName;
            SafeFileHandle handle = CreateFile(
                fullPipeName,
                GENERIC_READ | GENERIC_WRITE,
                0,
                IntPtr.Zero,
                OPEN_EXISTING,
                FILE_FLAG_OVERLAPPED,
                IntPtr.Zero);

            if (!handle.IsInvalid)
            {
                CloseHandle(handle.DangerousGetHandle());
            }
        }
    }
}

