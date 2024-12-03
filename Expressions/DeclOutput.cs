using IntelliVerilog.Core.Components;
using IntelliVerilog.Core.DataTypes;
using System;

namespace IntelliVerilog.Core.Expressions {
    public class DeclOutput<TData> : TypeSpecifiedOutput<TData>,IUntypedDeclPort where TData : DataType, IDataType<TData> {
        public override GeneralizedPortFlags Flags
            => GeneralizedPortFlags.Placeholder | GeneralizedPortFlags.WidthSpecified |
            GeneralizedPortFlags.SingleComponent | GeneralizedPortFlags.DeclPort;
        public override RightValue<TData> this[params GenericIndex[] range] { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public DeclOutput(TData type) : base(type, new([(int)type.WidthBits])) { }

        public IUntypedPort CreateInternalPlaceholder(IoBundle parent, IoMemberInfo member) {
            var currentComponent = parent;
            while (!(currentComponent is ComponentBase))
                currentComponent = currentComponent.Parent;


            return new InternalOutput<TData>((TData)UntypedType,this, parent, (ComponentBase)currentComponent, member);
        }

        public IUntypedPort CreateExternalPlaceholder(IoBundle parent, IoMemberInfo member, IUntypedConstructionPort internalPort) {
            var currentComponent = parent;
            while (!(currentComponent is ComponentBase))
                currentComponent = currentComponent.Parent;


            return new ExternalOutput<TData>((TData)UntypedType,this, parent, (ComponentBase)currentComponent, member, internalPort);
        }
    }
}
