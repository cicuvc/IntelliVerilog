using IntelliVerilog.Core.Analysis.TensorLike;
using IntelliVerilog.Core.DataTypes.Shape;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace IntelliVerilog.Core.DataTypes {
    public struct Size : IEquatable<Size>,IEnumerable<ShapeIndexValue> {
        public static Size Empty { get; } = new([]);
        public bool IsAllDetermined => m_Sizes.All(e => e.IsDefinite);
        private ShapeIndexValue[] m_Sizes;
        public readonly ShapeIndexValue this[int index] {
            get => m_Sizes[index];
        }
        public readonly ShapeIndexValue this[Index index] {
            get => m_Sizes[index];
        }
        public readonly ReadOnlySpan<ShapeIndexValue> this[Range index] {
            get => m_Sizes[index].AsSpan();
        }
        public int Length => m_Sizes.Length;
        public ReadOnlySpan<ShapeIndexValue> Span => m_Sizes.AsSpan();
        public Size(ReadOnlySpan<ShapeIndexValue> sizes) {
            m_Sizes = sizes.ToArray();
        }
        public bool Equals(Size other) {
            return m_Sizes.SequenceEqual(other.m_Sizes);
        }

        public override string ToString() {
            return $"({m_Sizes.Select(e=>e.ToString()).DefaultIfEmpty("").Aggregate((u,v)=>u + ", " + v)})";
        }
        public ReadOnlySpan<int> ToImmutableIntShape() {
            if(m_Sizes.Any(e => !e.IsConst)) throw new InvalidOperationException("Some dimensions not determined");
            return MemoryMarshal.Cast<ShapeIndexValue, int>(m_Sizes);
        }
        public int GetTotalBits() {
            return TensorIndexMathHelper.Product(ToImmutableIntShape());
        }
        public ReadOnlySpan<int> GetDenseStrides() {
            return TensorIndexMathHelper.CumulativeProductExclusive(ToImmutableIntShape());
        }
        public void RestrictShape(in Size targetSize) {
            if(targetSize.Length != Length) {
                throw new ArgumentException("Expect target size of same rank");
            }

            foreach(var (currentDim, targetDim) in Enumerable.Zip(m_Sizes, targetSize.m_Sizes)) {
                ShapeEquals.CreateExpression([currentDim, targetDim]);
            }
        }
        public IEnumerator<ShapeIndexValue> GetEnumerator() => ((IEnumerable<ShapeIndexValue>)m_Sizes).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() {
            return m_Sizes.GetEnumerator(); 
        }
    }
}
