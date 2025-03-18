using IntelliVerilog.Core.Expressions;
using IntelliVerilog.Core.Expressions.Algebra;

namespace IntelliVerilog.Core.DataTypes {
    public static class BoolExtensions {
        public static RightValue<Bool> Const(this bool x) {
            return BoolLiteral.ToLiteral(x);
        }
    }
}
