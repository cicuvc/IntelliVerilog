using System.Diagnostics;

namespace IntelliVerilog.Core.Analysis.TensorLike {
    public class TensorIndexModExpr : TensorIndexExpr {
        protected TensorIndexExpr m_BaseExpression;
        public TensorIndexExpr BaseExpression => m_BaseExpression;
        public int Divisor { get; }
        public override int MinValue { get; }
        public override int MaxValue { get; }
        public override int GreatestCommonDivisorValue { get; }
        public TensorIndexModExpr(TensorIndexExpr lhs, int divisor) {
            m_BaseExpression = lhs;
            Divisor = divisor;

            MinValue = 0;
            MaxValue = divisor - 1;
            GreatestCommonDivisorValue = 1;

            if(lhs.MaxValue - lhs.MinValue + 1 >= divisor) return;

            // we don't need precise possible range for optimization
            var leftShift = lhs.MinValue - (lhs.MinValue % divisor);
            var left = lhs.MinValue - leftShift;
            var right = lhs.MaxValue - leftShift;
            if(right < divisor) {
                MinValue = left;
                MaxValue = right;
                GreatestCommonDivisorValue = left == right ? left : 1;
            }
        }
        [DebuggerStepThrough]
        public override bool Accept(ITensorIndexExprVisitor visitor, ref TensorIndexExpr parentSlot)
            => visitor.Visit(this, ref parentSlot);

        public override bool VisitSubNodes(ITensorIndexExprVisitor visitor) {
            var changed = BaseExpression.Accept(visitor, ref m_BaseExpression);
            changed |= BaseExpression.VisitSubNodes(visitor);
            return changed;
        }
        public override string ToString() {
            return $"({BaseExpression} % {Divisor})";
        }
    }

}
