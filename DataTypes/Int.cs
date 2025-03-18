using IntelliVerilog.Core.DataTypes.Shape;
using IntelliVerilog.Core.Expressions.Algebra;
using System;

namespace IntelliVerilog.Core.DataTypes {
    public class Int : Bits1D<Int>, IDataType<Int> {
        public Int(ReadOnlySpan<ShapeIndexValue> shape) : base(shape) {
        }

        public override IAlg DefaultAlgebra => throw new NotImplementedException();
        public static Int CreateDefault() => new([1]);
        public static Int CreateWidth(ReadOnlySpan<ShapeIndexValue> shape) => new(shape);
    }
}
