using IntelliVerilog.Core.Analysis;
using IntelliVerilog.Core.Components;
using IntelliVerilog.Core.DataTypes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace IntelliVerilog.Core.Expressions {
    public class ExpressedInput<TData> : TypeSpecifiedInput<TData>, IExpressionAssignedIoType where TData : DataType, IDataType<TData> {
        public RightValue<TData> Expression { get; init; }
        public override GeneralizedPortFlags Flags => GeneralizedPortFlags.SingleComponent | GeneralizedPortFlags.WidthSpecified;

        public override RightValue<TData> this[params GenericIndex[] range] { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public AbstractValue UntypedExpression => Expression;

        public ExpressedInput(RightValue<TData> expression) : base(expression.TypedType, new([(int)expression.TypedType.WidthBits])) {
            Expression = expression;
        }
    }
    public interface IUnspecifiedPortFactory {
        static abstract IUntypedPort CreateUnspecified(IoBundle parent, ComponentBase root, IoMemberInfo member);
    }
    public abstract class Input<TData> : IoComponent<TData>, IUnspecifiedPortFactory where TData : DataType, IDataType<TData> {
        public override IoPortDirection Direction => IoPortDirection.Input;


        public static implicit operator Input<TData>(RightValue<TData> value) {
            return new ExpressedInput<TData>(value);
        }
        public static implicit operator Input<TData>(TData dataType) {
            return new DeclInput<TData>(dataType, new(dataType.WithShape.Append((int)dataType.WidthBits).ToArray()));
        }
        public static implicit operator Input<TData>(Output<TData> value) {
            return value.RValue;
        }
        public static IUntypedPort CreateUnspecified(IoBundle parent, ComponentBase root, IoMemberInfo member) {
            return new UnspecifiedLocatedInput<TData>(parent, root, member);
        }
    }
    public class UnspecifiedLocatedInput<TData> : Input<TData>,IUntypedLocatedPort,IUntypedDeclPort where TData : DataType, IDataType<TData> {
        public override RightValue<TData> this[params GenericIndex[] range] { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public override GeneralizedPortFlags Flags =>
            GeneralizedPortFlags.SingleComponent | GeneralizedPortFlags.Placeholder | GeneralizedPortFlags.DeclPort;

        public override ValueShape Shape { get; }
        public IoMemberInfo PortMember { get; }
        public IoBundle Parent { get; }
        public ComponentBase Component { get; }

        public IoPortPath Location => new(this, PortMember);

        public DataType UntypedType { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public UnspecifiedLocatedInput(IoBundle parent, ComponentBase root, IoMemberInfo member) {
            PortMember = member; ;
            Parent = parent;
            Component = root;
            Shape = new([(int)TData.CreateDefault().WidthBits]);
        }

        public IUntypedPort CreateInternalPlaceholder(IoBundle parent, IoMemberInfo member) {
            var currentComponent = parent;
            while (!(currentComponent is ComponentBase))
                currentComponent = currentComponent.Parent;

            return new InternalInput<TData>(TData.CreateDefault(), this, parent, (ComponentBase)currentComponent, member);
        }

        public IUntypedPort CreateExternalPlaceholder(IoBundle parent, IoMemberInfo member, IUntypedConstructionPort internalPort) {
            return new ExternalInput<TData>(TData.CreateDefault(), this, parent, parent.Component, member, internalPort);
        }
    }
    public abstract class TypeSpecifiedInput<TData> : Input<TData> where TData : DataType, IDataType<TData> {
        protected DataType m_UntypedType;
        public override ValueShape Shape { get; }
        public DataType UntypedType { get => m_UntypedType; set => throw new NotImplementedException(); }
        public TypeSpecifiedInput(TData type, ValueShape shape) {
            m_UntypedType = type;
            Shape = shape;
        }
    }

    public class DeclInput<TData> : TypeSpecifiedInput<TData>, IUntypedDeclPort where TData : DataType, IDataType<TData> {
        public override GeneralizedPortFlags Flags
            => GeneralizedPortFlags.Placeholder | GeneralizedPortFlags.WidthSpecified |
            GeneralizedPortFlags.SingleComponent | GeneralizedPortFlags.DeclPort;
        public override RightValue<TData> this[params GenericIndex[] range] { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public DeclInput(TData type,ValueShape shape) : base(type, shape) { }

        public IUntypedPort CreateInternalPlaceholder(IoBundle parent, IoMemberInfo member) {
            var currentComponent = parent;
            while (!(currentComponent is ComponentBase))
                currentComponent = currentComponent.Parent;


            return new InternalInput<TData>((TData)UntypedType, this, parent, (ComponentBase)currentComponent, member);
        }
        public IUntypedPort CreateExternalPlaceholder(IoBundle parent, IoMemberInfo member, IUntypedConstructionPort internalPort) {
            return new ExternalInput<TData>((TData)UntypedType, this, parent, parent.Component, member, internalPort);
        }
    }

    public class ExternalInput<TData> : TypeSpecifiedInput<TData>, IUntypedConstructionPort, IAssignableValue where TData : DataType, IDataType<TData> {
        public override GeneralizedPortFlags Flags =>
            GeneralizedPortFlags.WidthSpecified | GeneralizedPortFlags.SingleComponent |
            GeneralizedPortFlags.Placeholder | GeneralizedPortFlags.ExternalPort;
        public ExternalInput(TData dataType, IUntypedDeclPort creator,IoBundle parent, ComponentBase root, IoMemberInfo member, IUntypedConstructionPort internalPort) : base(dataType, creator.Shape) {
            PortMember = member; ;
            Parent = parent;
            Component = root;
            Creator = creator;
            InternalPort = internalPort;
            Name = GetDefaultName;
        }
        public unsafe override RightValue<TData> this[params GenericIndex[] indices] {
            get => throw new NotImplementedException();
            set {
                var returnTracker = IntelliVerilogLocator.GetService<ReturnAddressTracker>()!;
                var returnAddress = returnTracker.TrackReturnAddress(this);

                var analysisContext = IntelliVerilogLocator.GetService<AnalysisContext>()!;
                var currentModel = analysisContext.CurrentComponent?.InternalModel as ComponentBuildingModel;

                if (currentModel == null) return;

                var wrappedPort = new ExpressedOutput<TData>(value);
                currentModel.AssignSubModuleConnections(this, wrappedPort,new(indices), returnAddress);
            }
        }

        public IoMemberInfo PortMember { get; }

        public IoBundle Parent { get; }

        public ComponentBase Component { get; }

        public IoPortPath Location => new(this, PortMember);

        public IUntypedDeclPort Creator { get; }

        public IUntypedConstructionPort InternalPort { get; }
        public AssignmentInfo CreateAssignmentInfo() {
            return new IoPortAssignmentInfo(this);
        }
        public string GetDefaultName() {
            var path = Location;
            var portName = $"_{string.Join('_', path.Path.Select(e => e.Name))}_{path.Name}";
            return portName;
        }
    }
    public enum ClockDomainSignal {
        Clock = 0,
        Reset = 1,
        SyncReset = 2,
        ClockEnable = 3
    }
    public interface IClockPart { }
    public class ClockDomainWire : Wire<Bool>, IClockPart {
        private static string[] m_SignalNames = new string[4] {
            "clk", "rst", "sync_rst", "clken"
        };
        public ClockDomain ClockDom { get; }
        public ClockDomainSignal SignalType { get; }
        public ClockDomainWire(ClockDomain clockDom, ClockDomainSignal sigType) : base(Bool.CreateDefault(), new([1])) {
            ClockDom = clockDom;
            SignalType = sigType;

            var componentModel = IntelliVerilogLocator.GetService<AnalysisContext>()!.GetComponentBuildingModel(throwOnNull: true)!;
            componentModel.AssignEntityName($"gen_{clockDom.Name}_{m_SignalNames[(int)sigType]}",this);
        }
    }
    public class ClockDomainInput : TypeSpecifiedInput<Bool>, IUntypedConstructionPort, IClockPart {
        private static string[] m_SignalNames = new string[4] {
            "clk", "rst", "sync_rst", "clken"
        };
        public ClockDomain ClockDom { get; }
        public ClockDomainSignal SignalType { get; }
        public ClockDomainInput(ClockDomain clockDom, ClockDomainSignal sigType,ComponentBase component) : base(Bool.CreateDefault(), new([1])) {
            ClockDom = clockDom;
            SignalType = sigType;
            Component = component;
            var name = $"{clockDom.Name}_{m_SignalNames[(int)sigType]}";
            Name = () => name;
        }
        public override RightValue<Bool> this[params GenericIndex[] range] { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public IUntypedDeclPort Creator => throw new NotImplementedException();

        public IUntypedConstructionPort InternalPort => throw new NotImplementedException();

        public IoMemberInfo PortMember => throw new NotImplementedException();

        public IoBundle Parent => throw new NotImplementedException();

        public ComponentBase Component { get; }

        public IoPortPath Location => throw new NotImplementedException();

        public override GeneralizedPortFlags Flags =>
            GeneralizedPortFlags.WidthSpecified | GeneralizedPortFlags.SingleComponent |
            GeneralizedPortFlags.Placeholder | GeneralizedPortFlags.InternalPort |
            GeneralizedPortFlags.ClockReset;
    }
    public class InternalInput<TData> : TypeSpecifiedInput<TData>, IUntypedConstructionPort, IAssignableValue where TData : DataType, IDataType<TData> {
        public InternalInput(TData dataType, IUntypedDeclPort creator,IoBundle parent, ComponentBase root, IoMemberInfo member) : base(dataType, creator.Shape) {
            PortMember = member; ;
            Parent = parent;
            Component = root;
            Creator = creator;
            Name = GetDefaultName;
        }
        public string GetDefaultName() {
            var path = Location;
            var portName = $"{string.Join('_', path.Path.Select(e => e.Name))}_{path.Name}";
            return portName;
        }

        public AssignmentInfo CreateAssignmentInfo() {
            throw new NotImplementedException();
        }

        public override RightValue<TData> this[params GenericIndex[] range] {
            get => RValue[range];
            set {
                throw new InvalidOperationException();
            }
        }
        public override GeneralizedPortFlags Flags =>
            GeneralizedPortFlags.WidthSpecified | GeneralizedPortFlags.SingleComponent |
            GeneralizedPortFlags.Placeholder | GeneralizedPortFlags.InternalPort;
        public IoMemberInfo PortMember { get; }

        public IoBundle Parent { get; }

        public ComponentBase Component { get; }

        public IoPortPath Location => new(this, PortMember);
        public IUntypedDeclPort Creator { get; }

        public IUntypedConstructionPort InternalPort => this;
    }
}
