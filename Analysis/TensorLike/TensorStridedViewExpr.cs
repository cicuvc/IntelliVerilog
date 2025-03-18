using IntelliVerilog.Core.Expressions;
using Microsoft.VisualBasic;
using System;
using System.Collections.Immutable;
using System.Linq;

namespace IntelliVerilog.Core.Analysis.TensorLike {
    public struct TensorRawCombinationInfo {
        public TensorExpr BaseExpression { get; }
        public ImmutableArray<ValueTuple<int, int>> IndicesRange { get; }
        public TensorRawCombinationInfo(TensorExpr baseExpr, ReadOnlySpan<ValueTuple<int,int>> indicesRange) {
            BaseExpression = baseExpr;
            IndicesRange = indicesRange.ToImmutableArray();
        }
    }
    public class TensorRawCombination: TensorCombineExpr {
        public ImmutableArray<TensorRawCombinationInfo> Components { get; }
        public TensorRawCombination(ReadOnlySpan<int> shape, ReadOnlySpan<TensorRawCombinationInfo> components) : base(shape) {
            Components = components.ToImmutableArray();
        }
        public override bool Equals(TensorExpr? expr) {
            if(expr is not TensorRawCombination view) return false;
            if(!Shape.SequenceEqual(view.Shape)) return false;
            if(!Components.SequenceEqual(view.Components)) return false;
            return true;
        }
        public override TransformIndexParts[] TransformIndices(ReadOnlySpan<TensorIndexExpr> indices) {
            var indicesImm = indices.ToImmutableArray();
            return Components.Select(e => new TransformIndexParts(e.IndicesRange.AsSpan(), indicesImm.AsSpan(), e.BaseExpression)).ToArray();
        }
    }
    public class TensorConcat : TensorCombineExpr {
        public ImmutableArray<TensorExpr> BaseExpression { get; }
        public int Dimension { get; }
        public TensorConcat(ReadOnlySpan<TensorExpr> baseExpr,ReadOnlySpan<int> shape, int dim) : base(shape) {
            BaseExpression = baseExpr.ToImmutableArray();
            Dimension = dim;
        }
        public override bool Equals(TensorExpr? expr) {
            if(expr is not TensorConcat view) return false;
            if(!Shape.SequenceEqual(view.Shape)) return false;
            if(Dimension != view.Dimension) return false;
            if(!BaseExpression.SequenceEqual(view.BaseExpression)) return false;
            return true;
        }
        public override TransformIndexParts[] TransformIndices(ReadOnlySpan<TensorIndexExpr> indices) {
            var dim = Dimension;
            var partsEndOffset = BaseExpression.Select(e => e.Shape[dim]).ToArray();
            var partsStartOffset = partsEndOffset.ToArray();
            var cumSum = 0;
            for(var i=0;i< partsEndOffset.Length; i++) {
                partsStartOffset[i] = cumSum;
                partsEndOffset[i] += cumSum;
                cumSum += partsEndOffset[i];
            }
            var indicesImm = indices.ToImmutableArray();
            var parts = BaseExpression.Select(e => (range: e.Shape.Select(e => (0, e)).ToArray(), e)).ToArray();
            var finalParts = parts.Select((e, idx) => {
                e.range[dim] = (partsStartOffset[idx], partsEndOffset[idx]);

                var boundedIndices = indicesImm.ToArray();
                boundedIndices[dim] = new TensorIndexVarBoundExpr(boundedIndices[dim], partsStartOffset[idx], partsEndOffset[idx] - 1);
                return new TransformIndexParts(e.range, boundedIndices, e.e);
            });

            return finalParts.ToArray();
        }
    }
    public class TensorStridedViewExpr : TensorTransformExpr {
        public ImmutableArray<int> Strides { get; }
        public int Bias { get; }

        protected TensorExpr m_BaseExpression;
        public TensorStridedViewExpr(TensorExpr baseExpr, ReadOnlySpan<int> strides, ReadOnlySpan<int> shape, int bias = 0) : base(shape) {
            Strides = strides.ToImmutableArray();
            Bias = bias;
            m_BaseExpression = baseExpr;
        }
        public override bool Equals(TensorExpr? expr) {
            if(expr is not TensorStridedViewExpr view) return false;
            if(!m_BaseExpression.Equals(view.m_BaseExpression)) return false;
            if(!Shape.SequenceEqual(view.Shape)) return false;
            if(!Strides.SequenceEqual(view.Strides)) return false;
            if(Bias != view.Bias) return false;
            return true;
        }
        public override TransformIndex TransformIndices(ReadOnlySpan<TensorIndexExpr> indices) {
            if(indices.Length != Shape.Length) throw new ArgumentException("Indices length mismatch");

            var baseExpr = m_BaseExpression;
            var indicesImm = indices.ToArray();
            var position = new TensorIndexAffinePolyExpr(indicesImm, Strides.AsSpan(), Bias);

            var baseShapeLength = baseExpr.Shape.Length;
            var cumProd = GenerateCumProduct(baseExpr.Shape.AsSpan());
            
            var subIndices = baseExpr.Shape.Select((_, idx) => {
                return TensorIndexExpressionOptimizer.DefaultOptimizer.RunPass(new TensorIndexDivExpr(
                    new TensorIndexModExpr(position, cumProd[idx]), cumProd[idx + 1]
                    ));
            }).ToArray();

            var fullRanges = Shape.Select(e => (0, e)).ToArray();
            return new TransformIndex(subIndices, baseExpr);

        }
    }

}
