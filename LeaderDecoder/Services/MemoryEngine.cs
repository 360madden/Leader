using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace LeaderDecoder.Services
{
    /// <summary>
    /// LEADER MEMORY ENGINE v1.0
    /// Low-level memory access for RIFT client data enrichment.
    /// (SKELETON: Requires validated offsets for the current RIFT patch).
    /// </summary>
    public class MemoryEngine
    {
        [DllImport("kernel32.dll")]
        public static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll")]
        public static extern bool ReadProcessMemory(int hProcess, long lpBaseAddress, byte[] lpBuffer, int dwSize, ref int lpNumberOfBytesRead);

        [DllImport("kernel32.dll")]
        public static extern bool CloseHandle(IntPtr hObject);

        private const int PROCESS_WM_READ = 0x0010;

        /// <summary>
        /// Reads a float value from a specific memory address.
        /// </summary>
        public float ReadFloat(IntPtr processHandle, long address)
        {
            byte[] buffer = new byte[sizeof(float)];
            int bytesRead = 0;
            if (ReadProcessMemory((int)processHandle, address, buffer, buffer.Length, ref bytesRead))
            {
                return BitConverter.ToSingle(buffer, 0);
            }
            return 0.0f;
        }

        /// <summary>
        /// Attempts to find the RIFT process handle for a given PID.
        /// </summary>
        public IntPtr Attach(int pid)
        {
            return OpenProcess(PROCESS_WM_READ, false, pid);
        }

        public void Detach(IntPtr handle)
        {
            if (handle != IntPtr.Zero) CloseHandle(handle);
        }
    }
}
