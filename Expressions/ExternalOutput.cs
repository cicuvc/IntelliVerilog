using IntelliVerilog.Core.Analysis;
using IntelliVerilog.Core.Components;
using IntelliVerilog.Core.DataTypes;
using System;
using System.Linq;

namespace IntelliVerilog.Core.Expressions {
    public class ExternalOutput<TData> 
        : TypeSpecifiedOutput<TData>, IUntypedConstructionPort,IAssignableValue 
        where TData : DataType, IDataType<TData> {

        protected IoRightValueWrapper<TData>? m_CachedRightValue;
        public override AbstractValue UntypedRValue {
            get {
                if (m_CachedRightValue is null) m_CachedRightValue = new(this);
                return m_CachedRightValue;
            }
        }
        public override GeneralizedPortFlags Flags =>
            GeneralizedPortFlags.WidthSpecified | GeneralizedPortFlags.SingleComponent |
            GeneralizedPortFlags.Placeholder | GeneralizedPortFlags.ExternalPort;

        public IoMemberInfo PortMember { get; }
        public IoBundle Parent { get; }
        public ComponentBase Component { get; }
        public IUntypedDeclPort Creator { get; }
        public IoPortPath Location => new IoPortPath(this, PortMember);

        public IUntypedConstructionPort InternalPort { get; }

        public override RightValue<TData> this[Range range] { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public string GetDefaultName() {
            var path = Location;
            var portName = $"{string.Join('_', path.Path.Select(e => e.Name))}_{path.Name}";
            return portName;
        }

        public AssignmentInfo CreateAssignmentInfo() {
            throw new NotImplementedException();
        }

        public ExternalOutput(TData dataType,IUntypedDeclPort creator, IoBundle parent, ComponentBase root, IoMemberInfo member, IUntypedConstructionPort internalPort) : base(dataType) {
            PortMember = member; ;
            Parent = parent;
            Component = root;
            Creator = creator;
            InternalPort = internalPort;
            Name = GetDefaultName;
        }
        public override RightValue<Bool> this[int index] { 
            get {
                return RValue[index];
            }
            set {
                var analysisContext = IntelliVerilogLocator.GetService<AnalysisContext>()!;
                var currentModel = analysisContext.CurrentComponent?.InternalModel as ComponentBuildingModel;

                if (currentModel == null) return;


                throw new NotImplementedException();

                //currentModel.AssignExpression(this, value, index..(index + 1));
            }
        }
    }
}
