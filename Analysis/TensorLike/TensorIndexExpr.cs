using System;

namespace IntelliVerilog.Core.Analysis.TensorLike {
    public abstract class TensorIndexExpr {
        public abstract int MinValue { get; }
        public abstract int MaxValue { get; }
        public abstract int GreatestCommonDivisorValue { get; }
        public abstract bool Accept(ITensorIndexExprVisitor visitor, ref TensorIndexExpr parentSlot);
        public abstract bool VisitSubNodes(ITensorIndexExprVisitor visitor);
        public override string ToString() => throw new NotImplementedException();
    }

}
