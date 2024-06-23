using IntelliVerilog.Core.Analysis;
using IntelliVerilog.Core.Components;
using IntelliVerilog.Core.DataTypes;
using System;
using System.Reflection;

namespace IntelliVerilog.Core.Expressions {
    public abstract class IoComponent : IUntypedPort { 
        public abstract AbstractValue UntypedRValue { get; }
        public abstract IoPortDirection Direction { get; }
        public abstract GeneralizedPortFlags Flags { get; }
        public virtual Func<string> Name { get; set; } = () => "<unnamed port>";

        //public abstract void InitUnspecifiedLocated(IoBundle parent, ComponentBase root, IoMemberInfo member);
    }
    
    public abstract class IoComponent<TData> :
        IoComponent,
        IRightValueOps<IoComponent<TData>, TData>,
        IRightValueSelectionOps<TData>,
        ILeftValueOps<TData> 
        where TData : DataType, IDataType<TData> {

        protected IoRightValueWrapper<TData>? m_RightValueCache = null!;
        public virtual IoRightValueWrapper<TData> RValue {
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

        public RightValue<Bool> this[uint index] {
            get => RValue[index];
            set => RValue[index] = value;
        }
        public abstract RightValue<Bool> this[int index] {
            get;
            set;
        }
        public abstract RightValue<TData> this[Range range] {
            get;set;
        }
    }

}
