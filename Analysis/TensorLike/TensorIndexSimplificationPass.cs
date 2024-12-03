using System.Linq;

namespace IntelliVerilog.Core.Analysis.TensorLike {
    public class TensorIndexSimplificationPass : ITensorIndexExprVisitor {
        public bool Visit(TensorIndexVarExpr varExpr, ref TensorIndexExpr parentSlot) => false;

        public bool Visit(TensorIndexDivExpr varExpr, ref TensorIndexExpr parentSlot) {
            var lhs = varExpr.BaseExpression;
            var divisor = varExpr.Divisor;

            if(divisor == 1) {
                parentSlot = lhs;
                return true;
            }

            if(lhs.MinValue >= 0 && lhs.MaxValue < divisor) { // handle zero lhs
                parentSlot = TensorIndexVarExpr.Zero;
                return true;
            }

            if(lhs.MinValue / divisor == lhs.MaxValue / divisor) {
                parentSlot = TensorIndexVarExpr.CreateConst(lhs.MinValue / divisor);
                return true;
            }

            var changed = false;
            
            if(lhs is TensorIndexAffinePolyExpr poly) {
                if(poly.GreatestCommonDivisorValue % divisor == 0) {
                    var isReducable = true;
                    for(var i = 0; i < poly.SubExpressions.Length; i++) {
                        if(poly.Coefficients[i] % divisor == 0) continue;
                        if(poly.SubExpressions[i] is TensorIndexVarExpr idxVar) {
                            if(idxVar.MinValue == idxVar.MaxValue) {
                                if(idxVar.MinValue * poly.Coefficients[i] % divisor == 0)
                                    continue;
                            }
                        }
                        isReducable = false;
                        break;
                    }
                    if(isReducable) {
                        var newCoefficients = poly.Coefficients
                            .Select(e => (e % divisor == 0) ?
                                e / divisor :
                                e / TensorIndexMathHelper.GreatestCommonDivisor(e, divisor))
                            .ToArray();

                        var newSubExpressions = poly.Coefficients
                            .Select((e, idx) => {
                                if(e % divisor == 0) return poly.SubExpressions[idx];
                                var remainDivisor = divisor / TensorIndexMathHelper.GreatestCommonDivisor(e, divisor);
                                var subExpr = (TensorIndexVarExpr)poly.SubExpressions[idx];
                                return TensorIndexVarExpr.CreateConst(subExpr.MinValue / remainDivisor);
                            }).ToArray();

                        parentSlot = new TensorIndexAffinePolyExpr(newSubExpressions, newCoefficients, poly.Bias / divisor);
                        changed = true;
                    }
                }
            }
            return changed;
        }

        public bool Visit(TensorIndexModExpr varExpr, ref TensorIndexExpr parentSlot) {
            var lhs = varExpr.BaseExpression;
            if(lhs.MinValue == lhs.MaxValue && lhs.MinValue == 0) { // handle zero lhs
                parentSlot = TensorIndexVarExpr.Zero;
                return true;
            }

            if(lhs.GreatestCommonDivisorValue % varExpr.Divisor == 0) {
                parentSlot = TensorIndexVarExpr.Zero;
                return true;
            }

            if(lhs.MinValue >= 0 && lhs.MaxValue < varExpr.Divisor) {
                parentSlot = lhs;
                return true;
            }
            return false;
        }

        public bool Visit(TensorIndexAffinePolyExpr varExpr, ref TensorIndexExpr parentSlot) {
            if(varExpr.MinValue == varExpr.MaxValue && varExpr.MinValue == 0) { // handle zero expression
                parentSlot = TensorIndexVarExpr.Zero;
                return true;
            }

            if(varExpr.SubExpressions.Length == 0) {
                parentSlot = TensorIndexVarExpr.CreateConst(varExpr.Bias);
                return true;
            }

            if(varExpr.SubExpressions.Length == 1 && varExpr.Coefficients[0] == 1 && varExpr.Bias == 0) {
                parentSlot = varExpr.SubExpressions[0];
                return true;
            }

            var changed = false;
            var isZeroTerm = varExpr.Coefficients.Select((e, idx) => (e, idx))
                .Select(e => {
                    var expr = varExpr.SubExpressions[e.idx];
                    var isZero = e.e == 0 || (expr.MinValue == expr.MaxValue && expr.MinValue == 0);
                    var isConst = expr.MinValue == expr.MaxValue;
                    return (isZero, isConst, e.idx);
                });
            if(isZeroTerm.Sum(e => (e.isZero || e.isConst) ? 1 : 0) != 0) {
                var biasDelta = isZeroTerm.Where(e => e.isConst).DefaultIfEmpty()
                .Sum(e => varExpr.Coefficients[e.idx] * varExpr.SubExpressions[e.idx].MinValue);

                parentSlot = new TensorIndexAffinePolyExpr(
                    isZeroTerm.Where(e => !(e.isZero || e.isConst))
                    .Select(e => varExpr.SubExpressions[e.idx])
                    .ToArray(),
                    isZeroTerm.Where(e => !(e.isZero || e.isConst))
                    .Select(e => varExpr.Coefficients[e.idx])
                    .ToArray()
                    , varExpr.Bias + biasDelta);

                changed = true;
            }
            return changed;



        }

        public bool Visit(TensorIndexVarBoundExpr varExpr, ref TensorIndexExpr parentSlot) {
            var subExpr = varExpr.BaseExpression;
            if(subExpr.MinValue >= varExpr.MinValue && subExpr.MaxValue <= varExpr.MaxValue) {
                parentSlot = subExpr;
                return true;
            }
            return false;
        }
    }

}
