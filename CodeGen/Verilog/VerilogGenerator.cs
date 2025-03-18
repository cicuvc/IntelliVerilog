using Iced.Intel;
using IntelliVerilog.Core.Analysis;
using IntelliVerilog.Core.Analysis.TensorLike;
using IntelliVerilog.Core.Components;
using IntelliVerilog.Core.DataTypes;
using IntelliVerilog.Core.Expressions;
using IntelliVerilog.Core.Expressions.Algebra;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace IntelliVerilog.Core.CodeGen.Verilog {
    public class VerilogGenerationConfiguration: CodeGenConfiguration,ICodeGenConfiguration<VerilogGenerationConfiguration> {
        public int IndentCount { get; set; } = 4;
        public char IndentChar { get; set; } = ' ';
        public string NewLine { get; set; } = Environment.NewLine;
        public override string ExtensionName { get; set; } = ".v";

        public static ICodeGenBackend<VerilogGenerationConfiguration> CreateBackend() {
            return new VerilogGenerator();
        }
    }
    public class VerilogGenerationContext {
        public VerilogGenerator Generator { get; }
        public VerilogModule ModuleAst { get; }
        public Module Module { get; }
        public ComponentModel Model { get; }
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
        public VerilogGenerationContext(VerilogGenerationConfiguration configuration, VerilogGenerator generator, VerilogModule moduleAst) {
            Configuration = configuration;
            Generator = generator;
            ModuleAst = moduleAst;
            Module = moduleAst.BackModule;
            Model = Module.InternalModel;
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
            if (m_CurrentLineEmpty) {
                m_CurrentLineEmpty = false;
                m_Buffer.Append(m_IndentPrefix);
            }
            m_Buffer.Append(s);
        }
        public void AppendLine(string s = "") {
            Debug.Assert(!s.Contains('\r'));
            Debug.Assert(!s.Contains('\n'));
            if (m_CurrentLineEmpty) {
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
        void GenerateCode(VerilogGenerationContext context);
        void GenerateDecl(VerilogGenerationContext context);
        void GenerateBlock(VerilogGenerationContext context);
    }
    public abstract class VerilogValueDef: VerilogExpressionBase, IVerilogElement { 
        public abstract string Name { get; set; }
        public abstract void GenerateDecl(VerilogGenerationContext context);
        public abstract void GenerateBlock(VerilogGenerationContext context);
        public VerilogValueDef(ReadOnlySpan<int> shape): base(shape) { }
    }
    public class VerilogRegisterDef : VerilogValueDef {
        public RegisterDesc RegisterInfo { get; }
        public override string Name { get; set; }
        public override bool NoLineEnd => false;
        public VerilogRegisterDef(RegisterDesc register , string name, ReadOnlySpan<int> shape):base(shape) {
            RegisterInfo = register;
            Name = name;
        }
        public override void GenerateCode(VerilogGenerationContext context) {
            context.Append(Name);
        }
        public override void GenerateBlock(VerilogGenerationContext context) {
        }
        public override void GenerateDecl(VerilogGenerationContext context) {
            VerilogSyntaxHelpers.GenerateShapedValueDecl("reg", Name, context, Shape.AsSpan());

        }
    }

    public class VerilogWireDef : VerilogValueDef {
        public override string Name { get; set; }
        public override bool NoLineEnd => false;
        public IWireLike? WirePrototype { get; }
        public TensorExpr? AssignExpression {
            get => ((TensorDynamicExpr)m_Expression!).BaseExpression;
            set => ((TensorDynamicExpr)m_Expression!).BaseExpression = value;
        }
        public VerilogWireDef(string name, ReadOnlySpan<int> shape, IWireLike? wireLike = null):base(shape) {
            Name = name;
            m_Expression = new TensorDynamicExpr(shape);

            WirePrototype = wireLike;
        }
        public override void GenerateBlock(VerilogGenerationContext context) {
            
        }
        public override void GenerateCode(VerilogGenerationContext context) {
            context.Append(Name);
        }
        public override void GenerateDecl(VerilogGenerationContext context) {
            VerilogSyntaxHelpers.GenerateShapedValueDecl("wire", Name, context, Shape.AsSpan());
        }
    }
    public static class VerilogSyntaxHelpers {
        public static ImmutableArray<string> LoopVariables { get; } = ["i","j","k","l","m","n"];
        public static string GetLoopVariable(int idx) {
            if(LoopVariables.Length > idx) return LoopVariables[idx];
            return $"v{idx}";
        }
        public static void GenerateTensorBlock(VerilogGenerationContext context, VerilogValueDef lhs) {
            var indices = lhs.Shape.Select((e, idx) => new TensorIndexVarExpr<int>(0, e - 1, idx)).ToArray();
            var parts = lhs.Expression.ExpandAllIndices(indices);
            var identifiers = lhs.Shape.Select((_, idx) => GetLoopVariable(idx)).ToArray();

            var commonRange = parts[0].IndexRanges.Select((e, idx)=> {
                foreach(var j in parts) {
                    if(j.IndexRanges[idx] != e) return false;
                }
                return true;
            }).ToArray();

            var accessIndices = commonRange.Select((e, idx) => idx).Where(e => commonRange[e])
                .Concat(commonRange.Select((e, idx) => idx).Where(e => !commonRange[e])).ToArray();


            context.AppendLine("generate");
            using(context.BeginIndent()) {
                context.Append("genvar ");
                context.Append(identifiers.Aggregate((u, v) => $"{u}, {v}"));

                for(var i = 0; i < commonRange.Length; i++) {
                    if(commonRange[i]) {
                        var id = identifiers[i];
                        var start = parts[0].IndexRanges[i].Item1;
                        var end = parts[0].IndexRanges[i].Item2;
                        context.AppendLine($"for({id} = {start}; {id} < {end}; {id}++) begin");
                        context.BeginIndent();
                    }
                }

                foreach(var i in parts) {
                    for(var j = 0; j < commonRange.Length; j++) {
                        if(commonRange[j]) continue;
                        var id = identifiers[j];
                        var start = parts[0].IndexRanges[j].Item1;
                        var end = parts[0].IndexRanges[j].Item2;
                        context.AppendLine($"for({id} = {start}; {id} < {end}; {id}++) begin");
                        context.BeginIndent();
                    }
                    var lhsIndices = identifiers.Select(e => $"[{e}]").Aggregate((u, v) => u + v);
                    var rhsIndices = i.Indices.Select(e => $"[{identifiers[((TensorIndexVarExpr<int>)e).Identifier]}]").Aggregate((u,v)=>u + v);
                    if(i.BaseExpr is TensorLeafExpr leaf) {
                        if(leaf.UntypedData is VerilogValueDef value) {
                            context.Append($"assign {lhs.Name}{lhsIndices} = {value.Name}{rhsIndices}");
                            continue;
                        }
                    }

                    throw new NotImplementedException();
                }
            }
            context.AppendLine("endgenerate");
        }
        public static void GenerateShapedValueDecl(string type, string name, VerilogGenerationContext context, ReadOnlySpan<int> shape) {
            var lastWidth = shape[shape.Length - 1];
            if(lastWidth > 1) {
                context.AppendFormat("{0}[{1}:0] ", type, lastWidth - 1);
            } else {
                context.Append(type + " ");
            }

            context.Append(name);

            foreach(var i in shape[..^1]) {
                context.AppendFormat("[{0}:0]", i - 1);
            }
        }
    }
    public class VerilogEmptyLine : VerilogBlock {
        public override bool NoAutoNewLine { get; }
        public VerilogEmptyLine(bool noNewLine = false) {
            NoAutoNewLine = noNewLine;
        }
        public override void GenerateCode(VerilogGenerationContext context) {
            
        }
    }

    public class VeriloAssignment : VerilogAstNode, IVerilogElement {
        public VerilogAstNode LeftValue { get; }
        public VerilogAstNode RightValue { get; }
        public override bool NoLineEnd => false;
        public VeriloAssignment(VerilogAstNode leftValue, VerilogAstNode rightValue) {
            LeftValue = leftValue;
            RightValue = rightValue;
        }
        public override void GenerateCode(VerilogGenerationContext context) {
            
        }

        public void GenerateDecl(VerilogGenerationContext context) {
        }

        public void GenerateBlock(VerilogGenerationContext context) {
            context.Append("assign ");
            LeftValue.GenerateCode(context);
            context.Append(" = ");
            RightValue.GenerateCode(context);
        }
    }
    public class VerilogModuleInfoMaps {
        //public Dictionary<VerilogIoDecl, VerilogWireDef> StructuredIoPorts { get; } = new();
    }
    public class VerilogModule : VerilogAstNode {
        public HashSet<VerilogValueDef> ModuleValues { get; } = new();
        public IEnumerable<VerilogIoDecl> IoPorts => ModuleValues.OfType<VerilogIoDecl>();
        public IEnumerable<VerilogWireDef> Wires => ModuleValues.OfType<VerilogWireDef>();
        public IEnumerable<VerilogRegisterDef> Registers => ModuleValues.OfType<VerilogRegisterDef>();
        //public Dictionary<VerilogIoDecl, VerilogWireDef> StructuredIoPorts { get; } = new();
        //public Dictionary<IWireLike,VerilogWireDef> ExplicitWires { get; set; } = new();
        public Dictionary<string, VerilogWireDef> StageValueMap { get; } = new();
        public List<VerilogSubComponentGroupBlock> SubComponentGroups { get; } = new();
        public Module BackModule { get; }
        public List<IVerilogElement> Contents { get; } = new();
        public VerilogModule(Module backModule) => BackModule = backModule;
        public override bool NoLineEnd => true;
        public VerilogIoDecl FindClockInputs(ClockDomainSignal signalType, ClockDomain clockDom) {
            return IoPorts.Where(e => (e.DeclIoComponent is ClockDomainInput domainInput) && domainInput.ClockDom == clockDom && domainInput.SignalType == signalType).First();
        }
        public override void GenerateCode(VerilogGenerationContext context) {
            context.Append("module ");
            context.Append(BackModule.InternalModel.ModelName);
            context.AppendLine("(");

            using (context.BeginIndent()) {
                var lastPort = IoPorts.LastOrDefault();
                foreach (var i in IoPorts) {
                    i.GenerateCode(context);
                    if (i != lastPort) {
                        context.AppendLine(",");
                    } else {
                        context.AppendLine();
                    }
                }
            }

            context.AppendLine(");");

            using (context.BeginIndent()) {
                foreach (var i in Contents) {
                    i.GenerateCode(context);
                    if (!i.NoLineEnd) context.AppendLine(";");
                    else context.AppendLine();
                }
            }

            context.AppendLine("endmodule");
        }
    }
    public enum VerilogIoType {
        Input, Output, InOut
    }
    
    
    public class VerilogIoDecl: VerilogValueDef {
        public VerilogIoType Type { get; set; }
        public override string Name { get; set; }
        public bool IsRegister { get; set; }
        public IoComponent DeclIoComponent { get; set; }
        public override bool NoLineEnd => false;
        public string ShapedName { get; }
        public TensorExpr UnshapedExpression { get; }
        public VerilogIoDecl(IoComponent decl ,string name, VerilogIoType type, ReadOnlySpan<int> shape, bool isRegister = false):base(shape) {
            DeclIoComponent = decl;
            Name = name;
            Type = type;
            IsRegister = isRegister;
            ShapedName = $"s_{name}";

            var totalBits = TensorIndexMathHelper.Product(shape);

            if(type == VerilogIoType.InOut && shape.Length != 1) {
                throw new NotSupportedException("Port annnotated inout should not be shaped");
            }
            if(type == VerilogIoType.Input) {
                UnshapedExpression = new TensorVarExpr<VerilogIoDecl>(this, [totalBits]);
                m_Expression = TensorExpr.Reshape(UnshapedExpression, shape);
            }
            if(type == VerilogIoType.Output) {
                m_Expression = new TensorVarExpr<VerilogIoDecl>(this, shape);
                UnshapedExpression = TensorExpr.Flatten(Expression);
            }

            throw new NotSupportedException("Unknown port type");
        }
        public override void GenerateBlock(VerilogGenerationContext context) {

        }
        public override void GenerateCode(VerilogGenerationContext context) {
            context.Append(ShapedName);
        }
        public override void GenerateDecl(VerilogGenerationContext context) {
            var type = (Type switch { 
                VerilogIoType.Input => "input",
                VerilogIoType.Output => IsRegister ? "output reg" : "output" ,
                VerilogIoType.InOut => "inout",
                _ => throw new NotImplementedException()
            });
            VerilogSyntaxHelpers.GenerateShapedValueDecl(type, Name, context, [Shape.TotalBits]);
        }
    }
    public class VerilogModuleInstDecl : VerilogBlock {
        public Module SubModule { get; }
        public string Name { get; }
        public override bool NoLineEnd => false;
        public List<(string portName, VerilogAstNode value)> PortConnections { get; } = new();
        public VerilogModuleInstDecl(Module subModule, string instanceName) {
            SubModule = subModule;
            Name = instanceName;
        }
        public override void GenerateCode(VerilogGenerationContext context) {
            context.Append(SubModule.InternalModel.ModelName);
            context.Append(" ");
            context.Append(Name);
            context.AppendLine("(");

            using (context.BeginIndent()) {
                for(var i = 0; i< PortConnections.Count;i++) {
                    var connections = PortConnections[i];
                    context.AppendFormat(".{0}(", connections.portName);
                    connections.value.GenerateCode(context);
                    context.AppendLine(i == PortConnections.Count - 1 ? ")":"),");
                }
            }

            context.Append(")");
        }
    }
    public class VerilogKeyword : VerilogAstNode {
        public string Literal { get; }

        public override bool NoLineEnd => false;

        public VerilogKeyword(string literal) {
            Literal = literal;
        }
        public override void GenerateCode(VerilogGenerationContext context) {
            context.Append(Literal);
        }
    }
    public class VerilogIndexingSequence : VerilogAstNode {
        public ReadOnlyMemory<VerilogIndex> Indices { get; }
        public ReadOnlyMemory<int> Scales { get; }
        public override bool NoLineEnd => false;
        public VerilogIndexingSequence(ReadOnlyMemory<VerilogIndex> indices, ReadOnlyMemory<int> scales) {
            Indices = indices;
            Scales = scales;
        }

        public override void GenerateCode(VerilogGenerationContext context) {
            for(var i = 0; i < Indices.Length; i++) {
                Indices.Span[0].GenerateCode(context);
                context.AppendFormat(" * {0}", Scales.Span[i]);
                context.Append(i != Indices.Length - 1 ? " + " : "");
            }
        }
    }
    public abstract class VerilogIndex: VerilogAstNode {
        public static VerilogIndex[] FromSpecIndices(Func<AbstractValue, VerilogAstNode> expressionConvert,SpecifiedIndex[] indices) {
            return indices.Select<SpecifiedIndex, VerilogIndex>(e => {
                if (e.Flags == GenericIndexFlags.RangeIndex) return new VerilogFixedRangeIndex(e.Range);
                return new VerilogExpressionIndex(expressionConvert(e.IndexValue));
            }).ToArray();
        }
        public abstract void GenerateWithBaseIndex(VerilogGenerationContext context, VerilogAstNode baseIndex);
    }
    public class VerilogExpressionIndex : VerilogIndex {
        public VerilogAstNode SubExpression { get; }
        public override bool NoLineEnd => false;
        public VerilogExpressionIndex(VerilogAstNode identifier) {
            SubExpression = identifier;
        }
        public override void GenerateCode(VerilogGenerationContext context) {
            SubExpression.GenerateCode(context);
        }
        public override void GenerateWithBaseIndex(VerilogGenerationContext context, VerilogAstNode baseIndex) {
            SubExpression.GenerateCode(context);
            context.Append(" + ");
            baseIndex.GenerateCode(context);
        }
    }
    public class VerilogVariableIndex: VerilogIndex {
        public string VariableName { get; }
        public int Size { get; }
        public override bool NoLineEnd => false;
        public VerilogVariableIndex(string name, int size) {
            VariableName = name;
            Size = size;
        }
        public override void GenerateCode(VerilogGenerationContext context) {
            context.Append(VariableName);
        }
        public override void GenerateWithBaseIndex(VerilogGenerationContext context, VerilogAstNode baseIndex) {
            context.AppendFormat("{0} + ", VariableName);
            baseIndex.GenerateCode(context);
        }
    }
    public class VerilogFixedRangeIndex: VerilogIndex {
        public ImmutableArray<GenericIndex> Range { get; }
        public override bool NoLineEnd => false;
        public VerilogFixedRangeIndex(ReadOnlySpan<GenericIndex> range) {
            Range = range.ToImmutableArray();
        }
        public override void GenerateCode(VerilogGenerationContext context) {
            throw new NotImplementedException();
            // [TODO]
            //if(Range.Left + 1 == Range.Right) {
            //    context.Append(Range.Left.ToString());
            //} else {
            //    context.AppendFormat("{0}:{1}", Range.Left, Range.Right - 1);
            //}
        }
        public override void GenerateWithBaseIndex(VerilogGenerationContext context, VerilogAstNode baseIndex) {
            if (Range.Left + 1 == Range.Right) {
                context.Append(Range.Left.ToString());
                context.Append(" + ");
                baseIndex.GenerateCode(context);
            } else {
                context.AppendFormat("{0} + ", Range.Left);
                baseIndex.GenerateCode(context);
                context.AppendFormat(":{0} + ", Range.Right);
                baseIndex.GenerateCode(context);
            }
            
        }
    }

 

    public class VerilogRangeSelection : VerilogExpressionBase {
        public VerilogExpressionBase BaseValue { get; }
        public VerilogIndex[] SelectedRange { get; set; }
        public bool CanOmitIndexing { get; }
        public override bool NoLineEnd => false;
        protected static ReadOnlySpan<int> ResolveSelectionShape(ReadOnlySpan<int> baseShape, VerilogIndex[] range) {
            Debug.Assert(baseShape.Length == range.Length);

            

            throw new NotImplementedException();
        }
        public VerilogRangeSelection(VerilogExpressionBase baseValue, VerilogIndex[] range,bool canOmitIndexing = true):base(ResolveSelectionShape(baseValue.Shape, range)) {
            BaseValue = baseValue;
            SelectedRange = range;
            CanOmitIndexing = canOmitIndexing;
        }
        public override void GenerateCode(VerilogGenerationContext context) {
            BaseValue.GenerateCode(context);

            if (CanOmitIndexing && SelectedRange.Length == 1) return;
            foreach(var i in SelectedRange) {
                context.Append("[");
                context.Append("]");
            }
            
        }
    }
    public abstract class VerilogUnaryOperator : VerilogExpressionBase {
        public VerilogAstNode LeftValue { get; }
        public abstract string Operator { get; }
        public override bool NoLineEnd => false;
        public VerilogUnaryOperator(VerilogExpressionBase lhs):base(lhs.Shape) {
            LeftValue = lhs;
        }
        public override void GenerateCode(VerilogGenerationContext context) {
            context.Append("(");
            context.AppendFormat(Operator);
            LeftValue.GenerateCode(context);
            context.Append(")");
        }
    }
    public class VerilogNotOperator : VerilogUnaryOperator {
        public VerilogNotOperator(VerilogExpressionBase lhs) : base(lhs) {
        }

        public override string Operator => "~";
    }
    public abstract class VerilogExpressionBase : VerilogAstNode {
        protected TensorExpr? m_Expression;
        public ImmutableArray<int> Shape { get; }
        [DebuggerHidden]
        public TensorExpr Expression {
            get {
                if(m_Expression is null) m_Expression = CreateExpression();
                return m_Expression;
            }
        }

        protected virtual TensorExpr CreateExpression() {
            return new TensorVarExpr<VerilogExpressionBase>(this, Shape.AsSpan());
        }
        public VerilogExpressionBase(ReadOnlySpan<int> shape) {
            Shape = shape.ToImmutableArray();
        }
    }
    public abstract class VerilogBinaryOperator: VerilogExpressionBase {
        public VerilogExpressionBase LeftValue { get; }
        public VerilogExpressionBase RightValue { get; }
        public abstract string Operator { get; }
        public override bool NoLineEnd => false;
        public VerilogBinaryOperator(VerilogExpressionBase lhs, VerilogExpressionBase rhs):base(lhs.Shape) {
            LeftValue = lhs;
            RightValue = rhs;
        }
        public override void GenerateCode(VerilogGenerationContext context) {
            context.Append("(");
            LeftValue.GenerateCode(context);
            context.AppendFormat(Operator);
            RightValue.GenerateCode(context);
            context.Append(")");
        }
    }
    public class VerilogXorExpression : VerilogBinaryOperator {
        public VerilogXorExpression(VerilogExpressionBase lhs, VerilogExpressionBase rhs) : base(lhs, rhs) {
        }

        public override string Operator => " ^ ";
    }
    public class VerilogGreaterExpression : VerilogBinaryOperator {
        public VerilogGreaterExpression(VerilogExpressionBase lhs, VerilogExpressionBase rhs) : base(lhs, rhs) {
        }

        public override string Operator => " > ";
    }
    public class VerilogGreaterEqualExpression : VerilogBinaryOperator {
        public VerilogGreaterEqualExpression(VerilogExpressionBase lhs, VerilogExpressionBase rhs) : base(lhs, rhs) {
        }

        public override string Operator => " >= ";
    }
    public class VerilogLessEqualExpression : VerilogBinaryOperator {
        public VerilogLessEqualExpression(VerilogExpressionBase lhs, VerilogExpressionBase rhs) : base(lhs, rhs) {
        }

        public override string Operator => " <= ";
    }
    public class VerilogLessExpression : VerilogBinaryOperator {
        public VerilogLessExpression(VerilogExpressionBase lhs, VerilogExpressionBase rhs) : base(lhs, rhs) {
        }

        public override string Operator => " <";
    }
    public class VerilogAndExpression : VerilogBinaryOperator {
        public VerilogAndExpression(VerilogExpressionBase lhs, VerilogExpressionBase rhs) : base(lhs, rhs) {
        }

        public override string Operator => " & ";
    }
    public class VerilogAddExpression : VerilogBinaryOperator {
        public VerilogAddExpression(VerilogExpressionBase lhs, VerilogExpressionBase rhs) : base(lhs, rhs) {
        }

        public override string Operator => " + ";
    }
    public class VerilogEqualExpression : VerilogBinaryOperator {
        public VerilogEqualExpression(VerilogExpressionBase lhs, VerilogExpressionBase rhs) : base(lhs, rhs) {
        }

        public override string Operator => " == ";
    }
    public class VerilogNonEqualExpression : VerilogBinaryOperator {
        public VerilogNonEqualExpression(VerilogExpressionBase lhs, VerilogExpressionBase rhs) : base(lhs, rhs) {
        }

        public override string Operator => " != ";
    }
    public class VerilogSubExpression : VerilogBinaryOperator {
        public VerilogSubExpression(VerilogExpressionBase lhs, VerilogExpressionBase rhs) : base(lhs, rhs) {
        }

        public override string Operator => " - ";
    }
    public class VerilogMulExpression : VerilogBinaryOperator {
        public VerilogMulExpression(VerilogExpressionBase lhs, VerilogExpressionBase rhs) : base(lhs, rhs) {
        }

        public override string Operator => " * ";
    }
    public class VerilogDivExpression : VerilogBinaryOperator {
        public VerilogDivExpression(VerilogExpressionBase lhs, VerilogExpressionBase rhs) : base(lhs, rhs) {
        }

        public override string Operator => " / ";
    }
    public abstract class VerilogBlock: VerilogAstNode {
        public List<VerilogAstNode> SubNodes { get; } = new();
        public override bool NoLineEnd => true;
    }
    public class VerilogAlwaysFF : VerilogBlock,IVerilogElement {
        public List<VerilogRegisterDef> RegisterNames { get; } = new();
        public VerilogAstNode ClockSignal { get; set; }
        public VerilogAstNode? ResetSignal { get; set; }
        public VerilogAstNode? SoftResetSignal { get; set; }
        public VerilogAstNode? ClockEnableSignal { get; set; }
        public bool ClockPositiveEdge { get; set; } = true;
        public bool ResetPositiveEdge { get; set; } = true;
        public VerilogAlwaysFF(VerilogAstNode clockSignal) {
            ClockSignal = clockSignal;
        }
        protected void GenerateBodyWithReset(VerilogGenerationContext context) {
            if(ResetSignal != null) {
                context.Append(ResetPositiveEdge ? "if(" : "if(!");
                ResetSignal.GenerateCode(context);
                context.AppendLine(") begin");
                using (context.BeginIndent()) {
                    foreach(var  i in RegisterNames) {
                        i.GenerateCode(context);
                        context.AppendFormat(" <= {0}'b0;",i.RegisterInfo.UntypedType.WidthBits);
                        context.AppendLine();
                    }
                }
                context.AppendLine("end else begin");
                using (context.BeginIndent()) {
                    GenerateBody(context);
                }
                context.AppendLine("end");
            } else {
                GenerateBody(context);
            }
        }
        protected void GenerateBody(VerilogGenerationContext context) {
            foreach (var i in SubNodes) {
                i.GenerateCode(context);
                if (!i.NoAutoNewLine) context.AppendLine(i.NoLineEnd ? "" : ";");
            }
        }
        public override void GenerateCode(VerilogGenerationContext context) {
            
        }

        public void GenerateDecl(VerilogGenerationContext context) {
        }

        public void GenerateBlock(VerilogGenerationContext context) {
            context.AppendFormat("always@({0} ", ClockPositiveEdge ? "posedge" : "negedge");
            ClockSignal.GenerateCode(context);
            if(ResetSignal != null) {
                context.AppendFormat(" or {0} ", ResetPositiveEdge ? "posedge" : "negedge");
                ResetSignal.GenerateCode(context);
            }
            context.AppendLine(") begin");

            using(context.BeginIndent()) {
                GenerateBodyWithReset(context);
            }
            context.Append("end");
        }
    }
    public class VerilogConst : VerilogExpressionBase {
        public BigInteger Value { get; }
        public int BitWidth { get; }
        public override bool NoLineEnd => false;

        public VerilogConst(int bits, BigInteger value) : base(new([bits])) {
            BitWidth = bits;
            Value = value;
        }

        public override void GenerateCode(VerilogGenerationContext context) {
            // TODO: Fix minus number
            context.AppendFormat("{0}'d{1}", BitWidth, Value);
        }
    }
    public class VerilogCombinationExpression : VerilogAstNode {
        public VerilogAstNode[] Values { get; }
        public override bool NoLineEnd => false;
        public VerilogCombinationExpression(VerilogAstNode[] nodes) {
            Values = nodes;
        }
        public override void GenerateCode(VerilogGenerationContext context) {
            if(Values.Length > 1) context.Append("{");
            for(var i = 0; i < Values.Length; i++) {
                Values[i].GenerateCode(context);
                if (i != Values.Length - 1) context.Append(",");
            }

            if (Values.Length > 1) context.Append("}");
        }
    }
    public class VerilogCase: VerilogAstNode {
        public VerilogAstNode Condition { get; set; }
        public IEnumerable<VerilogAstNode>[] Branches { get; }
        public VerilogAstNode[] Constants { get; }

        public override bool NoLineEnd => true;

        public VerilogCase(VerilogAstNode condition, int branchCount) {
            Condition = condition;
            Constants = new VerilogAstNode[branchCount];
            Branches = new IEnumerable<VerilogAstNode>[branchCount];
        }
        public override void GenerateCode(VerilogGenerationContext context) {
            context.Append("case(");
            Condition.GenerateCode(context);
            context.AppendLine(")");

            using (context.BeginIndent()) {
                for(var i=0;i< Constants.Length; i++) {
                    Constants[i].GenerateCode(context);
                    context.AppendLine(": begin");
                    using (context.BeginIndent()) { 
                        foreach(var j in Branches[i]) {
                            j.GenerateCode(context);
                            context.AppendLine(j.NoLineEnd ? "" : ";");
                        }
                    }
                    context.AppendLine("end");
                }
            }

            context.AppendLine("endcase");
        }
    }
    public class VerilogBranch: VerilogAstNode {
        public VerilogAstNode Condition { get; }
        public List<VerilogAstNode> TrueBranches { get; } = new();
        public List<VerilogAstNode> FalseBranches { get; } = new();
        public VerilogBranch(VerilogAstNode condition) {
            Condition = condition;
        }

        public override bool NoLineEnd => true;

        public override void GenerateCode(VerilogGenerationContext context) {
            context.Append("if(");
            Condition.GenerateCode(context);
            context.AppendLine(") begin");
            using (context.BeginIndent()) {
                foreach(var i in TrueBranches) {
                    i.GenerateCode(context);
                    context.AppendLine(i.NoLineEnd ? "" : ";");
                }
            }
            if(FalseBranches.Count != 0) {
                context.AppendLine("end else begin");

                using (context.BeginIndent()) {
                    foreach (var i in FalseBranches) {
                        i.GenerateCode(context);
                        context.AppendLine(i.NoLineEnd ? "" : ";");
                    }
                }
            }
            context.Append("end");
        }
    }
    public class VerilogSubComponentGroupBlock : VerilogBlock, IVerilogElement {
        public SubComponentDesc InstantiationGroup { get; }
        public string InstantiationName { get; }
        public Dictionary<IoComponent, VerilogExpressionBase> ShapedIoPorts { get; } = new();
        public List<VerilogValueDef> GroupFlattenIoWires { get; } = new();
        public VerilogModule ParentModule { get; }
        public VerilogSubComponentGroupBlock(VerilogModule module, SubComponentDesc instGroup, string instName) {
            ParentModule = module;
            InstantiationGroup = instGroup;
            InstantiationName = instName;

            for(var i = 0; i < instGroup.Count; i++)
                SubNodes.Add(new VerilogModuleInstDecl((Module)instGroup[i], $"{instName}_{i}"));

            var subModel = instGroup[0].InternalModel;

            foreach(var portInfo in subModel.IoPortShape) {
                var portName = $"{instName}_{portInfo.Name()}";
                var portType = portInfo.Direction switch {
                    IoPortDirection.Input => VerilogIoType.Input,
                    IoPortDirection.Output => VerilogIoType.Output,
                    IoPortDirection.InOut => VerilogIoType.InOut,
                    _ => throw new NotImplementedException()
                };

                var wireDef = new VerilogWireDef(portName, new([instGroup.Count, .. portInfo.Shape.RawShape]));
                
                var flattenWireDef = wireDef;
                if(portInfo.Shape.Length > 1) {
                    flattenWireDef = new VerilogWireDef($"{portName}_f", new([instGroup.Count, portInfo.Shape.TotalBits]));

                    if(portInfo.Direction == IoPortDirection.Input) {
                        flattenWireDef.AssignExpression = TensorExpr.Reshape(wireDef.Expression, [instGroup.Count, portInfo.Shape.TotalBits]);
                    } else {
                        wireDef.AssignExpression = TensorExpr.Reshape(flattenWireDef.Expression, [instGroup.Count, .. portInfo.Shape.RawShape]);
                    }
                }

                GroupFlattenIoWires.Add(flattenWireDef);

                var path = portInfo.Location;
                for(var j = 0; j < instGroup.Count; j++) {
                    var externalOutput = path.TraceValue(instGroup[j]) ?? throw new NullReferenceException("Missing external placeholder");
                    var wireSelection = new VerilogRangeSelection(wireDef, [new VerilogFixedRangeIndex(new SpecifiedRange(j, (j + 1)))], false);
                    var flattenSelection = new VerilogRangeSelection(flattenWireDef, [new VerilogFixedRangeIndex(new SpecifiedRange(j, (j + 1)))], false);

                    ShapedIoPorts.Add(externalOutput, wireSelection);

                    ((VerilogModuleInstDecl)SubNodes[j]).PortConnections.Add((portInfo.Name(), flattenSelection));
                }
            }
        }
        public void GenerateBlock(VerilogGenerationContext context) {
            throw new NotImplementedException();
        }

        public override void GenerateCode(VerilogGenerationContext context) {
            throw new NotImplementedException();
        }

        public void GenerateDecl(VerilogGenerationContext context) {
            throw new NotImplementedException();
        }
    }
    public class VerilogAlwaysComb : VerilogBlock,IVerilogElement {
        public void GenerateBlock(VerilogGenerationContext context) {
            context.AppendLine("always@(*) begin");
            using(context.BeginIndent()) {
                foreach(var i in SubNodes) {
                    i.GenerateCode(context);
                    if(!i.NoAutoNewLine) context.AppendLine(i.NoLineEnd ? "" : ";");
                }
            }
            context.Append("end");
        }

        public override void GenerateCode(VerilogGenerationContext context) {
            
        }

        public void GenerateDecl(VerilogGenerationContext context) {

        }
    }
    public class VerilogOrExpression : VerilogBinaryOperator {
        public VerilogOrExpression(VerilogExpressionBase lhs, VerilogExpressionBase rhs) : base(lhs, rhs) {
        }

        public override string Operator => " | ";
    }

    public class VerilogTensorResult : VerilogValueDef {
        public override string Name { get; set; }
        public override bool NoLineEnd => true;

        public VerilogTensorResult(string name, TensorExpr expression):base(new(expression.Shape.AsSpan())) {
            Name = name;
            m_Expression = expression;
        }
        public override void GenerateCode(VerilogGenerationContext context) {
            context.Append(Name);
        }
        public override void GenerateDecl(VerilogGenerationContext context) {
            VerilogSyntaxHelpers.GenerateShapedValueDecl("wire", Name, context, Shape.AsSpan());
        }
        public override void GenerateBlock(VerilogGenerationContext context) {
            throw new NotImplementedException();
        }
    }

    public class VerilogGenerator : ICodeGenBackend<VerilogGenerationConfiguration> {
        public VerilogExpressionBase ConvertExpressions(AbstractValue value, ComponentModel model, VerilogModule module) {
            if (value is IUntypedBinaryExpression binaryExpr) {
                var lhs = ConvertExpressions(binaryExpr.UntypedLeft, model, module);
                var rhs = ConvertExpressions(binaryExpr.UntypedRight, model, module);
                if (value is BoolXorExpression) 
                    return new VerilogXorExpression(lhs, rhs);
                if (value is BoolAndExpression)
                    return new VerilogAndExpression(lhs, rhs);
                if (value is BoolOrExpression)
                    return new VerilogOrExpression(lhs, rhs);
                if (value is BoolEqualExpression) return new VerilogEqualExpression(lhs, rhs);
                if (value is BoolNonEqualExpression) return new VerilogNonEqualExpression(lhs, rhs);

                if (value is UIntAddExpression) {
                    return new VerilogAddExpression(lhs, rhs);
                }
                if (value is UIntSubExpression) {
                    return new VerilogSubExpression(lhs, rhs);
                }
                if (value is UIntMulExpression) {
                    return new VerilogMulExpression(lhs, rhs);
                }
                if (value is UIntDivExpression) {
                    return new VerilogDivExpression(lhs, rhs);
                }
                if (value is UIntXorExpression) {
                    return new VerilogXorExpression(lhs, rhs);
                }
                if (value is UIntAndExpression) {
                    return new VerilogAndExpression(lhs, rhs);
                }
                if (value is UIntOrExpression) {
                    return new VerilogOrExpression(lhs, rhs);
                }

                if (value is UIntEqualExpression) return new VerilogEqualExpression(lhs, rhs);
                if (value is UIntNonEqualExpression) return new VerilogNonEqualExpression(lhs, rhs);
                if (value is UIntGreaterExpression) return new VerilogGreaterExpression(lhs, rhs);
                if (value is UIntGreaterEqualExpression) return new VerilogGreaterEqualExpression(lhs, rhs);
                if (value is UIntLessExpression) return new VerilogLessExpression(lhs, rhs);
                if (value is UIntLessEqualExpression) return new VerilogLessEqualExpression(lhs, rhs);
            }

            if(value is IUntypedUnaryExpression unaryExpression) {
                var lhs = ConvertExpressions(unaryExpression.UntypedBaseValue, model, module);

                if (value is IUntypedCastExpression) {
                    return lhs;
                }
                if (value is UIntNotExpression) return new VerilogNotOperator(lhs);
                if (value is IUntypedGeneralBitSelectionExpression selectionExpression) {
                    var baseExpression = ConvertExpressions(selectionExpression.UntypedBaseValue, model, module);
                    var converter = (Func<AbstractValue, VerilogAstNode>)(e => ConvertExpressions(e, model, module));

                    var selection =  new VerilogRangeSelection(baseExpression, VerilogIndex.FromSpecIndices(converter,selectionExpression.SelectedRange.Indices));

                    return selection;
                }

                throw new NotImplementedException();

            }
            if(value is IUntypedIoRightValueWrapper rightValue) {
                var ioComponent = rightValue.UntypedComponent;
                var iUntyped = (IUntypedConstructionPort)ioComponent;
                if(iUntyped.Component == model.ReferenceModule) {
                    var ioRef = module.IoPorts.Where(e => e.DeclIoComponent == ioComponent).FirstOrDefault();
                    if (ioRef == null) throw new Exception($"IO port for {ioComponent.Name()} missing");

                    return ioRef;
                }

                var subGroup = module.SubComponentGroups.Find(e => e.InstantiationGroup.Contains(iUntyped.Component));
                if(subGroup is not null) {
                    return subGroup.ShapedIoPorts[ioComponent];
                }
                throw new NotImplementedException();
                
                
            }
            if (value is INamedStageExpression namedStaged) {
                var desc = (ISubValueStageDesc)namedStaged.Descriptor;
                var index = ((List<INamedStageExpression>)desc).IndexOf(namedStaged);
                var staged = module.StageValueMap[desc.InstanceName];
                var selection = new VerilogRangeSelection(staged, [new VerilogFixedRangeIndex(new(index, index + 1))]);


                return selection;
            }
            if(value is UIntLiteral literal) {
                return new VerilogConst((int)literal.UntypedType.WidthBits, literal.Value);
            }
            if (value is BoolLiteral bLiteral) {
                return new VerilogConst(1, bLiteral.Value ? 1 : 0);
            }
            if (value is RegisterValue registerValue) {
                return module.Registers.Where(e => e.RegisterInfo == registerValue.BaseRegister).First();
            }
            
            if(value is IWireRightValueWrapper wireWrapper) {
                return module.Wires.Where(e => e.WirePrototype == wireWrapper.UntyedWire).First();
            }
            if(value is IRegRightValueWrapper regWrapper) {
                return module.Registers.Where(e => e.RegisterInfo == regWrapper.UntyedReg).First();
            }
            throw new NotImplementedException();
        }
        protected void AddClockDomain(VerilogModule moduleAst, ClockDomain domain) {

        }
        protected VerilogModule ConvertModuleAst(Module module) {
            var moduleAst = new VerilogModule(module);

            var componentModel = module.InternalModel;

            var converter = (Func<AbstractValue, VerilogAstNode>)(e => ConvertExpressions(e, componentModel, moduleAst));


            foreach (var clockDom in componentModel.UsedClockDomains) {

            }

            // self io ports
            foreach(var portInfo in componentModel.IoPortShape) {
                var portName = portInfo.Name();
                var portType = portInfo.Direction switch { 
                    IoPortDirection.Input => VerilogIoType.Input,
                    IoPortDirection.Output => VerilogIoType.Output,
                    IoPortDirection.InOut => VerilogIoType.InOut,
                    _ => throw new NotImplementedException()
                };

                var promotedRegister = false;

                var portDecl = new VerilogIoDecl((IoComponent)portInfo, portName, portType, portInfo.Shape, promotedRegister);
                moduleAst.ModuleValues.Add(portDecl);
                moduleAst.Contents.Add(portDecl);
            }

            foreach(var (wire, wireAux) in componentModel.WireLikeObjects) {
                if(wire is Wire wireObject) {
                    var wireDef = new VerilogWireDef(wireObject.Name(), wireObject.Shape, wireObject);
                    moduleAst.ModuleValues.Add(wireDef);
                }
            }

            
            foreach (var (instName, instGroup) in componentModel.OverlappedObjects) {
                if (!(instGroup is SubValueStageDesc subStageDesc)) continue;

                var stageValueWire = new VerilogWireDef(instName, new([subStageDesc.Count, .. subStageDesc.SingleInstanceShape.RawShape]), subStageDesc);
                moduleAst.ModuleValues.Add(stageValueWire);

                moduleAst.StageValueMap.Add(instName, stageValueWire);


                for(var i = 0; i < subStageDesc.Count; i++) {
                    var selection = new VerilogRangeSelection(stageValueWire, [new VerilogFixedRangeIndex(new SpecifiedRange(i, (i + 1)))]);
                    var value = ConvertExpressions(subStageDesc[i].InternalValue, componentModel, moduleAst);
                    //moduleAst.Contents.Add(new VeriloAssignment(selection, value));
                }
            }

            foreach(var i in componentModel.Registers) {
                var registerDef = new VerilogRegisterDef(i, i.Name(), i.Shape);
                moduleAst.ModuleValues.Add(registerDef);
            }

            // prepare submodule io

            foreach(var (instName, instGroup) in componentModel.OverlappedObjects) {
                if (!(instGroup is SubComponentDesc subComponentInstGroup)) continue;

                var instNode = new VerilogSubComponentGroupBlock(moduleAst, subComponentInstGroup, instName);
                moduleAst.Contents.Add(instNode);
            }

            foreach (var (wire,wireAux) in componentModel.WireLikeObjects) {
                var assignable = default(IAssignableValue);
                var identifier = default(VerilogExpressionBase);
                var totalBits = 0;

                if(wire is Wire wireObject) {
                    totalBits = (int)wireObject.UntypedType.WidthBits;
                    assignable = wireObject;
                    identifier = moduleAst.Wires.Where(e => e.WirePrototype == wireObject).First();
                }
                if(wire is IUntypedConstructionPort port) {
                    if (port.Component != module) continue;
                    if(port is IAssignableValue) {
                        totalBits = (int)port.UntypedType.WidthBits;
                        assignable = (IAssignableValue)port;

                        var portNode = moduleAst.IoPorts.Where(e => e.DeclIoComponent == port).FirstOrDefault()
                            ?? throw new NullReferenceException($"Missing port ast node for {port.Name()}");
                        identifier = portNode;

                    }
                }

                if(assignable != null && identifier!=null) {
                    if (componentModel.GenericAssignments.ContainsKey(assignable)) {
                        var outputAssignments = componentModel.GenericAssignments[assignable];
                        foreach (var j in outputAssignments) {
                            var selection = new VerilogRangeSelection(identifier, VerilogIndex.FromSpecIndices(converter, j.SelectedRange.Indices));
                            var expr = ConvertExpressions(j.RightValue, componentModel, moduleAst);
                            moduleAst.Contents.Add(new VeriloAssignment(selection, expr));
                        }
                    }
                    

                }
            }


            var alwaysCombBlock = new VerilogAlwaysComb();

            alwaysCombBlock.SubNodes.AddRange(ExpandBehaviorBlock(componentModel.Behavior.TypedRoot.FalseBranch, componentModel, moduleAst, (e) => { 
                if(e is PrimaryAssignment assignment) {
                    return !(assignment.LeftValue is ClockDrivenRegister);
                }
                return true;
            }));
            if(alwaysCombBlock.SubNodes.Count!=0) moduleAst.Contents.Add(alwaysCombBlock);


            foreach(var i in componentModel.UsedClockDomains) {
                var registers = moduleAst.Registers.Where(e => {
                    if (e.RegisterInfo is ClockDrivenRegister realRegister)
                        return realRegister.ClockDom == i;
                    return false;
                });

                if (registers.Count() == 0) continue;

                var clock = moduleAst.IoPorts.Where(e => (e.DeclIoComponent is ClockDomainInput{ SignalType:ClockDomainSignal.Clock} domainInput) && domainInput.ClockDom == i).FirstOrDefault();
                if (clock == null) throw new NullReferenceException($"Unable to resolve clock signal for clock domain '{i.Name}'");


                var alwaysFF = new VerilogAlwaysFF(clock) {
                    ClockPositiveEdge = i.ClockRiseEdge,
                    ResetPositiveEdge = i.ResetHighActive
                };
                if(!(i.RawReset is null)) {
                    var reset = moduleAst.IoPorts.Where(e => (e.DeclIoComponent is ClockDomainInput { SignalType: ClockDomainSignal.Reset } domainInput) && domainInput.ClockDom == i).First();

                    if (reset == null) throw new NullReferenceException($"Unable to resolve reset signal for clock domain '{i.Name}'");
                    alwaysFF.ResetSignal = reset;
                }

                alwaysFF.SubNodes.AddRange(ExpandBehaviorBlock(componentModel.Behavior.TypedRoot.FalseBranch, componentModel, moduleAst, (e) => {
                    if (e is PrimaryAssignment assignment) {
                        return (assignment.LeftValue is ClockDrivenRegister realRegister) && (realRegister.ClockDom == i);
                    }
                    return true;
                }, true));
                alwaysFF.RegisterNames.AddRange(registers);

                moduleAst.Contents.Add(alwaysFF);
            }

            return moduleAst;
        }

        protected IEnumerable<VerilogAstNode> ExpandBehaviorBlock(IEnumerable<BehaviorDesc> block, ComponentModel compModel, VerilogModule moduleAst,Func<BehaviorDesc,bool> allowEmit, bool noBlocking = false) {
            var converter = (Func<AbstractValue, VerilogAstNode>)(e => ConvertExpressions(e, compModel, moduleAst));
            
            return block.Where(allowEmit).Select(e => {
                if (e is BranchDesc branch) {
                    var trueBlock = ExpandBehaviorBlock(branch.TrueBranch, compModel, moduleAst, allowEmit);
                    var falseBlock = ExpandBehaviorBlock(branch.FalseBranch, compModel, moduleAst, allowEmit);

                    if(trueBlock.Count() == 0 && falseBlock.Count() == 0) {
                        return new VerilogEmptyLine(true);
                    }

                    var ifClause = new VerilogBranch(ConvertExpressions(branch.Condition.Condition, compModel, moduleAst));
                    ifClause.TrueBranches.AddRange(trueBlock);
                    ifClause.FalseBranches.AddRange(falseBlock);
                    return (VerilogAstNode)ifClause;
                }
                if(e is PrimaryAssignment assignment) {
                    var leftValue = ResolveLeftValueRef(assignment.UntypedLeftValue, moduleAst);

                    var value = ConvertExpressions(assignment.RightValue, compModel, moduleAst);
                    var leftValueSelection = new VerilogRangeSelection(leftValue, VerilogIndex.FromSpecIndices(converter,assignment.SelectedRange.Indices));
                    var assignmentNode = new VerilogInAlwaysAssignment(leftValueSelection, value, noBlocking);
                    return (VerilogAstNode)assignmentNode;
                }
                if(e is SwitchDesc switchDesc) {
                    var bits = (int)switchDesc.SwitchValue.UntypedType.WidthBits;
                    var condition = ConvertExpressions(switchDesc.SwitchValue, compModel, moduleAst);
                    var caseNode = new VerilogCase(condition, switchDesc.BranchList.Length);

                    for(var i  = 0; i < switchDesc.BranchList.Length; i++) {
                        caseNode.Constants[i] = i != switchDesc.BranchList.Length - 1 ? new VerilogConst(bits, switchDesc[i]) : new VerilogKeyword("default");
                        caseNode.Branches[i] = ExpandBehaviorBlock(switchDesc.BranchList[i], compModel, moduleAst, allowEmit);
                    }

                    return (VerilogAstNode)caseNode;
                }
                throw new NotImplementedException();
            });

        }
        protected VerilogExpressionBase ResolveLeftValueRef(IAssignableValue leftValue, VerilogModule moduleAst) {
            if(leftValue is IUntypedConstructionPort port) {
                var portNode = moduleAst.IoPorts.Where(e => e.DeclIoComponent == port).FirstOrDefault() 
                    ?? throw new Exception($"Unable to resolve port ast node for port '{port.Name()}'");

                return portNode;

            }
            if(leftValue is RegisterDesc register) {
                var regNode = moduleAst.Registers.Where(e => e.RegisterInfo == register).FirstOrDefault()
                    ?? throw new Exception($"Unable to resolve register ast node for register '{register.Name()}'"); ;

                return regNode;
            }

            throw new NotImplementedException();
        }
        public string GenerateModuleCode(Module module, VerilogGenerationConfiguration? configuration = null) {
            var moduleAst = ConvertModuleAst(module);

            var context = new VerilogGenerationContext(configuration ?? new(), this, moduleAst);
            
            moduleAst.GenerateCode(context);

            return context.Dump();
        }
    }
    public class VerilogInAlwaysAssignment : VerilogAstNode {
        public VerilogAstNode LeftValue { get; }
        public VerilogAstNode RightValue { get; }
        public bool NoBlocking { get; }
        public override bool NoLineEnd => false;
        public VerilogInAlwaysAssignment(VerilogAstNode leftValue, VerilogAstNode rightValue, bool noBlocking = false) {
            LeftValue = leftValue;
            RightValue = rightValue;
            NoBlocking = noBlocking;
        }

        public override void GenerateCode(VerilogGenerationContext context) {
            LeftValue.GenerateCode(context);
            context.Append(NoBlocking ? " <= " : " = ");
            RightValue.GenerateCode(context);
        }
    }
}
