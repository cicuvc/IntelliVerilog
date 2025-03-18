using IntelliVerilog.Core.Expressions;
using IntelliVerilog.Core.Expressions.Algebra;
using System;

namespace IntelliVerilog.Core.DataTypes {
    public static class EnumExtension {
        public static RightValue<UInt> Const<TEnum>(this TEnum enumerate) where TEnum : unmanaged, Enum {
            return new UIntLiteral(StaticEnum<TEnum>.ConvertEnumValue(enumerate));
        }
    }
}
