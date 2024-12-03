using System;
using System.Linq;

namespace IntelliVerilog.Core.Analysis.TensorLike {
    public class TensorVarExpr<TData> : TensorLeafExpr where TData:class {
        public TData Data { get; }
        public override object UntypedData => Data;
        public TensorVarExpr(TData data, ReadOnlySpan<int> shape) : base(shape) {
            Data = data;
        }
        public override string ToString() => Data?.ToString() ?? "";
    }

}
