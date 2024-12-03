using IntelliVerilog.Core.Expressions;
using System.Diagnostics;

namespace IntelliVerilog.Core.Analysis.TensorLike {
    public class TensorIndexVarBoundExpr : TensorIndexExpr {
        public override int MinValue { get; }
        public override int MaxValue { get; }
        public override int GreatestCommonDivisorValue { get; }
        protected TensorIndexExpr m_BaseExpression;
        public TensorIndexExpr BaseExpression => m_BaseExpression;
        public TensorIndexVarBoundExpr(TensorIndexExpr baseExpression, int min, int max) {
            MinValue = min;
            MaxValue = max;
            m_BaseExpression = baseExpression;

            GreatestCommonDivisorValue = min == max ? min : 1;
        }

        public override bool Accept(ITensorIndexExprVisitor visitor, ref TensorIndexExpr parentSlot)
            => visitor.Visit(this, ref parentSlot);

        public override bool VisitSubNodes(ITensorIndexExprVisitor visitor) {
            var changed = BaseExpression.Accept(visitor, ref m_BaseExpression);
            changed |= BaseExpression.VisitSubNodes(visitor);
            return changed;
        }
    }
    public class TensorIndexVarExpr<TData> : TensorIndexVarExpr {
        public TData? Identifier { get; }
        public TensorIndexVarExpr(int min, int max, TData? id = default) : base(min, max) {
            Identifier = id;
        }
        public override string ToString() {
            return Identifier?.ToString() ?? base.ToString();
        }
    }
    public class TensorIndexVarExpr : TensorIndexExpr {
        public static TensorIndexVarExpr Zero { get; } = new(0, 0);
        public override int MinValue { get; }
        public override int MaxValue { get; }
        public override int GreatestCommonDivisorValue { get; }
        public TensorIndexVarExpr(int min, int max) {
            MinValue = min;
            MaxValue = max;

            GreatestCommonDivisorValue = min == max ? min : 1;
        }
        [DebuggerStepThrough]
        public override bool Accept(ITensorIndexExprVisitor visitor, ref TensorIndexExpr parentSlot)
            => visitor.Visit(this, ref parentSlot);

        public override bool VisitSubNodes(ITensorIndexExprVisitor visitor) => false;
        public override string ToString() {
            return (MinValue == MaxValue ? MinValue.ToString() : $"[{MinValue}, {MaxValue}]");
        }
        public static TensorIndexVarExpr CreateConst(int value) => new(value, value);
    }

}
