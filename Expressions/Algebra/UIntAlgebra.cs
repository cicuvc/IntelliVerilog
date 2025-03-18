using IntelliVerilog.Core.Analysis.TensorLike;
using IntelliVerilog.Core.DataTypes;
using IntelliVerilog.Core.DataTypes.Shape;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.Expressions.Algebra {
    public static class Bits1DWidthEvaluationHelpers {
        public static Size MaxWidth(Size lhsWidth, Size rhsWidth) {
            return new([ShapeMax.CreateExpression([lhsWidth[0], rhsWidth[0]])]);
        }
        public static Size AddWidth(Size lhsWidth, Size rhsWidth) {
            return new([ShapeAddition.CreateExpression([lhsWidth[0], rhsWidth[0]])]);
        }
    }
    public class UIntAddExpression : BinaryExpression<UInt> {
        public UIntAddExpression(RightValue<UInt> lhs, RightValue<UInt> rhs) 
            : base(lhs, rhs, Bits1DWidthEvaluationHelpers.MaxWidth(lhs.UntypedType.BitWidth, rhs.UntypedType.BitWidth)) {
        }
    }
    public class UIntSubExpression : BinaryExpression<UInt> {

        public UIntSubExpression(RightValue<UInt> lhs, RightValue<UInt> rhs) 
            : base(lhs, rhs, Bits1DWidthEvaluationHelpers.MaxWidth(lhs.UntypedType.BitWidth, rhs.UntypedType.BitWidth)) {
        }
    }
    public class UIntMulExpression : BinaryExpression<UInt> {
        public UIntMulExpression(RightValue<UInt> lhs, RightValue<UInt> rhs) : 
            base(lhs, rhs, Bits1DWidthEvaluationHelpers.AddWidth(lhs.UntypedType.BitWidth, rhs.UntypedType.BitWidth)) {
        }
    }
    public class UIntDivExpression : BinaryExpression<UInt> {
        public UIntDivExpression(RightValue<UInt> lhs, RightValue<UInt> rhs) 
            : base(lhs, rhs, Bits1DWidthEvaluationHelpers.MaxWidth(lhs.UntypedType.BitWidth, rhs.UntypedType.BitWidth)) {
        }
    }
    public class UIntXorExpression : BinaryExpression<UInt> {
        public UIntXorExpression(RightValue<UInt> lhs, RightValue<UInt> rhs)
            : base(lhs, rhs, Bits1DWidthEvaluationHelpers.MaxWidth(lhs.UntypedType.BitWidth, rhs.UntypedType.BitWidth)) {
        }
    }
    public class UIntAndExpression : BinaryExpression<UInt> {
        public UIntAndExpression(RightValue<UInt> lhs, RightValue<UInt> rhs)
            : base(lhs, rhs, Bits1DWidthEvaluationHelpers.MaxWidth(lhs.UntypedType.BitWidth, rhs.UntypedType.BitWidth)) {
        }
    }
    public class UIntOrExpression : BinaryExpression<UInt> {
        public UIntOrExpression(RightValue<UInt> lhs, RightValue<UInt> rhs)
            : base(lhs, rhs, Bits1DWidthEvaluationHelpers.MaxWidth(lhs.UntypedType.BitWidth, rhs.UntypedType.BitWidth)) {
        }
    }
    public class UIntNotExpression : UnaryExpression<UInt> {
        public UIntNotExpression(RightValue<UInt> lhs) : base(lhs, lhs.UntypedType.BitWidth) {
        }
    }
    public class BoolLiteral : RightValue<Bool> {
        public static BoolLiteral True { get; } = new(true);
        public static BoolLiteral False { get; } = new(false);
        public override Lazy<TensorExpr> TensorExpression { get; }
        public static BoolLiteral ToLiteral(bool value) => value ? True : False;
        public bool Value { get; }
        private BoolLiteral(bool value) : base(Bool.DefaultType) {
            Value = value;
            TensorExpression = new TensorVarExpr<BoolLiteral>(this, []);
        }
        public BoolLiteral(ReadOnlySpan<int> shape, bool value = false) 
            : base(Bool.CreateWidth(MemoryMarshal.Cast<int, ShapeIndexValue>(shape))) {
            Value = value;
            TensorExpression = new TensorVarExpr<BoolLiteral>(this, shape);
        }
        public static BoolLiteral Zeros(ReadOnlySpan<int> shape) => new(shape);
        public static BoolLiteral Ones(ReadOnlySpan<int> shape) => new(shape, true);

        public override void EnumerateSubNodes(Action<AbstractValue> callback) {
        }

        public override bool Equals(AbstractValue? other) {
            return ReferenceEquals(this, other);
        }
    }
    public class UIntLiteral : RightValue<UInt> {
        public BigInteger Value { get; }
        public override Lazy<TensorExpr> TensorExpression { get; }
        private static UInt MakeDataType(int minRequiredBits) {
            var width = new ShapeIndexValue(new ShapePlaceholder(minRequiredBits));
            return new([width]);
        }
        public UIntLiteral(ulong value) : base(MakeDataType(64 - (int)Lzcnt.X64.LeadingZeroCount(value))) {
            Value = value;
            TensorExpression = new Lazy<TensorExpr>(() => new TensorVarExpr<UIntLiteral>(this, UntypedType.Shape.ToImmutableIntShape()));
        }
        public UIntLiteral(BigInteger value) : base(MakeDataType((int)value.GetBitLength())) {
            Value = value;
            TensorExpression = new Lazy<TensorExpr>(() => new TensorVarExpr<UIntLiteral>(this, UntypedType.Shape.ToImmutableIntShape()));
        }
        public override bool Equals(AbstractValue? other) {
            if(other is UIntLiteral literal) {
                throw new NotImplementedException();
                //return literal.UntypedType.BitWidth[0] == UntypedType.BitWidth[0] && (literal.Value == Value);
            }
            return false;
        }

        public override void EnumerateSubNodes(Action<AbstractValue> callback) { }
    }
    public class UIntEqualExpression : BinaryRelationExpression<UInt> {
        public UIntEqualExpression(RightValue<UInt> lhs, RightValue<UInt> rhs) : base(lhs, rhs, new([1])) {
        }
    }
    public class UIntNonEqualExpression : BinaryRelationExpression<UInt> {
        public UIntNonEqualExpression(RightValue<UInt> lhs, RightValue<UInt> rhs) : base(lhs, rhs, new([1])) {
        }
    }
    public class UIntGreaterEqualExpression : BinaryRelationExpression<UInt> {
        public UIntGreaterEqualExpression(RightValue<UInt> lhs, RightValue<UInt> rhs) : base(lhs, rhs, new([1])) {
        }
    }
    public class UIntLessEqualExpression : BinaryRelationExpression<UInt> {
        public UIntLessEqualExpression(RightValue<UInt> lhs, RightValue<UInt> rhs) : base(lhs, rhs, new([1])) {
        }
    }
    public class UIntGreaterExpression : BinaryRelationExpression<UInt> {
        public UIntGreaterExpression(RightValue<UInt> lhs, RightValue<UInt> rhs) : base(lhs, rhs, new([1])) {
        }
    }
    public class UIntLessExpression : BinaryRelationExpression<UInt> {
        public UIntLessExpression(RightValue<UInt> lhs, RightValue<UInt> rhs) : base(lhs, rhs, new([1])) {
        }
    }
    public class UIntAlgebra : IAlg<UInt> {
        public static UIntAlgebra Instance { get; } = new();
        public RightValue<UInt> AddExpression(RightValue<UInt> lhs, RightValue<UInt> rhs)
            => new UIntAddExpression(lhs, rhs);

        public RightValue<UInt> AndExpression(RightValue<UInt> lhs, RightValue<UInt> rhs)
            => new UIntAndExpression(lhs, rhs);

        public bool BoolCast(RightValue<UInt> lhs) {
            throw new NotImplementedException();
        }

        public RightValue<UInt> DivExpression(RightValue<UInt> lhs, RightValue<UInt> rhs)
            => new UIntDivExpression(lhs, rhs);

        public RightValue<Bool> EqualExpression(RightValue<UInt> lhs, RightValue<UInt> rhs)
            => new UIntEqualExpression(lhs, rhs);

        public RightValue<Bool> GreaterEqualExpression(RightValue<UInt> lhs, RightValue<UInt> rhs)
             => new UIntGreaterEqualExpression(lhs, rhs);
        public RightValue<Bool> GreaterExpression(RightValue<UInt> lhs, RightValue<UInt> rhs)
             => new UIntGreaterExpression(lhs, rhs);

        public RightValue<Bool> LessEqualExpression(RightValue<UInt> lhs, RightValue<UInt> rhs)
            => new UIntLessEqualExpression(lhs, rhs);

        public RightValue<Bool> LessExpression(RightValue<UInt> lhs, RightValue<UInt> rhs)
             => new UIntLessExpression(lhs, rhs);

        public RightValue<UInt> MulExpression(RightValue<UInt> lhs, RightValue<UInt> rhs)
            => new UIntMulExpression(lhs, rhs);

        public RightValue<Bool> NonEqualExpression(RightValue<UInt> lhs, RightValue<UInt> rhs)
            => new UIntNonEqualExpression(lhs, rhs);

        public RightValue<UInt> NotExpression(RightValue<UInt> lhs)
            => new UIntNotExpression(lhs);

        public RightValue<UInt> OrExpression(RightValue<UInt> lhs, RightValue<UInt> rhs)
            => new UIntOrExpression(lhs, rhs);

        public void SetSelectionValue(RightValue<UInt> lhs, int index, RightValue<UInt> value) {
            throw new NotImplementedException();
        }

        public void SetSelectionValue(RightValue<UInt> lhs, Range range, RightValue<UInt> value) {
            throw new NotImplementedException();
        }

        public RightValue<UInt> SubExpression(RightValue<UInt> lhs, RightValue<UInt> rhs)
            => new UIntSubExpression(lhs, rhs);

        public RightValue<UInt> XorExpression(RightValue<UInt> lhs, RightValue<UInt> rhs)
            => new UIntXorExpression(lhs, rhs);
    }
}
