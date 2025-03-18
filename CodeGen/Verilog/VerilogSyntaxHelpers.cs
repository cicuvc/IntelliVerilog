using System;

namespace IntelliVerilog.Core.CodeGen.Verilog {
    public static class VerilogSyntaxHelpers {
        public static void GenerateWireDef(VerilogGenerationContext context,string type, ReadOnlySpan<int> shape, string name) {
            context.Append(type);
            if(shape.Length == 0 || (shape.Length == 1 && shape[0] == 1)) {
                context.Append(" ");
            } else {
                context.Append("[");
                for(var i=0;i< shape.Length; i++) {
                    context.AppendFormat("{0}:0", shape[i] - 1);
                    if(i != shape.Length - 1) context.Append(", ");
                }
                context.Append("] ");
            }
            context.Append(name);
        }
    }
}
