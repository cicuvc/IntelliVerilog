using IntelliVerilog.Core.Analysis;
using IntelliVerilog.Core.Components;
using IntelliVerilog.Core.DataTypes;
using System;
using System.Collections.Immutable;
using System.Reflection;

namespace IntelliVerilog.Core.Expressions {
    public abstract class IoComponent : IUntypedPort, IRightValueConvertible { 
        public abstract AbstractValue UntypedRValue { get; }
        public abstract IoPortDirection Direction { get; }
        public abstract GeneralizedPortFlags Flags { get; }
        public abstract Size Shape { get; }
        public virtual Func<string> Name { get; set; } = () => "<unnamed port>";
        public override string ToString() {
            return $"{GetType().Name}: {Name()}";
        }
        //public abstract void InitUnspecifiedLocated(IoBundle parent, ComponentBase root, IoMemberInfo member);
    }
    
    public abstract class IoComponent<TData> :
        IoComponent,
        IRightValueOps<IoComponent<TData>, TData>,
        IRightValueSelectionOps<TData>,
        ILeftValueOps<TData> , IRightValueConvertible<TData>
        where TData : DataType, IDataType<TData> {

        protected IoRightValueWrapper<TData>? m_RightValueCache = null!;
        public virtual RightValue<TData> RValue {
            get {
                if (m_RightValueCache is null) {
                    if(this is IUntypedConstructionPort constructed) {
                        m_RightValueCache = new(constructed);
                    } else {
                        throw new NotSupportedException();
                    }
                    
                }
                return m_RightValueCache;
            } 
        }
        public override AbstractValue UntypedRValue => RValue;
        
        public static RightValue<TData> operator +(IoComponent<TData> lhs, IoComponent<TData> rhs) {
            return lhs.RValue + rhs.RValue;
        }

        public static RightValue<TData> operator -(IoComponent<TData> lhs, IoComponent<TData> rhs) {
            return lhs.RValue - rhs.RValue;
        }

        public static RightValue<TData> operator *(IoComponent<TData> lhs, IoComponent<TData> rhs) {
            return lhs.RValue * rhs.RValue;
        }

        public static RightValue<TData> operator /(IoComponent<TData> lhs, IoComponent<TData> rhs) {
            return lhs.RValue / rhs.RValue;
        }

        public static RightValue<TData> operator &(IoComponent<TData> lhs, IoComponent<TData> rhs) {
            return lhs.RValue & rhs.RValue;
        }

        public static RightValue<TData> operator |(IoComponent<TData> lhs, IoComponent<TData> rhs) {
            return lhs.RValue | rhs.RValue;
        }

        public static RightValue<TData> operator ^(IoComponent<TData> lhs, IoComponent<TData> rhs) {
            return lhs.RValue ^ rhs.RValue;
        }
        public static implicit operator RightValue<TData>(IoComponent<TData> component) {
            return component.RValue;
        }
        public static implicit operator LeftValue<TData>(IoComponent<TData> component) {
            throw new NotImplementedException();
        }

        public static RightValue<Bool> operator ==(IoComponent<TData> lhs, IoComponent<TData> rhs) {
            return lhs.RValue == rhs.RValue;
        }

        public static RightValue<Bool> operator !=(IoComponent<TData> lhs, IoComponent<TData> rhs) {
            return lhs.RValue != rhs.RValue;
        }

        public static RightValue<Bool> operator >(IoComponent<TData> lhs, IoComponent<TData> rhs) {
            return lhs.RValue > rhs.RValue;
        }

        public static RightValue<Bool> operator <(IoComponent<TData> lhs, IoComponent<TData> rhs) {
            return lhs.RValue < rhs.RValue;
        }

        public static RightValue<Bool> operator >=(IoComponent<TData> lhs, IoComponent<TData> rhs) {
            return lhs.RValue >= rhs.RValue;
        }

        public static RightValue<Bool> operator <=(IoComponent<TData> lhs, IoComponent<TData> rhs) {
            return lhs.RValue <= rhs.RValue;
        }

        public static RightValue<TData> operator ~(IoComponent<TData> lhs) {
            return ~lhs.RValue;
        }

        public abstract RightValue<TData> this[params GenericIndex[] range] {
            get;set;
        }
        public override bool Equals(object? obj) {
            return ReferenceEquals(this, obj);
        }
        public override int GetHashCode() {
            return base.GetHashCode();
        }

    }

}
