using IntelliVerilog.Core.Components;
using IntelliVerilog.Core.DataTypes;
using IntelliVerilog.Core.Expressions;
using IntelliVerilog.Core.Expressions.Algebra;
using IntelliVerilog.Core.Utils;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
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
        
        public abstract IDictionary<IWireLike, WireLikeAuxInfo> WireLikeObjects { get; }
        public abstract IReadOnlyDictionary<IAssignableValue, AssignmentInfo> GenericAssignments { get; }
        public abstract IEnumerable<IUntypedConstructionPort> IoPortShape { get; } 
        public abstract IReadOnlyCollection<INamedStageExpression> IntermediateValues { get; }
        public abstract IReadOnlyDictionary<string, IntermediateValueDesc> IntermediateValueCount { get; }
        public abstract IReadOnlySet<RegisterDesc> Registers { get; }
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
        public bool PromotedRegister { get; set; } = false;
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

        public override void EnumerateSubNodes(Action<AbstractValue> callback) {
            callback(InternalValue);
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
    public class RegisterDesc:IAssignableValue {
        protected RegisterValue? m_RightValueCache;
        public DataType UntypedType { get; }
        public bool IsCombination { get; }
        public RegisterValue RVaule {
            get {
                if(m_RightValueCache == null) {
                    m_RightValueCache = new(this);
                }
                return m_RightValueCache;
            }
        }
        public Func<string> Name { get; set; } = () => "<unnamed>";
        public RegisterDesc(DataType type, bool isComb = false) {
            UntypedType = type;
            IsCombination = isComb;
        }
    }
    public class CombPseudoRegister : RegisterDesc {
        public IAssignableValue BackAssignable { get; }
        public CombPseudoRegister(IAssignableValue assignable) : base(assignable.UntypedType, true) {
            BackAssignable = assignable;
        }
    }
    public class RegisterValue : AbstractValue {
        public RegisterValue(RegisterDesc baseRegister) : base(baseRegister.UntypedType) {
            BaseRegister = baseRegister;
        }

        public RegisterDesc BaseRegister { get; }

        public override void EnumerateSubNodes(Action<AbstractValue> callback) { }

        public override bool Equals(AbstractValue? other) {
            throw new NotImplementedException();
        }

    }
    public interface ILazyNamedObject {
        Func<string> Name { get; set; }
    }
    public interface IAssignableValue: ILazyNamedObject {
        DataType UntypedType { get; }
        

    }
    public class LeftValueCombination : IAssignableValue {
        public IAssignableValue[] SubValues { get; }
        public DataType UntypedType { get; }
        public Func<string> Name { get; set; } = () => "<unnamed>";

        public LeftValueCombination(IAssignableValue[] subValues) {
            var type = subValues.First().UntypedType;
            if(subValues.Count(e=>e.UntypedType != type) > 0) {
                throw new InvalidOperationException("Type mismatch at combination");
            }

            var totalBits = subValues.Sum(e => e.UntypedType.WidthBits);

            UntypedType = type.CreateWithWidth((uint)totalBits);
            SubValues = subValues;
        }
    }
    public class AssignableLeftValueInfo {
        public IAssignableValue LeftValue { get; }
        public AssignableLeftValueInfo() {

        }
    }
    public interface IBasicAssignmentTerm {
        IAssignableValue UntypedLeftValue { get; }
        AbstractValue RightValue { get; set; }
        SpecifiedRange SelectedRange { get; }
        BehaviorDesc ToBeahviorDesc(IAssignableValue? redirectedLeftValue);
    }
    public class AssignmentInfo : List<IBasicAssignmentTerm> {
        public IAssignableValue UntypedLeftValue { get; }
        public RegisterDesc? PromotedRegister { get; set; }
        public AssignmentInfo(IAssignableValue leftValue) {
            UntypedLeftValue = leftValue;
        }
    }
    public class IoPortAssignmentInfo : AssignmentInfo {
        public IUntypedConstructionPort PortLeftValue => (IUntypedConstructionPort)UntypedLeftValue;
        public IoPortAssignmentInfo(IAssignableValue leftValue) : base(leftValue) {
        }
        public virtual void AssignPort(AbstractValue value, SpecifiedRange range) {
            Add(new PrimaryAssignment(UntypedLeftValue, value, range, nint.Zero));
        }
    }
    public class WireAssignmentInfo : AssignmentInfo {
        public Wire WireLeftValue => (Wire)UntypedLeftValue;
        public WireAssignmentInfo(IAssignableValue leftValue) : base(leftValue) {
        }
        public virtual void AssignPort(AbstractValue value, SpecifiedRange range) {
            Add(new PrimaryAssignment(UntypedLeftValue, value, range, nint.Zero));
        }
    }

    public class SubComponentPortAssignmentInfo : IoPortAssignmentInfo {
        public ComponentBase SubComponent { get; }
        public SubComponentPortAssignmentInfo(ComponentBase subComponent, IAssignableValue leftValue) : base(leftValue) {
            SubComponent = subComponent;
        }
    }
    public class HeapPointer {
        protected IReferenceTraceObject m_Reference;
        public ref IReferenceTraceObject Pointer => ref m_Reference;
        public HeapPointer(IReferenceTraceObject objectRef) {
            m_Reference = objectRef;
        }
    }
    public interface IReferenceTraceObject {

    }
    public interface IWireLike {
        Func<string> Name { get; set; }
    }
    public record CombLogicDependency(IWireLike DependencyDestination, CombLogicDependency? Parent) {
        
    }
    public class WireLikeAuxInfo {
        public IWireLike Wire { get; }
        public HashSet<CombLogicDependency> CombLogicDependencyMap { get; } = new();
        public HashSet<WireLikeAuxInfo> CombLogicInvDependencyMap { get; } = new();
        public int UnresolvedWires { get; set; }
        public WireLikeAuxInfo(IWireLike port) {
            Wire = port;
        }
    }
    public class ComponentBuildingModel : ComponentModel {
        protected List<IUntypedConstructionPort> m_IoPortShape = new();
        protected Dictionary<string, IntermediateValueDesc> m_ImmValueCounter = new();
        protected List<INamedStageExpression> m_StagedValues = new();
        protected Dictionary<string, SubComponentDesc> m_SubComponentObjects = new();
        protected HashSet<RegisterDesc> m_Registers = new();
        protected Dictionary<IAssignableValue, AssignmentInfo> m_GenericAssignments = new();

        protected Dictionary<IWireLike, WireLikeAuxInfo> m_WireLikeObjects = new();
        public Dictionary<IReferenceTraceObject, HeapPointer> ReferenceTraceObjects { get; } = new();

        public override IReadOnlySet<RegisterDesc> Registers => m_Registers;
        public override IEnumerable<IUntypedConstructionPort> IoPortShape => m_IoPortShape;
        public override IReadOnlyDictionary<string, SubComponentDesc> SubComponents => m_SubComponentObjects;
        public override IReadOnlyCollection<INamedStageExpression> IntermediateValues => m_StagedValues;
        public override IReadOnlyDictionary<string, IntermediateValueDesc> IntermediateValueCount => m_ImmValueCounter;

        public override IReadOnlyDictionary<IAssignableValue, AssignmentInfo> GenericAssignments => m_GenericAssignments;

        public override IDictionary<IWireLike, WireLikeAuxInfo> WireLikeObjects => m_WireLikeObjects;

        public ComponentBuildingModel(Type componentType, ComponentBase componentObject, object[] instParameters) : base(componentType, componentObject, instParameters) {
            
        }
        protected Dictionary<IAssignableValue, Bitset> CheckLatch(IEnumerable<BehaviorDesc> descriptors, Dictionary<IAssignableValue, Bitset> assignmentInfo) {
            var assignedPorts = new Dictionary<IAssignableValue, Bitset>();

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
                if (i is PrimaryAssignment assignment) {
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
            
            Behavior.Root.EnumerateDesc((e,branchPath) => { 
                if(e is PrimaryAssignment primaryAssign) {
                    if (!m_GenericAssignments.ContainsKey(primaryAssign.UntypedLeftValue)) {
                        m_GenericAssignments.Add(primaryAssign.UntypedLeftValue, new (primaryAssign.UntypedLeftValue));
                    }

                    var assignmentInfo = m_GenericAssignments[primaryAssign.UntypedLeftValue];

                    if (assignmentInfo.PromotedRegister == null) {
                        m_Registers.Add(assignmentInfo.PromotedRegister = new CombPseudoRegister(assignmentInfo.UntypedLeftValue));
                        var oldLeftValue = primaryAssign.UntypedLeftValue;
                        assignmentInfo.PromotedRegister.Name = () => $"_cbr_{oldLeftValue.Name()}";

                        if(oldLeftValue is IWireLike wire) {
                            if (m_WireLikeObjects.ContainsKey(wire)) {
                                var ioAuxInfo = m_WireLikeObjects[wire];
                                foreach (var k in branchPath) {
                                    TrackOutputDependencyList(k.Condition.Condition, ioAuxInfo);
                                }
                            }
                        }

                        foreach (var i in assignmentInfo) {
                            var subAssignment = new PrimaryAssignment(assignmentInfo.PromotedRegister, i.RightValue, i.SelectedRange, nint.Zero);

                            rootSet.Add(subAssignment);
                        }

                        assignmentInfo.Clear();

                        var totalBits = oldLeftValue.UntypedType.WidthBits;
                        assignmentInfo.Add(new PrimaryAssignment(oldLeftValue, assignmentInfo.PromotedRegister.RVaule, new(.., (int)totalBits), nint.Zero));
                    }
                    primaryAssign.LeftValue = assignmentInfo.PromotedRegister;
                }
            });
            Behavior.Root.FalseBranch.InsertRange(0, rootSet);

            Behavior.Root.EnumerateDesc((e, branchPath) => {
                if(e is PrimaryAssignment assignment) {
                    if(assignment.LeftValue is CombPseudoRegister pseudoRegister) {
                        if(pseudoRegister.BackAssignable is IWireLike wire) {
                            if (m_WireLikeObjects.ContainsKey(wire)) {
                                var ioPortAux = m_WireLikeObjects[wire];

                                TrackOutputDependencyList(assignment.RightValue, ioPortAux);
                            }
                        }
                        
                    }
                }
            });

            var combAlwaysPortList = new Dictionary<IAssignableValue, Bitset>();

            Behavior.Root.EnumerateDesc((e,_)=> { 
                if(e is PrimaryAssignment assignment) {
                    if (!combAlwaysPortList.ContainsKey(assignment.LeftValue)) {
                        var totalBits = (assignment.LeftValue).UntypedType.WidthBits;
                        combAlwaysPortList.Add(assignment.LeftValue, new((int)totalBits));
                    }
                    combAlwaysPortList[assignment.LeftValue][assignment.SelectedRange] = BitRegionState.True;
                }
            });

            var checkResult = CheckLatch(Behavior.Root.FalseBranch, combAlwaysPortList);
            foreach (var (k, v) in combAlwaysPortList) {
                var fullyAssigned = checkResult[k];
                if (!v.Equals(fullyAssigned)) {
                    throw new Exception("Latch detected");
                }
            }

            foreach(var (wire,wireAux) in m_WireLikeObjects) {
                if(wire is Wire wireObject) {
                    if (!m_GenericAssignments.ContainsKey(wireObject)) {
                        throw new NullReferenceException($"Missing drive for {wireObject.Name()}");
                    }
                    var assignments = m_GenericAssignments[wireObject];
                    var totalBits = (int)wireObject.UntypedType.WidthBits;
                    var bitset = new Bitset(totalBits);

                    foreach(var j in assignments) {
                        bitset[j.SelectedRange] = BitRegionState.True;
                    }
                    if (bitset[new SpecifiedRange(0, totalBits)] != BitRegionState.True) {
                        throw new NullReferenceException($"Incomplete drive for {wireObject.Name()}");
                    }
                }
            }
        }

        public void RegisterIoPorts(IUntypedPort port) {
            InternalRegisterIoPorts(port);
        }
        public HeapPointer RegisterWire(Wire wire) {
            var pointerStorage = new HeapPointer(wire);

            ReferenceTraceObjects.Add(wire, pointerStorage);
            m_WireLikeObjects.Add(wire, new(wire));

            return pointerStorage;
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
                    m_WireLikeObjects.Add(constructedInternalPort, new(constructedInternalPort));
                    m_IoPortShape.Add(constructedInternalPort);
                } else {
                    throw new NotSupportedException();
                }
                
                return;
            }
            throw new NotSupportedException();
        }
        protected void DependencyPropagation(CombLogicDependency dependency, WireLikeAuxInfo info) {
            if (m_WireLikeObjects.ContainsKey(dependency.DependencyDestination)) {
                var wireAux = m_WireLikeObjects[dependency.DependencyDestination];
                foreach (var i in wireAux.CombLogicDependencyMap) {
                    if (i.DependencyDestination == info.Wire) {
                        throw new InvalidOperationException("Combination logic loop detected");
                    }
                    var dependencyInfo = new CombLogicDependency(i.DependencyDestination, dependency);

                    if (!info.CombLogicDependencyMap.Contains(dependencyInfo)) {
                        info.CombLogicDependencyMap.Add(dependencyInfo);

                        DependencyPropagation(dependencyInfo, info);
                    }
                }
            } else {
                if(dependency.DependencyDestination is IUntypedConstructionPort externalPort) {
                    var componentModel = externalPort.Component.InternalModel;
                    var externalAux = componentModel.WireLikeObjects[externalPort.InternalPort];

                    foreach(var i in externalAux.CombLogicDependencyMap) {
                        if(i.DependencyDestination is IUntypedConstructionPort { Direction : IoPortDirection.Input } internalInput) {
                            var inputExtenalPort = internalInput.Location.TraceValue(externalPort.Component);

                            Debug.Assert(inputExtenalPort != null);

                            if (inputExtenalPort == info.Wire) {
                                throw new InvalidOperationException("Combination logic loop detected");
                            }
                            var dependencyInfo = new CombLogicDependency(inputExtenalPort, i);

                            if (!info.CombLogicDependencyMap.Contains(dependencyInfo)) {
                                info.CombLogicDependencyMap.Add(dependencyInfo);

                                DependencyPropagation(dependencyInfo, info);
                            }

                        }
                    }
                }
            }
        }
        protected void TrackOutputDependencyList(AbstractValue value, WireLikeAuxInfo info, Action<AbstractValue>? callback = null) {
            callback ??= e => {
                TrackOutputDependencyList(e, info, callback);
            };
            var depInfo = default(CombLogicDependency);
            if (value is IUntypedIoRightValueWrapper wrapper) {
                depInfo = new CombLogicDependency((IUntypedConstructionPort)wrapper.UntypedComponent, null);
            }
            if (value is IWireRightValueWrapper wireWrapper) {
                depInfo = new CombLogicDependency(wireWrapper.UntyedWire, null);
            }
            if(depInfo != null) {
                info.CombLogicDependencyMap.Add(depInfo);
                if(depInfo.DependencyDestination == info.Wire) {
                    throw new InvalidOperationException("Combination logic loop detected");

                }
                DependencyPropagation(depInfo, info);
            }
            
            value.EnumerateSubNodes(callback);
        }
        protected void AssignOutputExpression(IoComponent internalLeftValue, AbstractValue rightValue, SpecifiedRange range){
            if(internalLeftValue is IAssignableValue internalOutput) {
                if (!m_GenericAssignments.ContainsKey(internalOutput)) {
                    m_GenericAssignments.Add(internalOutput, new IoPortAssignmentInfo(internalOutput));
                }

                var ioAssign = (IoPortAssignmentInfo)m_GenericAssignments[internalOutput];
                ioAssign.AssignPort(rightValue, range);

                var ioAuxInfo = m_WireLikeObjects[internalLeftValue];

                TrackOutputDependencyList(rightValue, ioAuxInfo);
            } else {
                throw new NotImplementedException();
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
        public void AssignWire(string name, Wire wire) {
            wire.Name = () => name;
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

                if (oldGroup.Count == 0) m_SubComponentObjects.Remove(oldGroup.InstanceName);
            }

            subComponent.CatagoryName = name;
            subComponent.Name = () => {
                var componentSet = m_SubComponentObjects[subComponent.CatagoryName];
                return $"{componentSet.InstanceName}_{componentSet.IndexOf(subComponent)}";
            };
        }
        protected SubComponentDesc GetSubComponentDesc(ComponentBase subComponent) {
            if (!m_SubComponentObjects.ContainsKey(subComponent.CatagoryName)) {
                m_SubComponentObjects.Add(subComponent.CatagoryName, new(subComponent.CatagoryName));
            }
            return m_SubComponentObjects[subComponent.CatagoryName];
        }


        public void AssignSubModuleConnections(IAssignableValue leftValue, object rightValue, Range range, nint returnAddress) {
            if(leftValue is IoBundle bundle) {
                var ioAux = IoComponentProbableHelpers.QueryProbeAuxiliary(leftValue.GetType());
                foreach (var i in ioAux.GetIoMembers(leftValue.GetType())) {
                    var newSlotValue = i.GetValue(rightValue);
                    if (newSlotValue == null) continue;

                    var oldSlotValue = (IAssignableValue)i.GetValue(leftValue);

                    AssignSubModuleConnections(oldSlotValue, newSlotValue, range, returnAddress);
                }

                return;
            }
            if(leftValue is Wire wireLhs) {
                var rightExpression = ResolveRightValue(rightValue);
                var bits = (int)wireLhs.UntypedType.WidthBits;
                var specRange = new SpecifiedRange(range, bits);


                Debug.Assert(rightExpression != null);

                if (Behavior.IsInBranchContext) {
                    Behavior.NotifyAssignment(returnAddress, wireLhs, rightExpression, specRange);
                } else {
                    if (!m_GenericAssignments.ContainsKey(wireLhs)) {
                        m_GenericAssignments.Add(wireLhs, new WireAssignmentInfo(wireLhs));
                    }

                    var ioAssign = (WireAssignmentInfo)m_GenericAssignments[wireLhs];
                    ioAssign.AssignPort(rightExpression, specRange);

                    var ioAuxInfo = m_WireLikeObjects[wireLhs];

                    TrackOutputDependencyList(rightExpression, ioAuxInfo);
                }

                return;
            }
            if (leftValue is IoComponent ioComponentLhs) {
                switch (ioComponentLhs.Direction) {
                    case IoPortDirection.Input: {
                        if (ioComponentLhs.Flags.HasFlag(GeneralizedPortFlags.ExternalPort)) { // ExternalInput

                            var leftExternalInput = (IUntypedConstructionPort)ioComponentLhs;
                            var leftAssignable = (IAssignableValue)ioComponentLhs;

                            var rightExpression = ResolveRightValue(rightValue);

                            Debug.Assert(rightExpression != null);

                            if (!m_GenericAssignments.ContainsKey(leftAssignable)) {
                                m_GenericAssignments.Add(leftAssignable, new SubComponentPortAssignmentInfo(leftExternalInput.Component, leftAssignable));
                            }
                            var wireObject = (IWireLike)leftAssignable;
                            if (!m_WireLikeObjects.ContainsKey(wireObject)) {
                                m_WireLikeObjects.Add(wireObject, new WireLikeAuxInfo(wireObject));
                            }

                            var wireSet = (SubComponentPortAssignmentInfo)m_GenericAssignments[leftAssignable];

                            var bits = (int)wireSet.UntypedLeftValue.UntypedType.WidthBits;
                            var specRange = new SpecifiedRange(range, bits);

                            if (specRange.BitWidth != rightExpression.Type.WidthBits) {
                                throw new InvalidOperationException("Bit width mismatch");
                            }

                            if (Behavior.IsInBranchContext) {
                                Behavior.NotifyAssignment(returnAddress, wireSet.UntypedLeftValue, rightExpression, specRange);
                            } else {
                                wireSet.AssignPort(rightExpression, specRange);
                            }

                            var ioAuxInfo = m_WireLikeObjects[wireObject];
                            TrackOutputDependencyList(rightExpression, ioAuxInfo);

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
                                    var specRange = new SpecifiedRange(invertedOutput.SelectedRange, bits);

                                    if(specRange.BitWidth != ioComponentLhs.UntypedRValue.Type.WidthBits) {
                                        throw new InvalidOperationException("Bit width mismatch");
                                    }

                                    if (Behavior.IsInBranchContext) {
                                        Behavior!.NotifyAssignment(returnAddress, (IAssignableValue)invertedOutput.InternalOut, ioComponentLhs.UntypedRValue, specRange);
                                    } else {
                                        
                                        AssignOutputExpression(invertedOutput.InternalOut, ioComponentLhs.UntypedRValue, specRange );
                                    }
                                    break;
                                }
                            }

                            throw new NotImplementedException();
                        }
                        if (ioComponentLhs.Flags.HasFlag(GeneralizedPortFlags.InternalPort)) { // InternalOutput
                            // assign outputs
                            var rightExpression = ResolveRightValue(rightValue);

                            Debug.Assert(rightExpression != null);


                            var bits = (int)((IDataTypeSpecifiedPort)ioComponentLhs).UntypedType.WidthBits;
                            var specRange = new SpecifiedRange(range, bits);

                            if (specRange.BitWidth != rightExpression.Type.WidthBits) {
                                throw new InvalidOperationException("Bit width mismatch");
                            }

                            if (Behavior.IsInBranchContext) {
                                Behavior!.NotifyAssignment(returnAddress, (IAssignableValue)ioComponentLhs, rightExpression, specRange);
                            } else {

                                AssignOutputExpression(ioComponentLhs, rightExpression, specRange);
                            }

                            break;
                        }

                        throw new NotImplementedException();
                    }
                }

            }
        }
        protected AbstractValue ResolveRightValue(object rightValue) {
            if (rightValue is IExpressionAssignedIoType expression) {
                return expression.UntypedExpression;
            }
            if (rightValue is IUntypedConstructionPort portLike) {
                return portLike.UntypedRValue;
            }
            throw new NotImplementedException();
        }
        public override IEnumerable<(AbstractValue assignValue, SpecifiedRange range)> QueryAssignedSubComponentIoValues(IUntypedConstructionPort declComponent, ComponentBase subModule) {
            var componentDesc = GetSubComponentDesc(subModule);
            return m_GenericAssignments.Where((e) => {
                if(e.Key is IUntypedConstructionPort port) {
                    if (port.InternalPort == declComponent && port.Component == subModule) return true;
                }
                return false;
            }).SelectMany(e=>e.Value).Select(e=>(e.RightValue, e.SelectedRange));
        }
    }

}
