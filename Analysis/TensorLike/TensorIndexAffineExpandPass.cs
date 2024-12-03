using System;
using System.Linq;

namespace IntelliVerilog.Core.Analysis.TensorLike {
    public class TensorIndexAffineExpandPass : ITensorIndexExprVisitor {
        public bool Visit(TensorIndexVarExpr varExpr, ref TensorIndexExpr parentSlot) => false;

        public bool Visit(TensorIndexDivExpr varExpr, ref TensorIndexExpr parentSlot) {
            var changed = false;
            // expand mod expression into divisible and unknown parts
            if(varExpr.BaseExpression is TensorIndexAffinePolyExpr poly) {
                var divisor = varExpr.Divisor;
                var isDivisible = poly.SubExpressions.Select((e, idx) => {
                    var coef = poly.Coefficients[idx];
                    if(coef % divisor == 0) return true;
                    if(e.MinValue == e.MaxValue) { // single number case
                        if((coef * e.MinValue) % divisor == 0) return true;
                    }
                    return false;
                }).ToArray();

                var divisibleParts = poly.SubExpressions.Zip(isDivisible)
                    .Select((e, idx) => (e.First, e.Second, idx))
                    .Where(e => e.Second);

                var biasDivisiblePart = poly.Bias / divisor * divisor;
                var biasUnknownPart = poly.Bias % divisor;


                var unknownParts = poly.SubExpressions.Zip(isDivisible)
                    .Select((e, idx) => (e.First, e.Second, idx))
                    .Where(e => !e.Second);

                if((unknownParts.Count() != 0) && (divisibleParts.Count() != 0)) {
                    var unkExpression = new TensorIndexAffinePolyExpr(
                        unknownParts.Select(e => e.First).ToArray(),
                        unknownParts.Select(e => poly.Coefficients[e.idx]).ToArray(),
                        poly.Bias % divisor);

                    var divisibleExpression = new TensorIndexAffinePolyExpr(
                        divisibleParts.Select(e => e.First).ToArray(),
                        divisibleParts.Select(e => poly.Coefficients[e.idx]).ToArray(),
                        poly.Bias / divisor * divisor);

                    parentSlot = new TensorIndexAffinePolyExpr([
                        new TensorIndexDivExpr(unkExpression, divisor),
                        new TensorIndexDivExpr(divisibleExpression, divisor)], [1, 1], 0);
                    changed = true;
                } else if(biasDivisiblePart != 0) {
                    var newPoly = new TensorIndexAffinePolyExpr(poly.SubExpressions, poly.Coefficients.AsSpan(), poly.Bias % divisor);
                    parentSlot = new TensorIndexAffinePolyExpr([
                        new TensorIndexDivExpr(newPoly, divisor)], [1], poly.Bias / divisor);
                    changed = true;
                }
            }

            return changed;
        }

        public bool Visit(TensorIndexModExpr varExpr, ref TensorIndexExpr parentSlot) {
            // expand mod expression into divisible and unknown parts

            var changed = false;
            if(varExpr.BaseExpression is TensorIndexAffinePolyExpr poly) {
                var divisor = varExpr.Divisor;
                var isDivisible = poly.SubExpressions.Select((e, idx) => {
                    var coef = poly.Coefficients[idx];
                    if(coef % divisor == 0) return true;
                    if(e.MinValue == e.MaxValue) { // single number case
                        if((coef * e.MinValue) % divisor == 0) return true;
                    }
                    return false;
                }).ToArray();

                var divisibleParts = poly.SubExpressions.Zip(isDivisible)
                    .Where(e => e.Second);
                var unknownParts = poly.SubExpressions.Zip(isDivisible)
                    .Select((e, idx) => (e.First, e.Second, idx))
                    .Where(e => !e.Second);

                var biasRemain = poly.Bias % divisor;

                if(unknownParts.Count() == 0) { // all terms are divisible, eliminate the whole poly
                    parentSlot = TensorIndexVarExpr.CreateConst(biasRemain);
                    return true;
                }

                if(divisibleParts.Count() != 0) {
                    var baseExpression = new TensorIndexAffinePolyExpr(
                   unknownParts.Select(e => e.First).ToArray(),
                   unknownParts.Select(e => poly.Coefficients[e.idx]).ToArray(),
                   biasRemain);

                    parentSlot = new TensorIndexModExpr(baseExpression, divisor);
                    changed = true;
                }
            }
            return changed;
        }

        public bool Visit(TensorIndexAffinePolyExpr varExpr, ref TensorIndexExpr parentSlot) {
            var changed = false;
            var exprWithIndex = varExpr.SubExpressions
                .Where(e => e is TensorIndexAffinePolyExpr)
                .Select((e, idx) => (expr: (TensorIndexAffinePolyExpr)e, idx))
                .ToArray();

            if(exprWithIndex.Length != 0) {

                var expandSize = exprWithIndex.Sum(e => e.expr.SubExpressions.Length - 1);
                var biasDelta = exprWithIndex.Sum(e => e.expr.Bias);

                var newSubExpr = varExpr.SubExpressions.SelectMany(e => {
                    if(e is TensorIndexAffinePolyExpr subExpr) {
                        return subExpr.SubExpressions;
                    } else {
                        return [e];
                    }
                });
                var newCoef = varExpr.SubExpressions.SelectMany((e, idx) => {
                    var currentCoef = varExpr.Coefficients[idx];
                    if(e is TensorIndexAffinePolyExpr subExpr) {
                        return subExpr.Coefficients.Select(u => u * currentCoef);
                    } else {
                        return [currentCoef];
                    }
                });

                parentSlot = new TensorIndexAffinePolyExpr(newSubExpr.ToArray(), newCoef.ToArray(), varExpr.Bias + biasDelta);
                changed = true;
            }
            return changed;
        }

        public bool Visit(TensorIndexVarBoundExpr varExpr, ref TensorIndexExpr parentSlot) => false;
    }

}
