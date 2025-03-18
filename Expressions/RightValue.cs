using IntelliVerilog.Core.Analysis;
using IntelliVerilog.Core.Analysis.TensorLike;
using IntelliVerilog.Core.DataTypes;
using IntelliVerilog.Core.DataTypes.Shape;
using IntelliVerilog.Core.Expressions.Algebra;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.Expressions;

public interface IRecursiveExpression {
    void EnumerateSubNodes(Action<AbstractValue> callback);
}
public abstract class AbstractValue : IEquatable<AbstractValue>, IRecursiveExpression {
    public DataType UntypedType { get; }
    public IAlg Algebra => UntypedType.DefaultAlgebra;
    /// <summary>
    /// Untyped tensor expressions mantains shape and calculation graph of abstract value. 
    /// </summary>
    public abstract Lazy<TensorExpr> TensorExpression { get; }
    public AbstractValue(DataType type) {
        UntypedType = type;
    }

    public abstract bool Equals(AbstractValue? other);
    public AbstractValue Reshape(ReadOnlySpan<int> newShape) {
        var newDataType = UntypedType.CreateShapedType(MemoryMarshal.Cast<int, ShapeIndexValue>(newShape));

        if(UntypedType.IsShapeComplete) {
            Debug.Assert(TensorExpression.IsEvaluated);
            var derivedExpression = TensorExpr.Reshape(TensorExpression.Value, newShape);
            return new GeneralTransformExpression(newDataType, derivedExpression, this);
        } else {
            var newShapeImm = newShape.ToImmutableArray();
            var expressionLazy = new Lazy<TensorExpr>(() => TensorExpr.Reshape(TensorExpression.Value, newShapeImm.AsSpan()));
            return new GeneralTransformExpression(newDataType, expressionLazy, this);
        }
    }
    public AbstractValue View(ReadOnlySpan<GenericIndex> indices) {
        var tensorIndices = indices.ToImmutableArray().Select(e => e.ConstIndex).ToArray();

        var newDataType = UntypedType.CreateShapedType(ShapeEvaluation.View(UntypedType.Shape, indices).Span);

        if(UntypedType.IsShapeComplete) {
            Debug.Assert(TensorExpression.IsEvaluated);
            var derivedExpression = TensorExpr.View(TensorExpression.Value, tensorIndices);
            return new GeneralTransformExpression(newDataType, derivedExpression, this);
        } else {
            var expressionLazy = new Lazy<TensorExpr>(() => TensorExpr.View(TensorExpression.Value, tensorIndices));
            return new GeneralTransformExpression(newDataType, expressionLazy, this);
        }
    }
    public static AbstractValue GetConcat(ReadOnlySpan<AbstractValue> values, int axis) {
        var immValues = values.ToImmutableArray();

        var containsUndetermined = immValues.Any(e => !e.UntypedType.IsShapeComplete);

        var expressions = immValues.Select(e => e.TensorExpression.Value).ToArray();

        var newType = values[0].UntypedType.CreateShapedType(ShapeEvaluation.Concat(immValues.Select(e=>e.UntypedType.Shape).ToArray(), axis).Span);

        if(containsUndetermined) {
            var concatLazy = new Lazy<TensorExpr>(() => TensorExpr.Concat(expressions, axis));
            return new GeneralCombinationExpression(newType, concatLazy, values.ToImmutableArray());

        } else {
            var concatExpression = TensorExpr.Concat(expressions, axis);
            return new GeneralCombinationExpression(newType, concatExpression, values.ToImmutableArray());
        }
        
    }
    public AbstractValue UnwrapCast() {
        if (!(this is IUntypedCastExpression castExpression)) return this;
        var castSource = castExpression.UntypedBaseValue;
        while (castSource is IUntypedCastExpression cast) {
            castSource = cast.UntypedBaseValue;
        }
        return castSource;
    }
    public RightValue<TData> Cast<TData>(TData? type = null) where TData : DataType, IDataType<TData> {
        var nonNullType = type ?? TData.CreateDefault();
        if(nonNullType is null) throw new InvalidOperationException($"{typeof(TData).Name} doesn't have default shape");
        return new CastExpression<TData>(nonNullType, this);
    }
    public abstract void EnumerateSubNodes(Action<AbstractValue> callback);
}

public class GeneralCombinationExpression : AbstractValue{
    public ImmutableArray<AbstractValue> SubExpressions { get; }
    public override Lazy<TensorExpr> TensorExpression { get; }
    public GeneralCombinationExpression(DataType type, Lazy<TensorExpr> expression, IEnumerable<AbstractValue> baseValue) : base(type) {
        SubExpressions = baseValue.ToImmutableArray();
        TensorExpression = expression;
    }
    public override bool Equals(AbstractValue? other) {
        if (other is GeneralCombinationExpression combExpression) {
            return SubExpressions.SequenceEqual(combExpression.SubExpressions);
        }
        return false;
    }

    public override void EnumerateSubNodes(Action<AbstractValue> callback) {
        foreach (var i in SubExpressions) callback(i);
    }
}

public class GeneralTransformExpression : AbstractValue, IUntypedUnaryExpression {
    public override Lazy<TensorExpr> TensorExpression { get; }
    public AbstractValue UntypedBaseValue { get; }

    public GeneralTransformExpression(DataType type, Lazy<TensorExpr> expression, AbstractValue baseValue) :base(type) {
        UntypedBaseValue = baseValue;
        TensorExpression = expression;
    }
    public override bool Equals(AbstractValue? other) {
        if(other is not GeneralTransformExpression expression) return false;
        return expression.TensorExpression.Equals(TensorExpression);
    }

    public override void EnumerateSubNodes(Action<AbstractValue> callback) {
        callback(UntypedBaseValue);
    }
}

public interface IRightValueOps<TValue, TData> where TValue: IRightValueOps<TValue, TData> where TData: DataType, IDataType<TData> {
    static abstract RightValue<TData> operator +(TValue lhs, TValue rhs);
    static abstract RightValue<TData> operator -(TValue lhs, TValue rhs);
    static abstract RightValue<TData> operator /(TValue lhs, TValue rhs);
    static abstract RightValue<TData> operator *(TValue lhs, TValue rhs);
    static abstract RightValue<TData> operator ^(TValue lhs, TValue rhs);
    static abstract RightValue<TData> operator &(TValue lhs, TValue rhs);
    static abstract RightValue<TData> operator |(TValue lhs, TValue rhs);
    static abstract RightValue<Bool> operator ==(TValue lhs, TValue rhs);
    static abstract RightValue<Bool> operator !=(TValue lhs, TValue rhs);
    static abstract RightValue<Bool> operator >(TValue lhs, TValue rhs);
    static abstract RightValue<Bool> operator <(TValue lhs, TValue rhs);
    static abstract RightValue<Bool> operator >=(TValue lhs, TValue rhs);
    static abstract RightValue<Bool> operator <=(TValue lhs, TValue rhs);
    static abstract RightValue<TData> operator ~(TValue lhs);

}

public interface IRightValueSelectionOps<TData> where TData: DataType, IDataType<TData> {
    public RightValue<TData> this[params GenericIndex[] range] { get; }
}
public interface ILeftValueOps<TData> where TData : DataType, IDataType<TData> {
    public RightValue<TData> this[params GenericIndex[] range] { set; }
}

public interface IRightValueConvertible {
    AbstractValue UntypedRValue { get; }
}

public interface IRightValueConvertible<TData>: IRightValueConvertible where TData : DataType, IDataType<TData> {
    RightValue<TData> RValue { get; }
}
public abstract class RightValue<TData>: AbstractValue, IRightValueOps<RightValue<TData>, TData>, IRightValueSelectionOps<TData>, IRightValueConvertible<TData>,IReferenceTraceObject where TData: DataType,IDataType<TData> {
    public IAlg<TData> TypedAlgebra => (IAlg<TData>)Algebra;
    public TData TypedType => (TData)UntypedType;

    public RightValue<TData> RValue => this;

    public AbstractValue UntypedRValue => this;

    public RightValue(TData type) : base(type) {
    }
    protected static void CheckArithmeticCompatiblity(RightValue<TData> lhs, RightValue<TData> rhs) {
        if (lhs.Algebra != rhs.Algebra) {
            throw new ArithmeticException($"Attempt to apply arithmetic operation over algebra {lhs.Algebra} and {rhs.Algebra}") ;
        }
    }
    
    public static RightValue<TData> operator +(RightValue<TData> lhs, RightValue<TData> rhs) {
        CheckArithmeticCompatiblity(lhs, rhs);
        return lhs.TypedAlgebra.AddExpression(lhs, rhs);
    }
    public static RightValue<TData> operator -(RightValue<TData> lhs, RightValue<TData> rhs) {
        CheckArithmeticCompatiblity(lhs, rhs);
        return lhs.TypedAlgebra.SubExpression(lhs, rhs);
    }
    public static RightValue<TData> operator *(RightValue<TData> lhs, RightValue<TData> rhs) {
        CheckArithmeticCompatiblity(lhs, rhs);
        return lhs.TypedAlgebra.MulExpression(lhs, rhs);
    }
    public static RightValue<TData> operator /(RightValue<TData> lhs, RightValue<TData> rhs) {
        CheckArithmeticCompatiblity(lhs, rhs);
        return lhs.TypedAlgebra.DivExpression(lhs, rhs);
    }
    public static RightValue<TData> operator ^(RightValue<TData> lhs, RightValue<TData> rhs) {
        CheckArithmeticCompatiblity(lhs, rhs);
        return lhs.TypedAlgebra.XorExpression(lhs, rhs);
    }
    public static RightValue<TData> operator |(RightValue<TData> lhs, RightValue<TData> rhs) {
        CheckArithmeticCompatiblity(lhs, rhs);
        return lhs.TypedAlgebra.OrExpression(lhs, rhs);
    }
    public static RightValue<TData> operator &(RightValue<TData> lhs, RightValue<TData> rhs) {
        CheckArithmeticCompatiblity(lhs, rhs);
        return lhs.TypedAlgebra.AndExpression(lhs, rhs);
    }
    public static implicit operator bool(RightValue<TData> lhs) {
        var context = IntelliVerilogLocator.GetService<AnalysisContext>()!;
        var model = context.GetComponentBuildingModel(throwOnNull: true)!;
        var returnTracker = IntelliVerilogLocator.GetService<ReturnAddressTracker>()!;
        var returnAddress = returnTracker.TrackReturnAddress(lhs, paramIndex: 2);

        return model.Behavior.NotifyConditionEvaluation(returnAddress, lhs);
    }
    public static implicit operator RightValue<TData>(uint value) {
        if(value.Const() is RightValue<TData> result) {
            return result;
        }
        throw new InvalidCastException($"Incompatible cast from unsigned int to {TData.CreateDefault()}");
    }

    public static RightValue<Bool> operator ==(RightValue<TData> lhs, RightValue<TData> rhs) {
        return lhs.TypedAlgebra.EqualExpression(lhs, rhs);
    }

    public static RightValue<Bool> operator !=(RightValue<TData> lhs, RightValue<TData> rhs) {
        return lhs.TypedAlgebra.NonEqualExpression(lhs, rhs);
    }

    public static RightValue<Bool> operator >(RightValue<TData> lhs, RightValue<TData> rhs) {
        return lhs.TypedAlgebra.GreaterExpression(lhs, rhs);
    }

    public static RightValue<Bool> operator <(RightValue<TData> lhs, RightValue<TData> rhs) {
        return lhs.TypedAlgebra.LessExpression(lhs, rhs);
    }

    public static RightValue<Bool> operator >=(RightValue<TData> lhs, RightValue<TData> rhs) {
        return lhs.TypedAlgebra.GreaterEqualExpression(lhs, rhs);
    }

    public static RightValue<Bool> operator <=(RightValue<TData> lhs, RightValue<TData> rhs) {
        return lhs.TypedAlgebra.LessEqualExpression(lhs, rhs);
    }

    public static RightValue<TData> operator ~(RightValue<TData> lhs) {
        return lhs.TypedAlgebra.NotExpression(lhs);
    }
    public RightValue<TCast> Cast<TCast>() where TCast : DataType, IDataType<TCast> {
        return new CastExpression<TCast>(TCast.CreateWidth(UntypedType.Shape.Span),this);
    }

    public RightValue<TData> this[params GenericIndex[] range] {
        get {
            var selection = View(range);
            return new CastExpression<TData>((TData)selection.UntypedType, selection);
        }
        set => throw new NotImplementedException();
    }
    public TEnum ToSwitch<TEnum>() where TEnum : unmanaged, Enum {
        var context = IntelliVerilogLocator.GetService<AnalysisContext>()!;
        var model = context.GetComponentBuildingModel(throwOnNull: true)!;
        var returnTracker = IntelliVerilogLocator.GetService<ReturnAddressTracker>()!;
        var returnAddress = returnTracker.TrackReturnAddress(this, paramIndex: 2);

        return model.Behavior.NotifySwitchEnter<TEnum>(returnAddress, this);
    }
    public override int GetHashCode() {
        return base.GetHashCode();
    }
    public override bool Equals(object? obj) {
        return ReferenceEquals(this, obj);
    }
}



public abstract class LeftValue<TData> : RightValue<TData>, ILeftValueOps<TData> where TData : DataType, IDataType<TData> {
    public LeftValue(TData type) : base(type) {
    }

    
}

public interface IUntypedCastExpression: IUntypedUnaryExpression {
    
}
public class CastExpression<TDest> : RightValue<TDest>, IUntypedCastExpression where TDest : DataType, IDataType<TDest> {

    public AbstractValue UntypedBaseValue { get; }
    public override Lazy<TensorExpr> TensorExpression { get; }
    public CastExpression(TDest type, AbstractValue value) : base(type) {
        UntypedBaseValue = value;
        TensorExpression = value.TensorExpression;
    }
    public override bool Equals(AbstractValue? other) {
        if(other is CastExpression<TDest> expression) {
            return expression.UntypedBaseValue.Equals(UntypedBaseValue) && expression.UntypedType.Equals(UntypedType); 
        }
        return false;
    }

    public override void EnumerateSubNodes(Action<AbstractValue> callback) {
        callback(UntypedBaseValue);
    }
}
