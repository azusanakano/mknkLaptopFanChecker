using System;
using System.Runtime.InteropServices;

namespace Mknk.LaptopFanChecker
{
    internal static class CpuTopologyReader
    {
        private const int RelationProcessorCore = 0;
        private const int RelationProcessorPackage = 3;
        private const ushort AllProcessorGroups = 0xffff;
        private const int ErrorInsufficientBuffer = 122;

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetLogicalProcessorInformationEx(int relationship, IntPtr buffer, ref uint returnedLength);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        private static extern uint GetActiveProcessorCount(ushort groupNumber);

        public static void Populate(SystemProfile profile)
        {
            // Count Windows-visible packages/cores across the entire system, not process affinity.
            profile.CpuSocketCount = ReadRelationshipCount(RelationProcessorPackage);
            profile.CpuCoreCount = ReadRelationshipCount(RelationProcessorCore);
            profile.CpuLogicalProcessorCount = ReadLogicalProcessorCount();
        }

        private static int? ReadRelationshipCount(int relationship)
        {
            try
            {
                uint length = 0;
                GetLogicalProcessorInformationEx(relationship, IntPtr.Zero, ref length);
                if (Marshal.GetLastWin32Error() != ErrorInsufficientBuffer || length == 0)
                    return null;

                // The topology can grow between querying its size and reading it.
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    IntPtr buffer = Marshal.AllocHGlobal(checked((int)length));
                    try
                    {
                        uint returnedLength = length;
                        if (GetLogicalProcessorInformationEx(relationship, buffer, ref returnedLength))
                        {
                            if (returnedLength > length)
                                return null;
                            byte[] data = new byte[checked((int)returnedLength)];
                            Marshal.Copy(buffer, data, 0, data.Length);
                            return CountRecords(data, relationship);
                        }
                        if (Marshal.GetLastWin32Error() != ErrorInsufficientBuffer || returnedLength == 0)
                            return null;
                        length = returnedLength;
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                    }
                }
            }
            catch
            {
                // Keep unavailable counts unknown rather than substituting process-local counts.
            }
            return null;
        }

        internal static int? CountRecords(byte[] data, int relationship)
        {
            if (data == null)
                return null;
            int count = 0;
            int offset = 0;
            while (offset < data.Length)
            {
                if (data.Length - offset < 8)
                    return null;
                int type = BitConverter.ToInt32(data, offset);
                uint size = BitConverter.ToUInt32(data, offset + 4);
                if (size < 8 || size > data.Length - offset)
                    return null;
                if (type == relationship)
                    count++;
                offset += (int)size;
            }
            return count > 0 ? (int?)count : null;
        }

        private static int? ReadLogicalProcessorCount()
        {
            try
            {
                uint count = GetActiveProcessorCount(AllProcessorGroups);
                return count > 0 ? (int?)checked((int)count) : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
