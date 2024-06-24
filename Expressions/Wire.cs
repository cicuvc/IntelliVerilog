using IntelliVerilog.Core.Analysis;
using IntelliVerilog.Core.DataTypes;
using IntelliVerilog.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.Expressions {
    public interface IWireRightValueWrapper {
        Wire UntyedWire { get; }
    }
    public class WireRightValueWrapper<TData> : RightValue<TData> , IWireRightValueWrapper where TData : DataType, IDataType<TData> {
        public Wire<TData> WireDef { get; }
        public Wire UntyedWire => WireDef;
        public WireRightValueWrapper(Wire<TData> wire):base((TData)wire.UntypedType) {
            WireDef = wire;
        }
        public override void EnumerateSubNodes(Action<AbstractValue> callback) {}

        public override bool Equals(AbstractValue? other) {
            if(other is WireRightValueWrapper<TData> expression) {
                return expression.WireDef == WireDef;
            }
            return false;
        }
    }
    public abstract class Wire : IAssignableValue,IReferenceTraceObject,IWireLike, IOverlappedObject,IRightValueConvertible {
        public DataType UntypedType { get; }

        public Func<string> Name { get; set; } = () => "<unnamed wire>";

        public IOverlappedObjectDesc Descriptor { get; set; }

        public abstract AbstractValue UntypedRValue { get; }

        public Wire(DataType type) {
            var defaultName = $"W{Utility.GetRandomStringHex(16)}";

            UntypedType = type;
            Descriptor = new WireOverlappedDesc(defaultName, type) { this };
        }
        public static ref Wire<TData> New<TData>(TData type) where TData : DataType, IDataType<TData> {
            var context = IntelliVerilogLocator.GetService<AnalysisContext>()!;
            var componentModel = context.CurrentComponent!.InternalModel as ComponentBuildingModel;

            var wire = new Wire<TData>(type);

            var pointerStorage = componentModel.RegisterWire(wire);
            return ref Unsafe.As<IReferenceTraceObject, Wire<TData>>(ref pointerStorage.Pointer);
        }

        public AssignmentInfo CreateAssignmentInfo() {
            return new WireAssignmentInfo(this);
        }
    }
    public class ExpressedWire<TData>:Wire<TData> , IExpressionAssignedIoType where TData : DataType, IDataType<TData> {
        public ExpressedWire(RightValue<TData> expression) : base(expression.TypedType) {
            Expression = expression;
        }

        public RightValue<TData> Expression { get; }
        public AbstractValue UntypedExpression => Expression;
        public override RightValue<TData> RValue => Expression;
    }
    public class Wire<TData> : Wire,
        IRightValueOps<Wire<TData>, TData>,
        IRightValueSelectionOps<TData>,
        ILeftValueOps<TData> where TData : DataType, IDataType<TData> {
        protected WireRightValueWrapper<TData>? m_RValueCache = null;
        public virtual RightValue<TData> RValue {
            get {
                if (m_RValueCache is null) m_RValueCache = new(this);
                return m_RValueCache;
            }
        }
        public override AbstractValue UntypedRValue => RValue;
        public Wire(TData type) : base(type) {
        }
        
        public static implicit operator Wire<TData>(RightValue<TData> value) {
            return new ExpressedWire<TData>(value);
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
                var wrapper = new ExpressedWire<TData>(cast);

                currentModel.AssignSubModuleConnections(this, wrapper, index..(index+1), returnAddress);
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

                var wrapper = new ExpressedWire<TData>(value);

                currentModel.AssignSubModuleConnections(this, wrapper, range, returnAddress);
            }
        }

        public static RightValue<TData> operator +(Wire<TData> lhs, Wire<TData> rhs) {
            return lhs.RValue + rhs.RValue;
        }

        public static RightValue<TData> operator -(Wire<TData> lhs, Wire<TData> rhs) {
            return lhs.RValue - rhs.RValue;
        }

        public static RightValue<TData> operator *(Wire<TData> lhs, Wire<TData> rhs) {
            return lhs.RValue * rhs.RValue;
        }

        public static RightValue<TData> operator /(Wire<TData> lhs, Wire<TData> rhs) {
            return lhs.RValue / rhs.RValue;
        }

        public static RightValue<TData> operator &(Wire<TData> lhs, Wire<TData> rhs) {
            return lhs.RValue & rhs.RValue;
        }

        public static RightValue<TData> operator |(Wire<TData> lhs, Wire<TData> rhs) {
            return lhs.RValue | rhs.RValue;
        }

        public static RightValue<TData> operator ^(Wire<TData> lhs, Wire<TData> rhs) {
            return lhs.RValue ^ rhs.RValue;
        }

        public static RightValue<Bool> operator ==(Wire<TData> lhs, Wire<TData> rhs) {
            return lhs.RValue == rhs.RValue;
        }

        public static RightValue<Bool> operator !=(Wire<TData> lhs, Wire<TData> rhs) {
            return lhs.RValue != rhs.RValue;
        }

        public static RightValue<Bool> operator >(Wire<TData> lhs, Wire<TData> rhs) {
            return lhs.RValue > rhs.RValue;
        }

        public static RightValue<Bool> operator <(Wire<TData> lhs, Wire<TData> rhs) {
            return lhs.RValue < rhs.RValue;
        }

        public static RightValue<Bool> operator >=(Wire<TData> lhs, Wire<TData> rhs) {
            return lhs.RValue >= rhs.RValue;
        }

        public static RightValue<Bool> operator <=(Wire<TData> lhs, Wire<TData> rhs) {
            return lhs.RValue <= rhs.RValue;
        }

        public static RightValue<TData> operator ~(Wire<TData> lhs) {
            return ~lhs.RValue;
        }
    }
}
