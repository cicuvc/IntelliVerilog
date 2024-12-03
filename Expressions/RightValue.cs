using IntelliVerilog.Core.Analysis;
using IntelliVerilog.Core.DataTypes;
using IntelliVerilog.Core.Expressions.Algebra;
using System;
using System.Collections.Generic;
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
public abstract class AbstractValue :IEquatable<AbstractValue>, IRecursiveExpression {
    protected DataType m_ValueType;
    protected GCHandle m_WeakRef;
    public GCHandle WeakRef {
        get {
            if (!m_WeakRef.IsAllocated) m_WeakRef = GCHandle.Alloc(this, GCHandleType.Weak);
            return m_WeakRef;
        }
    }
    public virtual DataType Type { 
        get=> m_ValueType;
        set {
            if (m_ValueType.IsWidthSpecified) {
                throw new InvalidOperationException("Data type already specified");
            }
            m_ValueType = value;
            var shape = Shape.ToArray();
            shape[shape.Length - 1] = (int)value.WidthBits;
            Shape = new(shape);

            EnumerateSubNodes(PropagateDataType);
        }
    }
    public IAlg Algebra { get; }
    public ValueShape Shape { get; set; }
    public AbstractValue(DataType type, ValueShape shape , IAlg? algebra = null) {
        m_ValueType = type;
        Algebra = algebra ?? type.DefaultAlgebra;
        Shape = shape;
    }
    public abstract bool Equals(AbstractValue? other);
    public AbstractValue GetBitSelection(GenericIndices range) {
        var newShape = range.ResolveResultType(Shape);
        var newSelection = range.ResolveSelectionRange(Shape);
        return new GeneralBitsSelectionExpression(this, newShape, newSelection);
    }
    public AbstractValue GetCombination(int axis = -1,params AbstractValue[] values) {
        Debug.Assert(values.Length > 0);
        var newShape = values[0].Shape.ToArray();
        if (axis == -1) axis = newShape.Length - 1;

        foreach (var i in values) {
            var shape = i.Shape;
            for(var j = 0; j < shape.Length; j++) {
                if (j == axis) newShape[j] += shape[j];
                else if (shape[j] != newShape[j]) {
                    throw new InvalidOperationException("Incompatible shape");
                }
            }
        }
        return new GeneralCombinationExpression(values[0].Type, new(newShape), values);
    }
    public AbstractValue UnwrapCast() {
        if (!(this is IUntypedCastExpression castExpression)) return this;
        var castSource = castExpression.UntypedValue;
        while (castSource is IUntypedCastExpression cast) {
            castSource = cast.UntypedValue;
        }
        return castSource;
    }
    public RightValue<TData> Cast<TData>(TData? type = null) where TData : DataType, IDataType<TData> {
        type ??= TData.CreateDefault();
        return new CastExpression<TData>(type, Shape,this);
    }
    public abstract void EnumerateSubNodes(Action<AbstractValue> callback);

    private void PropagateDataType(AbstractValue subNode) {
        subNode.Type = m_ValueType;
    }

    ~AbstractValue() {
        if (m_WeakRef.IsAllocated) m_WeakRef.Free();
    }
}

public class GeneralCombinationExpression : AbstractValue{
    public AbstractValue[] SubExpressions { get; }
    public GeneralCombinationExpression(DataType type,ValueShape newShape,AbstractValue[] subExpressions) : base(type.CreateWithWidth((uint)newShape.TotalBits), newShape) {
        SubExpressions = subExpressions;
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
public interface IUntypedGeneralBitSelectionExpression: IUntypedUnaryExpression {
    SpecifiedIndices SelectedRange { get; }
}
public class GeneralBitsSelectionExpression : AbstractValue, IUntypedGeneralBitSelectionExpression {
    public SpecifiedIndices SelectedRange { get; }

    public AbstractValue UntypedValue { get; }

    public GeneralBitsSelectionExpression(AbstractValue baseExpression, ValueShape shape, SpecifiedIndices range) : base(baseExpression.Type.CreateWithWidth((uint)range.TotalBits), shape) {
        UntypedValue = baseExpression;
        SelectedRange = range;
    }
    public override bool Equals(AbstractValue? other) {
        if (other is GeneralBitsSelectionExpression expression) {
            if (expression.SelectedRange.Equals(SelectedRange) && expression.UntypedValue.Equals(UntypedValue)) {
                return true;
            }
        }
        return false;
    }

    public override void EnumerateSubNodes(Action<AbstractValue> callback) {
        callback(UntypedValue);
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
    ValueShape Shape { get; }
}

public interface IRightValueConvertible<TData>: IRightValueConvertible where TData : DataType, IDataType<TData> {
    RightValue<TData> RValue { get; }
}
public abstract class RightValue<TData>: AbstractValue, IRightValueOps<RightValue<TData>, TData>, IRightValueSelectionOps<TData>, IRightValueConvertible<TData>,IReferenceTraceObject where TData: DataType,IDataType<TData> {
    public IAlg<TData> TypedAlgebra => (IAlg<TData>)Algebra;
    public TData TypedType => (TData)Type;

    public RightValue<TData> RValue => this;

    public AbstractValue UntypedRValue => this;

    public RightValue(TData type, ValueShape shape,IAlg? algebra = null) : base(type, shape, algebra) {
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
        return new CastExpression<TCast>(TCast.CreateWidth(Type.WidthBits), Shape,this);
    }

    public RightValue<Bool> this[uint index] {
        get => throw new NotImplementedException();
        set => throw new NotImplementedException();
    }
    public RightValue<Bool> this[int index] {
        get {
            var indices = new GenericIndices(index);
            var newShape = indices.ResolveResultType(Shape);
            Debug.Assert(newShape.TotalBits == 1);

            return new CastExpression<Bool>(Bool.CreateDefault(), newShape,  GetBitSelection(new(index..(index + 1))));
        }
        set => throw new NotImplementedException();
    }
    public RightValue<TData> this[params GenericIndex[] range] {
        get {
            var indices = new GenericIndices(range);
            var selection = GetBitSelection(new(range));
            return new CastExpression<TData>(TData.CreateWidth((uint)selection.Shape.Last()), selection.Shape,selection );
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
    public LeftValue(TData type, ValueShape shape,IAlg? algebra = null) : base(type, shape, algebra) {
    }

    
}

public interface IUntypedCastExpression: IUntypedUnaryExpression {
    
}
public class CastExpression<TDest> : RightValue<TDest>, IUntypedCastExpression where TDest : DataType, IDataType<TDest> {

    public AbstractValue UntypedValue { get; }

    public CastExpression(TDest type, ValueShape shape, AbstractValue value) : base(type,shape, type.DefaultAlgebra) {
        UntypedValue = value;
    }

    public override bool Equals(AbstractValue? other) {
        if(other is CastExpression<TDest> expression) {
            return expression.UntypedValue.Equals(UntypedValue) && expression.Type.Equals(Type); 
        }
        return false;
    }

    public override void EnumerateSubNodes(Action<AbstractValue> callback) {
        callback(UntypedValue);
    }
}
