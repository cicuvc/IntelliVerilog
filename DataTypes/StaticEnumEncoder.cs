using System;
using System.Numerics;

namespace IntelliVerilog.Core.DataTypes {
    public abstract class StaticEnumEncoder {
        public abstract BigInteger ConvertEnumValue<TEnum>(TEnum value) where TEnum : unmanaged, Enum;
        public abstract uint InitEnumBits<TEnum>() where TEnum : unmanaged, Enum;
        public abstract int InitWithGivenBits<TEnum>(int bits) where TEnum : unmanaged, Enum,IConvertible;
        public static StaticEnumSequentialEncoder Sequential { get; } = new();
    }
}
