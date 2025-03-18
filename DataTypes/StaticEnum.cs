using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;

namespace IntelliVerilog.Core.DataTypes {
    public class StaticEnum<TEnum> : UInt where TEnum : unmanaged, Enum {
        protected static StaticEnumEncoder m_Encoder;
        protected static Dictionary<Type, StaticEnumEncoder> m_RegisteredEncoders = new() {
            { typeof(StaticEnumSequentialEncoder),StaticEnumEncoder.Sequential }
        };
        static StaticEnum() {
            m_Encoder = GetEncoder();
        }
        public static ulong TransformEnumValue(TEnum value) {
            return ((ulong)m_Encoder.ConvertEnumValue(value));
        }
        public static ulong[] GetEnumValues() {
            return Enum.GetValues<TEnum>().Select(TransformEnumValue).ToArray();
        }
        protected static StaticEnumEncoder GetEncoder() {
            var type = typeof(TEnum);
            var encoderType = type.GetCustomAttribute<EnumEncodeTypeAttribute>()?.Encoder ?? typeof(StaticEnumSequentialEncoder);
            return m_RegisteredEncoders[encoderType];
        }
        public StaticEnum(ReadOnlySpan<int> shape) 
            : base([..shape[0..^1], m_Encoder.InitWithGivenBits<TEnum>(shape[^1])]) {
        }
        public static BigInteger ConvertEnumValue(TEnum value) => m_Encoder.ConvertEnumValue(value);
    }
}
