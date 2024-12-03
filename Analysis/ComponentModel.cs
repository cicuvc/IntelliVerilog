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
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace IntelliVerilog.Core.Analysis {
    [StructLayout(LayoutKind.Explicit)]
    public struct SpecifiedIndex:IEquatable<SpecifiedIndex> {
        [FieldOffset(0)]
        private GenericIndexFlags m_Flag;
        [FieldOffset(4)]
        private SpecifiedRange m_Range;
        [FieldOffset(4)]
        private GCHandle m_Handle;
        public GenericIndexFlags Flags => m_Flag;
        public AbstractValue? IndexValue {
            get => m_Flag == GenericIndexFlags.ValueIndex ? (AbstractValue?)m_Handle.Target : null;
        }
        public SpecifiedRange Range => m_Range;
        public int BitWidth {
            get => m_Flag == GenericIndexFlags.RangeIndex ? m_Range.BitWidth : 1;
        }
        public SpecifiedIndex(GenericIndexFlags flag) {
            m_Flag = flag;
        }
        public SpecifiedIndex(int singleIndex) {
            m_Flag = GenericIndexFlags.SingleIndex;
            m_Range = new(singleIndex, singleIndex+1);
        }
        public SpecifiedIndex(SpecifiedRange range) {
            m_Flag = GenericIndexFlags.RangeIndex;
            m_Range = range;
        }
        public GenericIndex ToGenericIndex() {
            if (m_Flag == GenericIndexFlags.RangeIndex) return new(m_Range.ToRange(), GenericIndexFlags.RangeIndex);
            return new(IndexValue ?? throw new NullReferenceException());
        }
        public SpecifiedIndex(AbstractValue index) {
            m_Flag = GenericIndexFlags.ValueIndex;
            m_Handle = index.WeakRef;
        }

        public bool Equals(SpecifiedIndex other) {
            if (other.m_Flag != m_Flag) return false;
            if (other.m_Handle != m_Handle) return false;
            return true;
        }
    }
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
        public abstract IEnumerable<(AbstractValue assignValue, SpecifiedIndices range)> QueryAssignedSubComponentIoValues(IUntypedConstructionPort declComponent, ComponentBase subModule);
    
        public IEnumerable<ComponentBase> GetSubComponents() {
            return OverlappedObjects.Where(e => e.Value is SubComponentDesc).SelectMany(e => (IEnumerable<ComponentBase>)e.Value);
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
    public interface INamedStageExpression: IOverlappedObject {
        AbstractValue InternalValue { get; }
    }
    
    public class NamedStageExpression<TData> : RightValue<TData> , INamedStageExpression,IOverlappedObject where TData : DataType,IDataType<TData> {
        public AbstractValue InternalValue { get; }
        public IOverlappedObjectDesc Descriptor { get; set; }

        public NamedStageExpression(AbstractValue internalValue, SubValueStageDesc desc) : base((TData)internalValue.Type, internalValue.Shape, internalValue.Algebra) {
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
        public ValueShape Shape { get; }
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
        public RegisterDesc(DataType type, ValueShape shape, bool isComb = false) {
            UntypedType = type;
            IsCombination = isComb;
            Shape = shape;
        }

        public abstract AssignmentInfo CreateAssignmentInfo();
    }
    public class CombPseudoRegister : RegisterDesc {
        public IAssignableValue BackAssignable { get; }
        public CombPseudoRegister(IAssignableValue assignable) : base(assignable.UntypedType, assignable.Shape, true) {
            BackAssignable = assignable;
        }

        public override AssignmentInfo CreateAssignmentInfo() {
            return new WireAssignmentInfo(BackAssignable);
        }
    }
    public abstract class ClockDrivenRegister : RegisterDesc {
        public ClockDomain ClockDom { get; }
        public bool NoClockDomainCheck { get; set; } = false;
        public ClockDrivenRegister(DataType type, ValueShape shape,ClockDomain? clockDomain) : base(type,shape ,false) {
            ClockDom = clockDomain ?? ScopedLocator.GetService<ClockDomain>() ?? throw new Exception("Unknown clock domain");
        }
        public override AssignmentInfo CreateAssignmentInfo() {
            return new RegAssignmentInfo(this);
        }
    }
    public class RegisterValue : AbstractValue {
        public RegisterValue(RegisterDesc baseRegister) : base(baseRegister.UntypedType, baseRegister.Shape) {
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
    public interface IShapedValue {
        ValueShape Shape { get; }
    }
    public interface IAssignableValue: ILazyNamedObject, IShapedValue {
        DataType UntypedType { get; }


        AssignmentInfo CreateAssignmentInfo();
    }

    public interface IBasicAssignmentTerm {
        IAssignableValue UntypedLeftValue { get; }
        AbstractValue RightValue { get; set; }
        SpecifiedIndices SelectedRange { get; }
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
        public virtual void AssignPort(AbstractValue value, SpecifiedIndices range) {
            Add(new PrimaryAssignment(UntypedLeftValue, value, range, nint.Zero));
        }
    }
    public class WireAssignmentInfo : AssignmentInfo {
        public Wire WireLeftValue => (Wire)UntypedLeftValue;
        public override bool RegisterPromotable => true;
        public WireAssignmentInfo(IAssignableValue leftValue) : base(leftValue) {
        }
        public virtual void AssignPort(AbstractValue value, SpecifiedIndices range) {
            Add(new PrimaryAssignment(UntypedLeftValue, value, range, nint.Zero));
        }
    }
    public class RegAssignmentInfo : AssignmentInfo {
        public override bool RegisterPromotable => false;
        public RegAssignmentInfo(ClockDrivenRegister register) : base(register) {
        }
        public virtual void AssignPort(AbstractValue value, SpecifiedIndices range) {
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
        ValueShape SingleInstanceShape { get; }
    }
    public class SubValueStageDesc:List<INamedStageExpression>, ISubValueStageDesc,IWireLike {
        public string InstanceName { get; set; }
        public DataType UntypedType { get; }
        public ValueShape SingleInstanceShape { get; }
        public Func<string> Name { get; set; }
        public SubValueStageDesc(string instanceName, ValueShape singleInstanceShape,DataType type) {
            InstanceName = instanceName;
            UntypedType = type;
            SingleInstanceShape = singleInstanceShape;
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
                        assignment.SelectedRange.FillBitset(bitset);
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
                            var selection = SpecifiedIndices.FullIndices(oldLeftValue.Shape);
                            assignmentInfo.Add(new PrimaryAssignment(oldLeftValue, assignmentInfo.PromotedRegister.RVaule, selection, nint.Zero));
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
                    if(assignment.LeftValue is CombPseudoRegister) {
                        if (!combAlwaysPortList.ContainsKey(assignment.LeftValue)) {
                            var totalBits = (assignment.LeftValue).UntypedType.WidthBits;
                            combAlwaysPortList.Add(assignment.LeftValue, new((int)totalBits));
                        }
                        assignment.SelectedRange.FillBitset(combAlwaysPortList[assignment.LeftValue]);
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
                    var totalBits = (int)wireObject.UntypedType.WidthBits;
                    var bitset = new Bitset(totalBits);

                    foreach(var j in assignments) {
                        j.SelectedRange.FillBitset(bitset);
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
        protected void AssignOutputExpression(IoComponent internalLeftValue, AbstractValue rightValue, SpecifiedIndices range){
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
            if (!m_SubComponentObjects.ContainsKey(name))
                m_SubComponentObjects.Add(name, new SubValueStageDesc(name,rightValue.Shape,rightValue.Type));

            var desc = (ISubValueStageDesc)m_SubComponentObjects[name];

            if (!desc.SingleInstanceShape.Equals(rightValue.Shape)) {
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
                        
                        AssignSubModuleConnections(fakeClock, clkDomain.Clock, new(..), nint.Zero);
                    }
                    if (!(clkDomain.RawReset is null)) {
                        var fakeClock = new ClockDomainWire(clkDomain, ClockDomainSignal.Reset);
                        RegisterWire(fakeClock);

                        AssignSubModuleConnections(fakeClock, clkDomain.RawReset, new(..), nint.Zero);
                    }
                    if (!(clkDomain.RawSyncReset is null)) {
                        var fakeClock = new ClockDomainWire(clkDomain, ClockDomainSignal.SyncReset);
                        RegisterWire(fakeClock);

                        AssignSubModuleConnections(fakeClock, clkDomain.RawSyncReset, new(..), nint.Zero);
                    }
                    if (!(clkDomain.RawClockEnable is null)) {
                        var fakeClock = new ClockDomainWire(clkDomain, ClockDomainSignal.ClockEnable);
                        RegisterWire(fakeClock);

                        AssignSubModuleConnections(fakeClock, clkDomain.RawClockEnable, new(..), nint.Zero);
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


        public void AssignSubModuleConnections(IAssignableValue leftValue, object rightValue, GenericIndices range, nint returnAddress) {
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
                var bits = (int)register.UntypedType.WidthBits;
                var specRange = range.ResolveSelectionRange(register.Shape);
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
                var specRange = range.ResolveSelectionRange(wireLhs.Shape);

                Debug.Assert(rightExpression != null);

                if (Behavior.IsInBranchContext && returnAddress != nint.Zero) {
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
                            var specRange = range.ResolveSelectionRange(wireSet.UntypedLeftValue.Shape);

                            if (!rightExpression.Type.IsWidthSpecified) {
                                rightExpression.Type = rightExpression.Type.CreateWithWidth((uint)specRange.BaseShape.Last());
                            }
                            if (!specRange.IsCompatibleShape(rightExpression.Shape)) {
                                throw new InvalidOperationException("Bit width mismatch");
                            }

                            if (Behavior.IsInBranchContext && returnAddress != nint.Zero) {
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

                                var assignableLhs = (IAssignableValue)invertedOutput.InternalOut;

                                var specRange = invertedOutput.SelectedRange.ResolveSelectionRange(assignableLhs.Shape);  

                                if (!realLhs.UntypedType.IsWidthSpecified) {
                                    realLhs.UntypedType = realLhs.UntypedType.CreateWithWidth((uint)specRange.BaseShape.Last());
                                }

                                if (!specRange.IsCompatibleShape(ioComponentLhs.UntypedRValue.Shape)) {
                                    throw new InvalidOperationException("Bit width mismatch");
                                }
                                var wireObject = (IWireLike)invertedOutput.InternalOut;
                                if (!m_WireLikeObjects.ContainsKey(wireObject)) {
                                    m_WireLikeObjects.Add(wireObject, new ExternalPortTrivalAux((IUntypedConstructionPort)invertedOutput.InternalOut));
                                }

                                if (Behavior.IsInBranchContext && returnAddress != nint.Zero) {
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
                            var lhsAssignable = ((IAssignableValue)ioComponentLhs);
                            
                            if (!rightExpression.Type.IsWidthSpecified && !lhsType.IsWidthSpecified) {
                                throw new Exception("Unable to infer bit width");
                            }
                            if (!lhsType.IsWidthSpecified) {
                                lhsType = ((IDataTypeSpecifiedPort)ioComponentLhs).UntypedType = rightExpression.Type;
                            }

                            // Here we make sure lhs width has benn completely bounded
                            var lhsRange = range.ResolveSelectionRange(lhsAssignable.Shape);
                            if (!rightExpression.Type.IsWidthSpecified) {
                                rightExpression.Type = rightExpression.Type.CreateWithWidth((uint)lhsRange.BaseShape.Last());
                            }
    
                            if (!lhsRange.IsCompatibleShape(rightExpression.Shape)) {
                                throw new InvalidOperationException("Bit width mismatch");
                            }

                            if (Behavior.IsInBranchContext && returnAddress != nint.Zero) {
                                Behavior!.NotifyAssignment(returnAddress, (IAssignableValue)ioComponentLhs, rightExpression, lhsRange);
                            } else {
                                AssignOutputExpression(ioComponentLhs, rightExpression, lhsRange);
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
        public override IEnumerable<(AbstractValue assignValue, SpecifiedIndices range)> QueryAssignedSubComponentIoValues(IUntypedConstructionPort declComponent, ComponentBase subModule) {
            var componentDesc = (SubComponentDesc)(subModule).Descriptor;
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
