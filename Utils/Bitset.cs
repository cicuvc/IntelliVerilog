using IntelliVerilog.Core.Analysis;
using IntelliVerilog.Core.Analysis.TensorLike;
using IntelliVerilog.Core.DataTypes;
using IntelliVerilog.Core.DataTypes.Shape;
using IntelliVerilog.Core.Runtime.Unsafe;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.Utils {
    public sealed class BitsetND : IDisposable {
        private bool m_DisposedValue;
        private Size m_Shape;
        private MemoryRegion<byte> m_Data;
        public Size Shape => m_Shape;
        public BitsetND(Size shape) {
            if(!shape.IsAllDetermined) {
                throw new ArgumentException("Shape should be all determined");
            }

            var totalBits = shape.GetTotalBits();
            
            m_Shape = shape;
            m_Data = MemoryAPI.API.Alloc<byte>((uint)totalBits);
            m_Data.Memset(0);
        }
        public void SetRangeValue(ReadOnlySpan<GenericIndex> slices, bool value) {
            foreach(var i in ShapeEvaluation.GetStepper(m_Shape, slices.ToImmutableArray())) {
                m_Data[i] = (byte)(value ? 1 : 0);
            }
        }
        public bool ContainsNonZeroRange(ReadOnlySpan<GenericIndex> slices) {
            foreach(var i in ShapeEvaluation.GetStepper(m_Shape, slices.ToImmutableArray())) {
                if(m_Data[i] != 0) return true;
            }
            return false;
        }
        public bool ContainsZeroRange(ReadOnlySpan<GenericIndex> slices) {
            foreach(var i in ShapeEvaluation.GetStepper(m_Shape, slices.ToImmutableArray())) {
                if(m_Data[i] == 0) return true;
            }
            return false;
        }
        public void InplaceAnd(BitsetND bitset) {
            if(!bitset.m_Shape.Equals(m_Shape)) {
                throw new ArgumentException($"Unable to inplace logical-and on different shape {m_Shape} and {bitset.m_Shape}");
            }

            var currentPtr = 0ul;
            var srcMemory = bitset.m_Data;
            var dstMemory = m_Data;
            if(Avx2.IsSupported) {
                unsafe {
                    var alignBound = m_Data.ByteLength & ~0x1Ful;
                    var dstPtr = dstMemory.AsUnmanagedPtr();
                    var srcPtr = srcMemory.AsUnmanagedPtr();
                    for(; currentPtr < alignBound; currentPtr += 32) {
                        var dst = Avx.LoadDquVector256(dstPtr + currentPtr);
                        var src = Avx.LoadDquVector256(srcPtr + currentPtr);
                        dst = Avx2.And(dst, src);
                        Avx.StoreAligned(dstMemory.AsUnmanagedPtr() + currentPtr, dst);
                    }
                }
            }
            for(; currentPtr < dstMemory.ByteLength; currentPtr++) {
                dstMemory[currentPtr] &= srcMemory[currentPtr];
            }
        }
        private void Dispose(bool disposing) {
            if(!m_DisposedValue) {
                if(disposing) {}

                MemoryAPI.API.Free(m_Data);
                m_DisposedValue = true;
            }
        }
        public override string ToString() {
            var sb = new StringBuilder();
            BuildString(sb, 0, 0, 0);
            return sb.ToString();
        }
        private void BuildString(StringBuilder sb, int indent, int offset, int currentRank) {
            var elementLength = m_Shape[currentRank].Value;
            if(currentRank + 1 == m_Shape.Length) {
                sb.Append(' ', indent);
                sb.Append('[');
                for(var i = 0; i < elementLength; i++) {
                    

                    sb.Append(m_Data[offset + i] == 0 ? '0' : '1');
                    if(i!= elementLength - 1) {
                        sb.Append(',');
                    }
                    if(i % 32 == 31 && i + 1 != elementLength) {
                        sb.Append(Environment.NewLine);
                        sb.Append(' ', indent + 1);
                    }
                }

                sb.Append(']');
            } else {
                sb.Append(' ', indent);
                sb.Append('[');
                sb.Append(Environment.NewLine);

                var stride = 1;
                for(var i = currentRank + 1; i < m_Shape.Length; i++) {
                    stride *= m_Shape[i].Value;
                }
                for(var i = 0; i < elementLength; i++) {
                    BuildString(sb, indent + 4, offset + stride * i, currentRank + 1);
                    sb.Append(',');
                    sb.Append(Environment.NewLine);
                }


                sb.Append(Environment.NewLine);
                sb.Append(' ', indent);
                sb.Append(']');
            }
        }
        ~BitsetND() {
            Dispose(disposing: false);
        }

        public void Dispose() {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
    
}
