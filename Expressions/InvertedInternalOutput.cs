using IntelliVerilog.Core.DataTypes;
using System;

namespace IntelliVerilog.Core.Expressions {
    public class InvertedInternalOutput<TData> : RightValue<TData>, IInvertedOutput where TData : DataType,IDataType<TData> {
        public IoComponent InternalOut { get; }
        public Range SelectedRange { get; }
        public InvertedInternalOutput(TData type,IoComponent output, Range range) : base(type, type.DefaultAlgebra) {
            InternalOut = output;
            SelectedRange = range;
        }

        public override bool Equals(AbstractValue? other) {
            if (other is InvertedInternalOutput<TData> io) {
                return io.InternalOut == InternalOut && io.SelectedRange.Equals(io.SelectedRange);
            }
            return false;
        }
    }
}
