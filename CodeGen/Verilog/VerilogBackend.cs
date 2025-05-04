using IntelliVerilog.Core.Analysis;
using IntelliVerilog.Core.Analysis.TensorLike;
using IntelliVerilog.Core.Components;
using IntelliVerilog.Core.Expressions;
using IntelliVerilog.Core.Expressions.Algebra;
using IntelliVerilog.Core.Logging;
using IntelliVerilog.Core.Utils;
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
    public class VerilogModuleIo {
        public List<VerilogPort> Ports { get; } = new();
        public VerilogModule AstModule { get; }
        public VerilogModuleIo(VerilogModule astModule, Module module) {
            AstModule = astModule;

            var interalModel = module.InternalModel;
            foreach(var i in interalModel.IoPortShape) {

                var assignment = default(AssignmentInfo);
                if(i is IAssignableValue assignable)
                    interalModel.GenericAssignments.TryGetValue(assignable, out assignment);

                Ports.Add(new VerilogPort(astModule, (IoComponent)i, assignment));
            }
        }
        public void GenerateDecl(VerilogGenerationContext context) {
            using(context.BeginIndent()) {
                for(var i = 0; i < Ports.Count; i++) {
                    Ports[i].GenerateDecl(context);
                    if(i != Ports.Count - 1) {
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
    public interface IExpressionVerilogElement: IShapedVerilogElement {

    }
    public class VerilogGenericTensorOperation : IExpressionVerilogElement {
        public VerilogModule Module { get; }
        public TensorExpr DstExpression { get; }
        public ImmutableArray<int> NewShape { get; }
        public string NewIdentifier { get; }
        public ImmutableArray<int> Shape => NewShape;
        public VerilogGenericTensorOperation(VerilogModule module, TensorExpr dstExpression) {
            Module = module;
            NewShape = dstExpression.Shape;
            DstExpression = dstExpression;
            NewIdentifier = Module.ModuleLevelIDGenerator.GetID("tensor");

            module.MarkDeclRequired(this);
            module.MarkBlockRequired(this);
        }
        public bool NoLineEnd => false;

        public void GenerateBlock(VerilogGenerationContext context) {
            var tensorDestIndices = NewShape.Select((e, idx) => new TensorIndexVarExpr<string>(0, e - 1, $"idx{idx}")).ToArray();
            var transformParts = DstExpression.ExpandAllIndices(tensorDestIndices);

            context.AppendLine("generate");

            using(context.BeginIndent()) {
                context.Append("genvar ");
                for(var i =0;i< NewShape.Length; i++) {
                    context.AppendFormat("idx{0}{1}", i, i == NewShape.Length - 1 ? ";" : ", ");
                }
                context.AppendLine();

                foreach(var i in transformParts) {
                    GenerateNestedFor(context, i);
                }
            }

            context.Append("endgenerate");
        }

        protected void GenerateNestedFor(VerilogGenerationContext context, in TransformIndexParts part ,int rank = 0) {
            if(rank < NewShape.Length) {
                var (lower, upper) = part.IndexRanges[rank];
                context.AppendFormat("for(idx{0} = {1}; idx{0} < {2}; idx{0} = idx{0} + 1) begin", rank, lower, upper);
                context.AppendLine();
                using(context.BeginIndent()) {
                    GenerateNestedFor(context, part, rank + 1);
                }
                context.AppendLine("end");
            } else {
                GenerateAssignmentBody(context, part);
            }
        }

        protected void GenerateAssignmentBody(VerilogGenerationContext context, in TransformIndexParts part) {
            context.AppendFormat("assign {0}{1} = ", 
                NewIdentifier, 
                NewShape.Select((e, idx) => $"[idx{idx}]").Aggregate((u, v) => u + v));

            var baseElement = Module.ConvertExpression(part.BaseExpr);

            baseElement.GenerateCode(context);

            foreach(var i in part.Indices) {
                context.Append($"[{i.ToString()}]");
            }
            context.AppendLine(";");
        }

        public void GenerateCode(VerilogGenerationContext context) {
            context.Append(NewIdentifier);
        }

        public void GenerateDecl(VerilogGenerationContext context) {
            VerilogSyntaxHelpers.GenerateWireDef(context, "wire", NewShape.AsSpan(), NewIdentifier);
        }
    }
    public class VerilogAssignment : IVerilogElement {
        public bool NoLineEnd => false;
        public IShapedVerilogElement LeftIdentifier { get; }
        public IExpressionVerilogElement RightExpression { get; }
        public ImmutableArray<GenericIndex> LeftSelectors { get; }
        public VerilogAssignment(VerilogModule astModule,IShapedVerilogElement lhs, IExpressionVerilogElement rhs, ImmutableArray<GenericIndex> lhsSelection) {
            LeftIdentifier = lhs;
            RightExpression = rhs;
            LeftSelectors = lhsSelection;

            astModule.MarkBlockRequired(this);
        }
        public void GenerateBlock(VerilogGenerationContext context) {
            var lhsProxyTensor = new TensorVarExpr<object>(null, LeftIdentifier.Shape.AsSpan());
            var slices = LeftSelectors.Select(e => e.ToErased(true)).ToArray();

            var lhsSelectedTensor = TensorExpr.View(lhsProxyTensor, slices);
            if(!lhsSelectedTensor.Shape.SequenceEqual(RightExpression.Shape)) {
                throw new InvalidOperationException("Shape of both side of assignment mismatch");
            }

            var loopCopyNumElements = TensorIndexMathHelper.Product(RightExpression.Shape.AsSpan()[..^1]);
            var lastDimElements = RightExpression.Shape[^1];

            if(loopCopyNumElements <= context.Configuration.AssignmentUnrollLimit) {
                var flattenLhsSelectedTensor = TensorExpr.Reshape(lhsSelectedTensor, [loopCopyNumElements, lastDimElements]);
                var totalElements = flattenLhsSelectedTensor.Shape[0];

                var accessorExpr = new TensorIndexVarExpr(0, totalElements - 1);
                var elementExpr = new TensorIndexVarExpr(0, lastDimElements - 1);
                var transform = flattenLhsSelectedTensor.ExpandAllIndices(new ReadOnlySpan<TensorIndexExpr>([
                    accessorExpr,
                    elementExpr
                    ]))[0];

                var strides = TensorIndexMathHelper.CumulativeProductFull(RightExpression.Shape.AsSpan()[..^1]).ToArray();

                for(var i = 0; i < loopCopyNumElements; i++) {
                    accessorExpr.Value = i;

                    elementExpr.Value = elementExpr.MaxValue;
                    var lhsHighLastIndex = transform.Indices[^1].Evaluate();

                    elementExpr.Value = elementExpr.MinValue;
                    var lhsIndices = transform.Indices.Select((e,idx) => {
                        if(idx == transform.Indices.Length - 1) return $"{lhsHighLastIndex}:{e.Evaluate()}";
                        return e.Evaluate().ToString();
                    }).ToArray();

                    var rhsIndices = Enumerable.Range(0, strides.Length - 1).Select(e => ((i % strides[e]) / strides[e + 1]).ToString()).ToArray();
                    GenerateAssignmentStatement(context, lhsIndices, rhsIndices);
                }
            } else {
                throw new NotImplementedException();
            }
        }

        protected void GenerateAssignmentStatement(VerilogGenerationContext context, IEnumerable<string> lhsIndices, IEnumerable<string> rhsIndices) {
            context.Append("assign ");
            LeftIdentifier.GenerateCode(context);
            context.Append(lhsIndices.Select(e => $"[{e}]").DefaultIfEmpty("").Aggregate((u, v) => u + v));
            context.Append(" = ");
            RightExpression.GenerateCode(context);
            context.Append(rhsIndices.Select(e => $"[{e}]").DefaultIfEmpty("").Aggregate((u, v) => u + v));
        }

        public void GenerateCode(VerilogGenerationContext context) {
            throw new NotImplementedException();
        }

        public void GenerateDecl(VerilogGenerationContext context) {
            throw new NotImplementedException();
        }
    }
    public class VerilogWire : IExpressionVerilogElement {
        public string WireName { get; }
        public ImmutableArray<int> Shape { get; }
        public AssignmentInfo? Assignments { get; }
        public VerilogWire(VerilogModule astModule, Wire baseWire) {
            WireName = baseWire.Name();
            Shape = baseWire.Shape.ToImmutableIntShape().ToImmutableArray();

            var internalModel = astModule.BackModule.InternalModel;
            if(internalModel.GenericAssignments.TryGetValue(baseWire, out var assignments)) {
                Assignments = assignments;

                foreach(var i in Assignments) {
                    var rhs = astModule.ConvertExpression(i.RightValue);
                    var vAssignment = new VerilogAssignment(astModule, this, rhs, i.SelectedRange);

                    astModule.MarkBlockRequired(vAssignment);
                }
            } 
        }
        public bool NoLineEnd => false;

        public void GenerateBlock(VerilogGenerationContext context) {
        }

        public void GenerateCode(VerilogGenerationContext context) {
            context.Append(WireName);
        }

        public void GenerateDecl(VerilogGenerationContext context) {
            VerilogSyntaxHelpers.GenerateWireDef(context, "wire", Shape.AsSpan(), WireName);
        }
    }
    public class VerilogModule: IVerilogElement {
        protected VerilogModuleIo m_ModuleIo;
        public Module BackModule { get; }
        public string ModuleName { get; }
        
        public bool NoLineEnd => throw new NotImplementedException();

        protected HashSet<IVerilogElement> m_DeclRequiredElements = new();
        protected HashSet<IVerilogElement> m_BlockRequiredElements = new();
        protected Dictionary<IWireLike, IVerilogElement> m_ComplexElements = new();
        public IEnumerable<IVerilogElement> DeclRequiredElements 
            => m_DeclRequiredElements;
        public IEnumerable<IVerilogElement> BlockRequiredElements 
            => m_BlockRequiredElements;

        private IncrementalIdentifierGenerator m_IDGenerator;
        public ref IncrementalIdentifierGenerator ModuleLevelIDGenerator => ref m_IDGenerator;
        public VerilogModule(Module backModule) {
            BackModule = backModule;
            ModuleName = backModule.InternalModel.ModelName;

            m_ModuleIo = new(this, backModule);

            CollectModuleDetails();
        }
        protected void CollectModuleDetails() {
            var interalModel = BackModule.InternalModel;
            foreach(var i in m_ModuleIo.Ports) {
                var assignment = i.Assignments;

                if(assignment is not null) {
                    foreach(var j in assignment) {
                        var rhs = ConvertExpression(j.RightValue);

                        var astAssignment = new VerilogAssignment(this, i, rhs, j.SelectedRange);
                    }
                }
            }
            foreach(var i in interalModel.WireLikeObjects) {
                if(i.Key is Wire wire) {
                    var astWire = MakeWireLikeComplexElement(wire, () => new VerilogWire(this, wire));

                    MarkDeclRequired(astWire);
                    MarkBlockRequired(astWire);
                }
                if(i.Key is IoComponent ioPort) {
                    
                }
            }
        }
        public void MarkDeclRequired(IVerilogElement element) => m_DeclRequiredElements.Add(element);
        public void MarkBlockRequired(IVerilogElement element) => m_BlockRequiredElements.Add(element);
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

            using(context.BeginIndent()) {
                foreach(var i in m_DeclRequiredElements) {
                    i.GenerateDecl(context);
                    if(!i.NoLineEnd) context.AppendLine(";");
                }

                foreach(var i in m_BlockRequiredElements) {
                    i.GenerateBlock(context);
                    if(!i.NoLineEnd) context.AppendLine(";");
                }
            }

            context.Append("endmodule;");
        }

        public T MakeWireLikeComplexElement<T>(IWireLike wireLike, Func<T> constructor) where T:IVerilogElement{
            if(!m_ComplexElements.TryGetValue(wireLike, out var element)) {
                m_ComplexElements.Add(wireLike, element = constructor());
            }
            return (T)element;
        }
        public IExpressionVerilogElement ConvertExpression(TensorExpr value) {
            if(value is ITensorVarExpr expr) {
                var baseElement = expr.UntypedData;
                if(baseElement is Wire untypedWire)
                    return MakeWireLikeComplexElement(untypedWire, () => new VerilogWire(this, untypedWire));
            }
            throw new NotImplementedException();
        }
        public IExpressionVerilogElement ConvertExpression(AbstractValue value) {
            if(value is IWireRightValueWrapper wireRightValueWrapper) {
                return MakeWireLikeComplexElement(wireRightValueWrapper.UntyedWire, ()=> new VerilogWire(this, wireRightValueWrapper.UntyedWire));
            }
            if(value is IUntypedIoRightValueWrapper ioRightValueWrapper) {
                var astPort = m_ModuleIo.Ports.Find(e => ReferenceEquals(e.InternalPort, ioRightValueWrapper.UntypedComponent))!;
                return astPort;
            }
            if(value is IExpressionRightValueWrapper expression) {
                var tee = expression.TensorExpression.Value;

                return new VerilogGenericTensorOperation(this, tee);
            }
            if(value is IUntypedBinaryExpression binaryExpr) {
                var lhs = ConvertExpression(binaryExpr.UntypedLeft);
                var rhs = ConvertExpression(binaryExpr.UntypedRight);
                var tensor = binaryExpr.TensorExpression.Value;
                return VerilogOperatorMap.OpMap[value.GetType()](lhs, rhs, tensor);
            }
            if(value is INamedStageExpression namedExpr) {
                return ConvertExpression(namedExpr.InternalValue);
            }
            throw new NotImplementedException();
        }
    }
    public static class VerilogOperatorMap {
        public static Dictionary<Type, Func<IExpressionVerilogElement, IExpressionVerilogElement, TensorExpr, IExpressionVerilogElement>> OpMap { get; } = new() {
            {typeof(BoolXorExpression), (lhs, rhs, tensor) => new VerilogBinaryOperator("^", lhs, rhs, tensor) },
            {typeof(BoolAndExpression), (lhs, rhs, tensor) => new VerilogBinaryOperator("&", lhs, rhs, tensor) },
            {typeof(BoolOrExpression), (lhs, rhs, tensor) => new VerilogBinaryOperator("|", lhs, rhs, tensor) },
            {typeof(UIntAddExpression), (lhs, rhs, tensor) => new VerilogBinaryOperator("+", lhs, rhs, tensor) },
            {typeof(UIntSubExpression), (lhs, rhs, tensor) => new VerilogBinaryOperator("-", lhs, rhs, tensor) }
        };
    }
    public class VerilogBinaryOperator : IExpressionVerilogElement {
        public ImmutableArray<int> Shape => TensorExpression.Shape;
        public IExpressionVerilogElement LeftExpression { get; }
        public IExpressionVerilogElement RightExpression { get; }
        public string Operator { get; }
        public TensorExpr TensorExpression { get; }
        public bool NoLineEnd => true;
        public VerilogBinaryOperator(string op, IExpressionVerilogElement lhs, IExpressionVerilogElement rhs, TensorExpr tensorExpr) {
            Operator = op;
            LeftExpression = lhs;
            RightExpression = rhs;
            TensorExpression = tensorExpr;
        }

        public void GenerateBlock(VerilogGenerationContext context) {
            throw new NotImplementedException();
        }

        public void GenerateCode(VerilogGenerationContext context) {
            context.Append("(");
            LeftExpression.GenerateCode(context);
            context.Append($" {Operator} ");
            RightExpression.GenerateCode(context);
            context.Append(")");
        }

        public void GenerateDecl(VerilogGenerationContext context) {
            throw new NotImplementedException();
        }
    }
}
