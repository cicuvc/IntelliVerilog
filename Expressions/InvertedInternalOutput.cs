using IntelliVerilog.Core.Analysis.TensorLike;
using IntelliVerilog.Core.DataTypes;
using System;
using System.Collections.Immutable;

namespace IntelliVerilog.Core.Expressions {
    public class InvertedInternalOutput<TData> : RightValue<TData>, IInvertedOutput where TData : DataType,IDataType<TData> {
        public IoComponent InternalOut { get; }
        public ImmutableArray<GenericIndex> SelectedRange { get; }

        public override Lazy<TensorExpr> TensorExpression => throw new NotImplementedException();

        public InvertedInternalOutput(TData type,IoComponent output, ReadOnlySpan<GenericIndex> range) : base(type) {
            InternalOut = output;
            SelectedRange = range.ToImmutableArray();
        }

        public override bool Equals(AbstractValue? other) {
            if (other is InvertedInternalOutput<TData> io) {
                return io.InternalOut == InternalOut && io.SelectedRange.Equals(io.SelectedRange);
            }
            return false;
        }

        public override void EnumerateSubNodes(Action<AbstractValue> callback) { }
    }
}
