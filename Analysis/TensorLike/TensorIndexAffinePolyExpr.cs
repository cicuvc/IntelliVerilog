using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;

namespace IntelliVerilog.Core.Analysis.TensorLike {
    public class TensorIndexAffinePolyExpr : TensorIndexExpr {
        public TensorIndexExpr[] SubExpressions { get; }
        public ImmutableArray<int> Coefficients { get; }
        public int Bias { get; }
        public override int MinValue { get; }
        public override int MaxValue { get; }
        public override int GreatestCommonDivisorValue { get; }

        public TensorIndexAffinePolyExpr(TensorIndexExpr[] expressions, ReadOnlySpan<int> coefficients, int bias) {
            if(expressions.Length != coefficients.Length) {
                throw new ArgumentException("Number of subexpressions and coefficients not match");
            }

            SubExpressions = expressions;
            Coefficients = coefficients.ToImmutableArray();
            Bias = bias;

            var minValue = bias;
            var maxValue = bias;

            var gcd = bias;

            for(var i = 0; i < expressions.Length; i++) {
                var coeValue = coefficients[i];
                var singleTermMin = Math.Min(expressions[i].MinValue * coeValue, expressions[i].MaxValue * coeValue);
                var singleTermMax = Math.Max(expressions[i].MinValue * coeValue, expressions[i].MaxValue * coeValue);
                minValue += singleTermMin;
                maxValue += singleTermMax;

                var value = (expressions[i].MinValue == expressions[i].MaxValue) ? expressions[i].MinValue * coeValue : coeValue;
                gcd = TensorIndexMathHelper.GreatestCommonDivisor(value, gcd);
            }

            MinValue = minValue;
            MaxValue = maxValue;
            GreatestCommonDivisorValue = gcd;
        }
        [DebuggerStepThrough]
        public override bool Accept(ITensorIndexExprVisitor visitor, ref TensorIndexExpr parentSlot)
            => visitor.Visit(this, ref parentSlot);
        public override bool VisitSubNodes(ITensorIndexExprVisitor visitor) {
            var changed = false;
            for(var i = 0; i < SubExpressions.Length; i++) {
                changed |= SubExpressions[i].Accept(visitor, ref SubExpressions[i]);
                changed |= SubExpressions[i].VisitSubNodes(visitor);
            }
            return changed;
        }
        public override string ToString() {
            return "(" + SubExpressions
                .Select((e, idx) => $"{Coefficients[idx]} * {e}")
                .DefaultIfEmpty()
                .Aggregate((u, v) => $"{u} + {v}") + $" + {Bias})";
        }
    }

}
