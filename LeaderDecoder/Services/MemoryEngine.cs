using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace LeaderDecoder.Services
{
    /// <summary>
    /// LEADER MEMORY ENGINE v2.0
    /// Low-level memory access for RIFT client data enrichment.
    /// Traverses dynamic multi-level pointers with silent exception handling.
    /// </summary>
    public class MemoryEngine
    {
        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll")]
        private static extern bool ReadProcessMemory(int hProcess, long lpBaseAddress, byte[] lpBuffer, int dwSize, ref int lpNumberOfBytesRead);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr hObject);

        private const int PROCESS_WM_READ = 0x0010;

        /// <summary>
        /// Safely reads raw bytes from memory. Fails silently.
        /// </summary>
        private byte[] ReadMemory(IntPtr processHandle, long address, int size)
        {
            byte[] buffer = new byte[size];
            int bytesRead = 0;
            if (processHandle != IntPtr.Zero && address > 0)
            {
                ReadProcessMemory((int)processHandle, address, buffer, size, ref bytesRead);
            }
            return buffer;
        }

        public float ReadFloat(IntPtr processHandle, long address)
        {
            return BitConverter.ToSingle(ReadMemory(processHandle, address, sizeof(float)), 0);
        }

        public int ReadInt32(IntPtr processHandle, long address)
        {
            return BitConverter.ToInt32(ReadMemory(processHandle, address, sizeof(int)), 0);
        }

        public byte ReadByte(IntPtr processHandle, long address)
        {
            return ReadMemory(processHandle, address, 1)[0];
        }

        public string ReadString(IntPtr processHandle, long address, int maxLength = 32)
        {
            byte[] buffer = ReadMemory(processHandle, address, maxLength);
            string result = Encoding.UTF8.GetString(buffer);
            int nullIdx = result.IndexOf('\0');
            return nullIdx >= 0 ? result.Substring(0, nullIdx) : result;
        }

        /// <summary>
        /// Reads a multi-level pointer. Returns the absolute resolved address or 0 if it breaks.
        /// </summary>
        public long ReadMultiLevelPointer(IntPtr processHandle, long baseAddress, int[] offsets)
        {
            long currentAddress = baseAddress;
            for (int i = 0; i < offsets.Length; i++)
            {
                if (currentAddress == 0) return 0; // Null pointer deref guard

                // Read the 64-bit pointer address out of the current location
                byte[] buffer = ReadMemory(processHandle, currentAddress, sizeof(long));
                long nextHop = BitConverter.ToInt64(buffer, 0);

                if (nextHop == 0) return 0;
                
                // Add the offset for the next step (or final destination)
                currentAddress = nextHop + offsets[i];
            }
            return currentAddress;
        }

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
