using IntelliVerilog.Core.Analysis;
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
        public RegRightValueWrapper(Reg<TData> Reg) : base((TData)Reg.UntypedType) {
            RegDef = Reg;
        }
        public override void EnumerateSubNodes(Action<AbstractValue> callback) { }

        public override bool Equals(AbstractValue? other) {
            if (other is RegRightValueWrapper<TData> expression) {
                return expression.RegDef == RegDef;
            }
            return false;
        }
    }
    public abstract class Reg : ClockDrivenRegister, IAssignableValue, IReferenceTraceObject, IWireLike,IOverlappedObject, IRightValueConvertible {
        public IOverlappedObjectDesc Descriptor { get; set; }
        public abstract AbstractValue UntypedRValue { get; }

        public Reg(DataType type, ClockDomain? clockDom):base(type, clockDom) {
            var defaultName = $"R{Utility.GetRandomStringHex(16)}";
            Descriptor = new RegisterOverlappedDesc(defaultName, type) { this };

            Name = () => $"{Descriptor.InstanceName}_{((List<Reg>)Descriptor).IndexOf(this)}";

            var context = IntelliVerilogLocator.GetService<AnalysisContext>()!;
            var componentModel = context.CurrentComponent!.InternalModel as ComponentBuildingModel;

            componentModel.AddEntity(this);
            componentModel.RegisterClockDomain(ClockDom);
        }
        public static ref Reg<TData> New<TData>(TData type, ClockDomain? clockDom = null, bool noClockDomainCheck = false) where TData : DataType, IDataType<TData> {
            clockDom ??= ScopedLocator.GetService<ClockDomain>();
            Debug.Assert(clockDom != null);

            var context = IntelliVerilogLocator.GetService<AnalysisContext>()!;
            var componentModel = context.CurrentComponent!.InternalModel as ComponentBuildingModel;

            var Reg = new Reg<TData>(type, clockDom) { 
                NoClockDomainCheck = noClockDomainCheck
            };

            var pointerStorage = componentModel.RegisterReg(Reg);
            return ref Unsafe.As<IReferenceTraceObject, Reg<TData>>(ref pointerStorage.Pointer);
        }
        public static ref Reg<TData> Next<TData>(TData type, RightValue<TData> inputValue ,ClockDomain? clockDom = null, bool noClockDomainCheck = false) where TData : DataType, IDataType<TData> {
            ref var register = ref New(type, clockDom, noClockDomainCheck);

            var context = IntelliVerilogLocator.GetService<AnalysisContext>()!;
            var componentModel = context.CurrentComponent!.InternalModel as ComponentBuildingModel;

            var returnAddressTracker = IntelliVerilogLocator.GetService<ReturnAddressTracker>()!;
            var returnAddress = returnAddressTracker.TrackReturnAddress(inputValue, paramIndex: 3);
            componentModel.AssignSubModuleConnections(register, inputValue, .., returnAddress);

            return ref register;
        }

        public AssignmentInfo CreateAssignmentInfo() {
            return new RegAssignmentInfo(this);
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
    public class Reg<TData> : Reg,
        IRightValueOps<Reg<TData>, TData>,
        IRightValueSelectionOps<TData>,
        ILeftValueOps<TData> where TData : DataType, IDataType<TData> {
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
            return new ExpressedReg<TData>(value,null);
        }
        public RightValue<Bool> this[uint index] {
            get => RValue[index];
            set {
                throw new NotImplementedException();
            }
        }

        public RightValue<Bool> this[int index] {
            get => RValue[index];
            set {
                var returnTracker = IntelliVerilogLocator.GetService<ReturnAddressTracker>()!;
                var returnAddress = returnTracker.TrackReturnAddress(this);
                var analysisContext = IntelliVerilogLocator.GetService<AnalysisContext>()!;
                var currentModel = analysisContext.CurrentComponent?.InternalModel as ComponentBuildingModel;

                if (currentModel == null) return;

                var cast = new CastExpression<TData>((TData)UntypedType.CreateWithWidth(1), value);
                var wrapper = new ExpressedReg<TData>(cast, ClockDom);

                currentModel.AssignSubModuleConnections(this, wrapper, index..(index + 1), returnAddress);
            }
        }
        public RightValue<TData> this[Range range] {
            get => RValue[range];
            set {
                var returnTracker = IntelliVerilogLocator.GetService<ReturnAddressTracker>()!;
                var returnAddress = returnTracker.TrackReturnAddress(this);
                var analysisContext = IntelliVerilogLocator.GetService<AnalysisContext>()!;
                var currentModel = analysisContext.CurrentComponent?.InternalModel as ComponentBuildingModel;

                if (currentModel == null) return;

                var wrapper = new ExpressedReg<TData>(value, ClockDom);

                currentModel.AssignSubModuleConnections(this, wrapper, range, returnAddress);
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
    }
}
