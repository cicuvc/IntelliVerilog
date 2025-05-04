using System;
using System.Linq;

namespace IntelliVerilog.Core.Analysis.TensorLike {
    public interface ITensorVarExpr {
        object? UntypedData { get; }
    }
    public class TensorVarExpr<TData> : TensorLeafExpr, ITensorVarExpr where TData:class {
        public TData? Data { get; set; }
        public override object? UntypedData => Data;
        public TensorVarExpr(TData? data, ReadOnlySpan<int> shape) : base(shape) {
            Data = data;
        }
        public override string ToString() => Data?.ToString() ?? "";

        public override bool Equals(TensorExpr? expr) {
            if(expr is not TensorVarExpr<TData> varExpr) return false;
            if(varExpr.Data != Data) return false;
            if(!varExpr.Shape.SequenceEqual(Shape)) return false;
            return true;
        }
    }

}
