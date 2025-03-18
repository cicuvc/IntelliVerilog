using IntelliVerilog.Core.DataTypes.Shape;
using System;

namespace IntelliVerilog.Core.DataTypes {
    public abstract class Bits1D<TData> : DataType<TData> where TData : DataType<TData>, IDataType<TData> {
        public override Size BitWidth => new(Shape[^1..]);
        public override Size VecShape => new(Shape[0..^1]);
        public Bits1D(ReadOnlySpan<ShapeIndexValue> shape) : base(shape) {
        }
    }
}
