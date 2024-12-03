using IntelliVerilog.Core.Analysis;
using IntelliVerilog.Core.Components;
using IntelliVerilog.Core.DataTypes;
using System;
using System.Reflection;

namespace IntelliVerilog.Core.Expressions {
    public enum IoPortDirection:ushort {
        Input = 1,
        Output = 2,
        InOut = 3,
        Bundle = 4
    }
    public enum GeneralizedPortFlags:ushort {
        Placeholder = 1,
        WidthSpecified = 2,

        ExternalPort = 4,
        InternalPort = 8,
        DeclPort = 16,
        Constructed = 32,

        SingleComponent = 64,
        Bundle = 128,

        ClockReset = 256
    }
    public interface IUntypedDeclPort : IUntypedPort, IDataTypeSpecifiedPort {
        IUntypedPort CreateInternalPlaceholder(IoBundle parent, IoMemberInfo member);
        IUntypedPort CreateExternalPlaceholder(IoBundle parent, IoMemberInfo member, IUntypedConstructionPort internalPort);
    }
    public interface IDataTypeSpecifiedPort: IUntypedPort, IShapedValue {
        DataType UntypedType { get; set; }
    }
    public interface IUntypedLocatedPort: IUntypedPort {
        // Reflection member of the port
        IoMemberInfo PortMember { get; }
        // Parent of the port (usually a bundle). Note: modules are bundles
        IoBundle Parent { get; }
        // Owner of the bundle or port
        ComponentBase Component { get; }

        IoPortPath Location { get; }
    }
    public interface IUntypedConstructionPort: IUntypedPort, IDataTypeSpecifiedPort, IUntypedLocatedPort {
        AbstractValue UntypedRValue { get; }
        IUntypedDeclPort Creator { get; }
        IUntypedConstructionPort InternalPort { get; }
    }
    public interface IUntypedPort: IWireLike {
        IoPortDirection Direction { get; }
        GeneralizedPortFlags Flags { get; }
    }
}
