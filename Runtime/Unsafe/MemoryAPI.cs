using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.Runtime.Unsafe {
    public enum ProtectMode {
        Unknown = 0,
        Read = 0x01,
        Write = 0x02,
        Execute = 0x04,
        NoAccess = 0x8,
        Guard = 0x10,
    }
    public abstract class MemoryAPI {
        private readonly static MemoryAPI m_API;
        public static MemoryAPI API { get => m_API; }
        static MemoryAPI() {
            switch (Environment.OSVersion.Platform) {
                case PlatformID.Win32NT: {
                    m_API = new WindowsMemoryAPI();
                    break;
                }
                default:
                    throw new NotSupportedException();
            }
        }


        public unsafe MemoryRegion<byte> Alloc(ulong length) {
            var mi = new MemoryRegion<byte>((void*)InternalAlloc(length), length);
            return mi;
        }
        public abstract IntPtr InternalAlloc(ulong length);
        public abstract MemoryRegion<byte> AllocAlign(ulong alignment, ulong length);
        public abstract ProtectMode SetProtection(MemoryRegion<byte> address, ProtectMode mode);
        public abstract void InternalFree(IntPtr ptr);
        public unsafe void Free<T>(T* pointer) where T : unmanaged {
            InternalFree((IntPtr)pointer);
        }
        public unsafe void Free(void* pointer) {
            InternalFree((IntPtr)pointer);
        }
        public unsafe void Free(IntPtr pointer) {
            InternalFree(pointer);
        }
        public unsafe void Free(MemoryRegion<byte> mi) {
            InternalFree((IntPtr)mi.Address);
        }
        public void PrintMemoryAllocationInfo() {
        }

        protected abstract MemoryRegion<byte> InternalResize(MemoryRegion<byte> memory, ulong newLength);
        public unsafe MemoryRegion<byte> Resize(MemoryRegion<byte> memory, ulong newLength) {
            return InternalResize(memory, newLength);
        }
        public unsafe MemoryRegion<T> Alloc<T>() where T : unmanaged {
            var mi = new MemoryRegion<T>((void*)InternalAlloc((ulong)sizeof(T)), (ulong)sizeof(T));
            return mi;
        }
        public unsafe MemoryRegion<T> Alloc<T>(uint count) where T : unmanaged {
            var mi = new MemoryRegion<T>((void*)InternalAlloc((ulong)sizeof(T) * count), (ulong)sizeof(T) * count);
            return mi;
        }
    }
    class WindowsMemoryAPI : MemoryAPI {
        const int PAGE_READONLY = 0x02;
        const int PAGE_READWRITE = 0x04;
        const int PAGE_EXECUTE = 0x10;
        const int PAGE_EXECUTE_READ = 0x20;
        const int PAGE_EXECUTE_READWRITE = 0x40;
        static readonly Dictionary<ProtectMode, int> c_FlagTable = new Dictionary<ProtectMode, int>() {
            { ProtectMode.Read,PAGE_READONLY},
            { ProtectMode.Execute,PAGE_EXECUTE},
            { ProtectMode.Read|ProtectMode.Write,PAGE_READWRITE},
            { ProtectMode.Execute|ProtectMode.Read,PAGE_EXECUTE_READ},
            { ProtectMode.Execute|ProtectMode.Read|ProtectMode.Write,PAGE_EXECUTE_READWRITE},
            { ProtectMode.NoAccess, 0x1 },
            {ProtectMode.Guard,0x100  | 0x04}
        };
        static readonly Dictionary<int, ProtectMode> c_InvFlagTable;
        static WindowsMemoryAPI() {
            c_InvFlagTable = c_FlagTable.ToDictionary(k => k.Value, v => v.Key);
        }
        [DllImport("kernel32", CallingConvention = CallingConvention.StdCall)]
        public static extern int VirtualProtect(IntPtr dwAddr, ulong size, int protectMode, out int oldProtect);
        [DllImport("libmimalloc", EntryPoint = "mi_malloc")]
        public static extern IntPtr Mi_malloc(ulong length);
        [DllImport("libmimalloc", EntryPoint = "mi_free")]
        public static extern void Mi_free(IntPtr ptr);
        [DllImport("libmimalloc", EntryPoint = "mi_aligned_alloc")]
        public static extern IntPtr Mi_align(ulong align, ulong length);
        [DllImport("libmimalloc", EntryPoint = "mi_realloc")]
        public static extern IntPtr Mi_realloc(IntPtr ptr, ulong newSize);
        public override IntPtr InternalAlloc(ulong length) {
            return (Mi_malloc(length));
        }

        public unsafe override MemoryRegion<byte> AllocAlign(ulong alignment, ulong length) {
            return new MemoryRegion<byte>((void*)Mi_align(alignment, length), length);
        }
        protected unsafe override MemoryRegion<byte> InternalResize(MemoryRegion<byte> memory, ulong newLength) {
            return new MemoryRegion<byte>((void*)Mi_realloc((IntPtr)memory.Address, newLength), newLength);
        }

        public unsafe override ProtectMode SetProtection(MemoryRegion<byte> address, ProtectMode mode) {
            if (mode == ProtectMode.Unknown) mode = ProtectMode.Read | ProtectMode.Write;
            if (!c_FlagTable.ContainsKey(mode)) {
                throw new ArgumentException("Invalid Protection Flags");
            }

            VirtualProtect((IntPtr)address.Address, address.ByteLength, c_FlagTable[mode], out var old_flag);
            if (!c_InvFlagTable.ContainsKey(old_flag)) return ProtectMode.Unknown;
            return c_InvFlagTable[old_flag];
        }
        public override void InternalFree(IntPtr ptr) {
            Mi_free(ptr);
        }
    }
}
