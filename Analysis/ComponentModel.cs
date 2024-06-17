using IntelliVerilog.Core.Components;
using IntelliVerilog.Core.DataTypes;
using IntelliVerilog.Core.Expressions;
using IntelliVerilog.Core.Expressions.Algebra;
using IntelliVerilog.Core.Utils;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace IntelliVerilog.Core.Analysis {
    public struct SpecifiedRange {
        public int Left;
        public int Right;
        public int BitWidth => Right - Left;
        public SpecifiedRange(int left, int right) {
            Left = left;
            Right = Math.Max(right, left);
        }
        public SpecifiedRange(Range range, int maxElements) {
            Left = range.Start.GetOffset(maxElements);
            Right = range.End.GetOffset(maxElements);
        }
        
        public bool InRange(int x) {
            return Left <= x && Right > x;
        }
        public bool IsIntersect(in SpecifiedRange range) {
            var secondStart = Math.Max(range.Left, Left);
            var firstRange = range.Left <= Left ? range : this;
            return firstRange.InRange(secondStart);
        }
        public SpecifiedRange Intersect(in SpecifiedRange range) {
            var start = Math.Max(range.Left, Left);
            var end = Math.Min(range.Right, Right);
            return new(start, end);
        }
        public SpecifiedRange Union(in SpecifiedRange range) {
            var start = Math.Min(range.Left, Left);
            var end = Math.Max(range.Right, Right);
            return new(start, end);
        }
        public SpecifiedRange Substract(in SpecifiedRange range) {
            var start = range.InRange(Left) ? range.Right : Left;
            var end = range.InRange(Right) ? range.Left : Right;
            return new(start, end);
        }
        public SpecifiedRange AlignLeft(int left) {
            return new(left, left + Right - Left);
        }
        public static SpecifiedRange operator +(in SpecifiedRange lhs, int offset) {
            return new(lhs.Left + offset, lhs.Right + offset);
        }
        public static SpecifiedRange operator -(in SpecifiedRange lhs, int offset) {
            return new(lhs.Left - offset, lhs.Right - offset);
        }
        public Range ToRange() => new Range(Left, Right);
    }
    public interface ICodeAnalysisContext {
        
    }
    public class AnalysisContext : ICodeAnalysisContext {
        protected Dictionary<Type, ComponentModelCache> m_ModuleCache = new();
        public Dictionary<Type, IoMemberInfo[]> BundleMemberCache { get; } = new();
        protected Stack<ComponentBase> m_CurrentBuildModel = new();
        public Dictionary<Type, IoMemberInfo[]> IoMemberCache { get; } = new();

        public ComponentBase? CurrentComponent => m_CurrentBuildModel.Count > 0 ? m_CurrentBuildModel.Peek() : null;
        public AnalysisContext() {

        }
        public ComponentModelCache GetModuleModelCache(Type componentType) {
            if (!m_ModuleCache.ContainsKey(componentType)) {
                m_ModuleCache.Add(componentType, new(componentType));
            }
            return m_ModuleCache[componentType];
        }

        public void OnEnterConstruction(ComponentBase module) {
            m_CurrentBuildModel.Push(module);
        }
        public void OnExitConstruction(ComponentBase module) {
            if(m_CurrentBuildModel.Peek() == module) {
                m_CurrentBuildModel.Pop();
            }
        }
    }
    public class ComponentModelCache {
        protected Type m_ComponentType;
        protected List<(object[] parameters, ComponentModel model)> m_ModelCache = new();
        public ComponentModelCache(Type componentType) {
            m_ComponentType = componentType;
        }
        public ComponentModel QueryModelCache(object[] parameters, ComponentBase component,out bool found) {
            var index = m_ModelCache.FindIndex(e => e.parameters.SequenceEqual(parameters));
            found = index >= 0;
            if (index < 0) {
                index = m_ModelCache.Count;
                m_ModelCache.Add((parameters, new ComponentBuildingModel(m_ComponentType, component, parameters)));
            }
            return m_ModelCache[index].model;
        }
    }
    public struct IoPortPath:IEquatable<IoPortPath> {
        public ImmutableArray<IoMemberInfo> Path { get; }
        public string Name => Member.Name;
        public IoMemberInfo Member { get; }
        public IUntypedLocatedPort? PortDef { get; }
        public IoPortPath(IUntypedLocatedPort port, IoMemberInfo member) {
            Member = member;
            PortDef = port;

            var levelCount = 0;
            for (var i = port.Parent; i != port.Component; i = i.Parent)
                levelCount++;
            var path = new List<IoMemberInfo>();
            for (var i = port.Parent; i != port.Component; i = i.Parent) {
                path.Add(i.PortMember!);
            }
            path.Reverse();
            Path = path.ToImmutableArray();
        }
        public IoPortPath(ImmutableArray<IoMemberInfo> path, IoMemberInfo member, IUntypedConstructionPort? portDef = null) {
            Path = path;
            Member = member;
            PortDef = portDef;
        }
        public IoComponent TraceValue(IUntypedPort root) {
            foreach(var i in Path) {
                root = i.GetValue(root);
            }
            return (IoComponent)Member.GetValue(root);
        }
        public void TraceSetValue(IUntypedPort root, IUntypedPort value) {
            foreach (var i in Path) {
                root = i.GetValue(root);
            }
            Member.SetValue(root,value);
        }
        public override string ToString() {
            var sb = new StringBuilder();
            sb.AppendJoin(".", Path.Select(e=>e.Name).Append(Name));
            return sb.ToString();
        }

        public bool Equals(IoPortPath other) {
            return Path.SequenceEqual(other.Path) && Name == other.Name;
        }
        public override int GetHashCode() {
            var hashBuilder = new HashCode();
            foreach (var i in Path) hashBuilder.Add(i);
            hashBuilder.Add(Name); ;
            return hashBuilder.ToHashCode();
        }
    }
    public class IntermediateValueDesc {
        public int Count { get; set; }
        public DataType? UntypedType { get; }
        public IntermediateValueDesc(DataType? type) {
            UntypedType = type;
        }
    }
    public abstract class ComponentModel {
        protected object[] m_Parameters;
        protected Type m_ComponentType;
        protected ComponentBase m_ComponentObject;
        protected HashSet<ComponentBase> m_SubComponents = new();
        public BehaviorContext? Behavior { get; set; } 
        
        public abstract IReadOnlyDictionary<IUntypedConstructionPort, IoPortInternalInfo> IoPortShape { get; } 
        public abstract IReadOnlyCollection<INamedStageExpression> IntermediateValues { get; }
        public abstract IReadOnlyDictionary<string, IntermediateValueDesc> IntermediateValueCount { get; }
        public ComponentBase ReferenceModule => m_ComponentObject;
        public abstract IReadOnlyDictionary<string, SubComponentDesc> SubComponents { get; }
        public ComponentModel(Type componentType, ComponentBase componentObject, object[] instParameters) {
            m_ComponentType = componentType;
            m_ComponentObject = componentObject;
            m_Parameters = instParameters;
        }
        public abstract IEnumerable<(AbstractValue assignValue, SpecifiedRange range)> QueryAssignedSubComponentIoValues(IUntypedConstructionPort declComponent, ComponentBase subModule);
    }

    public record IoPortInternalAssignment(IUntypedConstructionPort InternalPort, AbstractValue Value, SpecifiedRange SelectionRange) {
        public void Deconstruct(out AbstractValue value, out SpecifiedRange selectionRange) {
            value = Value;
            selectionRange = SelectionRange;
        }
    }
    public class IoPortInternalInfo : List<IoPortInternalAssignment> {
        public Bitset BitsAssigned { get; set; }
        public IUntypedConstructionPort IoComponentDecl { get; }
        public IoPortInternalInfo(IUntypedConstructionPort declPort) {
            IoComponentDecl = declPort;
            BitsAssigned = new((int)declPort.UntypedType.WidthBits);
        }
        public void AssignPort(AbstractValue value, SpecifiedRange range) {
            if (BitsAssigned[range] != BitRegionState.False) {
                throw new InvalidOperationException("Multi-drive detected");
            }
            BitsAssigned[range] = BitRegionState.True;
            Add(new(IoComponentDecl, value, range));
        }
    }
    public interface INamedStageExpression {
        AbstractValue InternalValue { get; }
        string BaseName { get; set; }
        int Index { get; set; }
    }
    
    public class NamedStageExpression<TData> : RightValue<TData> , INamedStageExpression where TData : DataType {
        public AbstractValue InternalValue { get; }
        public string BaseName { get; set; }
        public int Index { get; set; }
        public NamedStageExpression(AbstractValue internalValue, string baseName, int index) : base((TData)internalValue.Type, internalValue.Algebra) {
            InternalValue = internalValue;
            BaseName = baseName;
            Index = index;
        }

        public override bool Equals(AbstractValue? other) {
            throw new NotImplementedException();
        }
    }
    public class IoPortExternalInfo : IoPortInternalInfo {
        public ComponentBase ComponentObject { get; }
        public IoPortExternalInfo(ComponentBase component, IUntypedConstructionPort declPort) : base(declPort) {
            ComponentObject = component;
        }
    }
    public class SubComponentDesc : List<ComponentBase>{
        public string InstanceName { get; }
        public List<IoPortExternalInfo> ExternalWires { get; } = new();
        public SubComponentDesc(string instanceName) {
            InstanceName = instanceName;
        }
    }
    public class BranchLatchCheckList: List<PrimaryAssignment> {
        public void AddPrimaryAssignment(PrimaryAssignment assignment) {
            foreach(var e in this) {
                if(e.LeftValue == assignment.LeftValue && e.SelectedRange.IsIntersect(assignment.SelectedRange)) {
                    throw new NotImplementedException("Assignment overlapped");
                }
            }
            var prevCoalesce = Find(e => {
                return e.LeftValue == assignment.LeftValue &&
                    e.SelectedRange.Right == assignment.SelectedRange.Left;
            });
            var nextCoalesce = Find(e => {
                return e.LeftValue == assignment.LeftValue &&
                    e.SelectedRange.Left == assignment.SelectedRange.Right;
            });
            if(prevCoalesce != null) {
                assignment.SelectedRange = new(prevCoalesce.SelectedRange.Left, assignment.SelectedRange.Right);
            }
            if(nextCoalesce != null) {
                assignment.SelectedRange = new(assignment.SelectedRange.Left, nextCoalesce.SelectedRange.Right);
            }
            if (prevCoalesce != null) Remove(prevCoalesce);
            if (nextCoalesce != null) Remove(nextCoalesce);
            Add(assignment);
        }
    }
    public class RegisterDesc {
        public DataType UntypedType { get; }
        public string Name { get; } = "";
        public RegisterDesc(DataType type) {
            UntypedType = type;
        }
    }
    public interface IAssignableValue {

    }
    public class ComponentBuildingModel : ComponentModel {
        protected Dictionary<IUntypedConstructionPort, IoPortInternalInfo> m_IoPortShape = new();
        protected Dictionary<string, IntermediateValueDesc> m_ImmValueCounter = new();
        protected List<INamedStageExpression> m_StagedValues = new();
        protected Dictionary<string, SubComponentDesc> m_SubComponentObjects = new();
        protected List<RegisterDesc> m_Registers = new();
        public override IReadOnlyDictionary<IUntypedConstructionPort, IoPortInternalInfo> IoPortShape => m_IoPortShape;
        public override IReadOnlyDictionary<string, SubComponentDesc> SubComponents => m_SubComponentObjects;
        public override IReadOnlyCollection<INamedStageExpression> IntermediateValues => m_StagedValues;
        public override IReadOnlyDictionary<string, IntermediateValueDesc> IntermediateValueCount => m_ImmValueCounter;

        public ComponentBuildingModel(Type componentType, ComponentBase componentObject, object[] instParameters) : base(componentType, componentObject, instParameters) {
            
        }
        protected Dictionary<IUntypedPort, Bitset> CheckLatch(IEnumerable<BehaviorDesc> descriptors, Dictionary<IUntypedPort, Bitset> assignmentInfo) {
            var assignedPorts = new Dictionary<IUntypedPort, Bitset>();

            foreach (var i in descriptors) {
                if(i is BranchDesc branch) {

                    foreach(var j in CheckLatch(branch.TrueBranch, assignmentInfo)) {
                        if (!assignedPorts.ContainsKey(j.Key)) {
                            assignedPorts.Add(j.Key, j.Value);
                        }
                        assignedPorts[j.Key].InplaceAnd(j.Value);
                    }
                    foreach (var j in CheckLatch(branch.FalseBranch, assignmentInfo)) {
                        if (!assignedPorts.ContainsKey(j.Key)) {
                            assignedPorts.Add(j.Key, j.Value);
                        }
                        assignedPorts[j.Key].InplaceAnd(j.Value);
                    }
                }
            }
            foreach(var i in assignmentInfo) {
                if (!assignedPorts.ContainsKey(i.Key)) {
                    assignedPorts.Add(i.Key, new(i.Value.TotalBits));
                }
            }
            foreach (var i in descriptors) {
                if (i is IPrimaryAssignment assignment) {
                    if (assignedPorts.ContainsKey(assignment.LeftValue)) {
                        var bitset = assignedPorts[assignment.LeftValue];
                        bitset[assignment.SelectedRange] = BitRegionState.True;
                    }
                }
            }
            return assignedPorts;
        }

        public void ModelCheck() {
            var rootSet = new List<BehaviorDesc>();
            

            Behavior.Root.EnumerateDesc(e => { 
                if(e is IPrimaryAssignment primaryAssign) {
                    if (!m_IoPortShape.ContainsKey(primaryAssign.LeftValue)) return;
                    var portDesc = m_IoPortShape[primaryAssign.LeftValue];

                    var intersectedAssignments = portDesc.Where(e => e.SelectionRange.IsIntersect(primaryAssign.SelectedRange)).ToArray();
                    foreach(var i in intersectedAssignments) {
                        portDesc.Remove(i);
                        if (i.SelectionRange.Left > primaryAssign.SelectedRange.Left && i.SelectionRange.Right < primaryAssign.SelectedRange.Right) {
                            var assignment = new PrimaryAssignment(i.InternalPort, i.Value, i.SelectionRange, nint.Zero);
                            rootSet.Add(assignment);
                            continue;
                        }
                        if (i.SelectionRange.Left < primaryAssign.SelectedRange.Left && i.SelectionRange.Right > primaryAssign.SelectedRange.Right) {
                            // split required
                            var leftPartValue = i.Value.GetBitSelection(0..(primaryAssign.SelectedRange.Left - i.SelectionRange.Left));
                            var rightPartValue = i.Value.GetBitSelection((primaryAssign.SelectedRange.Right - i.SelectionRange.Left)..);
                            var middlePartValue = i.Value.GetBitSelection((primaryAssign.SelectedRange.Left - i.SelectionRange.Left)..(primaryAssign.SelectedRange.Right - i.SelectionRange.Left));
                            var leftPart = new IoPortInternalAssignment(i.InternalPort, leftPartValue, new(i.SelectionRange.Left, primaryAssign.SelectedRange.Left));
                            var rightPart = new IoPortInternalAssignment(i.InternalPort, rightPartValue, new(primaryAssign.SelectedRange.Right, i.SelectionRange.Right));

                            portDesc.Add(leftPart);
                            portDesc.Add(rightPart);

                            var assignment = new PrimaryAssignment(i.InternalPort, middlePartValue, primaryAssign.SelectedRange, nint.Zero);
                            rootSet.Add(assignment);

                            continue;
                        }
                        if(i.SelectionRange.Left == primaryAssign.SelectedRange.Left && i.SelectionRange.Right == primaryAssign.SelectedRange.Right) {
                            var assignment = new PrimaryAssignment(i.InternalPort, i.Value, new(i.SelectionRange.Left, primaryAssign.SelectedRange.Right), nint.Zero);
                            rootSet.Add(assignment);

                            continue;
                        }
                        if(i.SelectionRange.Left >= primaryAssign.SelectedRange.Left) {
                            var rightPartValue = i.Value.GetBitSelection((i.SelectionRange.Right - primaryAssign.SelectedRange.Left)..);
                            var rightPart = new IoPortInternalAssignment(i.InternalPort, rightPartValue, new(primaryAssign.SelectedRange.Right, i.SelectionRange.Right));
                            portDesc.Add(rightPart);

                            var middlePartValue = i.Value.GetBitSelection(0..(i.SelectionRange.Left - primaryAssign.SelectedRange.Left));
                            var assignment = new PrimaryAssignment(i.InternalPort, middlePartValue, new(i.SelectionRange.Left, primaryAssign.SelectedRange.Right), nint.Zero);
                            rootSet.Add(assignment);

                            continue;
                        }
                        if (i.SelectionRange.Right <= primaryAssign.SelectedRange.Right) {
                            var leftPartValue = i.Value.GetBitSelection(0..(primaryAssign.SelectedRange.Left - i.SelectionRange.Left));
                            var leftPart = new IoPortInternalAssignment(i.InternalPort, leftPartValue, new(i.SelectionRange.Left, primaryAssign.SelectedRange.Left));
                            portDesc.Add(leftPart);

                            var middlePartValue = i.Value.GetBitSelection((primaryAssign.SelectedRange.Left - i.SelectionRange.Left)..);
                            var assignment = new PrimaryAssignment(i.InternalPort, middlePartValue, new(i.SelectionRange.Left, primaryAssign.SelectedRange.Right), nint.Zero);
                            rootSet.Add(assignment);

                            continue;
                        }

                    }
                }
            });
            Behavior.Root.FalseBranch.InsertRange(0, rootSet);

            var combAlwaysPortList = new Dictionary<IUntypedPort, Bitset>();

            Behavior.Root.EnumerateDesc(e => { 
                if(e is PrimaryAssignment assignment) {
                    if (!combAlwaysPortList.ContainsKey(assignment.LeftValue)) {
                        var totalBits = ((IUntypedConstructionPort)assignment.LeftValue).UntypedType.WidthBits;
                        combAlwaysPortList.Add(assignment.LeftValue, new((int)totalBits));
                    }
                    combAlwaysPortList[assignment.LeftValue][assignment.SelectedRange] = BitRegionState.True;
                }
            });

            var checkResult = CheckLatch(Behavior.Root.FalseBranch, combAlwaysPortList);
            foreach(var (k,v) in combAlwaysPortList) {
                var fullyAssigned = checkResult[k];
                if (!v.Equals(fullyAssigned)) {
                    throw new Exception("Latch detected");
                }
            }
        }
        
        
        public void RegisterIoPorts(IUntypedPort port) {
            InternalRegisterIoPorts(port);
        }
        protected void InternalRegisterIoPorts(IUntypedPort port) {
            if(port is IoBundle bundle) {
                var bundleType = bundle.GetType();
                var ioAux = IoComponentProbableHelpers.QueryProbeAuxiliary(bundleType);
                foreach(var i in ioAux.GetIoMembers(bundleType)) {
                    var subPort = i.GetValue(port);

                    InternalRegisterIoPorts(subPort);
                }

                return;
            }
            if(port is IoComponent) {
                if(port is IUntypedConstructionPort constructedInternalPort) {
                    m_IoPortShape.Add(constructedInternalPort, new(constructedInternalPort));
                } else {
                    throw new NotSupportedException();
                }
                
                return;
            }
            throw new NotSupportedException();
        }
        protected void AssignOutputExpression(IoComponent internalLeftValue, AbstractValue rightValue, SpecifiedRange range){
            if(internalLeftValue is IUntypedConstructionPort internalOutput) {
                var descriptor = m_IoPortShape[internalOutput];

                descriptor.AssignPort(rightValue, range);
            }
        }
 
        public AbstractValue AssignLocalSignalVariable(Type dataType, string name, AbstractValue rightValue) {
            if (!m_ImmValueCounter.ContainsKey(name)) m_ImmValueCounter.Add(name, new(rightValue.Type));

            var namedType = typeof(NamedStageExpression<>).MakeGenericType(dataType);

            var stagedValue = (INamedStageExpression)namedType.GetConstructor(new Type[] {typeof(AbstractValue),typeof(string),typeof(int) })
                .Invoke(new object[] { rightValue, name, m_ImmValueCounter[name].Count++ });

            m_StagedValues.Add(stagedValue);

            return (AbstractValue)stagedValue;
        }
        public void AssignLocalSubComponent(string name, ComponentBase subComponent) {
            if (!m_SubComponentObjects.ContainsKey(name)) {
                m_SubComponentObjects.Add(name, new(name));
            }
            var newGroup = m_SubComponentObjects[name];
            newGroup.Add(subComponent);

            if (m_SubComponentObjects.ContainsKey(subComponent.CatagoryName)) {
                var oldGroup = m_SubComponentObjects[subComponent.CatagoryName];
                oldGroup.Remove(subComponent);

                newGroup.ExternalWires.AddRange(oldGroup.ExternalWires.Where(e => e.ComponentObject == subComponent));
                oldGroup.ExternalWires.RemoveAll(e => e.ComponentObject == subComponent);

                if (oldGroup.Count == 0) m_SubComponentObjects.Remove(oldGroup.InstanceName);
            }

            subComponent.CatagoryName = name;
        }
        protected SubComponentDesc GetSubComponentDesc(ComponentBase subComponent) {
            if (!m_SubComponentObjects.ContainsKey(subComponent.CatagoryName)) {
                m_SubComponentObjects.Add(subComponent.CatagoryName, new(subComponent.CatagoryName));
            }
            return m_SubComponentObjects[subComponent.CatagoryName];
        }


        public void AssignSubModuleConnections(IUntypedPort leftValue, IUntypedPort rightValue, Range range, nint returnAddress) {
            if(leftValue is IoBundle bundle) {
                var ioAux = IoComponentProbableHelpers.QueryProbeAuxiliary(leftValue.GetType());
                foreach (var i in ioAux.GetIoMembers(leftValue.GetType())) {
                    var newSlotValue = i.GetValue(rightValue);
                    if (newSlotValue == null) continue;

                    var oldSlotValue = i.GetValue(leftValue);

                    AssignSubModuleConnections(oldSlotValue, newSlotValue, range, returnAddress);
                }

                return;
            }
            if (leftValue is IoComponent ioComponentLhs) {
                switch (ioComponentLhs.Direction) {
                    case IoPortDirection.Input: {
                        if (ioComponentLhs.Flags.HasFlag(GeneralizedPortFlags.ExternalPort)) { // ExternalInput

                            var leftExternalInput = (IUntypedConstructionPort)ioComponentLhs;

                            if (rightValue is IExpressionAssignedIoType expression) {
                                var subComponent = leftExternalInput.Component;
                                var componentDesc = GetSubComponentDesc(subComponent);
                                var wireSet = componentDesc.ExternalWires.Find(e => e.ComponentObject == subComponent && e.IoComponentDecl == leftExternalInput.InternalPort);
                                if(wireSet == null) {
                                    componentDesc.ExternalWires.Add(wireSet = new(subComponent, leftExternalInput.InternalPort));
                                }
                                var bits = (int)wireSet.IoComponentDecl.UntypedType.WidthBits;
                                if (Behavior.IsInBranchContext) {
                                    Behavior.NotifyComponentAssignment(returnAddress, wireSet, expression.UntypedExpression, new(range, bits));
                                } else {
                                    
                                    wireSet.AssignPort(expression.UntypedExpression, new(range, bits));
                                }
                            }
                            break;
                        }
                        if(ioComponentLhs.Flags.HasFlag(GeneralizedPortFlags.InternalPort)) { // InternalInput
                            var leftInternalInput = (IUntypedConstructionPort)ioComponentLhs;
                            throw new InvalidOperationException($"Assignment on internal io port {leftInternalInput.Location}");
                        }
                        
                        throw new NotImplementedException();
                    }
                    case IoPortDirection.Output: {
                        if (ioComponentLhs.Flags.HasFlag(GeneralizedPortFlags.ExternalPort)) { // ExternalOutput
                            // inverted case
                            if (rightValue is IExpressionAssignedIoType expression) {
                                if(expression.UntypedExpression is IInvertedOutput invertedOutput) {
                                    var bits = (int)invertedOutput.InternalOut.UntypedRValue.Type.WidthBits;
                                    if (Behavior.IsInBranchContext) {
                                        Behavior!.NotifyAssignment(returnAddress, invertedOutput.InternalOut, ioComponentLhs.UntypedRValue, new(invertedOutput.SelectedRange, bits));
                                    } else {
                                        
                                        AssignOutputExpression(invertedOutput.InternalOut, ioComponentLhs.UntypedRValue, new (invertedOutput.SelectedRange, bits));
                                    }
                                    break;
                                }
                            }

                            throw new NotImplementedException();
                        }
                        if (ioComponentLhs.Flags.HasFlag(GeneralizedPortFlags.InternalPort)) { // InternalOutput
                            // assign outputs
                            if(rightValue is IExpressionAssignedIoType expression) {
                                var bits = (int)((IDataTypeSpecifiedPort)ioComponentLhs).UntypedType.WidthBits;
                                if (Behavior.IsInBranchContext) {
                                    Behavior!.NotifyAssignment(returnAddress, ioComponentLhs, expression.UntypedExpression, new(range, bits));
                                } else {
                                    
                                    AssignOutputExpression(ioComponentLhs, expression.UntypedExpression, new(range, bits));
                                }
                            }
                            break;
                        }

                        throw new NotImplementedException();
                    }
                }

            }
        }

        public override IEnumerable<(AbstractValue assignValue, SpecifiedRange range)> QueryAssignedSubComponentIoValues(IUntypedConstructionPort declComponent, ComponentBase subModule) {
            var componentDesc = GetSubComponentDesc(subModule);
            var wireSet = componentDesc.ExternalWires.Find(e => e.ComponentObject == subModule && e.IoComponentDecl == declComponent);
            return wireSet.Select((e) => (e.Value, e.SelectionRange));
        }
    }

}
