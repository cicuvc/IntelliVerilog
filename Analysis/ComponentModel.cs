using IntelliVerilog.Core.Analysis.TensorLike;
using IntelliVerilog.Core.Components;
using IntelliVerilog.Core.DataTypes;
using IntelliVerilog.Core.DataTypes.Shape;
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
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace IntelliVerilog.Core.Analysis {
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
        public ComponentBuildingModel? GetComponentBuildingModel(bool throwOnNull = false) {
            var model = (ComponentBuildingModel?)(m_CurrentBuildModel.Count > 0 ? m_CurrentBuildModel.Peek().InternalModel : null);
            if(throwOnNull && model is null) throw new NullReferenceException("Component building model not available");
            return model;
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
        public IoComponent? TraceValue(IUntypedPort root) {
            foreach(var i in Path) {
                root = i.GetValue(root) ?? throw new Exception("Trace got null reference"); ;
            }
            return (IoComponent?)Member.GetValue(root);
        }
        public void TraceSetValue(IUntypedPort root, IUntypedPort value) {
            foreach (var i in Path) {
                root = i.GetValue(root) ?? throw new Exception("Trace got null reference");
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
        protected BehaviorContext? m_BehaviorContext;
        public string ModelName { get; }
        public BehaviorContext Behavior {
            get => m_BehaviorContext ?? throw new InvalidOperationException("Attempt to get uninitialized behavior record context");
            set {
                Debug.Assert(m_BehaviorContext is null);
                m_BehaviorContext = value;
            }
        }  
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

            ModelName = $"{ReflectionHelpers.DenseTypeName(componentType)}_{Utility.GetArraySignature(instParameters)}${ScopedLocator.GetService<ClockDomain>()?.Name ?? "<no domain!!>"}";
        }
        public IEnumerable<ComponentBase> GetSubComponents() {
            return OverlappedObjects.Where(e => e.Value is SubComponentDesc).SelectMany(e => (IEnumerable<ComponentBase>)e.Value);
        }
    }

    public class IoPortInternalAssignment {
        public IUntypedConstructionPort InternalPort { get; }
        public AbstractValue Value { get; }
        public ImmutableArray<GenericIndex> SelectionRange { get; }
        public IoPortInternalAssignment(IUntypedConstructionPort internalPort, AbstractValue value, ReadOnlySpan<GenericIndex> selectionRange) {
            InternalPort = internalPort;
            Value = value;
            SelectionRange = selectionRange.ToImmutableArray();
        }
        public void Deconstruct(out AbstractValue value, out ReadOnlySpan<GenericIndex> selectionRange) {
            value = Value;
            selectionRange = SelectionRange.AsSpan();
        }
    }
    public class IoPortInternalInfo : List<IoPortInternalAssignment> {
        public BitsetND BitsAssigned { get; set; }
        public IUntypedConstructionPort IoComponentDecl { get; }
        public bool PromotedRegister { get; set; } = false;
        public IoPortInternalInfo(IUntypedConstructionPort declPort) {
            IoComponentDecl = declPort;
            BitsAssigned = new(declPort.UntypedType.Shape);
        }
        public void AssignPort(AbstractValue value, ReadOnlySpan<GenericIndex> range) {
            if (BitsAssigned.ContainsNonZeroRange(range)) {
                throw new InvalidOperationException("Multi-drive detected");
            }
            BitsAssigned.SetRangeValue(range, true);
            Add(new(IoComponentDecl, value, range));
        }
    }
    public interface INamedStageExpression: IOverlappedObject {
        AbstractValue InternalValue { get; }
    }
    
    public class NamedStageExpression<TData> : RightValue<TData> , INamedStageExpression,IOverlappedObject where TData : DataType,IDataType<TData> {
        public AbstractValue InternalValue { get; }
        public IOverlappedObjectDesc Descriptor { get; set; }

        public override Lazy<TensorExpr> TensorExpression => throw new NotImplementedException();

        public NamedStageExpression(AbstractValue internalValue, SubValueStageDesc desc) : base((TData)internalValue.UntypedType) {
            InternalValue = internalValue;
            Descriptor = desc;
        }

        public override bool Equals(AbstractValue? other) {
            if(other is NamedStageExpression<TData> stageExpr) {
                return stageExpr.Descriptor == Descriptor && stageExpr.InternalValue.Equals(InternalValue);
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
        public string InstanceName { get; set; }

        IOverlappedObject IReadOnlyList<IOverlappedObject>.this[int index] => this[index];

        public SubComponentDesc(string instanceName) {
            InstanceName = instanceName;
        }

        IEnumerator<IOverlappedObject> IEnumerable<IOverlappedObject>.GetEnumerator() {
            return GetEnumerator();
        }
    }
    
    public abstract class RegisterDesc:IAssignableValue {
        protected RegisterValue? m_RightValueCache;
        public DataType UntypedType { get; }
        public Size Shape { get; }
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
        public RegisterDesc(DataType type,  bool isComb = false) {
            UntypedType = type;
            IsCombination = isComb;
            Shape = type.Shape;
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
        public ClockDrivenRegister(DataType type, ClockDomain? clockDomain) : base(type ,false) {
            ClockDom = clockDomain ?? ScopedLocator.GetService<ClockDomain>() ?? throw new Exception("Unknown clock domain");
        }
        public override AssignmentInfo CreateAssignmentInfo() {
            return new RegAssignmentInfo(this);
        }
    }
    public class RegisterValue : AbstractValue {

        public RegisterValue(RegisterDesc baseRegister) : base(baseRegister.UntypedType) {
            BaseRegister = baseRegister;

            if(baseRegister.Shape.IsAllDetermined) {
                TensorExpression = new(new TensorVarExpr<RegisterDesc>(baseRegister, baseRegister.Shape.ToImmutableIntShape()));
            } else {
                TensorExpression = new(() => new TensorVarExpr<RegisterDesc>(baseRegister, baseRegister.Shape.ToImmutableIntShape()));
            }
        }

        public RegisterDesc BaseRegister { get; }
        public override Lazy<TensorExpr> TensorExpression { get; }

        public override void EnumerateSubNodes(Action<AbstractValue> callback) { }

        public override bool Equals(AbstractValue? other) {
            throw new NotImplementedException();
        }

    }
    public interface ILazyNamedObject {
        Func<string> Name { get; set; }
    }
    public interface IShapedValue {
        Size Shape { get; }
    }
    public interface IAssignableValue: ILazyNamedObject, IShapedValue {
        DataType UntypedType { get; }


        AssignmentInfo CreateAssignmentInfo();
    }

    public interface IBasicAssignmentTerm {
        IAssignableValue UntypedLeftValue { get; }
        AbstractValue RightValue { get; set; }
        ImmutableArray<GenericIndex> SelectedRange { get; }
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
        public virtual void AssignPort(AbstractValue value, ReadOnlySpan<GenericIndex> range) {
            Add(new PrimaryAssignment(UntypedLeftValue, value, range, nint.Zero));
        }
    }
    public class WireAssignmentInfo : AssignmentInfo {
        public Wire WireLeftValue => (Wire)UntypedLeftValue;
        public override bool RegisterPromotable => true;
        public WireAssignmentInfo(IAssignableValue leftValue) : base(leftValue) {
        }
        public virtual void AssignPort(AbstractValue value, ReadOnlySpan<GenericIndex> range) {
            Add(new PrimaryAssignment(UntypedLeftValue, value, range, nint.Zero));
        }
    }
    public class RegAssignmentInfo : AssignmentInfo {
        public override bool RegisterPromotable => false;
        public RegAssignmentInfo(ClockDrivenRegister register) : base(register) {
        }
        public virtual void AssignPort(AbstractValue value, ReadOnlySpan<GenericIndex> range) {
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
        public ref T AsRef<T>() where T : IReferenceTraceObject {
            Debug.Assert(m_Reference is T);

            return ref Unsafe.As<IReferenceTraceObject,T>(ref m_Reference);
        }
    }
    public interface IReferenceTraceObject {

    }
    public interface IWireLike {
        Func<string> Name { get; set; }
    }
    public interface IOverlappedObject {
        IOverlappedObjectDesc Descriptor { get; set; }

    }
    public interface IOverlappedObjectDesc:IReadOnlyList<IOverlappedObject>,IEnumerable {
        string InstanceName { get; set; }
    }
    public class WireOverlappedDesc : List<Wire>, IOverlappedObjectDesc {
        public string InstanceName { get; set; }
        public DataType UntypedType { get; }

        IOverlappedObject IReadOnlyList<IOverlappedObject>.this[int index] => this[index];

        public WireOverlappedDesc(string instanceName, DataType type) {
            InstanceName = instanceName;
            UntypedType = type;
        }

        IEnumerator<IOverlappedObject> IEnumerable<IOverlappedObject>.GetEnumerator() {
            return GetEnumerator();
        }
    }
    public class RegisterOverlappedDesc : List<Reg>, IOverlappedObjectDesc {
        public string InstanceName { get; set; }
        public DataType UntypedType { get; }
        public RegisterOverlappedDesc(string instanceName, DataType type) {
            InstanceName = instanceName;
            UntypedType = type;
        }
        IOverlappedObject IReadOnlyList<IOverlappedObject>.this[int index] => this[index];
        IEnumerator<IOverlappedObject> IEnumerable<IOverlappedObject>.GetEnumerator() {
            return GetEnumerator();
        }
    }
    public interface ISubValueStageDesc: IOverlappedObjectDesc {
        ImmutableArray<int> SingleInstanceShape { get; }
    }
    public class SubValueStageDesc:List<INamedStageExpression>, ISubValueStageDesc,IWireLike {
        public string InstanceName { get; set; }
        public DataType UntypedType { get; }
        public ImmutableArray<int> SingleInstanceShape { get; }
        public Func<string> Name { get; set; }
        public SubValueStageDesc(string instanceName, ReadOnlySpan<int> singleInstanceShape,DataType type) {
            InstanceName = instanceName;
            UntypedType = type;
            SingleInstanceShape = singleInstanceShape.ToImmutableArray();
            Name = () => InstanceName;
        }
        IOverlappedObject IReadOnlyList<IOverlappedObject>.this[int index] => this[index];
        IEnumerator<IOverlappedObject> IEnumerable<IOverlappedObject>.GetEnumerator() {
            throw new NotImplementedException();
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
        protected Dictionary<IAssignableValue, BitsetND> CheckLatch(IEnumerable<BehaviorDesc> descriptors, Dictionary<IAssignableValue, BitsetND> assignmentInfo) {
            var assignedPorts = new Dictionary<IAssignableValue, BitsetND>();

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
                    assignedPorts.Add(i.Key, new(i.Value.Shape));
                }
            }
            foreach (var i in descriptors) {
                if (i is PrimaryAssignment assignment) {
                    if (assignedPorts.ContainsKey(assignment.LeftValue)) {
                        var bitset = assignedPorts[assignment.LeftValue];
                        if(bitset.ContainsNonZeroRange(assignment.SelectedRange.AsSpan())) {
                            throw new InvalidOperationException("Multi-drive detected");
                        }
                        bitset.SetRangeValue(assignment.SelectedRange.AsSpan(), true);
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
                                var subAssignment = new PrimaryAssignment(assignmentInfo.PromotedRegister, i.RightValue, i.SelectedRange.AsSpan(), nint.Zero);

                                rootSet.Add(subAssignment);
                            }

                            assignmentInfo.Clear();

                            assignmentInfo.Add(new PrimaryAssignment(oldLeftValue, assignmentInfo.PromotedRegister.RVaule, [], nint.Zero));
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

            var combAlwaysPortList = new Dictionary<IAssignableValue, BitsetND>();

            Behavior.Root.EnumerateDesc((e,_)=> { 
                if(e is PrimaryAssignment assignment) {
                    if(assignment.LeftValue is CombPseudoRegister) {
                        if (!combAlwaysPortList.ContainsKey(assignment.LeftValue)) {
                            var assignmentShape = (assignment.LeftValue).UntypedType.Shape;
                            combAlwaysPortList.Add(assignment.LeftValue, new(assignmentShape));
                        }
                        var targetBitset = combAlwaysPortList[assignment.LeftValue];
                        if(targetBitset.ContainsNonZeroRange(assignment.SelectedRange.AsSpan())) {
                            throw new InvalidOperationException("Multi-drive detected");
                        }
                        targetBitset.SetRangeValue(assignment.SelectedRange.AsSpan(), true);
                    }
                    
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
                    var bitset = new BitsetND(wireObject.UntypedType.Shape);

                    foreach(var j in assignments) {
                        if(bitset.ContainsNonZeroRange(j.SelectedRange.AsSpan())) {
                            throw new InvalidOperationException("Multi-drive detected");
                        }
                        bitset.SetRangeValue(j.SelectedRange.AsSpan(), true);
                    }
                    if(bitset.ContainsZeroRange([])) {
                        throw new NullReferenceException($"Incomplete drive for {wireObject.Name()}");
                    }
                }
            }
        }

        public void RegisterIoPorts(IUntypedPort port) {
            InternalRegisterIoPorts(port);
        }
        public HeapPointer RegisterHeapPointer(IReferenceTraceObject traceObject) {
            var pointerStorage = new HeapPointer(traceObject);

            ReferenceTraceObjects.Add(traceObject, pointerStorage);

            return pointerStorage;
        }
        public HeapPointer RegisterWire(Wire wire) {
            
            m_WireLikeObjects.Add(wire, new WireTrivalAux(wire));

            return RegisterHeapPointer(wire);
        }
        public HeapPointer RegisterReg(Reg wire) {
            m_WireLikeObjects.Add(wire, new RegTrivalAux(wire));

            m_Registers.Add(wire);

            return RegisterHeapPointer(wire);
        }
        protected void InternalRegisterIoPorts(IUntypedPort port) {
            if(port is IoBundle bundle) {
                var bundleType = bundle.GetType();
                var ioAux = IoComponentProbableHelpers.QueryProbeAuxiliary(bundleType);
                foreach(var i in ioAux.GetIoMembers(bundleType)) {
                    var subPort = i.GetValue(port);

                    if (subPort == null) throw new Exception($"Internal port {port.Name()} not initialized");
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
                if (!(wrapper.UntypedComponent is IClockPart)) {
                    if (!m_WireLikeObjects.ContainsKey(wrapper.UntypedComponent)) {
                        var component = ((IUntypedConstructionPort)wrapper.UntypedComponent).Component;
                        var componentModel = component.InternalModel;
                        var externalPort = (IUntypedConstructionPort)wrapper.UntypedComponent;
                        var internalPort = externalPort.InternalPort;
                        var internalPortAux = componentModel.WireLikeObjects[internalPort];

                        foreach (var i in internalPortAux.Precursors) {
                            if (i.Wire is IUntypedConstructionPort { Direction: IoPortDirection.Input } internalInput) {
                                var external = internalInput.Location.TraceValue(component) ?? throw new NullReferenceException($"Missing external port instance for port {internalInput.Name()}");

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
            }
            if (value is IWireRightValueWrapper wireWrapper) {
                m_WireLikeObjects[wireWrapper.UntyedWire].Connect(info);
            }
            if (value is IRegRightValueWrapper regWrapper) {
                m_WireLikeObjects[regWrapper.UntyedReg].Connect(info);
            }
            value.EnumerateSubNodes(callback);
        }
        protected void AssignOutputExpression(IoComponent internalLeftValue, AbstractValue rightValue, ReadOnlySpan<GenericIndex> range){
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
            var rightValueShape = rightValue.UntypedType.Shape.ToImmutableIntShape();

            if (!m_SubComponentObjects.ContainsKey(name))
                m_SubComponentObjects.Add(name, new SubValueStageDesc(name, rightValueShape, rightValue.UntypedType));

            var desc = (ISubValueStageDesc)m_SubComponentObjects[name];

            if (!desc.SingleInstanceShape.AsSpan().SequenceEqual(rightValueShape)) {
                throw new InvalidOperationException("Staged variable shape changed");
            }

            var namedType = typeof(NamedStageExpression<>).MakeGenericType(dataType);
            var stagedValue = (INamedStageExpression)namedType.GetConstructor(new Type[] {typeof(AbstractValue),typeof(SubValueStageDesc) })!
                .Invoke(new object[] { rightValue, desc });

            ((IList)desc).Add(stagedValue);

            return (AbstractValue)stagedValue;
        }
        public RightValue<Bool>? ResolveClockDomainSignal(ClockDomain clkDomain, ClockDomainSignal type) {
            Debug.Assert(!(clkDomain is InvalidClockDomain));

            RegisterClockDomain(clkDomain);

            var inputValue = m_IoPortShape.Find(e => {
                if(e is ClockDomainInput clkInput) {
                    return clkInput.ClockDom == clkDomain &&
                        clkInput.SignalType == type;
                }
                return false;
            })?.UntypedRValue;

            if (inputValue != null) return inputValue.Cast<Bool>();

            inputValue = m_WireLikeObjects.Where(e => {
                if (e.Key is ClockDomainWire clkInput) {
                    return clkInput.ClockDom == clkDomain &&
                        clkInput.SignalType == type;
                }
                return false;
            }).Select(e=>(ClockDomainWire)e.Key).FirstOrDefault()?.UntypedRValue;

            if (inputValue != null) return inputValue.Cast<Bool>();

            return null;
        }
        public void RegisterClockDomain(ClockDomain clkDomain) {
            if (clkDomain is InvalidClockDomain) return;
            if (!m_ClockDomains.Contains(clkDomain)) {
                m_ClockDomains.Add(clkDomain);

                if(clkDomain.CreationModel != this) {
                    if (!(clkDomain.RawClock is null)) {
                        var fakeClock = new ClockDomainInput(clkDomain, ClockDomainSignal.Clock, m_ComponentObject);
                        m_IoPortShape.Add(fakeClock);
                    }
                    if (!(clkDomain.RawReset is null)) {
                        var fakeClock = new ClockDomainInput(clkDomain, ClockDomainSignal.Reset, m_ComponentObject);
                        m_IoPortShape.Add(fakeClock);
                    }
                    if (!(clkDomain.RawSyncReset is null)) {
                        var fakeClock = new ClockDomainInput(clkDomain, ClockDomainSignal.SyncReset, m_ComponentObject);
                        m_IoPortShape.Add(fakeClock);
                    }
                    if (!(clkDomain.RawClockEnable is null)) {
                        var fakeClock = new ClockDomainInput(clkDomain, ClockDomainSignal.ClockEnable,m_ComponentObject);
                        m_IoPortShape.Add(fakeClock);
                    }
                } else {
                    if (!(clkDomain.RawClock is null)) {
                        var fakeClock = new ClockDomainWire(clkDomain, ClockDomainSignal.Clock);
                        RegisterWire(fakeClock);
                        
                        AssignSubModuleConnections(fakeClock, clkDomain.Clock, [(..)], nint.Zero);
                    }
                    if (!(clkDomain.RawReset is null)) {
                        var fakeClock = new ClockDomainWire(clkDomain, ClockDomainSignal.Reset);
                        RegisterWire(fakeClock);

                        AssignSubModuleConnections(fakeClock, clkDomain.RawReset, [(..)], nint.Zero);
                    }
                    if (!(clkDomain.RawSyncReset is null)) {
                        var fakeClock = new ClockDomainWire(clkDomain, ClockDomainSignal.SyncReset);
                        RegisterWire(fakeClock);

                        AssignSubModuleConnections(fakeClock, clkDomain.RawSyncReset, [(..)], nint.Zero);
                    }
                    if (!(clkDomain.RawClockEnable is null)) {
                        var fakeClock = new ClockDomainWire(clkDomain, ClockDomainSignal.ClockEnable);
                        RegisterWire(fakeClock);

                        AssignSubModuleConnections(fakeClock, clkDomain.RawClockEnable, [(..)], nint.Zero);
                    }
                }
            }
        }
        public void AddEntity(IOverlappedObject overlapped) {
            if (m_SubComponentObjects.ContainsKey(overlapped.Descriptor.InstanceName)) {
                throw new Exception("What?");
            } else {
                m_SubComponentObjects.Add(overlapped.Descriptor.InstanceName, overlapped.Descriptor);
            }
        }
        public void AssignEntityName(string name, IOverlappedObject overlapped) {
            if (m_SubComponentObjects.ContainsKey(name)) {
                var desc = m_SubComponentObjects[name];

                if (desc != overlapped.Descriptor) {
                    var untypedList = (IList)desc;
                    untypedList.Add(overlapped);

                    Debug.Assert(overlapped.Descriptor.Count == 1);
                    m_SubComponentObjects.Remove(overlapped.Descriptor.InstanceName);

                    overlapped.Descriptor = desc;
                } else {
                    throw new NotImplementedException();
                }
            } else {
                m_SubComponentObjects.Remove(overlapped.Descriptor.InstanceName);

                overlapped.Descriptor.InstanceName = name;
                m_SubComponentObjects.Add(name, overlapped.Descriptor);
            }
        }

        /// <summary>
        /// Handle all connections in submodule definition
        /// </summary>
        /// <param name="leftValue">assignment target</param>
        /// <param name="rightValue"></param>
        /// <param name="range">ranges applied on assignment target</param>
        /// <param name="returnAddress">identifier for CFG tracking</param>
        /// <exception cref="Exception"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="NotImplementedException"></exception>
        public void AssignSubModuleConnections(IAssignableValue leftValue, object rightValue, ReadOnlySpan<GenericIndex> range, nint returnAddress) {
            if(leftValue is IoBundle bundle) {
                var ioAux = IoComponentProbableHelpers.QueryProbeAuxiliary(leftValue.GetType());
                foreach (var i in ioAux.GetIoMembers(leftValue.GetType())) {
                    var newSlotValue = i.GetValue(rightValue);
                    if (newSlotValue == null) continue;

                    var oldSlotValue = (IAssignableValue)(i.GetValue(leftValue) ?? throw new Exception("Attempt to connect to uninitialized port-like entity"));

                    AssignSubModuleConnections(oldSlotValue, newSlotValue, range, returnAddress);
                }

                return;
            }
            if(leftValue is Reg register) {
                var rightExpression = ResolveRightValue(rightValue);
                Debug.Assert(rightExpression != null);

                Debug.Assert(m_Registers.Contains(register));

                Behavior.NotifyAssignment(returnAddress, register, rightExpression, range);

                var ioAuxInfo = m_WireLikeObjects[register];
                TrackOutputDependencyList(rightExpression, ioAuxInfo);

                return;
            }
            if (leftValue is Wire wireLhs) {
                var rightExpression = ResolveRightValue(rightValue);

                Debug.Assert(rightExpression != null);

                
                if(Behavior.IsInBranchContext && returnAddress != nint.Zero) {
                    // [TRACE] In CFG tracking environment
                    Behavior.NotifyAssignment(returnAddress, wireLhs, rightExpression, range);
                } else {
                    // [TRACE] A plain assignment
                    if(!m_GenericAssignments.ContainsKey(wireLhs)) {
                        m_GenericAssignments.Add(wireLhs, new WireAssignmentInfo(wireLhs));
                    }

                    var ioAssign = (WireAssignmentInfo)m_GenericAssignments[wireLhs];
                    ioAssign.AssignPort(rightExpression, range);
                }

                var ioAuxInfo = m_WireLikeObjects[wireLhs];
                TrackOutputDependencyList(rightExpression, ioAuxInfo);

                return;
            }
            if (leftValue is IoComponent ioComponentLhs) {
                switch (ioComponentLhs.Direction) {
                    case IoPortDirection.Input: {
                        if (ioComponentLhs.Flags.HasFlag(GeneralizedPortFlags.ExternalPort)) {
                            // [TRACE] ExternalInput: Connect to input port of submodule

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

                            var lhsResolvedShape = ShapeEvaluation.View(wireSet.UntypedLeftValue.Shape, range);

                            // Register shape equivalant relationship
                            rightExpression.UntypedType.Shape.RestrictShape(lhsResolvedShape);

                            if (Behavior.IsInBranchContext && returnAddress != nint.Zero) {
                                Behavior.NotifyAssignment(returnAddress, wireSet.UntypedLeftValue, rightExpression, range);
                            } else {
                                wireSet.AssignPort(rightExpression, range);
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

                                var assignableLhs = (IAssignableValue)invertedOutput.InternalOut;

                                var realLhsShape = ShapeEvaluation.View(realLhs.Shape, range);
                                assignableLhs.Shape.RestrictShape(realLhsShape);

                                var wireObject = (IWireLike)invertedOutput.InternalOut;
                                if (!m_WireLikeObjects.ContainsKey(wireObject)) {
                                    m_WireLikeObjects.Add(wireObject, new ExternalPortTrivalAux((IUntypedConstructionPort)invertedOutput.InternalOut));
                                }

                                if (Behavior.IsInBranchContext && returnAddress != nint.Zero) {
                                    Behavior!.NotifyAssignment(returnAddress, (IAssignableValue)invertedOutput.InternalOut, ioComponentLhs.UntypedRValue, range);
                                } else {
                                    AssignOutputExpression(invertedOutput.InternalOut, ioComponentLhs.UntypedRValue, range);
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
                            var lhsAssignable = ((IAssignableValue)ioComponentLhs);

                            var lhsResolvedShape = ShapeEvaluation.View(lhsType.Shape, range);

                            rightExpression.UntypedType.Shape.RestrictShape(lhsResolvedShape);

                            if (Behavior.IsInBranchContext && returnAddress != nint.Zero) {
                                Behavior!.NotifyAssignment(returnAddress, (IAssignableValue)ioComponentLhs, rightExpression, range);
                            } else {
                                AssignOutputExpression(ioComponentLhs, rightExpression, range);
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
            if (rightValue is IRegRightValueWrapper) return (AbstractValue)rightValue;
            if(rightValue is IWireRightValueWrapper) return (AbstractValue)rightValue;
            if (rightValue is IRightValueConvertible convertible) {
                if (convertible.UntypedRValue == convertible) return convertible.UntypedRValue;
                return ResolveRightValue(convertible.UntypedRValue);
            }
            throw new NotImplementedException();
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
