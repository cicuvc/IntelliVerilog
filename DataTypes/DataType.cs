using IntelliVerilog.Core.Expressions;
using IntelliVerilog.Core.Expressions.Algebra;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.DataTypes {
    public interface IDataType<TData> where TData: DataType {
        public static abstract TData CreateDefault();
    }
    public abstract class DataType {
        public bool IsDeclaration { get; }
        public uint WidthBits { get; }
        public bool IsWidthSpecified => WidthBits != uint.MaxValue;
        public DataType(uint bits, bool isDecl = true) {
            WidthBits = bits;
            IsDeclaration = isDecl;
        }
        public abstract IAlg DefaultAlgebra { get; }
        public abstract DataType CreateWithWidth(uint bits);
    }
    public class Bool : DataType, IDataType<Bool> {
        public Bool(bool isDecl = true) : base(1, isDecl) {
        }

        public override IAlg DefaultAlgebra => BoolAlgebra.Instance;

        public static Bool CreateDefault() => new();

        public override DataType CreateWithWidth(uint bits) {
            Debug.Assert(bits == 0);
            return new Bool();
        }
    }
    public class RawBits : DataType {
        public RawBits(uint bits) : base(bits) {
        }

        public override IAlg DefaultAlgebra => throw new NotImplementedException();

        public override DataType CreateWithWidth(uint bits) {
            return new RawBits(bits);
        }
    }
    public class UInt : DataType, IDataType<UInt> {
        public UInt(uint bits) : base(bits) {
        }

        public static UInt CreateDefault() {
            return new(1);
        }

        public override DataType CreateWithWidth(uint bits) {
            return new UInt(bits);
        }

        public override IAlg DefaultAlgebra => UIntAlgebra.Instance;
    }
    public static class UIntExtensions {
        public static UInt Bits(this uint x) {
            return new(x);
        }
        public static RightValue<UInt> Const(this uint x) {
            return new UIntLiteral(x);
        }
    }
    public class Int : RawBits {
        public Int(uint bits) : base(bits) {
        }
    }
    public abstract class StaticEnumEncoder {
        public abstract BigInteger ConvertEnumValue<TEnum>(TEnum value) where TEnum : unmanaged, Enum;
        public abstract uint InitEnumBits<TEnum>() where TEnum : unmanaged, Enum;
        public abstract uint InitWithGivenBits<TEnum>(uint bits) where TEnum : unmanaged, Enum,IConvertible;
        public static StaticEnumSequentialEncoder Sequential { get; } = new();
    }
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

        public override uint InitWithGivenBits<TEnum>(uint bits) {
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
    public static class EnumExtension {
        public static RightValue<UInt> Const<TEnum>(this TEnum enumerate) where TEnum : unmanaged, Enum {
            return new UIntLiteral(StaticEnum<TEnum>.ConvertEnumValue(enumerate));
        }
    }

    public class EnumEncodeTypeAttribute:Attribute {
        public Type Encoder { get; }
        public EnumEncodeTypeAttribute(Type encoder) {
            Encoder = encoder;
        }
    }
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
        public StaticEnum(uint bits) 
            : base(m_Encoder.InitWithGivenBits<TEnum>(bits)) {
        }
        public static BigInteger ConvertEnumValue(TEnum value) => m_Encoder.ConvertEnumValue(value);
    }
}
