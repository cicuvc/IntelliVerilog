using IntelliVerilog.Core.DataTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.Expressions.Algebra {
    public class UIntAddExpression : BinaryExpression<UInt> {
        public UIntAddExpression(RightValue<UInt> lhs, RightValue<UInt> rhs) : base(lhs, rhs, UInt.CreateDefault(), UInt.CreateDefault().DefaultAlgebra) {
        }
    }
    public class UIntSubExpression : BinaryExpression<UInt> {
        public UIntSubExpression(RightValue<UInt> lhs, RightValue<UInt> rhs) : base(lhs, rhs, UInt.CreateDefault(), UInt.CreateDefault().DefaultAlgebra) {
        }
    }
    public class UIntMulExpression : BinaryExpression<UInt> {
        public UIntMulExpression(RightValue<UInt> lhs, RightValue<UInt> rhs) : base(lhs, rhs, UInt.CreateDefault(), UInt.CreateDefault().DefaultAlgebra) {
        }
    }
    public class UIntDivExpression : BinaryExpression<UInt> {
        public UIntDivExpression(RightValue<UInt> lhs, RightValue<UInt> rhs) : base(lhs, rhs, UInt.CreateDefault(), UInt.CreateDefault().DefaultAlgebra) {
        }
    }
    public class UIntXorExpression : BinaryExpression<UInt> {
        public UIntXorExpression(RightValue<UInt> lhs, RightValue<UInt> rhs) : base(lhs, rhs, UInt.CreateDefault(), UInt.CreateDefault().DefaultAlgebra) {
        }
    }
    public class UIntAndExpression : BinaryExpression<UInt> {
        public UIntAndExpression(RightValue<UInt> lhs, RightValue<UInt> rhs) : base(lhs, rhs, UInt.CreateDefault(), UInt.CreateDefault().DefaultAlgebra) {
        }
    }
    public class UIntOrExpression : BinaryExpression<UInt> {
        public UIntOrExpression(RightValue<UInt> lhs, RightValue<UInt> rhs) : base(lhs, rhs, UInt.CreateDefault(), UInt.CreateDefault().DefaultAlgebra) {
        }
    }
    public class UIntNotExpression : UnaryExpression<UInt> {
        public UIntNotExpression(RightValue<UInt> lhs) : base(lhs, UInt.CreateDefault(), UInt.CreateDefault().DefaultAlgebra) {
        }
    }
    public class UIntBitsSelectionExpression : RightValue<UInt> , IUntypedUnaryExpression {
        public RightValue<UInt> BaseExpression { get; }
        public Range SelectedRange { get; }

        public AbstractValue UntypedValue => BaseExpression;

        public UIntBitsSelectionExpression(UInt type, RightValue<UInt> baseExpression, Range range) : base(type, type.DefaultAlgebra) {
            BaseExpression = baseExpression;
            SelectedRange = range;
        }
        public override bool Equals(AbstractValue? other) {
            if (other is UIntBitsSelectionExpression expression) {
                if (expression.SelectedRange.Equals(SelectedRange) && expression.BaseExpression.Equals(BaseExpression)) {
                    return true;
                }
            }
            return false;
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

        public RightValue<UInt> GetSelectionValue(RightValue<UInt> lhs, int index)
            => new UIntBitsSelectionExpression(new(1), lhs, new Range(index, index + 1));

        public RightValue<UInt> MulExpression(RightValue<UInt> lhs, RightValue<UInt> rhs)
            => new UIntMulExpression(lhs, rhs);

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
