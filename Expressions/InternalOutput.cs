using IntelliVerilog.Core.Analysis;
using IntelliVerilog.Core.Components;
using IntelliVerilog.Core.DataTypes;
using IntelliVerilog.Core.Expressions.Algebra;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;

namespace IntelliVerilog.Core.Expressions {
    public class IoInvertibleRightValueWrapper<TData> : IoRightValueWrapper<TData>,IInvertedOutput where TData : DataType, IDataType<TData> {
        public IoInvertibleRightValueWrapper(InternalOutput<TData> ioPort) : base(ioPort) {
        }

        public IoComponent InternalOut => UntypedComponent;

        public ImmutableArray<GenericIndex> SelectedRange => throw new NotImplementedException();
    }
    public class InternalOutput<TData> : TypeSpecifiedOutput<TData>, IUntypedConstructionPort, IAssignableValue where TData : DataType, IDataType<TData> {
        public InternalOutput(TData dataType,IUntypedDeclPort creator, IoBundle parent, ComponentBase root, IoMemberInfo member) : base(dataType) {
            PortMember = member; ;
            Parent = parent;
            Component = root;
            Creator = creator;
            Name = GetDefaultName;
            
        }
        public override IoRightValueWrapper<TData> RValue {
            get {
                if (m_RightValueCache is null) {
                    m_RightValueCache = new IoInvertibleRightValueWrapper<TData>(this);
                }
                return m_RightValueCache;
            }
        }
        public string GetDefaultName() {
            var path = Location;
            var portName = $"{string.Join('_', path.Path.Select(e => e.Name))}_{path.Name}";
            return portName;
        }

        public AssignmentInfo CreateAssignmentInfo() {
            return new IoPortAssignmentInfo(this);
        }

        public unsafe override RightValue<TData> this[params GenericIndex[] range] {
            get {
                return new InvertedInternalOutput<TData>((TData)UntypedType, this, new(range));
            }
            set {
                var returnTracker = IntelliVerilogLocator.GetService<ReturnAddressTracker>()!;
                var returnAddress = returnTracker.TrackReturnAddress(this);
                var analysisContext = IntelliVerilogLocator.GetService<AnalysisContext>()!;
                var currentModel = analysisContext.CurrentComponent?.InternalModel as ComponentBuildingModel;

                if (currentModel == null) return;

                var wrapper = new ExpressedInput<TData>(value);

                currentModel.AssignSubModuleConnections(this, wrapper, new(range), returnAddress);
            }
        }

        public override GeneralizedPortFlags Flags =>
            GeneralizedPortFlags.WidthSpecified | GeneralizedPortFlags.SingleComponent |
            GeneralizedPortFlags.Placeholder | GeneralizedPortFlags.InternalPort;
        public IoMemberInfo PortMember { get; }
        public IoBundle Parent { get; }
        public IUntypedDeclPort Creator { get; }
        public ComponentBase Component { get; }

        public IoPortPath Location => new IoPortPath(this, PortMember);

        public IUntypedConstructionPort InternalPort => this;
    }
}
