using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Immutable;

namespace IntelliVerilog.Core.Analysis.TensorLike {
    public static class TensorOpHelpers {
        public static ReadOnlySpan<int> GetShapeOpView(ReadOnlySpan<int> oldShape, ReadOnlySpan<SliceIndex> slices) {
            var slicesImm = slices.ToImmutableArray();

            var addDim = slicesImm.Count(e => e.IndexType == SliceIndexType.None);
            var killDim = slicesImm.Count(e => e.IndexType == SliceIndexType.Access);

            var extendedIndices = slicesImm.Concat(Enumerable.Range(0, oldShape.Length + addDim - slices.Length).Select(e => SliceIndex.All)).ToArray();
            var currentDim = 0;
            var appliedDim = extendedIndices.Select(e => {
                if(e.IndexType == SliceIndexType.None) return currentDim;
                return currentDim++;
            }).ToArray();

            var shape = oldShape.ToImmutableArray();
            var newShape = extendedIndices.Select((e, idx) => {
                if(e.IndexType == SliceIndexType.Slice) return e.GetLength(shape[appliedDim[idx]]);
                if(e.IndexType == SliceIndexType.None) return 1;
                return -1;
            }).Where(e => e >= 0).ToArray();

            return newShape;
        }
    }
}
