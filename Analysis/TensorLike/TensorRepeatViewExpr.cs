using System;
using System.Collections.Immutable;
using System.Linq;

namespace IntelliVerilog.Core.Analysis.TensorLike {
    public class TensorRepeatViewExpr : TensorTransformExpr {
        public TensorExpr BaseExpression { get; }
        public ImmutableArray<int> Repeats { get; }
        public TensorRepeatViewExpr(TensorExpr baseExpr, int[] shape, ReadOnlySpan<int> repeats) : base(shape) {
            BaseExpression = baseExpr;
            Repeats = repeats.ToImmutableArray();
        }

        public override TransformIndex TransformIndices(ReadOnlySpan<TensorIndexExpr> indices) {
            if(indices.Length != Shape.Length) throw new ArgumentException("Indices length mismatch");

            var indicesImm = indices.ToImmutableArray();
            var baseExpr = BaseExpression;
            var repeats = Repeats;

            var subIndices = baseExpr.Shape.Select((_, idx) => {
                return TensorIndexExpressionOptimizer.DefaultOptimizer.RunPass(new TensorIndexDivExpr(
                    indicesImm[idx], repeats[idx]
                    ));
            }).ToArray();

            return new TransformIndex(subIndices, baseExpr);
        }
    }

}
