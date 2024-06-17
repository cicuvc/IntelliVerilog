using IntelliVerilog.Core.Analysis;
using IntelliVerilog.Core.Runtime.Unsafe;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.Utils {
    public enum BitRegionState {
        False = 0, True = 1, Mix = 2

    }
    public class Bitset:IDisposable,IEquatable<Bitset> {
        private const int m_ElementBits = 64;
        private MemoryRegion<ulong> m_Data;
        private bool m_DisposedValue;
        private int m_TotalBits;

        public int TotalBits => m_TotalBits;

        public Bitset(int bits) {
            m_TotalBits = bits;
            m_Data = MemoryAPI.API.Alloc<ulong>((uint)Math.Ceiling(1.0 * bits / m_ElementBits));
            for (var i = 0u; i < m_Data.ElementLength; i++) m_Data[i] = 0;
        }
        public Bitset Clone() {
            var newSet = new Bitset(TotalBits);
            for(var i = 0u; i < m_Data.ElementLength; i++) {
                newSet.m_Data[i] = m_Data[i];
            }
            return newSet;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong GetMask(int length) {
            return (length >= m_ElementBits ? 0: (1ul<<length)) - 1;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong GetElemRange(ulong value, int start, int end,out ulong mask) {
            mask = GetMask(end - start);
            return (value >> start) & mask;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SetElemRange(ref ulong value, int start, int end, bool en) {
            var mask = GetMask(end - start);
            if (en) value |= mask << start;
            else value &= ~(mask << start);
        }
        public bool this[Index index] {
            get {
                var start = index.GetOffset(m_TotalBits);
                var startElem = start / m_ElementBits;
                return ((m_Data[startElem] >> (start % m_ElementBits)) & 1) != 0;
            }
            set {
                var start = index.GetOffset(m_TotalBits);
                var startElem = start / m_ElementBits;
                if (value) {
                    m_Data[startElem] |= 1ul << startElem;
                } else {
                    m_Data[startElem] &= ~(1ul << startElem);
                }
            }
        }
        public void InplaceAnd(Bitset bitset) {
            Debug.Assert(bitset.TotalBits == TotalBits);

            for(var i=0u;i< m_Data.ElementLength; i++) {
                m_Data[i] &= bitset.m_Data[i];
            }
        }

        public BitRegionState this[SpecifiedRange range] {
            get {
                var start = range.Left;
                var end = range.Right;

                Debug.Assert(start < end);

                var startElem = start / m_ElementBits;
                var endElem = 1 + (end - 1) / m_ElementBits;

                var endOffset = startElem == endElem - 1 ? 1 + ((end - 1) % m_ElementBits) : m_ElementBits;
                var firstElem = GetElemRange(m_Data[startElem++], start % m_ElementBits, endOffset, out var mask);
                var state = firstElem == mask ? BitRegionState.True : (firstElem == 0 ? BitRegionState.False : BitRegionState.Mix);

                for(var i  = startElem; i < endElem && state != BitRegionState.Mix; i++) {
                    var endBit = i == endElem - 1 ? 1 + ((end - 1) % m_ElementBits) : 64;
                    var elem = GetElemRange(m_Data[i], 0, endBit, out mask);

                    if (mask != elem && elem != 0) return BitRegionState.Mix;
                    if((state == BitRegionState.True ^ (elem != 0))) return BitRegionState.Mix;
                }

                return state;
            }
            set {
                Debug.Assert(value != BitRegionState.Mix);

                var en = value == BitRegionState.True;

                var start = range.Left;
                var end = range.Right;

                Debug.Assert(start < end);

                var startElem = start / m_ElementBits;
                var endElem = 1 + (end - 1) / m_ElementBits;

                var endOffset = startElem == endElem - 1 ? 1 + ((end - 1) % m_ElementBits) : m_ElementBits;
                SetElemRange(ref m_Data[startElem++], start % m_ElementBits, endOffset, en);
                
                for (var i = startElem; i < endElem; i++) {
                    var endBit = i == endElem - 1 ? 1 + ((end - 1) % m_ElementBits) : 64;
                    SetElemRange(ref m_Data[i], 0, endBit, en);
                }

            }
        }
        protected virtual void Dispose(bool disposing) {
            if (!m_DisposedValue) {
                if (disposing) {
                }

                MemoryAPI.API.Free(m_Data);
                m_DisposedValue = true;
            }
        }
        ~Bitset() => Dispose(disposing: false);
        public void Dispose() {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
        public override string ToString() {
            scoped var sb = (Span<char>)stackalloc char[m_TotalBits];
            for(var i = 0; i < m_TotalBits; i += m_ElementBits) {
                var value = m_Data[i / m_ElementBits];
                for(var j=0;j < m_ElementBits && i+j < m_TotalBits; j++, value >>= 1) {
                    sb[i + j] = ((value & 1) != 0) ? '1' : '0';
                }
            }
            return new string(sb);
        }

        public bool Equals(Bitset? other) {
            if (other?.m_TotalBits != m_TotalBits) return false;
            for(var i = 0u; i < m_Data.ElementLength; i++) {
                if (m_Data[i] != other.m_Data[i]) return false;
            }
            return true;
        }
    }
}
