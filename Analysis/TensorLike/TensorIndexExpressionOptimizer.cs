using System.Collections.Generic;

namespace IntelliVerilog.Core.Analysis.TensorLike {
    public class TensorIndexExpressionOptimizer {
        public static TensorIndexExpressionOptimizer DefaultOptimizer { get; }
        static TensorIndexExpressionOptimizer() {
            DefaultOptimizer = new();
            DefaultOptimizer.AddPass(new TensorIndexAffineExpandPass());
            DefaultOptimizer.AddPass(new TensorIndexSimplificationPass());
        }
        protected List<ITensorIndexExprVisitor> m_OptimizationPasses = new();
        public void AddPass(ITensorIndexExprVisitor pass) => m_OptimizationPasses.Add(pass);
        public TensorIndexExpr RunPass(TensorIndexExpr expression) {
            while(true) {
                var change = false;
                foreach(var i in m_OptimizationPasses) {
                    change |= expression.Accept(i, ref expression);
                    change |= expression.VisitSubNodes(i);
                }
                //Console.WriteLine(expression);
                if(!change) break;
            }
            return expression;
        }
    }

}
