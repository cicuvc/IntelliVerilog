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
    public abstract class BinaryRelationExpression<TData> : RightValue<Bool>, IUntypedBinaryExpression where TData : DataType, IDataType<TData> {
        public RightValue<TData> Left { get; }
        public RightValue<TData> Right { get; }
        public AbstractValue UntypedRight => Right;
        public AbstractValue UntypedLeft => Left;
        protected BinaryRelationExpression(RightValue<TData> lhs, RightValue<TData> rhs) : base(Bool.CreateDefault()) {
            if(!lhs.Type.IsWidthSpecified && !rhs.Type.IsWidthSpecified) {
                throw new Exception("Unable to infer data type width");
            }
            if (!lhs.Type.IsWidthSpecified) lhs.Type = rhs.Type;
            if (!rhs.Type.IsWidthSpecified) rhs.Type = lhs.Type;

            Left = lhs;
            Right = rhs;
        }
        public override bool Equals(AbstractValue? other) {
            if (other is BinaryExpression<TData> expression) {
                if (expression.Left.Equals(Left) && expression.Right.Equals(Right)) {
                    if (other.GetType() == GetType()) return true;
                }
            }
            return false;
        }
        public override void EnumerateSubNodes(Action<AbstractValue> callback) {
            callback(UntypedLeft);
            callback(UntypedRight);
        }
    }
    public abstract class BinaryExpression<TData> : RightValue<TData> , IUntypedBinaryExpression where TData : DataType, IDataType<TData> {
        public RightValue<TData> Left { get; }
        public RightValue<TData> Right { get; }
        public AbstractValue UntypedRight => Right;
        public AbstractValue UntypedLeft => Left;
        protected static TData MakeDefaultWidth(RightValue<TData> lhs, RightValue<TData> rhs) {
            if(!lhs.Type.IsWidthSpecified && !lhs.Type.IsWidthSpecified) {
                return (TData)lhs.Type.CreateWithWidth(uint.MaxValue);
            }
            if(!lhs.Type.IsWidthSpecified) {
                return (TData)(lhs.Type = rhs.Type);
            }
            if (!rhs.Type.IsWidthSpecified) {
                return (TData)(rhs.Type = lhs.Type);
            }
            if(rhs.Type.WidthBits != lhs.Type.WidthBits) {
                throw new ArithmeticException("Bit width mismatch");
            }
            return (TData)lhs.Type;
        }
        protected BinaryExpression(RightValue<TData> lhs, RightValue<TData> rhs) : base(MakeDefaultWidth(lhs,rhs)) {
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
        public override void EnumerateSubNodes(Action<AbstractValue> callback) {
            callback(UntypedLeft);
            callback(UntypedRight);
        }
    }
    public abstract class UnaryExpression<TData> : RightValue<TData> where TData : DataType, IDataType<TData> {
        public RightValue<TData> Left { get; }
        protected UnaryExpression(RightValue<TData> lhs) : base(lhs.TypedType) {
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
        public override void EnumerateSubNodes(Action<AbstractValue> callback) {
            callback(Left);
        }
    }
}
