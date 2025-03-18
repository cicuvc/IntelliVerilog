using IntelliVerilog.Core.Analysis.TensorLike;
using IntelliVerilog.Core.Components;
using IntelliVerilog.Core.Expressions;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.CodeGen.Verilog {
    public class VerilogBackend : ICodeGenBackend<VerilogGenerationConfiguration> {
        public string GenerateModuleCode(Module module, VerilogGenerationConfiguration? configuration) {
            configuration ??= new();
            var moduleAst = new VerilogModule(module);

            var context = new VerilogGenerationContext(configuration, this);
            moduleAst.GenerateBlock(context);

            return context.Dump();
        }
    }

    public class VerilogGenerationContext {
        public VerilogBackend Generator { get; }
        public VerilogGenerationConfiguration Configuration { get; }
        protected StringBuilder m_Buffer;
        protected int m_Indent;
        protected string m_IndentPrefix = "";
        protected bool m_CurrentLineEmpty = true;

        public bool CurrentLineEmpty => m_CurrentLineEmpty;
        private struct VerilogGenerationIndent : IDisposable {
            private VerilogGenerationContext m_Context;
            public VerilogGenerationIndent(VerilogGenerationContext context) => m_Context = context;
            public void Dispose() => m_Context.EndIndent();
        }
        public VerilogGenerationContext(VerilogGenerationConfiguration configuration, VerilogBackend generator) {
            Configuration = configuration;
            Generator = generator;
            m_Buffer = new();
        }
        public IDisposable BeginIndent() {
            m_Indent += Configuration.IndentCount;
            m_IndentPrefix = new(Configuration.IndentChar, m_Indent);
            return new VerilogGenerationIndent(this);
        }
        public void EndIndent() {
            m_Indent -= Configuration.IndentCount;
            m_IndentPrefix = new(Configuration.IndentChar, m_Indent);
        }
        public string Dump() {
            return m_Buffer.ToString();
        }
        public void Append(string s) {
            Debug.Assert(!s.Contains('\r'));
            Debug.Assert(!s.Contains('\n'));
            if(m_CurrentLineEmpty) {
                m_CurrentLineEmpty = false;
                m_Buffer.Append(m_IndentPrefix);
            }
            m_Buffer.Append(s);
        }
        public void AppendLine(string s = "") {
            Debug.Assert(!s.Contains('\r'));
            Debug.Assert(!s.Contains('\n'));
            if(m_CurrentLineEmpty) {
                m_CurrentLineEmpty = false;
                m_Buffer.Append(m_IndentPrefix);
            }
            m_Buffer.Append(s);
            m_Buffer.Append(Configuration.NewLine);
            m_CurrentLineEmpty = true;
        }
        public void AppendFormat(string fmt, params object[] values)
            => Append(string.Format(fmt, values));
    }
    public abstract class VerilogAstNode {
        public abstract bool NoLineEnd { get; }
        public virtual bool NoAutoNewLine { get; } = false;
        public abstract void GenerateCode(VerilogGenerationContext context);
    }
    public interface IVerilogElement {
        bool NoLineEnd { get; }
        /// <summary>
        /// Generate identifier to refer to this element
        /// </summary>
        /// <param name="context"></param>
        void GenerateCode(VerilogGenerationContext context);
        /// <summary>
        /// In front of module content to declare literals or identifiers
        /// </summary>
        /// <param name="context"></param>
        void GenerateDecl(VerilogGenerationContext context);
        /// <summary>
        /// Generate block body of element
        /// </summary>
        /// <param name="context"></param>
        void GenerateBlock(VerilogGenerationContext context);
    }
    public enum VerilogPortDirection {
        Input, Output, InOut
    }
    public struct VerilogModuleIo {
        public List<VerilogPort> Ports { get; } = new();
        public VerilogModuleIo(Module module) {
            var interalModel = module.InternalModel;
            foreach(var i in interalModel.IoPortShape) {
                Ports.Add(new((IoComponent)i));
            }
        }
        public void GenerateDecl(VerilogGenerationContext context) {
            using(context.BeginIndent()) {
                for(var i = 0; i < Ports.Count; i++) {
                    Ports[i].GenerateDecl(context);
                    if(i == Ports.Count - 1) {
                        context.Append(",");
                    }
                    context.AppendLine();
                }
            }
        }
    }
    public interface IShapedVerilogElement : IVerilogElement {
        ImmutableArray<int> Shape { get; }
    }
    public class VerilogReshapeSignalDef : IVerilogElement {
        public IShapedVerilogElement SourceElement { get; }
        public ImmutableArray<int> SourceShape { get; }
        public ImmutableArray<int> NewShape { get; }
        public string NewIdentifier { get; }
        public VerilogReshapeSignalDef(IShapedVerilogElement sourceElement, ImmutableArray<int> newShape, string newIdentifier) {
            SourceElement = sourceElement;
            SourceShape = sourceElement.Shape.ToImmutableArray();
            NewShape = newShape;
            NewIdentifier = newIdentifier;
        }
        public bool NoLineEnd => throw new NotImplementedException();

        public void GenerateBlock(VerilogGenerationContext context) {
            context.Append("generate");

            using(context.BeginIndent()) {
                context.Append("genvar ");
                for(var i =0;i< NewShape.Length; i++) {
                    context.AppendFormat("idx{0}{1}", i, i == NewShape.Length - 1 ? ";" : ", ");
                }
                context.AppendLine();

                GenerateNestedFor(context);
            }

            context.Append("endgenerate;");
        }

        protected void GenerateNestedFor(VerilogGenerationContext context, int rank = 0) {
            if(rank < NewShape.Length - 1) {
                context.AppendFormat("for(idx{0} = 0; idx{0} < {1}; idx{0} = idx{0} + 1) begin", rank, NewShape[rank]);
                context.AppendLine();
                using(context.BeginIndent()) {
                    GenerateNestedFor(context, rank + 1);
                }
                context.AppendLine("end");
            } else {
                GenerateAssignmentBody(context);
            }
        }

        protected void GenerateAssignmentBody(VerilogGenerationContext context) {
            var tensorDestIndices = NewShape.Select((e, idx) => new TensorIndexVarExpr<string>(0, e, $"idx{idx}")).ToArray();
            
        }

        public void GenerateCode(VerilogGenerationContext context) {
            throw new NotImplementedException();
        }

        public void GenerateDecl(VerilogGenerationContext context) {
            VerilogSyntaxHelpers.GenerateWireDef(context, "wire", NewShape.AsSpan(), NewIdentifier);
        }
    }
    public class VerilogModule: IVerilogElement {
        protected VerilogModuleIo m_ModuleIo;
        public Module BackModule { get; }
        public string ModuleName { get; }
        

        public bool NoLineEnd => throw new NotImplementedException();

        public VerilogModule(Module backModule) {
            BackModule = backModule;
            ModuleName = backModule.InternalModel.ModelName;

            m_ModuleIo = new(backModule);
        }

        public void GenerateCode(VerilogGenerationContext context) {
            throw new NotImplementedException();
        }

        public void GenerateDecl(VerilogGenerationContext context) {
            throw new NotImplementedException();
        }

        public void GenerateBlock(VerilogGenerationContext context) {
            context.Append("module ");
            context.Append(ModuleName);

            context.AppendLine("(");
            m_ModuleIo.GenerateDecl(context);
            context.AppendLine(");");


            context.Append("endmodule;");
        }
    }
}
