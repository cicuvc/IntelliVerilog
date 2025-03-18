using System;
using System.Collections.Generic;
using System.Numerics;

namespace IntelliVerilog.Core.DataTypes {
    public class StaticEnumSequentialEncoder : StaticEnumEncoder {
        protected Dictionary<Type, (uint minRequiredBits, Dictionary<uint, uint>)> m_WidthCache = new();

        public override BigInteger ConvertEnumValue<TEnum>(TEnum value) {
            if (!m_WidthCache.ContainsKey(typeof(TEnum))) {
                InitEnumBits<TEnum>();
            }
            var (_, convMap) = m_WidthCache[typeof(TEnum)];
            return convMap[Convert.ToUInt32(value)];
        }

        public override uint InitEnumBits<TEnum>() {
            var enumValues = Enum.GetValues<TEnum>();
            var bits = (uint)(Math.Floor(Math.Log2(enumValues.Length - 1)) + 1);
            var enumMap = new Dictionary<uint, uint>();
            var index = 0u;
            foreach(var i in enumValues) {
                var enumValue = Convert.ToUInt32(i);
                enumMap.Add(enumValue, index++);
            }
            m_WidthCache.Add(typeof(TEnum), (bits, enumMap));
            return bits;
        }

        public override int InitWithGivenBits<TEnum>(int bits) {
            if (!m_WidthCache.ContainsKey(typeof(TEnum))) {
                InitEnumBits<TEnum>();
            }
            var (requiredBits, _) = m_WidthCache[typeof(TEnum)];
            if (requiredBits > bits) {
                throw new OverflowException($"Enumeration type {typeof(TEnum).Name} cannot be represent in {bits} bits");
            }
            return bits;
        }
    }
}
