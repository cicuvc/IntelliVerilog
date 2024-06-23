using IntelliVerilog.Core.Analysis;
using IntelliVerilog.Core.Components;
using IntelliVerilog.Core.DataTypes;
using IntelliVerilog.Core.Expressions.Algebra;
using System;
using System.Reflection;

namespace IntelliVerilog.Core.Expressions {
    // Unspecified Output
    public abstract class Output<TData> : IoComponent<TData>,IUnspecifiedPortFactory where TData : DataType,IDataType<TData> {
        public override IoPortDirection Direction => IoPortDirection.Output;

        public static IUntypedPort CreateUnspecified(IoBundle parent, ComponentBase root, IoMemberInfo member) {
            return new UnspecifiedLocatedOutput<TData>(parent, root, member);
        }

        public static implicit operator Output<TData>(RightValue<TData> value) {
            return new ExpressedOutput<TData>(value);
        }
        public static implicit operator Output<TData>(TData type) {
            return new DeclOutput<TData>(type);
        }
    }
    public class UnspecifiedLocatedOutput<TData> : Output<TData>, IUntypedLocatedPort,IUntypedDeclPort where TData : DataType, IDataType<TData> {
        public override RightValue<Bool> this[int index] { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public override RightValue<TData> this[Range range] { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public override GeneralizedPortFlags Flags =>
            GeneralizedPortFlags.SingleComponent | GeneralizedPortFlags.Placeholder | GeneralizedPortFlags.DeclPort;

        public IoMemberInfo PortMember { get; }
        public IoBundle Parent { get; }
        public ComponentBase Component { get; }

        public IoPortPath Location => new(this, PortMember);

        public DataType UntypedType { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public UnspecifiedLocatedOutput(IoBundle parent, ComponentBase root, IoMemberInfo member) {
            PortMember = member; ;
            Parent = parent;
            Component = root;
        }
        public IUntypedPort CreateInternalPlaceholder(IoBundle parent, IoMemberInfo member) {
            var currentComponent = parent;
            while (!(currentComponent is ComponentBase))
                currentComponent = currentComponent.Parent;

            return new InternalOutput<TData>(TData.CreateDefault(), this, parent, (ComponentBase)currentComponent, member);
        }

        public IUntypedPort CreateExternalPlaceholder(IoBundle parent, IoMemberInfo member, IUntypedConstructionPort internalPort) {

            return new ExternalOutput<TData>(TData.CreateDefault(), this, parent, parent.Component, member, internalPort);
        }
    }
    public abstract class TypeSpecifiedOutput<TData> : Output<TData> where TData : DataType, IDataType<TData> {
        protected DataType m_UntypedType;
        public DataType UntypedType {
            get => m_UntypedType;
            set {
                if (m_UntypedType.IsWidthSpecified) {
                    throw new InvalidOperationException("Override specified type!!");
                }
                m_UntypedType = value;
                m_RightValueCache = null;
            }
        }
        public TypeSpecifiedOutput(TData type){
            m_UntypedType = type;
        }
    }
}
