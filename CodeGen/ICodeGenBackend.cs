using IntelliVerilog.Core.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.CodeGen {
    public interface ICodeGenBackend<TConfiguration> where TConfiguration: CodeGenConfiguration, ICodeGenConfiguration<TConfiguration>,new() {
        string GenerateModuleCode(Module module, TConfiguration? configuration);
    }
    public class CodeGenConfiguration {

    }
    public interface ICodeGenConfiguration<TConfiguration> where TConfiguration: CodeGenConfiguration, ICodeGenConfiguration<TConfiguration>,new() {
        static abstract ICodeGenBackend<TConfiguration> CreateBackend();
    }
}
