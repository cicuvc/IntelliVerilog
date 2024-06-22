using IntelliVerilog.Core.Analysis;
using IntelliVerilog.Core.DataTypes;
using IntelliVerilog.Core.Expressions.Algebra;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.Expressions;

public interface IRecursiveExpression {
    void EnumerateSubNodes(Action<AbstractValue> callback);
}
public abstract class AbstractValue :IEquatable<AbstractValue>, IRecursiveExpression {
    protected DataType m_ValueType;
    public virtual DataType Type { 
        get=> m_ValueType;
        set {
            if (m_ValueType.IsWidthSpecified) {
                throw new InvalidOperationException("Data type already specified");
            }
            m_ValueType = value;

            EnumerateSubNodes(PropagateDataType);
        }
    }
    public IAlg Algebra { get; }
    public AbstractValue(DataType type, IAlg? algebra = null) {
        m_ValueType = type;
        Algebra = algebra ?? type.DefaultAlgebra;
    }
    public abstract bool Equals(AbstractValue? other);
    public AbstractValue GetBitSelection(Range range) {
        return new GeneralBitsSelectionExpression(this, new(range, (int)Type.WidthBits));
    }
    public AbstractValue GetCombination(params AbstractValue[] values) {
        return new GeneralCombinationExpression(values[0].Type, values);
    }

    public abstract void EnumerateSubNodes(Action<AbstractValue> callback);

    private void PropagateDataType(AbstractValue subNode) {
        subNode.Type = m_ValueType;
    }
}

public class GeneralCombinationExpression : AbstractValue{
    public AbstractValue[] SubExpressions { get; }
    public GeneralCombinationExpression(DataType type,AbstractValue[] subExpressions) : base(type.CreateWithWidth((uint)subExpressions.Sum(e => e.Type.WidthBits))) {
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
public class GeneralBitsSelectionExpression : AbstractValue, IUntypedUnaryExpression {
    public AbstractValue BaseExpression { get; }
    public SpecifiedRange SelectedRange { get; }

    public AbstractValue UntypedValue => BaseExpression;

    public GeneralBitsSelectionExpression(AbstractValue baseExpression, SpecifiedRange range) : base(baseExpression.Type.CreateWithWidth((uint)range.BitWidth)) {
        BaseExpression = baseExpression;
        SelectedRange = range;
    }
    public override bool Equals(AbstractValue? other) {
        if (other is GeneralBitsSelectionExpression expression) {
            if (expression.SelectedRange.Equals(SelectedRange) && expression.BaseExpression.Equals(BaseExpression)) {
                return true;
            }
        }
        return false;
    }

    public override void EnumerateSubNodes(Action<AbstractValue> callback) {
        callback(BaseExpression);
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
}

public interface IRightValueSelectionOps<TData> where TData: DataType, IDataType<TData> {
    public RightValue<Bool> this[uint index] { get; }
    public RightValue<Bool> this[int index] { get; }
    public RightValue<TData> this[Range range] { get; }
}
public interface ILeftValueOps<TData> where TData : DataType, IDataType<TData> {
    public RightValue<Bool> this[uint index] { set; }
    public RightValue<Bool> this[int index] { set; }
    public RightValue<TData> this[Range range] { set; }
}



public abstract class RightValue<TData>: AbstractValue, IRightValueOps<RightValue<TData>, TData>, IRightValueSelectionOps<TData> where TData: DataType,IDataType<TData> {
    public IAlg<TData> TypedAlgebra => (IAlg<TData>)Algebra;
    public TData TypedType => (TData)Type;
    public RightValue(TData type, IAlg? algebra = null) : base(type, algebra) {
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
        var model = context.CurrentComponent.InternalModel as ComponentBuildingModel;
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

    public RightValue<Bool> this[uint index] {
        get => throw new NotImplementedException();
        set => throw new NotImplementedException();
    }
    public RightValue<Bool> this[int index] {
        get => new CastExpression<Bool>(Bool.CreateDefault(), GetBitSelection(index..(index+1)));
        set => throw new NotImplementedException();
    }
    public RightValue<TData> this[Range range] {
        get => new CastExpression<TData>((TData)Type.CreateWithWidth((uint)range.GetOffsetAndLength((int)Type.WidthBits).Length), GetBitSelection(range));
        set => throw new NotImplementedException();
    }
    public TEnum ToSwitch<TEnum>() where TEnum : unmanaged, Enum {
        var context = IntelliVerilogLocator.GetService<AnalysisContext>()!;
        var model = context.CurrentComponent.InternalModel as ComponentBuildingModel;
        var returnTracker = IntelliVerilogLocator.GetService<ReturnAddressTracker>()!;
        var returnAddress = returnTracker.TrackReturnAddress(this, paramIndex: 2);

        return model.Behavior.NotifySwitchEnter<TEnum>(returnAddress, this);
    }
}



public abstract class LeftValue<TData> : RightValue<TData>, ILeftValueOps<TData> where TData : DataType, IDataType<TData> {
    public LeftValue(TData type, IAlg? algebra = null) : base(type, algebra) {
    }

    
}

public class CastExpression<TDest> : RightValue<TDest>, IUntypedUnaryExpression where TDest : DataType, IDataType<TDest> {
    public AbstractValue SourceValue { get; }

    public AbstractValue UntypedValue => SourceValue;

    public CastExpression(TDest type, AbstractValue value) : base(type, type.DefaultAlgebra) {
        SourceValue = value;
    }

    public override bool Equals(AbstractValue? other) {
        if(other is CastExpression<TDest> expression) {
            return expression.SourceValue.Equals(SourceValue) && expression.Type == Type; 
        }
        return false;
    }

    public override void EnumerateSubNodes(Action<AbstractValue> callback) {
        callback(SourceValue);
    }
}
