namespace IntelliVerilog.Core.Analysis.TensorLike {
    public interface ITensorIndexExprVisitor {
        bool Visit(TensorIndexVarBoundExpr varExpr, ref TensorIndexExpr parentSlot);
        bool Visit(TensorIndexVarExpr varExpr, ref TensorIndexExpr parentSlot);
        bool Visit(TensorIndexDivExpr varExpr, ref TensorIndexExpr parentSlot);
        bool Visit(TensorIndexModExpr varExpr, ref TensorIndexExpr parentSlot);
        bool Visit(TensorIndexAffinePolyExpr varExpr, ref TensorIndexExpr parentSlot);
    }

}
