﻿using IntelliVerilog.Core.Analysis;
using IntelliVerilog.Core.Analysis.TensorLike;
using IntelliVerilog.Core.DataTypes;
using IntelliVerilog.Core.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace IntelliVerilog.Core.Expressions {
    public interface IRegRightValueWrapper {
        Reg UntyedReg { get; }
    }
    public class RegRightValueWrapper<TData> : RightValue<TData>, IRegRightValueWrapper where TData : DataType, IDataType<TData> {
        public Reg<TData> RegDef { get; }
        public Reg UntyedReg => RegDef;

        public override Lazy<TensorExpr> TensorExpression => throw new NotImplementedException();

        public RegRightValueWrapper(Reg<TData> Reg) : base((TData)Reg.UntypedType) {
            RegDef = Reg;
        }
        public override void EnumerateSubNodes(Action<AbstractValue> callback) { }

        public override bool Equals(AbstractValue? other) {
            if (other is RegRightValueWrapper<TData> expression) {
                return ReferenceEquals(expression.RegDef, RegDef);
            }
            return false;
        }
    }
    public abstract class Reg : ClockDrivenRegister, IAssignableValue, IReferenceTraceObject, IWireLike,IOverlappedObject, IRightValueConvertible {
        public IOverlappedObjectDesc Descriptor { get; set; }
        public abstract AbstractValue UntypedRValue { get; }

        public Reg(DataType type,ClockDomain? clockDom):base(type, clockDom) {
            var defaultName = $"R{Utility.GetRandomStringHex(16)}";
            Descriptor = new RegisterOverlappedDesc(defaultName, type) { this };

            Name = () => $"{Descriptor.InstanceName}_{((List<Reg>)Descriptor).IndexOf(this)}";

            var context = IntelliVerilogLocator.GetService<AnalysisContext>()!;
            var componentModel = context.GetComponentBuildingModel() ?? throw new NullReferenceException("Component building model not available");

            componentModel.AddEntity(this);
            componentModel.RegisterClockDomain(ClockDom);
        }
        public static ref Reg<TData> New<TData>(TData type, ReadOnlySpan<int> shape = default,ClockDomain? clockDom = null, bool noClockDomainCheck = false) where TData : DataType, IDataType<TData> {
            clockDom ??= ScopedLocator.GetService<ClockDomain>();
            Debug.Assert(clockDom != null);

            var context = IntelliVerilogLocator.GetService<AnalysisContext>()!;
            var componentModel = context.GetComponentBuildingModel(throwOnNull: true)!;

            
            var Reg = new Reg<TData>(TData.CreateWidth([.. shape, .. type.Shape]), clockDom) { 
                NoClockDomainCheck = noClockDomainCheck
            };

            var pointerStorage = componentModel.RegisterReg(Reg);
            return ref Unsafe.As<IReferenceTraceObject, Reg<TData>>(ref pointerStorage.Pointer);
        }
        public static ref Reg<TData> Next<TData>(TData type, RightValue<TData> inputValue,ClockDomain? clockDom = null, bool noClockDomainCheck = false) where TData : DataType, IDataType<TData> {
            ref var register = ref New(inputValue.TypedType, [], clockDom, noClockDomainCheck);

            var context = IntelliVerilogLocator.GetService<AnalysisContext>()!;
            var componentModel = context.GetComponentBuildingModel(throwOnNull: true)!;

            var returnAddressTracker = IntelliVerilogLocator.GetService<ReturnAddressTracker>()!;
            var returnAddress = returnAddressTracker.TrackReturnAddress(inputValue, paramIndex: 3);
            componentModel.AssignSubModuleConnections(register, inputValue, new(Array.Empty<GenericIndex>()), returnAddress);

            return ref register!;
        }

    }
    public class ExpressedReg<TData> : Reg<TData>, IExpressionAssignedIoType where TData : DataType, IDataType<TData> {
        public ExpressedReg(RightValue<TData> expression, ClockDomain? clockDom) : base(expression.TypedType, clockDom) {
            Expression = expression;
        }

        public RightValue<TData> Expression { get; }
        public AbstractValue UntypedExpression => Expression;
        public override RightValue<TData> RValue => Expression;
    }
    public class InvalidClockDomain :ClockDomain{
        public static InvalidClockDomain Instance { get; } = new();
        public InvalidClockDomain() :base("invalid", new DummyClockReset()){ }
    }
    public class Reg<TData> : Reg,
        IRightValueOps<Reg<TData>, TData>,
        IRightValueSelectionOps<TData>,
        ILeftValueOps<TData>, IRightValueConvertible<TData> where TData : DataType, IDataType<TData> {
        protected RegRightValueWrapper<TData>? m_RValueCache = null;
        public virtual RightValue<TData> RValue {
            get {
                if (m_RValueCache is null) m_RValueCache = new(this);
                return m_RValueCache;
            }
        }
        public override AbstractValue UntypedRValue => RValue;
        public Reg(TData type, ClockDomain? clockDom) : base(type, clockDom) {
        }

        public static implicit operator Reg<TData>(RightValue<TData> value) {
            return new ExpressedReg<TData>(value, InvalidClockDomain.Instance);
        }

        public RightValue<TData> this[params GenericIndex[] range] {
            get => RValue[range];
            set {
                var returnTracker = IntelliVerilogLocator.GetService<ReturnAddressTracker>()!;
                var returnAddress = returnTracker.TrackReturnAddress(this);
                var analysisContext = IntelliVerilogLocator.GetService<AnalysisContext>()!;
                var currentModel = analysisContext.CurrentComponent?.InternalModel as ComponentBuildingModel;

                if (currentModel == null) return;

                var wrapper = new ExpressedReg<TData>(value, ClockDom);

                currentModel.AssignSubModuleConnections(this, wrapper, new(range), returnAddress);
            }
        }

        public static RightValue<TData> operator +(Reg<TData> lhs, Reg<TData> rhs) {
            return lhs.RValue + rhs.RValue;
        }

        public static RightValue<TData> operator -(Reg<TData> lhs, Reg<TData> rhs) {
            return lhs.RValue - rhs.RValue;
        }

        public static RightValue<TData> operator *(Reg<TData> lhs, Reg<TData> rhs) {
            return lhs.RValue * rhs.RValue;
        }

        public static RightValue<TData> operator /(Reg<TData> lhs, Reg<TData> rhs) {
            return lhs.RValue / rhs.RValue;
        }

        public static RightValue<TData> operator &(Reg<TData> lhs, Reg<TData> rhs) {
            return lhs.RValue & rhs.RValue;
        }

        public static RightValue<TData> operator |(Reg<TData> lhs, Reg<TData> rhs) {
            return lhs.RValue | rhs.RValue;
        }

        public static RightValue<TData> operator ^(Reg<TData> lhs, Reg<TData> rhs) {
            return lhs.RValue ^ rhs.RValue;
        }

        public static RightValue<Bool> operator ==(Reg<TData> lhs, Reg<TData> rhs) {
            return lhs.RValue == rhs.RValue;
        }

        public static RightValue<Bool> operator !=(Reg<TData> lhs, Reg<TData> rhs) {
            return lhs.RValue != rhs.RValue;
        }

        public static RightValue<Bool> operator >(Reg<TData> lhs, Reg<TData> rhs) {
            return lhs.RValue > rhs.RValue;
        }

        public static RightValue<Bool> operator <(Reg<TData> lhs, Reg<TData> rhs) {
            return lhs.RValue < rhs.RValue;
        }

        public static RightValue<Bool> operator >=(Reg<TData> lhs, Reg<TData> rhs) {
            return lhs.RValue >= rhs.RValue;
        }

        public static RightValue<Bool> operator <=(Reg<TData> lhs, Reg<TData> rhs) {
            return lhs.RValue <= rhs.RValue;
        }

        public static RightValue<TData> operator ~(Reg<TData> lhs) {
            return ~lhs.RValue;
        }
        public override bool Equals(object? obj) {
            return ReferenceEquals(this, obj);
        }
        public override int GetHashCode() {
            return base.GetHashCode();
        }

    }
}
