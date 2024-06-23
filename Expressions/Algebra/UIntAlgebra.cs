using IntelliVerilog.Core.DataTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.Expressions.Algebra {
    public class UIntAddExpression : BinaryExpression<UInt> {
        public UIntAddExpression(RightValue<UInt> lhs, RightValue<UInt> rhs) : base(lhs, rhs) {
        }
    }
    public class UIntSubExpression : BinaryExpression<UInt> {
        public UIntSubExpression(RightValue<UInt> lhs, RightValue<UInt> rhs) : base(lhs, rhs) {
        }
    }
    public class UIntMulExpression : BinaryExpression<UInt> {
        public UIntMulExpression(RightValue<UInt> lhs, RightValue<UInt> rhs) : base(lhs, rhs) {
        }
    }
    public class UIntDivExpression : BinaryExpression<UInt> {
        public UIntDivExpression(RightValue<UInt> lhs, RightValue<UInt> rhs) : base(lhs, rhs) {
        }
    }
    public class UIntXorExpression : BinaryExpression<UInt> {
        public UIntXorExpression(RightValue<UInt> lhs, RightValue<UInt> rhs) : base(lhs, rhs) {
        }
    }
    public class UIntAndExpression : BinaryExpression<UInt> {
        public UIntAndExpression(RightValue<UInt> lhs, RightValue<UInt> rhs) : base(lhs, rhs) {
        }
    }
    public class UIntOrExpression : BinaryExpression<UInt> {
        public UIntOrExpression(RightValue<UInt> lhs, RightValue<UInt> rhs) : base(lhs, rhs) {
        }
    }
    public class UIntNotExpression : UnaryExpression<UInt> {
        public UIntNotExpression(RightValue<UInt> lhs) : base(lhs) {
        }
    }

    public class UIntLiteral : RightValue<UInt> {
        public BigInteger Value { get; }
        public override DataType Type {
            set { 
                base.Type = value;
                if (Value.GetBitLength() > value.WidthBits) {
                    throw new OverflowException($"Unsigned literal {Value} requires {Value.GetBitLength()} bits while expression infers a width of {value.WidthBits} bits. Numeric cut-off occurs");
                }
            }
        }

        public UIntLiteral(ulong value):base(new(uint.MaxValue)) {
            Value = value;
        }
        public UIntLiteral(BigInteger value) : base(new(uint.MaxValue)) {
            Value = value;
        }
        public override bool Equals(AbstractValue? other) {
            if(other is UIntLiteral literal) {
                return literal.Type.WidthBits == Type.WidthBits && (literal.Value == Value);
            }
            return false;
        }

        public override void EnumerateSubNodes(Action<AbstractValue> callback) { }
    }
    public class UIntEqualExpression : BinaryRelationExpression<UInt> {
        public UIntEqualExpression(RightValue<UInt> lhs, RightValue<UInt> rhs) : base(lhs, rhs) {
        }
    }
    public class UIntNonEqualExpression : BinaryRelationExpression<UInt> {
        public UIntNonEqualExpression(RightValue<UInt> lhs, RightValue<UInt> rhs) : base(lhs, rhs) {
        }
    }
    public class UIntGreaterEqualExpression : BinaryRelationExpression<UInt> {
        public UIntGreaterEqualExpression(RightValue<UInt> lhs, RightValue<UInt> rhs) : base(lhs, rhs) {
        }
    }
    public class UIntLessEqualExpression : BinaryRelationExpression<UInt> {
        public UIntLessEqualExpression(RightValue<UInt> lhs, RightValue<UInt> rhs) : base(lhs, rhs) {
        }
    }
    public class UIntGreaterExpression : BinaryRelationExpression<UInt> {
        public UIntGreaterExpression(RightValue<UInt> lhs, RightValue<UInt> rhs) : base(lhs, rhs) {
        }
    }
    public class UIntLessExpression : BinaryRelationExpression<UInt> {
        public UIntLessExpression(RightValue<UInt> lhs, RightValue<UInt> rhs) : base(lhs, rhs) {
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
