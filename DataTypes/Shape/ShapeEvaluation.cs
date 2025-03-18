using IntelliVerilog.Core.Analysis.TensorLike;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.DataTypes.Shape {
    public static class ShapeEvaluation {
        public class ShapeViewDetails {
            /// <summary>
            /// Extended slices to assign slices on non-specified indices
            /// Example: ndarray A of shape [4,4,4] accessed by slices [2:3,2], got extended slices [2:3,2,:]
            /// </summary>
            public ImmutableArray<SliceIndex> ExtendedSlices { get; }
            /// <summary>
            /// Specify dimensions of source ndarray to be applied by each extended slices
            /// Example: ndarray A of shape [4,4,4] access by slices [1, None, 2], which got extended slices
            /// [1, None, 2, :], applied dims is [0, 0 (None), 1, 2]
            /// </summary>
            public ImmutableArray<int> AppliedDim { get; }
            /// <summary>
            /// Indicates each dimensions of final shape comes from which accessor
            /// </summary>
            public ImmutableArray<int> FromSlice { get; }
            /// <summary>
            /// Final shape of view operation
            /// </summary>
            public ImmutableArray<int> ResultShape { get; }
            public ShapeViewDetails(ReadOnlySpan<SliceIndex> extSlices, ReadOnlySpan<int> appliedDim, ReadOnlySpan<int> fromSlice, ReadOnlySpan<int> resultShape) {
                ExtendedSlices = extSlices.ToImmutableArray();
                AppliedDim = appliedDim.ToImmutableArray();
                FromSlice = fromSlice.ToImmutableArray();
                ResultShape = resultShape.ToImmutableArray();
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static IEnumerable<int> GetStepper(Size originShape, ImmutableArray<GenericIndex> slices) {

            var pureSliceForm = slices.ToImmutableArray().Select(e =>
                e.Type == SliceIndexType.Variable ? throw new ArgumentException("Access bitset with variable index")
                : e.ConstIndex).ToArray();
            var details = ShapeEvaluation.ViewDetails(originShape, pureSliceForm);
            var resultShape = details.ResultShape;
            var totalElements = TensorIndexMathHelper.Product(details.ResultShape.AsSpan());
            var resultStrides = TensorIndexMathHelper.CumulativeProductExclusive(details.ResultShape.AsSpan());

            var strides = originShape.GetDenseStrides().ToImmutableArray();

            var baseOffset = details.AppliedDim.Select((e, idx) => details.ExtendedSlices[idx].Start.GetOffset(originShape[e].Value) * strides[e]).Sum();
            var counter = new int[resultShape.Length];
            var counterPtr = resultShape.Length - 1;

            var currentPtr = baseOffset;
            var appliedDim = details.AppliedDim;
            var fromSliceIdx = details.FromSlice;

            var newShapeStrideMap = new int[fromSliceIdx.Length];
            for(var i = 0; i < fromSliceIdx.Length; i++) {
                newShapeStrideMap[i] = strides[appliedDim[fromSliceIdx[i]]];
            }

            for(var i = 0; i < totalElements; i++) {
                yield return currentPtr;

                for(var j = 0; j < counter.Length; j++) {
                    Console.Write($"{counter[j]}, ");
                }
                Console.WriteLine();

                while(counterPtr >= 1 && counter[counterPtr] + 1 >= resultShape[counterPtr])
                    counterPtr--;

                if(counterPtr >= 0) {
                    counter[counterPtr]++;
                    currentPtr += newShapeStrideMap[counterPtr++];
                    for(; counterPtr < counter.Length; counterPtr++) {
                        currentPtr -= newShapeStrideMap[counterPtr] * counter[counterPtr];
                        counter[counterPtr] = 0;
                    }
                    counterPtr--;
                }
            }
        }
        public static Size View(Size baseShape, ReadOnlySpan<GenericIndex> slices) {
            return View(baseShape, slices.ToImmutableArray().Select(e => e.ToErased()).ToArray());
        }
        public static Size View(Size baseShape, ReadOnlySpan<SliceIndex> slices) {
            var slicesImm = slices.ToImmutableArray();

            var addDim = slicesImm.Count(e => e.IndexType == SliceIndexType.None);
            var killDim = slicesImm.Count(e => e.IndexType.HasFlag(SliceIndexType.Access));

            var extendedIndices = slicesImm.Concat(Enumerable.Range(0, baseShape.Length + addDim - slices.Length).Select(e => SliceIndex.All)).ToArray();
            var currentDim = 0;
            var appliedDim = extendedIndices.Select(e => {
                if(e.IndexType == SliceIndexType.None) return currentDim;
                return currentDim++;
            }).ToArray();

            var newShape = extendedIndices.Select((e, idx) => {
                if(e.IndexType == SliceIndexType.Slice) return ShapeInterval.CreateExpression(baseShape[appliedDim[idx]], new(e));
                if(e.IndexType == SliceIndexType.None) return new ShapeIndexValue(1);
                return ShapeIndexValue.Invalid;
            }).Where(e => e.IsValid).ToArray();

            return new(newShape);
        }
        public static ShapeViewDetails ViewDetails(Size baseShape, ReadOnlySpan<SliceIndex> slices) {
            var slicesImm = slices.ToImmutableArray();

            var addDim = slicesImm.Count(e => e.IndexType == SliceIndexType.None);
            var killDim = slicesImm.Count(e => e.IndexType.HasFlag(SliceIndexType.Access));

            var extendedIndices = slicesImm.Concat(Enumerable.Range(0, baseShape.Length + addDim - slices.Length).Select(e => SliceIndex.All)).ToArray();
            var currentDim = 0;
            var appliedDim = extendedIndices.Select(e => {
                if(e.IndexType == SliceIndexType.None) return currentDim;
                return currentDim++;
            }).ToArray();

            var fromSlices = extendedIndices.Select((e,idx) => {
                if(e.IndexType == SliceIndexType.Access) return -1;
                return idx;
            }).Where(e=>e>=0).ToArray();

            var newShape = extendedIndices.Select((e, idx) => {
                if(e.IndexType == SliceIndexType.Slice) return e.GetLength(baseShape[appliedDim[idx]].Value);
                if(e.IndexType == SliceIndexType.None) return 1;
                return -1;
            }).Where(e=>e>=0).ToArray();

            return new ShapeViewDetails(extendedIndices, appliedDim, fromSlices, newShape);
        }
        public static Size Concat(ReadOnlySpan<Size> expressions, int dim) {
            var pivot = expressions[0];
            var possibleRange = pivot.Span.ToImmutableArray().Select(e => e.Range).ToArray();

            // Find common size on non-axis dimensions
            for(var idx = 0; idx < expressions.Length; idx++) {
                var i = expressions[idx];
                if(i.Length != pivot.Length)
                    throw new InvalidOperationException("Unable to concatenate tensors of different dimensions");

                for(var j = 0; j < i.Length; j++) {
                    if(dim != j) {
                        possibleRange[j] &= i[j].Range;
                        if(possibleRange[j].IsImpossible) {
                            throw new InvalidOperationException($"Impossible to find a consistent size at dim {j} for tensor {idx} ");
                        }
                    }
                }
            }

            // propagation on non-axis dimensions
            for(var idx = 0; idx < expressions.Length; idx++) {
                var i = expressions[idx];
                for(var j = 0; j < i.Length; j++) {
                    if(dim != j) i[j].Expression?.PropagateValue(possibleRange[j]);
                }
            }

            var dimSize = ShapeAddition.CreateExpression(expressions.ToImmutableArray().Select(e => e[dim]).ToArray());

            var newShape = pivot.Span.ToArray();
            newShape[dim] = dimSize;

            return new(newShape);
        }

        public static Size BinaryOperatorVec(Size lhsVecShape, Size rhsVecShape, out int lhsExpand, out int rhsExpand) {
            var commonDims = Math.Max(lhsVecShape.Length, rhsVecShape.Length);
            var lhsExpandShape = (ImmutableArray<ShapeIndexValue>)([.. Enumerable.Repeat(new ShapeIndexValue(1), lhsExpand = commonDims - lhsVecShape.Length), .. lhsVecShape.Span]);
            var rhsExpandShape = (ImmutableArray<ShapeIndexValue>)([.. Enumerable.Repeat(new ShapeIndexValue(1), rhsExpand = commonDims - rhsVecShape.Length), .. rhsVecShape.Span]);

            var vecShape = lhsExpandShape.Zip(rhsExpandShape).Select((e, idx) => {
                if(e.First.IsConst && e.Second.IsConst) {
                    if(e.First.Range.Left != e.Second.Range.Left) {
                        if(e.First.Range.Left != 1 && e.Second.Range.Left != 1) {
                            throw new ArithmeticException($"Unable to broadcast with shape at dim {idx}");
                        }
                        return new ShapeIndexValue(e.First.Range.Left + e.Second.Range.Left - 1);
                    }
                    return new ShapeIndexValue(e.First.Range.Left);
                }
                if(e.First.IsConst ^ e.Second.IsConst) {
                    var constValue = e.First.IsConst ? e.First.Range.Left : e.Second.Range.Left;
                    if(constValue == 1) {
                        return e.First.IsConst ? e.Second : e.First;
                    } else {
                        return ShapeEquals.CreateExpression([e.First, e.Second]);
                    }
                }
                return ShapeEquals.CreateExpression([e.First, e.Second]);
            }).ToArray();

            return new(vecShape);
        }
    }
}
