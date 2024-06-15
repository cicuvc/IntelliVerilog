using IntelliVerilog.Core.DataTypes;
using IntelliVerilog.Core.Expressions.Algebra;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.Expressions;
public abstract class AbstractValue :IEquatable<AbstractValue> {
    public DataType Type { get; }
    public IAlg Algebra { get; }
    public AbstractValue(DataType type, IAlg? algebra = null) {
        Type = type;
        Algebra = algebra ?? type.DefaultAlgebra;
    }

    public abstract bool Equals(AbstractValue? other);
}

public interface IRightValueOps<TValue, TData> where TValue: IRightValueOps<TValue, TData> where TData: DataType {
    static abstract RightValue<TData> operator +(TValue lhs, TValue rhs);
    static abstract RightValue<TData> operator -(TValue lhs, TValue rhs);
    static abstract RightValue<TData> operator /(TValue lhs, TValue rhs);
    static abstract RightValue<TData> operator *(TValue lhs, TValue rhs);
    static abstract RightValue<TData> operator ^(TValue lhs, TValue rhs);
    static abstract RightValue<TData> operator &(TValue lhs, TValue rhs);
    static abstract RightValue<TData> operator |(TValue lhs, TValue rhs);
}

public interface IRightValueSelectionOps<TData> where TData: DataType {
    public RightValue<Bool> this[uint index] { get; }
    public RightValue<Bool> this[int index] { get; }
    public RightValue<TData> this[Range range] { get; }
}
public interface ILeftValueOps<TData> where TData : DataType {
    public RightValue<Bool> this[uint index] { set; }
    public RightValue<Bool> this[int index] { set; }
    public RightValue<TData> this[Range range] { set; }
}

public abstract class RightValue<TData>: AbstractValue, IRightValueOps<RightValue<TData>, TData>, IRightValueSelectionOps<TData> where TData: DataType {
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
        return lhs.TypedAlgebra.BoolCast(lhs);
    }

    public RightValue<Bool> this[uint index] {
        get => throw new NotImplementedException();
        set => throw new NotImplementedException();
    }
    public RightValue<Bool> this[int index] {
        get => new CastExpression<TData, Bool>(Bool.CreateDefault(), TypedAlgebra.GetSelectionValue(this, index));
        set => throw new NotImplementedException();
    }
    public RightValue<TData> this[Range range] {
        get => throw new NotImplementedException();
        set => throw new NotImplementedException();
    }
}

public abstract class LeftValue<TData> : RightValue<TData>, ILeftValueOps<TData> where TData : DataType {
    public LeftValue(TData type, IAlg? algebra = null) : base(type, algebra) {
    }

    
}

public class CastExpression<TSource, TDest> : RightValue<TDest>, IUntypedUnaryExpression where TSource : DataType where TDest : DataType {
    public RightValue<TSource> SourceValue { get; }

    public AbstractValue UntypedValue => SourceValue;

    public CastExpression(TDest type, RightValue<TSource> value) : base(type, type.DefaultAlgebra) {
        SourceValue = value;
    }

    public override bool Equals(AbstractValue? other) {
        if(other is CastExpression<TSource, TDest> expression) {
            return expression.SourceValue.Equals(SourceValue); 
        }
        return false;
    }
}
