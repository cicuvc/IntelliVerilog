using IntelliVerilog.Core.Expressions;
using IntelliVerilog.Core.Expressions.Algebra;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.DataTypes {
    public interface IDataType<TData> where TData: DataType {
        public static abstract TData CreateDefault();
    }
    public abstract class DataType {
        public bool IsDeclaration { get; }
        public uint WidthBits { get; }
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
    }
    public class Int : RawBits {
        public Int(uint bits) : base(bits) {
        }
    }
    public abstract class StaticEnumEncoder {
        public abstract uint GetEnumBits<TEnum>() where TEnum : Enum;

        public static StaticEnumSequentialEncoder Sequential { get; } = new();
    }
    public class StaticEnumSequentialEncoder : StaticEnumEncoder {
        public override uint GetEnumBits<TEnum>() {
            return 0;
        }
    }
    public class StaticEnum<TEnum> : RawBits where TEnum : Enum {
        public StaticEnum(StaticEnumEncoder? encoding = null) 
            : base((encoding ?? StaticEnumEncoder.Sequential).GetEnumBits<TEnum>()) {
        }
    }
}
