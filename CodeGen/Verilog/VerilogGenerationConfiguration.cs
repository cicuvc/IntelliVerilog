using System;

namespace IntelliVerilog.Core.CodeGen.Verilog {
    public class VerilogGenerationConfiguration : CodeGenConfiguration, ICodeGenConfiguration<VerilogGenerationConfiguration> {
        public int IndentCount { get; set; } = 4;
        public char IndentChar { get; set; } = ' ';
        public string NewLine { get; set; } = Environment.NewLine;
        public override string ExtensionName { get; set; } = ".v";

        public static ICodeGenBackend<VerilogGenerationConfiguration> CreateBackend() {
            return new VerilogBackend();
        }
    }
}
