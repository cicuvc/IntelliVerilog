using IntelliVerilog.Core.DataTypes;
using IntelliVerilog.Core.Expressions.Algebra;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.Expressions {
    public interface IUntypedUnaryExpression {
        AbstractValue UntypedValue { get; }
    }
    public interface IUntypedBinaryExpression {
        AbstractValue UntypedRight { get; }
        AbstractValue UntypedLeft { get; }
    }
    public abstract class BinaryExpression<TData> : RightValue<TData> , IUntypedBinaryExpression where TData : DataType {
        public RightValue<TData> Left { get; }
        public RightValue<TData> Right { get; }
        public AbstractValue UntypedRight => Right;
        public AbstractValue UntypedLeft => Left;
        protected BinaryExpression(RightValue<TData> lhs, RightValue<TData> rhs, TData type, IAlg? algebra = null) : base(type, algebra) {
            Left = lhs;
            Right = rhs;
        }
        public override bool Equals(AbstractValue? other) {
            if(other is BinaryExpression<TData> expression) {
                if(expression.Left.Equals(Left) && expression.Right.Equals(Right)) {
                    if (other.GetType() == GetType()) return true;
                }
            }
            return false;
        }
    }
    public abstract class UnaryExpression<TData> : RightValue<TData> where TData : DataType {
        public RightValue<TData> Left { get; }
        protected UnaryExpression(RightValue<TData> lhs, TData type, IAlg? algebra = null) : base(type, algebra) {
            Left = lhs;
        }
        public override bool Equals(AbstractValue? other) {
            if (other is BinaryExpression<TData> expression) {
                if (expression.Left.Equals(Left)) {
                    if (other.GetType() == GetType()) return true;
                }
            }
            return false;
        }
    }
}
