using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.Analysis.TensorLike {
    public enum SliceIndexType {
        Slice = 0x0,
        Access = 0x1, // reduce dim
        None = 0x2, // expand dim
        Variable = Access | 0x4
    }
    public struct SliceIndex {
        public static SliceIndex All { get; } = ..; // [:] in python
        public static SliceIndex None { get; } = new(0, 1, 1, SliceIndexType.None); // [None]
        public Index Start { get; }
        public Index End { get; }
        public int Interval { get; }
        public SliceIndexType IndexType { get; }
        public SliceIndex(Index start, Index end, int interval = 1, SliceIndexType type = SliceIndexType.Slice) {
            Start = start;
            End = end;
            Interval = interval;
            IndexType = type;
        }
        public int GetLength(int totalLength) {
            return (End.GetOffset(totalLength) - Start.GetOffset(totalLength)) / Interval;
        }
        public static implicit operator SliceIndex(Range range)
            => new(range.Start, range.End);
        public static implicit operator SliceIndex(int index)
            => new(index, index + 1, 1, SliceIndexType.Access);
        public override string ToString() {
            return IndexType switch {
                SliceIndexType.Slice => $"Slice({Start}, {End}, {Interval})",
                SliceIndexType.None => "None",
                SliceIndexType.Access => $"{Start}",
                SliceIndexType.Variable => "Variable",
                _ => throw new NotImplementedException()
            };
        }
    }
    public struct TransformIndex {
        public ImmutableArray<TensorIndexExpr> Indices { get; }
        public TensorExpr BaseExpr { get; }
        public TransformIndex(ReadOnlySpan<TensorIndexExpr> indices, TensorExpr baseExpr) {
            Indices = indices.ToImmutableArray();
            BaseExpr = baseExpr;
        }
    }
    public struct TransformIndexParts {
        private TransformIndex m_TransformIndex;
        public ImmutableArray<ValueTuple<int, int>> IndexRanges { get; }
        public ImmutableArray<TensorIndexExpr> Indices => m_TransformIndex.Indices;
        public TensorExpr BaseExpr => m_TransformIndex.BaseExpr;
        public TransformIndexParts(ReadOnlySpan<ValueTuple<int, int>> indexRanges, ReadOnlySpan<TensorIndexExpr> indices, TensorExpr baseExpr) {
            IndexRanges = indexRanges.ToImmutableArray();
            m_TransformIndex = new(indices, baseExpr);
        }
    }
    public abstract class TensorCombineExpr : TensorExpr {
        protected TensorCombineExpr(ReadOnlySpan<int> shape) : base(shape) {
        }
        public abstract TransformIndexParts[] TransformIndices(ReadOnlySpan<TensorIndexExpr> indices);
        public TransformIndexParts[] ExpandIndices(ReadOnlySpan<TensorIndexExpr> indices) {
            var transformed = TransformIndices(indices);
            for(var i =0;i< transformed.Length; i++) {
                if(transformed[i].BaseExpr is TensorTransformExpr transformExpr) {
                    var oldInfo = transformed[i];
                    var subTransform = transformExpr.ExpandIndices(oldInfo.Indices.AsSpan());
                    transformed[i] = new(oldInfo.IndexRanges.AsSpan(), subTransform.Indices.AsSpan(), subTransform.BaseExpr);
                }
            }
            
            return transformed;
        }
    }
    public class TensorDynamicExpr : TensorTransformExpr {
        public TensorExpr? BaseExpression { get; set; }
        public TensorDynamicExpr(ReadOnlySpan<int> shape) : base(shape) {
        }

        public override TransformIndex TransformIndices(ReadOnlySpan<TensorIndexExpr> indices) {
            if(BaseExpression is null) throw new NullReferenceException("Dynamic expression not binded");
            return new TransformIndex(indices, BaseExpression);
        }
        public override bool Equals(TensorExpr? expr) {
            if(expr is TensorDynamicExpr dyn) {
                return dyn.BaseExpression?.Equals(BaseExpression) ?? Shape.Equals(dyn.Shape);
            }
            return false;
        }
    }
    public abstract class TensorTransformExpr : TensorExpr {
        protected TensorTransformExpr(ReadOnlySpan<int> shape) : base(shape) {
        }
        public abstract TransformIndex TransformIndices(ReadOnlySpan<TensorIndexExpr> indices);
        public TransformIndex ExpandIndices(ReadOnlySpan<TensorIndexExpr> indices) {
            var transformed = TransformIndices(indices);
            if(transformed.BaseExpr is TensorTransformExpr transformExpr) {
                return transformExpr.ExpandIndices(transformed.Indices.AsSpan());
            }
            return transformed;
        }
    }
    public abstract class TensorLeafExpr : TensorExpr {
        public abstract object? UntypedData { get; }
         
        protected TensorLeafExpr(ReadOnlySpan<int> shape) : base(shape) {
        }
        
    }
    public abstract class TensorExpr:IEquatable<TensorExpr> {
        public ImmutableArray<int> Shape { get; }
        public TensorExpr this[params SliceIndex[] slices] {
            get {
                return View(this, slices);
            }
        }
        public TensorExpr(ReadOnlySpan<int> shape) {
            Shape = shape.ToImmutableArray();
        }
        public abstract bool Equals(TensorExpr? expr);
        public TransformIndexParts[] ExpandAllIndices<TData>(ReadOnlySpan<TData> data) {
            var indices = data.ToImmutableArray()
                .Select((e, idx) => new TensorIndexVarExpr<TData>(0, Shape[idx] - 1, e)).ToArray();
            return ExpandAllIndices(indices);
        }
        public TransformIndexParts[] ExpandAllIndices(ReadOnlySpan<TensorIndexExpr> indices) {
            if(this is TensorTransformExpr transformExpr) {
                var ranges = Shape.Select(e => (0, e)).ToArray();
                var transformed = transformExpr.ExpandIndices(indices);
                return [new TransformIndexParts(ranges, transformed.Indices.AsSpan(), transformed.BaseExpr)];
            }
            if(this is TensorCombineExpr combExpr) {
                return combExpr.ExpandIndices(indices);
            }
            if(this is TensorLeafExpr leafExpr) {
                var ranges = Shape.Select(e => (0, e)).ToArray();
                return [new(ranges, indices, this)];
            }
            throw new NotImplementedException();
        }
        //protected abstract TransformIndexParts[] TransformIndices(ReadOnlySpan<TensorIndexExpr> indices);
        protected static int[] GenerateCumProduct(ReadOnlySpan<int> shape) {
            var baseShapeLength = shape.Length;
            var cumProd = new int[baseShapeLength + 1];
            cumProd[baseShapeLength] = 1;

            for(var i = baseShapeLength - 1; i >= 0; i--) {
                cumProd[i] = cumProd[i + 1] * shape[i];
            }
            return cumProd;
        }
        public static TensorExpr Flatten(TensorExpr x) {
            var totalSize = x.Shape.Aggregate((u, v) => u * v);
            return Reshape(x, [totalSize]);
        }
        public static TensorExpr Unsqueeze(TensorExpr x, int dim) {
            var finalShape = (int[])[
                .. x.Shape[..dim],
                1, .. x.Shape[dim..]];
            return Reshape(x, finalShape);
        }
        public static TensorExpr Squeeze(TensorExpr x, int[]? dims = null) {
            var dimMask = (Span<int>)stackalloc int[x.Shape.Length];
            x.Shape.CopyTo(dimMask);

            if(dims is not null) {
                foreach(var i in dims) {
                    if(dimMask[i] != 1)
                        throw new InvalidOperationException("Cannot squeeze dimension whose size != 1");
                    dimMask[i] = -1;
                }
            } else {
                for(var i = 0; i < dimMask.Length; i++) {
                    dimMask[i] = dimMask[i] == 1 ? -1 : dimMask[i];
                }
            }
            var finalShape = x.Shape.Where((e) => e != -1).ToArray();
            return Reshape(x, finalShape);
        }
        public static TensorExpr Repeat(TensorExpr x, ReadOnlySpan<int> repeats, bool isInterleaved = false) {
            Debug.Assert(x.Shape.Length >= repeats.Length);

            var repeatsArray = repeats.ToImmutableArray();
            var extendedRepeats = x.Shape.Select((e, idx) => repeatsArray.Length <= idx ? 1 : repeatsArray[idx]).ToArray();
            var newShape = x.Shape.Select((e, idx) => extendedRepeats[idx] * e).ToArray();

            return new TensorRepeatViewExpr(x, newShape, extendedRepeats, isInterleaved);
        }
        public static TensorExpr View(TensorExpr x, ReadOnlySpan<SliceIndex> slices) {
            var slicesImm = slices.ToImmutableArray();

            var addDim = slicesImm.Count(e => e.IndexType == SliceIndexType.None);
            var killDim = slicesImm.Count(e => e.IndexType == SliceIndexType.Access);

            var extendedIndices = slicesImm.Concat(Enumerable.Range(0, x.Shape.Length + addDim - slices.Length).Select(e=>SliceIndex.All)).ToArray();
            var currentDim = 0;
            var appliedDim = extendedIndices.Select(e => {
                if(e.IndexType == SliceIndexType.None) return currentDim;
                return currentDim++;
            }).ToArray();

            var shape = x.Shape;
            var newShape = extendedIndices.Select((e,idx) => {
                if(e.IndexType == SliceIndexType.Slice) return e.GetLength(shape[appliedDim[idx]]);
                if(e.IndexType == SliceIndexType.None) return 1;
                return -1;
            }).Where(e=>e>=0).ToArray();

            
            var strides = GenerateCumProduct(shape.AsSpan())[1..];
            var bias = extendedIndices.Select((e, index) => strides[appliedDim[index]] * e.Start.GetOffset(shape[appliedDim[index]]) * e.Interval).Sum();
            var newStrides = extendedIndices.Select((e, idx) => {
                if(e.IndexType == SliceIndexType.Slice)
                    return strides[appliedDim[idx]] * e.Interval;
                if(e.IndexType == SliceIndexType.None) return 0;
                return -1;
            }).Where(e=>e>=0).ToArray();

            return new TensorStridedViewExpr(x, newStrides, newShape, bias);
        }
        public static TensorExpr Reshape(TensorExpr x, ReadOnlySpan<int> shape) {
            if(x.Shape.Aggregate((u, v) => u * v) != shape.ToImmutableArray().Aggregate((u, v) => u * v)) {
                throw new ArgumentException("Tensor size mismatch");
            }
            var cumProd = GenerateCumProduct(shape);
            return new TensorStridedViewExpr(x, cumProd[1..], shape);
        }

        public static TensorExpr Permute(TensorExpr x, int[] dims) {
            if(x.Shape.Length != dims.Length) {
                throw new ArgumentException("Tensor dimension mismatch");
            }
            var shape = dims.Select(e => x.Shape[e]).ToArray();

            return new TensorPermute(x, shape, dims);
        }
        public static TensorExpr RawCombine(ReadOnlySpan<TensorExpr> expressions, ReadOnlySpan<ImmutableArray<(int left,int right)>> indicesRange) {
            var maxIndices = new int[indicesRange[0].Length];
            foreach(var i in indicesRange) {
                for(var j = 0; j < maxIndices.Length; j++) {
                    maxIndices[j] = Math.Max(maxIndices[j], i[j].right);
                }
            }
            var expressionsImm = expressions.ToImmutableArray();

            return new TensorRawCombination(maxIndices, indicesRange.ToImmutableArray().Select((e, idx) => {
                return new TensorRawCombinationInfo(expressionsImm[idx], e.AsSpan());
            }).ToArray());
        }
        public static TensorExpr Concat(TensorExpr[] expressions, int dim) {
            var pivot = expressions[0];
            var dimSize = 0;
            for(var idx =0;idx < expressions.Length;idx++) {
                var i = expressions[idx];
                if(i.Shape.Length != pivot.Shape.Length)
                    throw new InvalidOperationException("Unable to concatenate tensors of different dimensions");
                for(var j = 0; j< i.Shape.Length;j++) {
                    if(pivot.Shape[j] != i.Shape[j] && dim != j) {
                        throw new InvalidOperationException($"Tensor {idx} has different size at dimensin {j}");
                    }
                }
                dimSize += i.Shape[dim];
            }
            var newShape = pivot.Shape.ToArray();
            newShape[dim] = dimSize;

            return new TensorConcat(expressions, newShape, dim);
        }
    }

}
