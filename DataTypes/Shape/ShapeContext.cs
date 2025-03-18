using IntelliVerilog.Core.Analysis.TensorLike;
using IntelliVerilog.Core.Expressions;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.DataTypes.Shape {
    // [TODO] Add backward shape propagation
    public struct ShapeRange:IEquatable<ShapeRange> {
        public int Left;
        public int Right;
        public bool IsDefinite => Left + 1 == Right;
        public bool IsImpossible => Left >= Right;
        public ShapeRange(int left, int right) {
            Left = left;
            Right = right;
        }
        public bool Equals(ShapeRange other) {
            return Left == other.Left && Right == other.Right;
        }
        public bool IsInclusiveSubsetOf(ShapeRange other) {
            return other.Right >= Right && other.Left <= Left;
        }
        public static ShapeRange operator &(ShapeRange lhs, ShapeRange rhs) {
            return new(Math.Max(lhs.Left, rhs.Left), Math.Min(lhs.Right, rhs.Right));
        }
        public static ShapeRange operator *(ShapeRange lhs, int rhs) {
            return new(lhs.Left * rhs, (lhs.Right - 1) * rhs + 1);
        }
        public override string ToString() {
            return $"[{Left}, {Right})";
        }
    }
    public abstract class ShapeExpressionBase {
        protected ShapeRange m_Range;
        public bool IsDefinite => m_Range.IsDefinite;
        public ShapeRange Range => m_Range;
        public ShapeExpressionBase(ShapeRange initRange) {
            m_Range = initRange;
        }
        public void PropagateValue(ShapeRange newRange) {
            if(newRange.IsInclusiveSubsetOf(m_Range)) {
                throw new InvalidOperationException("New shape range not meet requirement");
            }

            m_Range = newRange;
            PropagateValueImpl(newRange);
        }
        protected abstract void PropagateValueImpl(ShapeRange newRange);
        public virtual int DetermineValue() {
            var configuration = ScopedLocator.GetServiceNonNull<ShapeInferConfiguration>();
            switch(configuration.DeterministricOptions) {
                case ShapeInferNonDeterministricOptions.ForceDeterministric: {
                    if(!Range.IsDefinite) throw new InvalidOperationException("Unable to find deterministric shape");
                    return Range.Left;
                }
                case ShapeInferNonDeterministricOptions.MinPossibleValue: return Range.Left;
                case ShapeInferNonDeterministricOptions.MaxPossibleValue: return Range.Right - 1;
                default: throw new NotImplementedException();
            }
        }
    }
    public class ShapePlaceholder : ShapeExpressionBase {
        public ShapePlaceholder(int minWidth = 0, int maxWidth = int.MaxValue - 1):base(new(minWidth, maxWidth + 1)) { }
        protected override void PropagateValueImpl(ShapeRange newRange) { }

    }
    public class ShapeFactor: ShapeExpressionBase {
        public static ShapeIndexValue CreateExpression(ShapeIndexValue baseExpression, int factor) {
            if(baseExpression.IsConst) return new(baseExpression.Range.Left * factor);
            return new(new ShapeFactor(baseExpression.Expression!, factor));
        }
        public ShapeExpressionBase BaseExpression { get; }
        public int Factor { get; }
        public ShapeFactor(ShapeExpressionBase baseExpression, int factor): base(baseExpression.Range * factor) {
            BaseExpression = baseExpression;
            Factor = factor;
        }
        protected override void PropagateValueImpl(ShapeRange newRange) {
            var lowerBound = newRange.Left / Factor;
            var upperBound = (newRange.Right - 1) / Factor + 1;
            BaseExpression.PropagateValue(new(lowerBound, upperBound));
        }
    }
    public class ShapeMax: ShapeExpressionBase {
        public static ShapeIndexValue CreateExpression(ReadOnlySpan<ShapeIndexValue> expressions) {
            var expressionsImm = expressions.ToImmutableArray();
            if(expressionsImm.All(e => e.IsConst)) {
                var maxValue = expressionsImm.Select(e => e.Range.Left).Max();
                return new(maxValue);
            }
            return new(new ShapeMax(expressions));
        }
        public ImmutableArray<ShapeIndexValue> BaseShapes { get; }
        private static ShapeRange GetRange(ReadOnlySpan<ShapeIndexValue> baseShape) {
            var lowerBound = baseShape.ToImmutableArray().Select(e => e.Range.Left).Max();
            var upperBound = baseShape.ToImmutableArray().Select(e => e.Range.Right).Max();
            if(lowerBound >= upperBound) throw new InvalidOperationException("Shape equal requirement is impossible to meet");
            var range = new ShapeRange(lowerBound, upperBound);
            return range;
        }

        protected override void PropagateValueImpl(ShapeRange newRange) {
            foreach(var i in BaseShapes) {
                if(!i.IsConst) {
                    var expr = i.Expression;
                    if(expr is not null) {
                        var newLowerBound = expr.Range.Left;
                        var newUpperBound = Math.Min(expr.Range.Right, newRange.Right);
                        expr.PropagateValue(new(newLowerBound, newUpperBound));
                    }
                }
            }
        }

        public ShapeMax(ReadOnlySpan<ShapeIndexValue> baseShape) : base(GetRange(baseShape)) {
            BaseShapes = baseShape.ToImmutableArray();
        }
    }
    public class ShapeEquals : ShapeExpressionBase {
        public static ShapeIndexValue CreateExpression(ReadOnlySpan<ShapeIndexValue> baseShape) {
            if(baseShape.ToImmutableArray().All(e => e.IsConst)) {
                var sizes = baseShape.ToImmutableArray().Select(e => e.Range.Left).ToArray();
                if(sizes.Min() != sizes.Max()) throw new InvalidOperationException("Shape equal requirement is impossible to meet");
                return new(sizes[0]);
            }
            return new(new ShapeEquals(baseShape));
        }
        public ImmutableArray<ShapeIndexValue> BaseShapes { get; }
        private static ShapeRange GetRange(ReadOnlySpan<ShapeIndexValue> baseShape) {
            var upperBound = baseShape.ToImmutableArray().Select(e => e.Range.Right).Min();
            var lowerBound = baseShape.ToImmutableArray().Select(e => e.Range.Left).Max();
            if(lowerBound >= upperBound) throw new InvalidOperationException("Shape equal requirement is impossible to meet");
            var range = new ShapeRange(lowerBound, upperBound);
            foreach(var i in baseShape) {
                i.Expression?.PropagateValue(new(lowerBound, upperBound));
            }
            return range;
        }
        public ShapeEquals(ReadOnlySpan<ShapeIndexValue> baseShape) : base(GetRange(baseShape)) {
            BaseShapes = baseShape.ToImmutableArray();
        }
        protected override void PropagateValueImpl(ShapeRange newRange) {
            foreach(var i in BaseShapes) {
                i.Expression?.PropagateValue(newRange);
            }
        }
    }
    public class ShapeAddition: ShapeExpressionBase {
        public static ShapeIndexValue CreateExpression(ReadOnlySpan<ShapeIndexValue> expressions) {
            if(expressions.ToImmutableArray().All(e => e.IsConst)) {
                return new(expressions.ToImmutableArray().Sum(e => e.Range.Left));
            }
            return new(new ShapeAddition(expressions));
        }
        protected static ShapeRange GetRange(ReadOnlySpan<ShapeIndexValue> baseExpressions) {
            var minSize = baseExpressions.ToImmutableArray().Select(e => e.Range.Left).Sum();
            var maxSize = baseExpressions.ToImmutableArray().Select(e => e.Range.Right).Sum();
            return new(minSize, maxSize);
        }
        public ImmutableArray<ShapeIndexValue> SubExpressions { get; }
        public ShapeAddition(ReadOnlySpan<ShapeIndexValue> expressions): base(GetRange(expressions)) {
            SubExpressions = expressions.ToImmutableArray();
        }
        protected override void PropagateValueImpl(ShapeRange newRange) {}
    }
    public class ShapeInterval : ShapeExpressionBase {
        public static ShapeIndexValue CreateExpression(ShapeIndexValue baseExpression, GenericIndex slice) {
            if(slice.ConstIndex.IndexType != SliceIndexType.Slice) throw new NotSupportedException();
            if(baseExpression.IsConst) return new(slice.ConstIndex.GetLength(baseExpression.Range.Left));

            var context = IntelliVerilogLocator.GetServiceNonNull<ShapeContext>();
            return new(new ShapeInterval(baseExpression.Expression!, slice));
        }
        protected static ShapeRange GetRange(ShapeExpressionBase baseShape, GenericIndex index) {
            if(index.VariableIndex is not null) throw new NotImplementedException();
            else {
                var slice = index.ConstIndex;
                if(slice.IndexType == SliceIndexType.Slice) {

                    if((!slice.Start.IsFromEnd) && (!slice.End.IsFromEnd)) {
                        var maxIndex = slice.Start.Value +
                            (slice.End.Value - slice.Start.Value - 1) / slice.Interval * slice.Interval;
                        var size = (slice.End.Value - slice.Start.Value) / slice.Interval;

                        if(baseShape.Range.Right <= maxIndex) throw new IndexOutOfRangeException();
                        baseShape.PropagateValue(new(maxIndex, baseShape.Range.Right));

                        return new(size, size + 1);
                    }

                    if((slice.Start.IsFromEnd) && (slice.End.IsFromEnd)) {
                        var baseMinRequiredRight = slice.Start.Value;
                        var size = (slice.Start.Value - slice.End.Value) / slice.Interval;

                        if(baseShape.Range.Right <= baseMinRequiredRight) throw new IndexOutOfRangeException();
                        baseShape.PropagateValue(new(baseMinRequiredRight, baseShape.Range.Right));

                        return new(size, size + 1);
                    }

                    if((!slice.Start.IsFromEnd) && (slice.End.IsFromEnd)) {
                        var baseMinRequiredRight = slice.Start.Value + slice.End.Value;
                        if(baseShape.Range.Right <= baseMinRequiredRight) throw new IndexOutOfRangeException();
                        baseShape.PropagateValue(new(baseMinRequiredRight, baseShape.Range.Right));

                        return new(0, baseShape.Range.Right / slice.Interval);
                    }

                    if((slice.Start.IsFromEnd) && (!slice.End.IsFromEnd)) {
                        var baseMaxAllowLeft = slice.Start.Value + slice.End.Value;
                        if(baseShape.Range.Left > baseMaxAllowLeft) throw new IndexOutOfRangeException();
                        baseShape.PropagateValue(new(baseShape.Range.Left, baseMaxAllowLeft + 1));

                        return new(0, baseShape.Range.Right / slice.Interval);
                    }

                    throw new UnreachableException();
                } else {
                    throw new NotSupportedException();
                }
                
            }
        }
        public GenericIndex Index { get; }
        public ShapeExpressionBase BaseShape { get; }
        public ShapeInterval(ShapeExpressionBase baseShape, GenericIndex index):base(GetRange(baseShape, index)) {
            Index = index;
            BaseShape = baseShape;
        }
        protected static int CalcLeftFromStartTotalLength(int start, int end, int interval, int resultSize) {
            return resultSize * interval + start + end;
        }
        protected static int CalcRightFromStartTotalLength(int start, int end, int interval, int resultSize) {
            return -resultSize * interval + start + end;
        }
        protected override void PropagateValueImpl(ShapeRange newRange) {
            var slice = Index.ConstIndex;

            Debug.Assert(slice.IndexType == SliceIndexType.Slice);
            Debug.Assert(slice.Start.IsFromEnd ^ slice.End.IsFromEnd);

            int maxPossibleBaseLength, minPossibleBaseLength;
            if(slice.Start.IsFromEnd) {
                maxPossibleBaseLength = CalcRightFromStartTotalLength(slice.Start.Value, slice.End.Value, slice.Interval, newRange.Left);
                minPossibleBaseLength = CalcRightFromStartTotalLength(slice.Start.Value, slice.End.Value, slice.Interval, newRange.Right - 1);
            } else {
                minPossibleBaseLength = CalcLeftFromStartTotalLength(slice.Start.Value, slice.End.Value, slice.Interval, newRange.Left);
                maxPossibleBaseLength = CalcLeftFromStartTotalLength(slice.Start.Value, slice.End.Value, slice.Interval, newRange.Right - 1);
            }

            var newBaseLeft = Math.Max(minPossibleBaseLength, BaseShape.Range.Left);
            var newBaseRight = Math.Min(maxPossibleBaseLength + 1, BaseShape.Range.Right);

            BaseShape.PropagateValue(new(newBaseLeft, newBaseRight));
        }
    }
    public struct ShapeIndexValue {
        public static ShapeIndexValue Invalid { get; } = new(int.MaxValue);
        private int m_Value;
        public int Value => m_Value;
        public bool IsConst => m_Value >= 0;
        public bool IsValid => m_Value != int.MaxValue;
        public bool IsDefinite => IsConst || Expression!.IsDefinite;
        public ShapeExpressionBase? Expression {
            get {
                if(m_Value >= 0) return null;
                var context = IntelliVerilogLocator.GetServiceNonNull<ShapeContext>();
                return context.ResolveIndex(-m_Value);
            }
        }
        public ShapeRange Range => IsConst ? new(m_Value, m_Value + 1) : Expression!.Range;
        
        public ShapeIndexValue(int constValue) {
            if(constValue < 0) {
                throw new ArgumentOutOfRangeException("Shape must be positive");
            }
            m_Value = constValue;
        }
        public ShapeIndexValue(ShapeExpressionBase expression) {
            var context = IntelliVerilogLocator.GetServiceNonNull<ShapeContext>();
            m_Value = -context.GetExpressionID(expression);
        }
        public void DetermineValue() {
            if(IsConst) return;
            if(!IsDefinite) throw new InvalidOperationException("Unable to determine value of index");
            m_Value = Range.Left;
        }
        public static implicit operator ShapeIndexValue(int value) => new(value);
        public override string ToString() {
            if(IsConst) return m_Value.ToString();
            return Expression!.Range.ToString();
        }
    }
    public enum ShapeInferNonDeterministricOptions {
        ForceDeterministric,
        MinPossibleValue,
        MaxPossibleValue,
    }
    public class ShapeInferConfiguration {
        public ShapeInferNonDeterministricOptions DeterministricOptions { get; set; } = ShapeInferNonDeterministricOptions.ForceDeterministric;
    }
    public class ShapeContext {
        protected struct ShapeExpressionTuple:IEquatable<ShapeExpressionTuple> {
            public int Key;
            public ShapeExpressionBase? Expression;
            public ShapeExpressionTuple(int key) => Key = key;
            public ShapeExpressionTuple(ShapeExpressionBase expression, int key = -1) {
                Expression = expression;
                Key = key;
            }
            public bool Equals(ShapeExpressionTuple other) {
                if(Key == -1) 
                    return ReferenceEquals(Expression, other.Expression);
                return Key == other.Key;
            }
            public override int GetHashCode() {
                if(Key == -1) return Expression!.GetHashCode();
                return Key;
            }
        }

        protected int m_KeyIndex;
        protected HashSet<ShapeExpressionTuple> m_ShapeExpressionTuples = new();
        public ShapeExpressionBase ResolveIndex(int key) {
            if(!m_ShapeExpressionTuples.TryGetValue(new(key), out var tuple)) {
                throw new KeyNotFoundException("Expression not found");
            }
            return tuple.Expression ?? throw new NullReferenceException("Null expression??");
        }
        public int GetExpressionID(ShapeExpressionBase expression) {
            if(!m_ShapeExpressionTuples.TryGetValue(new(expression), out var tuple)) {
                m_ShapeExpressionTuples.Add(tuple = new(expression, ++m_KeyIndex));
            }
            return tuple.Key;
        }
    }
}
