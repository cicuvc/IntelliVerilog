using IntelliVerilog.Core.Components;
using IntelliVerilog.Core.DataTypes;
using IntelliVerilog.Core.Expressions;
using IntelliVerilog.Core.Expressions.Algebra;
using IntelliVerilog.Core.Utils;
using System;
using System.Collections;
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
        public string ModelName { get; }
        public BehaviorContext? Behavior { get; set; } 
        public abstract IEnumerable<ClockDomain> UsedClockDomains { get; }
        public abstract IDictionary<IWireLike, WireTrivalAux> WireLikeObjects { get; }
        public abstract IReadOnlyDictionary<IAssignableValue, AssignmentInfo> GenericAssignments { get; }
        public abstract IEnumerable<IUntypedConstructionPort> IoPortShape { get; } 
        public abstract IEnumerable<RegisterDesc> Registers { get; }
        public ComponentBase ReferenceModule => m_ComponentObject;
        public abstract IReadOnlyDictionary<string, IOverlappedObjectDesc> OverlappedObjects { get; }
        public ComponentModel(Type componentType, ComponentBase componentObject, object[] instParameters) {
            m_ComponentType = componentType;
            m_ComponentObject = componentObject;
            m_Parameters = instParameters;

            ModelName = $"{componentType.Name}_{Utility.GetArraySignature(instParameters)}";
        }
        public abstract IEnumerable<(AbstractValue assignValue, SpecifiedRange range)> QueryAssignedSubComponentIoValues(IUntypedConstructionPort declComponent, ComponentBase subModule);
    
        public IEnumerable<ComponentBase> GetSubComponents() {
            return OverlappedObjects.Where(e => e.Value is SubComponentDesc).SelectMany(e => (SubComponentDesc)e.Value);
        }
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
        SubValueStageDesc StageDesc { get; }
    }
    
    public class NamedStageExpression<TData> : RightValue<TData> , INamedStageExpression where TData : DataType,IDataType<TData> {
        public AbstractValue InternalValue { get; }
        public SubValueStageDesc StageDesc { get; }
        public NamedStageExpression(AbstractValue internalValue, SubValueStageDesc desc) : base((TData)internalValue.Type, internalValue.Algebra) {
            InternalValue = internalValue;
            StageDesc = desc;
        }

        public override bool Equals(AbstractValue? other) {
            if(other is NamedStageExpression<TData> stageExpr) {
                return stageExpr.StageDesc == StageDesc && stageExpr.InternalValue.Equals(InternalValue);
            }
            return false;
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
    public class SubComponentDesc : List<ComponentBase>, IOverlappedObjectDesc {
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
    public abstract class RegisterDesc:IAssignableValue {
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

        public abstract AssignmentInfo CreateAssignmentInfo();
    }
    public class CombPseudoRegister : RegisterDesc {
        public IAssignableValue BackAssignable { get; }
        public CombPseudoRegister(IAssignableValue assignable) : base(assignable.UntypedType, true) {
            BackAssignable = assignable;
        }

        public override AssignmentInfo CreateAssignmentInfo() {
            return new WireAssignmentInfo(BackAssignable);
        }
    }
    public abstract class ClockDrivenRegister : RegisterDesc {
        public ClockDomain ClockDom { get; }
        public bool NoClockDomainCheck { get; set; } = false;
        public ClockDrivenRegister(DataType type, ClockDomain clockDomain) : base(type, false) {
            ClockDom = clockDomain;
        }
        public override AssignmentInfo CreateAssignmentInfo() {
            return new RegAssignmentInfo(this);
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

        AssignmentInfo CreateAssignmentInfo();
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
    public abstract class AssignmentInfo : List<IBasicAssignmentTerm> {
        public IAssignableValue UntypedLeftValue { get; }
        public RegisterDesc? PromotedRegister { get; set; }
        public abstract bool RegisterPromotable { get; }
        public AssignmentInfo(IAssignableValue leftValue) {
            UntypedLeftValue = leftValue;
        }
    }
    public class IoPortAssignmentInfo : AssignmentInfo {
        public IUntypedConstructionPort PortLeftValue => (IUntypedConstructionPort)UntypedLeftValue;
        public override bool RegisterPromotable => true;
        public IoPortAssignmentInfo(IAssignableValue leftValue) : base(leftValue) {
        }
        public virtual void AssignPort(AbstractValue value, SpecifiedRange range) {
            Add(new PrimaryAssignment(UntypedLeftValue, value, range, nint.Zero));
        }
    }
    public class WireAssignmentInfo : AssignmentInfo {
        public Wire WireLeftValue => (Wire)UntypedLeftValue;
        public override bool RegisterPromotable => true;
        public WireAssignmentInfo(IAssignableValue leftValue) : base(leftValue) {
        }
        public virtual void AssignPort(AbstractValue value, SpecifiedRange range) {
            Add(new PrimaryAssignment(UntypedLeftValue, value, range, nint.Zero));
        }
    }
    public class RegAssignmentInfo : AssignmentInfo {
        public override bool RegisterPromotable => false;
        public RegAssignmentInfo(ClockDrivenRegister register) : base(register) {
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
    public interface IOverlappedObjectDesc:IList,IEnumerable {
        string InstanceName { get; }
    }
    public class SubValueStageDesc:List<INamedStageExpression>, IOverlappedObjectDesc {
        public string InstanceName { get; }
        public DataType UntypedType { get; }
        public SubValueStageDesc(string instanceName,DataType type) {
            InstanceName = instanceName;
            UntypedType = type;
        }
    }
    public class ComponentBuildingModel : ComponentModel {
        protected HashSet<ClockDomain> m_ClockDomains = new();
        protected List<IUntypedConstructionPort> m_IoPortShape = new();
        protected Dictionary<string, IOverlappedObjectDesc> m_SubComponentObjects = new();
        protected List<RegisterDesc> m_Registers = new();
        protected Dictionary<IAssignableValue, AssignmentInfo> m_GenericAssignments = new();

        protected Dictionary<IWireLike, WireTrivalAux> m_WireLikeObjects = new();
        public Dictionary<IReferenceTraceObject, HeapPointer> ReferenceTraceObjects { get; } = new();

        public override IEnumerable<ClockDomain> UsedClockDomains => m_ClockDomains;
        public override IEnumerable<RegisterDesc> Registers => m_Registers;
        public override IEnumerable<IUntypedConstructionPort> IoPortShape => m_IoPortShape;
        public override IReadOnlyDictionary<string, IOverlappedObjectDesc> OverlappedObjects => m_SubComponentObjects;
        public override IReadOnlyDictionary<IAssignableValue, AssignmentInfo> GenericAssignments => m_GenericAssignments;
        public override IDictionary<IWireLike, WireTrivalAux> WireLikeObjects => m_WireLikeObjects;

        public ComponentBuildingModel(Type componentType, ComponentBase componentObject, object[] instParameters) : base(componentType, componentObject, instParameters) {
            
        }
        protected Dictionary<IAssignableValue, Bitset> CheckLatch(IEnumerable<BehaviorDesc> descriptors, Dictionary<IAssignableValue, Bitset> assignmentInfo) {
            var assignedPorts = new Dictionary<IAssignableValue, Bitset>();

            foreach (var i in descriptors) {
                if(i is IBranchLikeDesc branch) {
                    foreach(var u in branch.GetBranches()) {
                        foreach (var j in CheckLatch(u, assignmentInfo)) {
                            if (!assignedPorts.ContainsKey(j.Key)) {
                                assignedPorts.Add(j.Key, j.Value);
                            }
                            assignedPorts[j.Key].InplaceAnd(j.Value);
                        }
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
                if (e is PrimaryAssignment primaryAssign) {
                    if (!m_GenericAssignments.ContainsKey(primaryAssign.UntypedLeftValue)) {
                        m_GenericAssignments.Add(primaryAssign.UntypedLeftValue, (primaryAssign.UntypedLeftValue.CreateAssignmentInfo()));
                    }

                    var assignmentInfo = m_GenericAssignments[primaryAssign.UntypedLeftValue];

                    if (assignmentInfo.RegisterPromotable) {
                        if (assignmentInfo.PromotedRegister == null) {
                            m_Registers.Add(assignmentInfo.PromotedRegister = new CombPseudoRegister(assignmentInfo.UntypedLeftValue));
                            var oldLeftValue = primaryAssign.UntypedLeftValue;
                            assignmentInfo.PromotedRegister.Name = () => $"_cbr_{oldLeftValue.Name()}";

                            if (oldLeftValue is IWireLike wire) {
                                if (m_WireLikeObjects.ContainsKey(wire)) {
                                    var ioAuxInfo = m_WireLikeObjects[wire];
                                    foreach (var k in branchPath) {
                                        if(k.ConditionExpression != null)
                                            TrackOutputDependencyList(k.ConditionExpression, ioAuxInfo);
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
                }
            });
            Behavior.TypedRoot.FalseBranch.InsertRange(0, rootSet);

            Behavior.Root.EnumerateDesc((e, branchPath) => {
                if (e is PrimaryAssignment assignment) {
                    if (assignment.LeftValue is CombPseudoRegister pseudoRegister) {
                        if (pseudoRegister.BackAssignable is IWireLike wire) {
                            if (m_WireLikeObjects.ContainsKey(wire)) {
                                var ioPortAux = m_WireLikeObjects[wire];

                                TrackOutputDependencyList(assignment.RightValue, ioPortAux);
                            }
                        }

                    }
                }
            });

            Behavior.Root.EnumerateDesc((e, branchPath) => {
                if (e is PrimaryAssignment assignment) {
                    if (assignment.LeftValue is ClockDrivenRegister realRegister) {

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

            var checkResult = CheckLatch(Behavior.TypedRoot.FalseBranch, combAlwaysPortList);
            foreach (var (k, v) in combAlwaysPortList) {
                var fullyAssigned = checkResult[k];
                if (!v.Equals(fullyAssigned)) {
                    throw new Exception("Latch detected");
                }
            }

            foreach (var (wire,wireAux) in m_WireLikeObjects) {
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
            m_WireLikeObjects.Add(wire, new WireTrivalAux(wire));

            return pointerStorage;
        }
        public HeapPointer RegisterReg(Reg wire) {
            var pointerStorage = new HeapPointer(wire);

            ReferenceTraceObjects.Add(wire, pointerStorage);
            m_WireLikeObjects.Add(wire, new RegTrivalAux(wire));

            m_Registers.Add(wire);

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
                    m_WireLikeObjects.Add(constructedInternalPort, new InternalPortTrivalAux(constructedInternalPort));
                    m_IoPortShape.Add(constructedInternalPort);
                } else {
                    throw new NotSupportedException();
                }
                
                return;
            }
            throw new NotSupportedException();
        }
        protected void TrackOutputDependencyList(AbstractValue value, WireTrivalAux info, Action<AbstractValue>? callback = null) {
            callback ??= e => {
                TrackOutputDependencyList(e, info, callback);
            };
            if (value is IUntypedIoRightValueWrapper wrapper) {
                if(!m_WireLikeObjects.ContainsKey(wrapper.UntypedComponent)) {
                    var component = ((IUntypedConstructionPort)wrapper.UntypedComponent).Component;
                    var componentModel = component.InternalModel;
                    var externalPort = (IUntypedConstructionPort)wrapper.UntypedComponent;
                    var internalPort = externalPort.InternalPort;
                    var internalPortAux = componentModel.WireLikeObjects[internalPort];

                    foreach(var i in internalPortAux.Precursors) {
                        if(i.Wire is IUntypedConstructionPort { Direction: IoPortDirection.Input } internalInput) {
                            var external = internalInput.Location.TraceValue(component);

                            if (!m_WireLikeObjects.ContainsKey(external)) {
                                m_WireLikeObjects.Add(external, new ExternalPortTrivalAux((IUntypedConstructionPort)external));
                            }
                            m_WireLikeObjects[external].Connect(info);
                        }
                    }
                } else {
                    m_WireLikeObjects[wrapper.UntypedComponent].Connect(info);
                }
            }
            if (value is IWireRightValueWrapper wireWrapper) {
                m_WireLikeObjects[wireWrapper.UntyedWire].Connect(info);
            }
            if (value is IRegRightValueWrapper regWrapper) {
                m_WireLikeObjects[regWrapper.UntyedReg].Connect(info);
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
            if (!m_SubComponentObjects.ContainsKey(name)) m_SubComponentObjects.Add(name, new SubValueStageDesc(name,rightValue.Type));
            var desc = m_SubComponentObjects[name];

            var namedType = typeof(NamedStageExpression<>).MakeGenericType(dataType);
            var stagedValue = (INamedStageExpression)namedType.GetConstructor(new Type[] {typeof(AbstractValue),typeof(SubValueStageDesc) })
                .Invoke(new object[] { rightValue, desc });

            desc.Add(stagedValue);

            return (AbstractValue)stagedValue;
        }
        public void AssignWire(string name, Wire wire) {
            wire.Name = () => name;
        }
        public void RegisterClockDomain(ClockDomain clkDomain) {
            if (!m_ClockDomains.Contains(clkDomain)) {
                m_ClockDomains.Add(clkDomain);

                if(!(clkDomain.Clock is null)) {
                    var fakeClock = new ClockDomainInput(clkDomain,ClockDomainSignal.Clock);
                    m_IoPortShape.Add(fakeClock);
                }
                if (!(clkDomain.Reset is null)) {
                    var fakeClock = new ClockDomainInput(clkDomain, ClockDomainSignal.Reset);
                    m_IoPortShape.Add(fakeClock);
                }
                if (!(clkDomain.SyncReset is null)) {
                    var fakeClock = new ClockDomainInput(clkDomain, ClockDomainSignal.SyncReset);
                    m_IoPortShape.Add(fakeClock);
                }
                if (!(clkDomain.ClockEnable is null)) {
                    var fakeClock = new ClockDomainInput(clkDomain, ClockDomainSignal.ClockEnable);
                    m_IoPortShape.Add(fakeClock);
                }
            }
        }
        public void AssignReg(string name, Reg wire) {
            Debug.Assert(m_Registers.Contains(wire));
            wire.Name = () => name;

            if(wire.ClockDom != null)
                RegisterClockDomain(wire.ClockDom);
        }
        public void AddSubComponent(ComponentBase subComponent) {
            var identifier = $"M{Utility.GetRandomStringHex(16)}";
            var group = new SubComponentDesc(identifier) { subComponent };
            subComponent.CatagoryName = identifier;
            subComponent.Name = () => $"{identifier}_{group.IndexOf(subComponent)}";

            m_SubComponentObjects.Add(identifier, group);
        }
        public void AssignLocalSubComponent(string name, ComponentBase subComponent) {
            if (!m_SubComponentObjects.ContainsKey(name)) {
                m_SubComponentObjects.Add(name, new SubComponentDesc(name));
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

            foreach(var i in (subComponent.InternalModel.UsedClockDomains)) {
                RegisterClockDomain(i);
            }
        }
        protected SubComponentDesc GetSubComponentDesc(ComponentBase subComponent) {
            if (!m_SubComponentObjects.ContainsKey(subComponent.CatagoryName)) {
                m_SubComponentObjects.Add(subComponent.CatagoryName, new SubComponentDesc(subComponent.CatagoryName));
            }
            return (SubComponentDesc)m_SubComponentObjects[subComponent.CatagoryName];
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
            if(leftValue is Reg register) {
                var rightExpression = ResolveRightValue(rightValue);
                var bits = (int)register.UntypedType.WidthBits;
                var specRange = new SpecifiedRange(range, bits);
                Debug.Assert(rightExpression != null);

                Debug.Assert(m_Registers.Contains(register));

                Behavior.NotifyAssignment(returnAddress, register, rightExpression, specRange);

                var ioAuxInfo = m_WireLikeObjects[register];
                TrackOutputDependencyList(rightExpression, ioAuxInfo);

                return;
            }
            if (leftValue is Wire wireLhs) {
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
                }

                var ioAuxInfo = m_WireLikeObjects[wireLhs];
                TrackOutputDependencyList(rightExpression, ioAuxInfo);

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
                                m_WireLikeObjects.Add(wireObject, new ExternalPortTrivalAux(leftExternalInput));
                            }

                            var wireSet = (SubComponentPortAssignmentInfo)m_GenericAssignments[leftAssignable];

                            var bits = (int)wireSet.UntypedLeftValue.UntypedType.WidthBits;
                            var specRange = new SpecifiedRange(range, bits);

                            if (!rightExpression.Type.IsWidthSpecified) {
                                rightExpression.Type = wireSet.UntypedLeftValue.UntypedType;
                            }
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
                        if(ioComponentLhs.Flags.HasFlag(GeneralizedPortFlags.InternalPort)) { // InternalInput  // inverted case
                            var leftInternalInput = (IUntypedConstructionPort)ioComponentLhs;
                            throw new InvalidOperationException($"Assignment on internal io port {leftInternalInput.Location}");
                        }
                        
                        throw new NotImplementedException();
                    }
                    case IoPortDirection.Output: {
                        if (ioComponentLhs.Flags.HasFlag(GeneralizedPortFlags.ExternalPort)) { // ExternalOutput
                            // inverted case
                            var rightExpression = ResolveRightValue(rightValue);


                            if (rightExpression is IInvertedOutput invertedOutput) {
                                var realLhs = (IUntypedConstructionPort)invertedOutput.InternalOut;


                                if (!realLhs.UntypedType.IsWidthSpecified) {
                                    realLhs.UntypedType = ioComponentLhs.UntypedRValue.Type;
                                }

                                var bits = (int)realLhs.UntypedType.WidthBits;
                                var specRange = new SpecifiedRange(invertedOutput.SelectedRange, bits);

                                if (specRange.BitWidth != ioComponentLhs.UntypedRValue.Type.WidthBits) {
                                    throw new InvalidOperationException("Bit width mismatch");
                                }
                                var wireObject = (IWireLike)invertedOutput.InternalOut;
                                if (!m_WireLikeObjects.ContainsKey(wireObject)) {
                                    m_WireLikeObjects.Add(wireObject, new ExternalPortTrivalAux((IUntypedConstructionPort)invertedOutput.InternalOut));
                                }

                                if (Behavior.IsInBranchContext) {
                                    Behavior!.NotifyAssignment(returnAddress, (IAssignableValue)invertedOutput.InternalOut, ioComponentLhs.UntypedRValue, specRange);
                                } else {
                                    AssignOutputExpression(invertedOutput.InternalOut, ioComponentLhs.UntypedRValue, specRange);
                                }


                                break;
                            }


                            throw new NotImplementedException();
                        }
                        if (ioComponentLhs.Flags.HasFlag(GeneralizedPortFlags.InternalPort)) { // InternalOutput
                            // assign outputs
                            var rightExpression = ResolveRightValue(rightValue);

                            Debug.Assert(rightExpression != null);

                            var lhsType = ((IDataTypeSpecifiedPort)ioComponentLhs).UntypedType;
 
                            if (!rightExpression.Type.IsWidthSpecified && !lhsType.IsWidthSpecified) {
                                throw new Exception("Unable to infer bit width");
                            }
                            if (!lhsType.IsWidthSpecified) {
                                lhsType = ((IDataTypeSpecifiedPort)ioComponentLhs).UntypedType = rightExpression.Type;
                            }
                            if (!rightExpression.Type.IsWidthSpecified) {
                                rightExpression.Type = lhsType;
                            }

                            var bits = (int)lhsType.WidthBits;
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
    
    public class WireTrivalAux {
        public IWireLike Wire { get; }
        protected HashSet<WireTrivalAux> m_SuccessorSet = new();
        protected HashSet<WireTrivalAux> m_PrecursorSet = new();
        public IEnumerable<WireTrivalAux> Successors => m_SuccessorSet;
        public IEnumerable<WireTrivalAux> Precursors => m_PrecursorSet;
        public WireTrivalAux(IWireLike wire) {
            Wire = wire;
        }
        protected virtual void OnAddPrecursorSet(WireTrivalAux other) {
            if(other == this) {
                throw new Exception("Combination logic loop detected");
            }
        }
        protected virtual IEnumerable<WireTrivalAux> GetMergePrecursorSet() {
            return Precursors;
        }
        protected virtual IEnumerable<WireTrivalAux> GetMergeSuccessorSet() {
            return Successors;
        }
        protected virtual void OnAddSuccessorSet(WireTrivalAux other) { }
        public void Connect(WireTrivalAux next) {
            var prevGroup = GetMergePrecursorSet().Append(this);
            var succGroup = next.GetMergeSuccessorSet().Append(next);
            foreach(var i in prevGroup) {
                foreach(var j in succGroup) {
                    i.OnAddSuccessorSet(j);
                    i.m_SuccessorSet.Add(j);

                    j.OnAddPrecursorSet(i);
                    j.m_PrecursorSet.Add(i);
                }
            }
        }
        public bool FindSelfAndSuccessor(Predicate<IWireLike> predicate) {
            if (predicate(Wire)) return true;
            return m_SuccessorSet.Where(e => predicate(e.Wire)).Count() != 0;
        }
        public bool FindSelfAndPrecursor(Predicate<IWireLike> predicate) {
            if (predicate(Wire)) return true;
            return m_PrecursorSet.Where(e => predicate(e.Wire)).Count() != 0;
        }
    }
    public interface IAuxWithClockDomain {
        ClockDomain? ClockDom { get; set; }
        bool NoClockDomainCheck { get; }
        
    }
    public class RegTrivalAux : WireTrivalAux, IAuxWithClockDomain {
        public Reg Register => (Reg)Wire;
        public ClockDomain? ClockDom { get; set; }
        public bool NoClockDomainCheck => Register.NoClockDomainCheck;
        public RegTrivalAux(Reg wire) : base(wire) {
            ClockDom = wire.ClockDom;
        }
        protected override IEnumerable<WireTrivalAux> GetMergePrecursorSet() {
            return Array.Empty<WireTrivalAux>();
        }
        protected override IEnumerable<WireTrivalAux> GetMergeSuccessorSet() {
            return Array.Empty<WireTrivalAux>();
        }
        protected override void OnAddPrecursorSet(WireTrivalAux other) {
            //base.OnAddPrecursorSet(other);

            if(other is IAuxWithClockDomain otherWithDomain) {
                if (NoClockDomainCheck || otherWithDomain.NoClockDomainCheck) return;
                if (!(otherWithDomain.ClockDom?.IsSynchoroizedWith(Register.ClockDom) ?? true)) {
                    throw new Exception("Clock domoain cross detected");
                }
            }
        }
        protected override void OnAddSuccessorSet(WireTrivalAux other) {
            //base.OnAddSuccessorSet(other);

            if (other is IAuxWithClockDomain otherWithDomain) {
                if (NoClockDomainCheck || otherWithDomain.NoClockDomainCheck) return;
                if (!(otherWithDomain.ClockDom?.IsSynchoroizedWith(Register.ClockDom) ?? true)) {
                    throw new Exception("Clock domoain cross detected");
                }
            }
        }
    }
    public class InternalPortTrivalAux : WireTrivalAux, IAuxWithClockDomain {
        public IUntypedConstructionPort InternalPort { get; }
        public bool NoClockDomainCheck => false;
        public InternalPortTrivalAux(IWireLike wire) : base(wire) {
            InternalPort = (IUntypedConstructionPort)wire;
        }

        public ClockDomain? ClockDom { get ; set; }
        protected override void OnAddSuccessorSet(WireTrivalAux other) {
            base.OnAddSuccessorSet(other);

            if (InternalPort.Direction == IoPortDirection.Input) {
                if (other is IAuxWithClockDomain clockDomain) {
                    if (NoClockDomainCheck || clockDomain.NoClockDomainCheck) return;
                    ClockDom ??= clockDomain.ClockDom;
                    if (!(ClockDom?.IsSynchoroizedWith(clockDomain.ClockDom) ?? true)) {
                        throw new Exception("Clock domain cross detected");
                    }
                }
            }
        }
        protected override void OnAddPrecursorSet(WireTrivalAux other) {
            base.OnAddPrecursorSet(other);

            if(InternalPort.Direction == IoPortDirection.Output) {
                if (other is IAuxWithClockDomain clockDomain) {
                    if (NoClockDomainCheck || clockDomain.NoClockDomainCheck) return;
                    ClockDom ??= clockDomain.ClockDom;
                    if (!(ClockDom?.IsSynchoroizedWith(clockDomain.ClockDom) ?? true)) {
                        throw new Exception("Clock domain cross detected");
                    }
                }
            }
        }
    }
    public class ExternalPortTrivalAux: WireTrivalAux, IAuxWithClockDomain {
        public ComponentModel InternalModel { get; }
        public InternalPortTrivalAux InnerAux { get; }
        public bool NoClockDomainCheck => false;
        public ClockDomain? ClockDom {
            get => InnerAux.ClockDom;
            set => throw new NotImplementedException(); 
        }

        public ExternalPortTrivalAux(IUntypedConstructionPort externalPort) : base(externalPort) {
            InternalModel = externalPort.Component.InternalModel;
            InnerAux = (InternalPortTrivalAux)InternalModel.WireLikeObjects[externalPort.InternalPort];
        }
    }
}
