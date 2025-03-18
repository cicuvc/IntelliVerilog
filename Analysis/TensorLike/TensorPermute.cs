using System;
using System.Collections.Immutable;
using System.Linq;

namespace IntelliVerilog.Core.Analysis.TensorLike {
    public class TensorPermute : TensorTransformExpr {
        public ImmutableArray<int> Dims { get; }
        public TensorExpr BaseExpression { get; }
        
        public TensorPermute(TensorExpr baseExpr,ReadOnlySpan<int> shape, ReadOnlySpan<int> dims) : base(shape) {
            BaseExpression = baseExpr;
            Dims = dims.ToImmutableArray();
        }

        public override TransformIndex TransformIndices(ReadOnlySpan<TensorIndexExpr> indices) {
            var indicesImm = indices.ToImmutableArray();
            var newIndices = Dims.Select(e => indicesImm[e]).ToArray();
            var fullRanges = Shape.Select(e => (0, e)).ToArray();

            return new TransformIndex(newIndices, BaseExpression);
        }

        public override bool Equals(TensorExpr? expr) {
            if(expr is TensorPermute perm) {
                return Dims.SequenceEqual(perm.Dims) && BaseExpression.Equals(perm.BaseExpression);
            }
            return false;
        }
    }

}
