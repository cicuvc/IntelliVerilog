using IntelliVerilog.Core.Analysis;
using IntelliVerilog.Core.DataTypes;
using IntelliVerilog.Core.Expressions;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;

namespace IntelliVerilog.Core.CodeGen.Verilog {
    public class VerilogPort : IExpressionVerilogElement {
        public VerilogModule AstModule { get; }
        public bool NoLineEnd => false;
        public VerilogPortDirection Direction { get; }
        public IoComponent InternalPort { get; }
        public Size PortRealShape { get; }
        public int PortFlattenSize { get; }
        public string PortName { get; }

        public ImmutableArray<int> Shape { get; }

        public AssignmentInfo? Assignments { get; }

        public VerilogPort(VerilogModule astModule,IoComponent internalPort, AssignmentInfo? assignmentInfo) {
            AstModule = astModule;
            Assignments = assignmentInfo;

            InternalPort = internalPort;
            Direction = internalPort.Direction switch {
                IoPortDirection.Input => VerilogPortDirection.Input,
                IoPortDirection.Output => VerilogPortDirection.Output,
                IoPortDirection.InOut => VerilogPortDirection.InOut,
                _ => throw new NotSupportedException()
            };

            PortRealShape = internalPort.Shape;
            PortFlattenSize = PortRealShape.GetTotalBits();
            Shape = (new int[] { PortFlattenSize }).ToImmutableArray();

            PortName = internalPort.Name();
        }

        public void GenerateBlock(VerilogGenerationContext context) {

        }

        public void GenerateCode(VerilogGenerationContext context) {
            context.Append(PortName);
        }

        public void GenerateDecl(VerilogGenerationContext context) {
            var type = Direction switch {
                VerilogPortDirection.Input => "input",
                VerilogPortDirection.Output => "output",
                VerilogPortDirection.InOut => "inout",
                _ => throw new NotSupportedException()
            };
            VerilogSyntaxHelpers.GenerateWireDef(context, type, Shape.AsSpan(), PortName);
        }
    }
}
