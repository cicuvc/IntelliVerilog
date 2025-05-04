using IntelliVerilog.Core.DataTypes.Shape;
using IntelliVerilog.Core.Expressions.Algebra;
using System;

namespace IntelliVerilog.Core.DataTypes {
    public class Bool : DataType<Bool>, IDataType<Bool> {
        public static Bool DefaultType { get; } = new([1]);
        public Bool(ReadOnlySpan<ShapeIndexValue> shape) : base(shape) {
        }
        public override IAlg DefaultAlgebra => BoolAlgebra.Instance;
        public override Size BitWidth => new([1]);
        public override Size VecShape => Shape;
        public static Bool CreateDefault() => DefaultType;
        public static Bool CreateWidth(ReadOnlySpan<ShapeIndexValue> shape) => new(shape);
    }
}
