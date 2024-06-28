using Iced.Intel;
using IntelliVerilog.Core.Analysis;
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
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.CodeGen.Verilog {
    public class VerilogGenerationConfiguration: CodeGenConfiguration,ICodeGenConfiguration<VerilogGenerationConfiguration> {
        public int IndentCount { get; set; } = 4;
        public char IndentChar { get; set; } = ' ';
        public string NewLine { get; set; } = Environment.NewLine;

        public static ICodeGenBackend<VerilogGenerationConfiguration> CreateBackend() {
            return new VerilogGenerator();
        }
    }
    public class VerilogGenerationContext {
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
        public VerilogGenerationContext(VerilogGenerationConfiguration configuration) {
            Configuration = configuration;
            m_Buffer = new();
        }
        public IDisposable BeginIndent() {
            m_Indent += Configuration.IndentCount;
            m_IndentPrefix = new(Configuration.IndentChar, m_Indent);
            return new VerilogGenerationIndent(this);
        }
        protected void EndIndent() {
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
    public abstract class VerilogValueDef: VerilogAstNode { 
        public abstract string Name { get; set; }
    }
    public class VerilogRegisterDef : VerilogValueDef {
        protected VerilogPureIdentifier? m_IdentifierCache = null;
        public RegisterDesc RegisterInfo { get; }
        public override string Name { get; set; }
        public uint Width { get; set; }
        public int[] Shape { get; set; }
        public VerilogPureIdentifier Identifier {
            get {
                if (m_IdentifierCache == null) m_IdentifierCache = new(Name);
                return m_IdentifierCache;
            }
        }

        public override bool NoLineEnd => false;

        public VerilogRegisterDef(RegisterDesc register , string name, uint width, int[] shape) {
            RegisterInfo = register;
            Name = name;
            Width = width;
            Shape = shape;
        }
        public override void GenerateCode(VerilogGenerationContext context) {
            if (Width > 1) {
                context.AppendFormat("reg[{0}:0] ", Width - 1);
            } else {
                context.Append("reg ");
            }

            context.Append(Name);

            foreach (var i in Shape) {
                context.AppendFormat("[{0}:0]", i - 1);
            }
        }
    }
    public class VerilogWireDef : VerilogValueDef {
        protected VerilogPureIdentifier? m_IdentifierCache = null;
        public override string Name { get; set; }
        public uint Width { get; set; }
        public int[] Shape { get; set; }
        public VerilogPureIdentifier Identifier {
            get {
                if (m_IdentifierCache == null) m_IdentifierCache = new(Name);
                return m_IdentifierCache;
            }
        }

        public override bool NoLineEnd => false;

        public VerilogWireDef(string name, uint width, int[] shape) {
            Name = name;
            Width = width;
            Shape = shape;
        }
        public override void GenerateCode(VerilogGenerationContext context) {
            if(Width > 1) {
                context.AppendFormat("wire[{0}:0] ", Width - 1);
            } else {
                context.Append("wire ");
            }

            context.Append(Name);

            foreach(var i in Shape) {
                context.AppendFormat("[{0}:0]", i - 1);
            }
        }
    }
    public class VerilogEmptyLine : VerilogAstNode {
        public override bool NoAutoNewLine { get; }
        public override bool NoLineEnd => true;
        public VerilogEmptyLine(bool noNewLine = false) {
            NoAutoNewLine = noNewLine;
        }
        public override void GenerateCode(VerilogGenerationContext context) {
            
        }
    }

    public class VeriloAssignment : VerilogAstNode {
        public VerilogAstNode LeftValue { get; }
        public VerilogAstNode RightValue { get; }
        public override bool NoLineEnd => false;
        public VeriloAssignment(VerilogAstNode leftValue, VerilogAstNode rightValue) {
            LeftValue = leftValue;
            RightValue = rightValue;
        }
        public override void GenerateCode(VerilogGenerationContext context) {
            context.Append("assign ");
            LeftValue.GenerateCode(context);
            context.Append(" = ");
            RightValue.GenerateCode(context);
        }
    }
    public class VerilogModule : VerilogAstNode {
        public List<VerilogIoDecl> IoPorts { get; set; } = new();
        public Dictionary<IWireLike,VerilogWireDef> ExplicitWires { get; set; } = new();
        public List<VerilogRegisterDef> Registers { get; } = new();
        public Dictionary<IoComponent, VerilogAstNode> SubModuleOutputMap { get; } = new();
        public Module BackModule { get; }
        public List<VerilogAstNode> Contents { get; } = new();
        public VerilogModule(Module backModule) => BackModule = backModule;
        public override bool NoLineEnd => true;
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
        public uint Width { get; set; }
        public bool IsRegister { get; set; }
        public IoComponent DeclIoComponent { get; set; }
        public override bool NoLineEnd => false;
        public VerilogPureIdentifier Identifier { get; }
        public VerilogIoDecl(IoComponent decl ,string name, VerilogIoType type,  uint width = 1, bool isRegister = false) {
            DeclIoComponent = decl;
            Name = name;
            Type = type;
            Width = width;
            IsRegister = isRegister;
            Identifier = new(name);
        }

        public override void GenerateCode(VerilogGenerationContext context) {
            context.Append(Type switch { 
                VerilogIoType.Input => "input",
                VerilogIoType.Output => IsRegister ? "output reg" : "output" ,
                VerilogIoType.InOut => "inout",
                _ => throw new NotImplementedException()
            });
            if(Width > 1) {
                context.AppendFormat("[{0}:0] ", Width - 1);
            } else {
                context.Append(" ");
            }
            context.Append(Name);
        }
    }
    public class ModuleInstDecl : VerilogAstNode {
        public Module SubModule { get; }
        public string Name { get; }
        public override bool NoLineEnd => false;
        public List<(string portName, VerilogAstNode value)> PortConnections { get; } = new();
        public ModuleInstDecl(Module subModule, string instanceName) {
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
    public class VerilogPureIdentifier : VerilogValueDef {
        public override string Name { get; set; }
        public override bool NoLineEnd => false;
        public VerilogPureIdentifier(string name) {
            Name = name;
        }

        public override void GenerateCode(VerilogGenerationContext context) {
            context.Append(Name);
        }
    }
    public class VerilogRangeSelection :VerilogAstNode{
        public VerilogAstNode BaseValue { get; }
        public SpecifiedRange SelectedRange { get; set; }
        public int TotalBits { get; }
        public bool CanOmitIndexing { get; }
        public override bool NoLineEnd => false;
        public VerilogRangeSelection(VerilogAstNode baseValue, SpecifiedRange range, int totalBits,bool canOmitIndexing = true) {
            BaseValue = baseValue;
            SelectedRange = range;
            TotalBits = totalBits;
            CanOmitIndexing = canOmitIndexing;
        }

        public override void GenerateCode(VerilogGenerationContext context) {
            BaseValue.GenerateCode(context);

            if (TotalBits == 1 && CanOmitIndexing) return;
            var start = SelectedRange.Left;
            var end = SelectedRange.Right - 1;
            if (start == end) {
                context.AppendFormat("[{0}]", end);

            } else {
                context.AppendFormat("[{0}:{1}]", end, start);
            }
        }
    }
    public abstract class VerilogUnaryOperator : VerilogAstNode {
        public VerilogAstNode LeftValue { get; }
        public abstract string Operator { get; }
        public override bool NoLineEnd => false;
        public VerilogUnaryOperator(VerilogAstNode lhs) {
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
        public VerilogNotOperator(VerilogAstNode lhs) : base(lhs) {
        }

        public override string Operator => "~";
    }
    public abstract class VerilogBinaryOperator: VerilogAstNode {
        public VerilogAstNode LeftValue { get; }
        public VerilogAstNode RightValue { get; }
        public abstract string Operator { get; }
        public override bool NoLineEnd => false;
        public VerilogBinaryOperator(VerilogAstNode lhs, VerilogAstNode rhs) {
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
        public VerilogXorExpression(VerilogAstNode lhs, VerilogAstNode rhs) : base(lhs, rhs) {
        }

        public override string Operator => " ^ ";
    }
    public class VerilogGreaterExpression : VerilogBinaryOperator {
        public VerilogGreaterExpression(VerilogAstNode lhs, VerilogAstNode rhs) : base(lhs, rhs) {
        }

        public override string Operator => " > ";
    }
    public class VerilogGreaterEqualExpression : VerilogBinaryOperator {
        public VerilogGreaterEqualExpression(VerilogAstNode lhs, VerilogAstNode rhs) : base(lhs, rhs) {
        }

        public override string Operator => " >= ";
    }
    public class VerilogLessEqualExpression : VerilogBinaryOperator {
        public VerilogLessEqualExpression(VerilogAstNode lhs, VerilogAstNode rhs) : base(lhs, rhs) {
        }

        public override string Operator => " <= ";
    }
    public class VerilogLessExpression : VerilogBinaryOperator {
        public VerilogLessExpression(VerilogAstNode lhs, VerilogAstNode rhs) : base(lhs, rhs) {
        }

        public override string Operator => " <";
    }
    public class VerilogAndExpression : VerilogBinaryOperator {
        public VerilogAndExpression(VerilogAstNode lhs, VerilogAstNode rhs) : base(lhs, rhs) {
        }

        public override string Operator => " & ";
    }
    public class VerilogAddExpression : VerilogBinaryOperator {
        public VerilogAddExpression(VerilogAstNode lhs, VerilogAstNode rhs) : base(lhs, rhs) {
        }

        public override string Operator => " + ";
    }
    public class VerilogEqualExpression : VerilogBinaryOperator {
        public VerilogEqualExpression(VerilogAstNode lhs, VerilogAstNode rhs) : base(lhs, rhs) {
        }

        public override string Operator => " == ";
    }
    public class VerilogNonEqualExpression : VerilogBinaryOperator {
        public VerilogNonEqualExpression(VerilogAstNode lhs, VerilogAstNode rhs) : base(lhs, rhs) {
        }

        public override string Operator => " != ";
    }
    public class VerilogSubExpression : VerilogBinaryOperator {
        public VerilogSubExpression(VerilogAstNode lhs, VerilogAstNode rhs) : base(lhs, rhs) {
        }

        public override string Operator => " - ";
    }
    public class VerilogMulExpression : VerilogBinaryOperator {
        public VerilogMulExpression(VerilogAstNode lhs, VerilogAstNode rhs) : base(lhs, rhs) {
        }

        public override string Operator => " * ";
    }
    public class VerilogDivExpression : VerilogBinaryOperator {
        public VerilogDivExpression(VerilogAstNode lhs, VerilogAstNode rhs) : base(lhs, rhs) {
        }

        public override string Operator => " / ";
    }
    public class VerilogAlwaysFF : VerilogAstNode {
        public List<VerilogAstNode> SubNodes { get; } = new();
        public List<VerilogRegisterDef> RegisterNames { get; } = new();
        public VerilogAstNode ClockSignal { get; set; }
        public VerilogAstNode? ResetSignal { get; set; }
        public VerilogAstNode? SoftResetSignal { get; set; }
        public VerilogAstNode? ClockEnableSignal { get; set; }
        public bool ClockPositiveEdge { get; set; } = true;
        public bool ResetPositiveEdge { get; set; } = true;
        public override bool NoLineEnd => true;
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
                        i.Identifier.GenerateCode(context);
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
            context.AppendFormat("always@({0} ", ClockPositiveEdge ? "posedge" : "negedge");
            ClockSignal.GenerateCode(context);
            if(ResetSignal != null) {
                context.AppendFormat(" or {0} ", ResetPositiveEdge ? "posedge" : "negedge");
                ResetSignal.GenerateCode(context);
            }
            context.AppendLine(") begin");

            using (context.BeginIndent()) {
                GenerateBodyWithReset(context);
            }
            context.Append("end");
        }
    }
    public class VerilogConst : VerilogAstNode {
        public BigInteger Value { get; }
        public int BitWidth { get; }

        public override bool NoLineEnd => false;

        public VerilogConst(int bits, BigInteger value) {
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
    public class VerilogAlwaysComb : VerilogAstNode {
        public List<VerilogAstNode> SubNodes { get; } = new();
        public override bool NoLineEnd => true;
        
        public override void GenerateCode(VerilogGenerationContext context) {
            context.AppendLine("always@(*) begin");
            using (context.BeginIndent()) {
                foreach(var i in SubNodes) {
                    i.GenerateCode(context);
                    if(!i.NoAutoNewLine) context.AppendLine(i.NoLineEnd ? "" : ";");
                }
            }
            context.Append("end");
        }
    }
    public class VerilogOrExpression : VerilogBinaryOperator {
        public VerilogOrExpression(VerilogAstNode lhs, VerilogAstNode rhs) : base(lhs, rhs) {
        }

        public override string Operator => " | ";
    }
    public class VerilogGenerator : ICodeGenBackend<VerilogGenerationConfiguration> {
        protected VerilogAstNode ConvertExpressions(AbstractValue value, ComponentModel model, VerilogModule module) {
            if(value is IUntypedBinaryExpression binaryExpr) {
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
                var lhs = ConvertExpressions(unaryExpression.UntypedValue, model, module);

                if (value is IUntypedCastExpression) {
                    return lhs;
                }
                if (value is UIntNotExpression) return new VerilogNotOperator(lhs);
                if (value is IUntypedGeneralBitSelectionExpression selectionExpression) {
                    var baseExpression = ConvertExpressions(selectionExpression.UntypedValue, model, module);
                    var selection = new VerilogRangeSelection(baseExpression, selectionExpression.SelectedRange, (int)selectionExpression.UntypedValue.Type.WidthBits);

                    return selection;
                }

                throw new NotImplementedException();

            }
            if(value is IUntypedIoRightValueWrapper rightValue) {
                var ioComponent = rightValue.UntypedComponent;
                var iUntyped = (IUntypedConstructionPort)ioComponent;
                if(iUntyped.Component == model.ReferenceModule) {
                    var ioRef = module.IoPorts.Find(e => e.DeclIoComponent == ioComponent);
                    if (ioRef == null) throw new Exception($"IO port for {ioComponent.Name()} missing");

                    return new VerilogPureIdentifier(ioRef.Name);
                }

                
                if (model.OverlappedObjects.Where(e => (e.Value is SubComponentDesc componentDesc) && componentDesc.Contains(iUntyped.Component)).Count() != 0) {
                    var wireDecl = module.SubModuleOutputMap[ioComponent];
                    return wireDecl ;
                }
                throw new NotImplementedException();
                
                
            }
            if (value is INamedStageExpression namedStaged) {
                var desc = namedStaged.Descriptor;
                var index = ((List<INamedStageExpression>)desc).IndexOf(namedStaged);
                var identifier = new VerilogPureIdentifier(desc.InstanceName);
                var selection = new VerilogRangeSelection(identifier, new(index, index+1), desc.Count);


                return selection;
            }
            if(value is UIntLiteral literal) {
                return new VerilogConst((int)literal.Type.WidthBits, literal.Value);
            }
            if(value is RegisterValue registerValue) {
                var register = module.Registers.Find(e => e.RegisterInfo == registerValue.BaseRegister);
                Debug.Assert(register != null);

                return register.Identifier;
            }
            
            if(value is IWireRightValueWrapper wireWrapper) {
                var wire = module.ExplicitWires[wireWrapper.UntyedWire];
                Debug.Assert(wire != null);

                return wire.Identifier;
            }
            if(value is IRegRightValueWrapper regWrapper) {
                var register = module.Registers.Find(e => {
                    return e.RegisterInfo == regWrapper.UntyedReg;
                });

                Debug.Assert(register != null);

                return register.Identifier;
            }
            throw new NotImplementedException();
        }
        protected void AddClockDomain(VerilogModule moduleAst, ClockDomain domain) {

        }
        protected VerilogModule ConvertModuleAst(Module module) {
            var moduleAst = new VerilogModule(module);

            var componentModel = module.InternalModel;

            foreach(var clockDom in componentModel.UsedClockDomains) {

            }

            foreach(var portInfo in componentModel.IoPortShape) {
                var portName = portInfo.Name();
                var portType = portInfo.Direction switch { 
                    IoPortDirection.Input => VerilogIoType.Input,
                    IoPortDirection.Output => VerilogIoType.Output,
                    IoPortDirection.InOut => VerilogIoType.InOut,
                    _ => throw new NotImplementedException()
                };

                var promotedRegister = false;
                
                moduleAst.IoPorts.Add(new((IoComponent)portInfo,portName, portType, portInfo.UntypedType.WidthBits, promotedRegister));
            }

            foreach(var (wire, wireAux) in componentModel.WireLikeObjects) {
                if(wire is Wire wireObject) {
                    var wireDef = new VerilogWireDef(wireObject.Name(), wireObject.UntypedType?.WidthBits ?? 1, Array.Empty<int>());
                    moduleAst.ExplicitWires.Add(wireObject, wireDef);
                    moduleAst.Contents.Add(wireDef);
                }
            }

            var stageValueMap = new Dictionary<string, VerilogWireDef>();
            foreach (var (instName, instGroup) in componentModel.OverlappedObjects) {
                if (!(instGroup is SubValueStageDesc subStageDesc)) continue;

                var stageValueWire = new VerilogWireDef(instName, (subStageDesc.UntypedType.WidthBits), new int[] { subStageDesc.Count });
                moduleAst.Contents.Add(stageValueWire);

                stageValueMap.Add(instName, stageValueWire);

                

            }

            moduleAst.Contents.Add(new VerilogEmptyLine());

            foreach(var i in componentModel.Registers) {
                var registerDef = new VerilogRegisterDef(i, i.Name(), i.UntypedType.WidthBits, Array.Empty<int>());
                moduleAst.Registers.Add(registerDef);
                moduleAst.Contents.Add(registerDef);
            }

            foreach(var (instName, instGroup) in componentModel.OverlappedObjects) {
                if (!(instGroup is SubComponentDesc subComponentInstGroup)) continue;
                var subModel = subComponentInstGroup[0].InternalModel;

                foreach (var portInfo in subModel.IoPortShape) {
                    if(portInfo.Direction == IoPortDirection.Output) {

                        var portName = $"{instName}_{portInfo.Name()}";
                        var portType = portInfo.Direction switch {
                            IoPortDirection.Input => VerilogIoType.Input,
                            IoPortDirection.Output => VerilogIoType.Output,
                            IoPortDirection.InOut => VerilogIoType.InOut,
                            _ => throw new NotImplementedException()
                        };

                        var wireDef = new VerilogWireDef(portName, portInfo.UntypedType.WidthBits, new int[] { instGroup.Count});

                        moduleAst.Contents.Add(wireDef);

                        var path = portInfo.Location;
                        for (var j=0;j< subComponentInstGroup.Count; j++) {
                            var externalOutput = path.TraceValue(subComponentInstGroup[j]) ?? throw new NullReferenceException("Missing external output placeholder");
                            var selection = new VerilogRangeSelection(wireDef.Identifier, new(j,(j + 1)), instGroup.Count, false);
                            
                            moduleAst.SubModuleOutputMap.Add(externalOutput, selection);
                        }
                        

                        
                    }
                    
                }
            }

            moduleAst.Contents.Add(new VerilogEmptyLine());

            foreach (var (instName, instGroup) in componentModel.OverlappedObjects) {
                if (!(instGroup is SubValueStageDesc subStageDesc)) continue;

                var stageValueWire = stageValueMap[instName];
                for (var i = 0; i < subStageDesc.Count; i++) {
                    var selection = new VerilogRangeSelection(stageValueWire.Identifier, new(i, (i + 1)), subStageDesc.Count);
                    var value = ConvertExpressions(subStageDesc[i].InternalValue, componentModel, moduleAst);
                    moduleAst.Contents.Add(new VeriloAssignment(selection, value));
                }
            }

            foreach (var (wire,wireAux) in componentModel.WireLikeObjects) {
                var assignable = default(IAssignableValue);
                var identifier = default(VerilogAstNode);
                var totalBits = 0;

                if(wire is Wire wireObject) {
                    totalBits = (int)wireObject.UntypedType.WidthBits;
                    assignable = wireObject;
                    identifier = moduleAst.ExplicitWires[wireObject].Identifier;
                }
                if(wire is IUntypedConstructionPort port) {
                    if (port.Component != module) continue;
                    if(port is IAssignableValue) {
                        totalBits = (int)port.UntypedType.WidthBits;
                        assignable = (IAssignableValue)port;

                        var portNode = moduleAst.IoPorts.Find(e => e.DeclIoComponent == port) ?? throw new NullReferenceException($"Missing port ast node for {port.Name()}");
                        identifier = portNode.Identifier;

                    }
                }

                if(assignable != null && identifier!=null) {
                    if (componentModel.GenericAssignments.ContainsKey(assignable)) {
                        var outputAssignments = componentModel.GenericAssignments[assignable];
                        foreach (var j in outputAssignments) {
                            var selection = new VerilogRangeSelection(identifier, j.SelectedRange, totalBits);
                            var expr = ConvertExpressions(j.RightValue, componentModel, moduleAst);
                            moduleAst.Contents.Add(new VeriloAssignment(selection, expr));
                        }
                    }
                    

                }



            }

            moduleAst.Contents.Add(new VerilogEmptyLine());

            foreach (var (instName, instGroup) in componentModel.OverlappedObjects) {
                //if (!(instGroup is SubComponentDesc subComponentInstGroup)) continue;
                for(var j=0;j< instGroup.Count; j++) {
                    if (instGroup[j] is Module subModule) {
                        var subModel = subModule.InternalModel;
                        var modelInst = new ModuleInstDecl(subModule, subModule.Name());

                        foreach (var portInfo in subModel.IoPortShape) {
                            
                            var portName = portInfo.Name();
                            if (portInfo.Direction == IoPortDirection.Output) {
                                var path = portInfo.Location;
                                var externalOutput = path.TraceValue(subModule) ?? throw new NullReferenceException($"Missing port instance for port '{portInfo.Name()}'");

                                var exportWire = moduleAst.SubModuleOutputMap[externalOutput];


                                modelInst.PortConnections.Add((portName, exportWire));
                            }
                            if (portInfo.Direction == IoPortDirection.Input) {
                                if(portInfo is ClockDomainInput clockDomInput) {
                                    var connectedPort = moduleAst.IoPorts.Find(e => { 
                                        if(e.DeclIoComponent is ClockDomainInput parentDomInput) {
                                            if(parentDomInput.ClockDom == clockDomInput.ClockDom &&
                                                parentDomInput.SignalType == clockDomInput.SignalType) {
                                                return true;
                                            }
                                        }
                                        return false;
                                    })?.Identifier;
                                    if (connectedPort == null) {
                                        connectedPort = moduleAst.ExplicitWires.Where(e => { 
                                            if(e.Key is ClockDomainWire clkWire) {
                                                return clkWire.ClockDom == clockDomInput.ClockDom &&
                                                    clkWire.SignalType == clockDomInput.SignalType;
                                            }
                                            return false;
                                        } ).Select(e=>e.Value.Identifier).First();
                                    }

                                    if (connectedPort == null) {
                                        throw new Exception($"Missing clock signal {clockDomInput.SignalType} for clock domain {clockDomInput.ClockDom.Name}");
                                    } 
                                    modelInst.PortConnections.Add((portName, connectedPort));

                                    
                                } else {
                                    var assignments = componentModel.QueryAssignedSubComponentIoValues(portInfo, subModule);
                                    if (assignments.Count() == 0) continue;

                                    var subValues = assignments.OrderByDescending(e => e.range.Left)
                                            .Select(e => {
                                                return ConvertExpressions(e.assignValue, componentModel, moduleAst);
                                            }).ToArray();

                                    var portInputValue = new VerilogCombinationExpression(subValues);

                                    modelInst.PortConnections.Add((portName, portInputValue));
                                }
                            }

                        }

                        moduleAst.Contents.Add(modelInst);
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

                var clock = moduleAst.IoPorts.Find(e => (e.DeclIoComponent is ClockDomainInput{ SignalType:ClockDomainSignal.Clock} domainInput) && domainInput.ClockDom == i);
                if (clock == null) throw new NullReferenceException($"Unable to resolve clock signal for clock domain '{i.Name}'");


                var alwaysFF = new VerilogAlwaysFF(clock.Identifier) {
                    ClockPositiveEdge = i.ClockRiseEdge,
                    ResetPositiveEdge = i.ResetHighActive
                };
                if(!(i.RawReset is null)) {
                    var reset = moduleAst.IoPorts.Find(e => (e.DeclIoComponent is ClockDomainInput { SignalType: ClockDomainSignal.Reset } domainInput) && domainInput.ClockDom == i);

                    if (reset == null) throw new NullReferenceException($"Unable to resolve reset signal for clock domain '{i.Name}'");
                    alwaysFF.ResetSignal = reset.Identifier;
                }

                alwaysFF.SubNodes.AddRange(ExpandBehaviorBlock(componentModel.Behavior.TypedRoot.FalseBranch, componentModel, moduleAst, (e) => {
                    if (e is PrimaryAssignment assignment) {
                        return (assignment.LeftValue is ClockDrivenRegister realRegister) && (realRegister.ClockDom == i);
                    }
                    return true;
                }));
                alwaysFF.RegisterNames.AddRange(registers);

                moduleAst.Contents.Add(alwaysFF);
            }

            return moduleAst;
        }
        protected IEnumerable<VerilogAstNode> ExpandBehaviorBlock(IEnumerable<BehaviorDesc> block, ComponentModel compModel, VerilogModule moduleAst,Func<BehaviorDesc,bool> allowEmit) {
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

                    var noBlocking = false;

                    var value = ConvertExpressions(assignment.RightValue, compModel, moduleAst);
                    var leftValueSelection = new VerilogRangeSelection(leftValue, assignment.SelectedRange, (int)assignment.LeftValue.UntypedType.WidthBits);
                    var assignmentNode = new VerilogInAlwaysAssignment(leftValueSelection, value, noBlocking);
                    return (VerilogAstNode)assignmentNode;
                }
                if(e is SwitchDesc switchDesc) {
                    var bits = (int)switchDesc.SwitchValue.Type.WidthBits;
                    var condition = ConvertExpressions(switchDesc.SwitchValue, compModel, moduleAst);
                    var caseNode = new VerilogCase(condition, switchDesc.BranchList.Length);

                    for(var i  = 0; i < switchDesc.BranchList.Length; i++) {
                        caseNode.Constants[i] = i != switchDesc.BranchList.Length - 1 ? new VerilogConst(bits, switchDesc[i]) : new VerilogPureIdentifier("default");
                        caseNode.Branches[i] = ExpandBehaviorBlock(switchDesc.BranchList[i], compModel, moduleAst, allowEmit);
                    }

                    return (VerilogAstNode)caseNode;
                }
                throw new NotImplementedException();
            });

        }
        protected VerilogAstNode ResolveLeftValueRef(IAssignableValue leftValue, VerilogModule moduleAst) {
            if(leftValue is IUntypedConstructionPort port) {
                var portNode = moduleAst.IoPorts.Find(e => e.DeclIoComponent == port) ?? throw new Exception($"Unable to resolve port ast node for port '{port.Name()}'");

                return portNode.Identifier;

            }
            if(leftValue is RegisterDesc register) {
                var regNode = moduleAst.Registers.Find(e => e.RegisterInfo == register) ?? throw new Exception($"Unable to resolve register ast node for register '{register.Name()}'"); ;

                return regNode.Identifier;
            }

            throw new NotImplementedException();
        }
        public string GenerateModuleCode(Module module, VerilogGenerationConfiguration? configuration = null) {
            var context = new VerilogGenerationContext(configuration ?? new());
            var moduleAst = ConvertModuleAst(module);

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
