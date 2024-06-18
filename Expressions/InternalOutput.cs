using IntelliVerilog.Core.Analysis;
using IntelliVerilog.Core.Components;
using IntelliVerilog.Core.DataTypes;
using System;
using System.Linq;
using System.Reflection;

namespace IntelliVerilog.Core.Expressions {
    public class InternalOutput<TData> : TypeSpecifiedOutput<TData>, IUntypedConstructionPort, IAssignableValue where TData : DataType, IDataType<TData> {
        public Func<string> Name { get; set; }
        public InternalOutput(TData dataType,IUntypedDeclPort creator, IoBundle parent, ComponentBase root, IoMemberInfo member) : base(dataType) {
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
        public unsafe override RightValue<Bool> this[int index] {
            get {
                return new InvertedInternalOutput<Bool>(Bool.CreateDefault(), this, index..(index + 1));
            }
            set {
                var returnTracker = IntelliVerilogLocator.GetService<ReturnAddressTracker>()!;
                var returnAddress = returnTracker.TrackReturnAddress(this);
                var analysisContext = IntelliVerilogLocator.GetService<AnalysisContext>()!;
                var currentModel = analysisContext.CurrentComponent?.InternalModel as ComponentBuildingModel;

                if (currentModel == null) return;

                var wrapper = new ExpressedOutput<Bool>(value);

                currentModel.AssignSubModuleConnections(this, wrapper, index..(index + 1), returnAddress);
            } 
        }
        public unsafe override RightValue<TData> this[Range range] {
            get {
                return new InvertedInternalOutput<TData>((TData)UntypedType, this, range);
            }
            set {
                var returnTracker = IntelliVerilogLocator.GetService<ReturnAddressTracker>()!;
                var returnAddress = returnTracker.TrackReturnAddress(this);
                var analysisContext = IntelliVerilogLocator.GetService<AnalysisContext>()!;
                var currentModel = analysisContext.CurrentComponent?.InternalModel as ComponentBuildingModel;

                if (currentModel == null) return;

                var wrapper = new ExpressedInput<TData>(value);

                currentModel.AssignSubModuleConnections(this, wrapper, range, returnAddress);
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
