using IntelliVerilog.Core.Analysis.TensorLike;
using IntelliVerilog.Core.DataTypes;
using IntelliVerilog.Core.DataTypes.Shape;
using IntelliVerilog.Core.Expressions.Algebra;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.Expressions {
    public class BitsExtendExpression<TData> : UnaryExpression<TData> where TData : Bits1D<TData>, IDataType<TData> {
        public bool IsArithmetric { get; }
        public BitsExtendExpression(RightValue<TData> lhs, int targetSize, bool isArithmetric) : base(lhs, new([new(targetSize)])) {
            IsArithmetric = isArithmetric;
        }
    }
    public interface IUntypedUnaryExpression {
        AbstractValue UntypedBaseValue { get; }
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
        public override Lazy<TensorExpr> TensorExpression { get; }
        protected static Bool MakeShape(RightValue<TData> lhs, RightValue<TData> rhs, Size estimatedWidth) {
            // Example: A: int[5,5,32], B: int[5,1,28] => bool[5,5]
            // Example: A: int[5,5,32], B: int[5,5,?] => bool[5,5]

            var vecShape = ShapeEvaluation.BinaryOperatorVec(lhs.UntypedType.VecShape, rhs.UntypedType.VecShape, out _, out _);

            return Bool.CreateWidth([..vecShape.Span, ..estimatedWidth.Span]);
        }
        protected BinaryRelationExpression(RightValue<TData> lhs, RightValue<TData> rhs, Size estimatedWidth) : base(MakeShape(lhs, rhs, estimatedWidth)) {
            Left = lhs;
            Right = rhs;

            if(lhs.TypedType.IsShapeComplete && rhs.TypedType.IsShapeComplete) {
                TensorExpression = MakeTensorExpression(lhs, rhs);
            } else {
                TensorExpression = new(() => MakeTensorExpression(lhs, rhs));
            }
        }
        protected TensorExpr MakeTensorExpression(RightValue<TData> lhs, RightValue<TData> rhs) {
            Debug.Assert(lhs.TypedType.IsShapeComplete && rhs.TypedType.IsShapeComplete);

            return new TensorVarExpr<BinaryRelationExpression<TData>>(this, UntypedType.Shape.ToImmutableIntShape());

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
        public override Lazy<TensorExpr> TensorExpression { get; }
        
        
        /// <summary>
        /// Infer shape of result of binary operation.
        /// </summary>
        /// <param name="lhs">Left-hand side expression</param>
        /// <param name="rhs">Right-hand side expression</param>
        /// <param name="estimatedWidth">Estimated width of result</param>
        /// <param name="autoPropagation">When shape of either side of expression is undetermined, automatically propagate shape from the determined side to undetermined side</param>
        /// <returns></returns>
        protected static TData MakeDefaultWidth(RightValue<TData> lhs, RightValue<TData> rhs, Size estimatedWidth) {
            var vecShape = ShapeEvaluation.BinaryOperatorVec(lhs.UntypedType.VecShape, rhs.UntypedType.VecShape, out _, out _);

            return TData.CreateWidth([.. vecShape.Span, ..estimatedWidth.Span]);
        }
        protected TensorExpr MakeTensorExpression() {
            Debug.Assert(TypedType.IsShapeComplete);
            return new TensorVarExpr<BinaryExpression<TData>>(this, TypedType.Shape.ToImmutableIntShape());

        }
        protected BinaryExpression(RightValue<TData> lhs, RightValue<TData> rhs, Size estimatedWidth) : base(MakeDefaultWidth(lhs,rhs, estimatedWidth)) {
            Left = lhs;
            Right = rhs;

            if(lhs.TypedType.IsShapeComplete && rhs.TypedType.IsShapeComplete) {
                TensorExpression = MakeTensorExpression();
            } else {
                TensorExpression = new(MakeTensorExpression);
            }
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
    public abstract class UnaryExpression<TData> : RightValue<TData>, IUntypedUnaryExpression where TData : DataType, IDataType<TData> {
        public RightValue<TData> Left { get; }
        public override Lazy<TensorExpr> TensorExpression { get; }
        public AbstractValue UntypedBaseValue => Left;
        protected TensorExpr MakeTensorExpression() {
            return new TensorVarExpr<UnaryExpression<TData>>(this, UntypedType.Shape.ToImmutableIntShape());
        }
        protected UnaryExpression(RightValue<TData> lhs, Size estimatedSize) : base(TData.CreateWidth([..lhs.TypedType.VecShape.Span, ..estimatedSize.Span])) {
            Left = lhs;

            if(lhs.TypedType.IsShapeComplete) {
                TensorExpression = MakeTensorExpression();
            } else {
                TensorExpression = new(MakeTensorExpression);
            }
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
