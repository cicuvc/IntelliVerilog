using IntelliVerilog.Core.DataTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.Expressions.Algebra {
    public class BoolAddExpression : BinaryExpression<Bool> {
        public BoolAddExpression(RightValue<Bool> lhs, RightValue<Bool> rhs) : base(lhs, rhs) {
        }
    }
    public class BoolSubExpression : BinaryExpression<Bool> {
        public BoolSubExpression(RightValue<Bool> lhs, RightValue<Bool> rhs) : base(lhs, rhs) {
        }
    }
    public class BoolMulExpression : BinaryExpression<Bool> {
        public BoolMulExpression(RightValue<Bool> lhs, RightValue<Bool> rhs) : base(lhs, rhs) {
        }
    }
    public class BoolDivExpression : BinaryExpression<Bool> {
        public BoolDivExpression(RightValue<Bool> lhs, RightValue<Bool> rhs) : base(lhs, rhs) {
        }
    }
    public class BoolXorExpression : BinaryExpression<Bool> {
        public BoolXorExpression(RightValue<Bool> lhs, RightValue<Bool> rhs) : base(lhs, rhs) {
        }
    }
    public class BoolAndExpression : BinaryExpression<Bool> {
        public BoolAndExpression(RightValue<Bool> lhs, RightValue<Bool> rhs) : base(lhs, rhs) {
        }
    }
    public class BoolOrExpression : BinaryExpression<Bool> {
        public BoolOrExpression(RightValue<Bool> lhs, RightValue<Bool> rhs) : base(lhs, rhs) {
        }
    }
    public class BoolNotExpression : UnaryExpression<Bool> {
        public BoolNotExpression(RightValue<Bool> lhs) : base(lhs) {
        }
    }
    public class BoolEqualExpression : BinaryRelationExpression<Bool> {
        public BoolEqualExpression(RightValue<Bool> lhs, RightValue<Bool> rhs) : base(lhs, rhs) {
        }
    }
    public class BoolNonEqualExpression : BinaryRelationExpression<Bool> {
        public BoolNonEqualExpression(RightValue<Bool> lhs, RightValue<Bool> rhs) : base(lhs, rhs) {
        }
    }
    public class BoolAlgebra : IAlg<Bool> {
        public static BoolAlgebra Instance { get; } = new();
        public RightValue<Bool> AddExpression(RightValue<Bool> lhs, RightValue<Bool> rhs)
            => new BoolAddExpression(lhs, rhs);

        public RightValue<Bool> AndExpression(RightValue<Bool> lhs, RightValue<Bool> rhs)
            => new BoolAndExpression(lhs, rhs);

        public bool BoolCast(RightValue<Bool> lhs) {
            throw new NotImplementedException();
        }

        public RightValue<Bool> DivExpression(RightValue<Bool> lhs, RightValue<Bool> rhs)
            => new BoolDivExpression(lhs, rhs);

        public RightValue<Bool> EqualExpression(RightValue<Bool> lhs, RightValue<Bool> rhs)
            => new BoolEqualExpression(lhs, rhs);

        public AbstractValue GetCombinationValue(AbstractValue[] expressions) {
            throw new NotImplementedException();
        }

        public RightValue<Bool> GetSelectionValue(RightValue<Bool> lhs, int index) {
            throw new NotImplementedException();
        }

        public RightValue<Bool> GetSelectionValue(RightValue<Bool> lhs, Range range) {
            throw new NotImplementedException();
        }

        public RightValue<Bool> MulExpression(RightValue<Bool> lhs, RightValue<Bool> rhs)
            => new BoolMulExpression(lhs, rhs);

        public RightValue<Bool> NonEqualExpression(RightValue<Bool> lhs, RightValue<Bool> rhs)
            => new BoolNonEqualExpression(lhs, rhs);

        public RightValue<Bool> NotExpression(RightValue<Bool> lhs)
            => new BoolNotExpression(lhs);

        public RightValue<Bool> OrExpression(RightValue<Bool> lhs, RightValue<Bool> rhs)
            => new BoolOrExpression(lhs, rhs);

        public void SetSelectionValue(RightValue<Bool> lhs, int index, RightValue<Bool> value) {
            throw new NotImplementedException();
        }

        public void SetSelectionValue(RightValue<Bool> lhs, Range range, RightValue<Bool> value) {
            throw new NotImplementedException();
        }

        public RightValue<Bool> SubExpression(RightValue<Bool> lhs, RightValue<Bool> rhs)
            => new BoolSubExpression(lhs, rhs);

        public RightValue<Bool> XorExpression(RightValue<Bool> lhs, RightValue<Bool> rhs)
            => new BoolXorExpression(lhs, rhs);
    }
}
