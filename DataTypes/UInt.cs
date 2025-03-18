using IntelliVerilog.Core.DataTypes.Shape;
using IntelliVerilog.Core.Expressions;
using IntelliVerilog.Core.Expressions.Algebra;
using System;

namespace IntelliVerilog.Core.DataTypes {
    public class UInt : Bits1D<UInt>, IDataType<UInt>, IAutoWidthAdjustType<UInt> {
        public UInt(ReadOnlySpan<ShapeIndexValue> shape) : base(shape) {
        }

        public static UInt CreateDefault() => new([1]);

        public static UInt CreateWidth(ReadOnlySpan<ShapeIndexValue> shape) => new(shape);

        public RightValue<UInt> CreateExpansion(RightValue<UInt> value, Size newWidth) {
            throw new NotImplementedException();
        }

        public RightValue<UInt> CreateShrink(RightValue<UInt> value, Size newWidth) {
            throw new NotImplementedException();
        }

        public AbstractValue CreateExpansion(AbstractValue value, Size newWidth) {
            throw new NotImplementedException();
        }

        public AbstractValue CreateShrink(AbstractValue value, Size newWidth) {
            throw new NotImplementedException();
        }

        public override IAlg DefaultAlgebra => UIntAlgebra.Instance;
    }
}
