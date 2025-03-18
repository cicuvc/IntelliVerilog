using IntelliVerilog.Core.Expressions;
using IntelliVerilog.Core.Expressions.Algebra;
using System;

namespace IntelliVerilog.Core.DataTypes {
    public static class UIntExtensions {
        public static UInt Bits(this uint x) {
            return new([(int)x]);
        }
        public static UInt Bits(this uint x, ReadOnlySpan<int> shape) {
            return new([.. shape, (int)x]);
        }
        public static RightValue<UInt> Const(this uint x) {
            return new UIntLiteral(x);
        }
    }
}
