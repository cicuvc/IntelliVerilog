using IntelliVerilog.Core.DataTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.Expressions.Algebra {

    public interface IAlg {
    }
    public interface IAlg<T>: IAlg where T:DataType{
        RightValue<T> AddExpression(RightValue<T> lhs, RightValue<T> rhs);
        RightValue<T> SubExpression(RightValue<T> lhs, RightValue<T> rhs);
        RightValue<T> MulExpression(RightValue<T> lhs, RightValue<T> rhs);
        RightValue<T> DivExpression(RightValue<T> lhs, RightValue<T> rhs);
        RightValue<T> XorExpression(RightValue<T> lhs, RightValue<T> rhs);
        RightValue<T> AndExpression(RightValue<T> lhs, RightValue<T> rhs);
        RightValue<T> OrExpression(RightValue<T> lhs, RightValue<T> rhs);
        RightValue<T> NotExpression(RightValue<T> lhs);

        void SetSelectionValue(RightValue<T> lhs, int index, RightValue<T> value);
        void SetSelectionValue(RightValue<T> lhs, Range range, RightValue<T> value);
        bool BoolCast(RightValue<T> lhs);
    }
}
