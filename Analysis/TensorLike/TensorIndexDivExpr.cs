using System;
using System.Diagnostics;

namespace IntelliVerilog.Core.Analysis.TensorLike {
    public class TensorIndexDivExpr : TensorIndexExpr {
        protected TensorIndexExpr m_BaseExpression;
        public TensorIndexExpr BaseExpression => m_BaseExpression;
        public int Divisor { get; }
        public override int MinValue { get; }
        public override int MaxValue { get; }

        public override int GreatestCommonDivisorValue { get; }

        public TensorIndexDivExpr(TensorIndexExpr lhs, int divisor) {
            m_BaseExpression = lhs;
            Divisor = divisor;

            MinValue = Math.Min(lhs.MinValue / divisor, lhs.MaxValue / divisor);
            MaxValue = Math.Max(lhs.MinValue / divisor, lhs.MaxValue / divisor);
            GreatestCommonDivisorValue = lhs.GreatestCommonDivisorValue % divisor == 0 ?
                lhs.GreatestCommonDivisorValue / divisor : (MaxValue == MinValue ? MinValue : 1);
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
            return $"({BaseExpression} / {Divisor})";
        }
    }

}
