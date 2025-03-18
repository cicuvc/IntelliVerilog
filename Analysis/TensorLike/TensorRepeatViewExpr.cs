using System;
using System.Collections.Immutable;
using System.Linq;

namespace IntelliVerilog.Core.Analysis.TensorLike {
    public class TensorRepeatViewExpr : TensorTransformExpr {
        /// <summary>
        /// Example: [1,2,3,4]
        /// Interleaved 2-repeat: [1,2,3,4,1,2,3,4]
        /// Normal 2-repeat: [1,1,2,2,3,3,4,4]
        /// </summary>
        public bool IsInterleaved { get; }
        public TensorExpr BaseExpression { get; }
        public ImmutableArray<int> Repeats { get; }
        public TensorRepeatViewExpr(TensorExpr baseExpr, int[] shape, ReadOnlySpan<int> repeats, bool isInterleaved) : base(shape) {
            BaseExpression = baseExpr;
            Repeats = repeats.ToImmutableArray();
            IsInterleaved = isInterleaved;
        }

        public override bool Equals(TensorExpr? expr) {
            if(expr is not TensorRepeatViewExpr view) return false;
            if(!view.Repeats.SequenceEqual(Repeats)) return false;
            if(!view.BaseExpression.Equals(BaseExpression)) return false;
            return true;
        }
        public override TransformIndex TransformIndices(ReadOnlySpan<TensorIndexExpr> indices) {
            if(indices.Length != Shape.Length) throw new ArgumentException("Indices length mismatch");

            var indicesImm = indices.ToImmutableArray();
            var baseExpr = BaseExpression;
            var repeats = Repeats;

            var subIndices = baseExpr.Shape.Select((_, idx) => {
                TensorIndexExpr indicesExpr = IsInterleaved ?
                    new TensorIndexModExpr(indicesImm[idx], baseExpr.Shape[idx]):
                    new TensorIndexDivExpr(indicesImm[idx], repeats[idx]);
                return TensorIndexExpressionOptimizer.DefaultOptimizer.RunPass(indicesExpr);
            }).ToArray();

            return new TransformIndex(subIndices, baseExpr);
        }
    }

}
