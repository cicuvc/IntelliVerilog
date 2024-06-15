using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.Runtime.Unsafe {
    public unsafe struct MemoryRegion<T> where T : unmanaged {
        private T* m_Buffer;
        private ulong m_Length;
        public T* Address { get => m_Buffer; }
        public ulong ByteLength => m_Length;
        public readonly ulong ElementLength => m_Length / (uint)sizeof(T);
        public MemoryRegion(void* buffer, ulong len) {
            m_Buffer = (T*)buffer;
            m_Length = len;
        }
        public ref T this[long index] {
            get => ref m_Buffer[index];
            //set => m_Buffer[index] = value;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public void CopyTo<V>(MemoryRegion<V> destination) where V : unmanaged {
            Debug.Assert(destination.ByteLength == ByteLength);

            Buffer.MemoryCopy(m_Buffer, destination.m_Buffer, m_Length, destination.ByteLength);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public void CopyTo<V>(MemoryRegion<V> destination, ulong srcOffset, ulong dstOffset, ulong size) where V : unmanaged {
            Debug.Assert(destination.ByteLength >= dstOffset + size);
            Debug.Assert(ByteLength >= srcOffset + size);

            Buffer.MemoryCopy((byte*)m_Buffer + srcOffset, (byte*)destination.m_Buffer + dstOffset, size, size);
        }
        public void Free() => MemoryAPI.API.Free(m_Buffer);
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public MemoryRegion<V> AsRegion<V>() where V : unmanaged {
            return new MemoryRegion<V>(m_Buffer, m_Length);
        }
        public T* AsUnmanagedPtr() => (T*)m_Buffer;
        public static implicit operator IntPtr(MemoryRegion<T> region) => (IntPtr)region.m_Buffer;
    }
}
