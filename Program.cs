﻿using Iced.Intel;
using IntelliVerilog.Core.Analysis;
using IntelliVerilog.Core.Analysis.TensorLike;
using IntelliVerilog.Core.CodeGen;
using IntelliVerilog.Core.CodeGen.Verilog;
using IntelliVerilog.Core.Components;
using IntelliVerilog.Core.DataTypes;
using IntelliVerilog.Core.DataTypes.Shape;
using IntelliVerilog.Core.Examples;
using IntelliVerilog.Core.Examples.TestSuites;
using IntelliVerilog.Core.Expressions;
using IntelliVerilog.Core.Expressions.Algebra;
using IntelliVerilog.Core.Logging;
using IntelliVerilog.Core.Runtime.Core;
using IntelliVerilog.Core.Runtime.Injection;
using IntelliVerilog.Core.Runtime.Native;
using IntelliVerilog.Core.Runtime.Services;
using IntelliVerilog.Core.Runtime.Unsafe;
using IntelliVerilog.Core.Utils;
using SharpPdb.Managed.Windows;
using SharpUtilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

using static IntelliVerilog.Core.GenericIndex;

namespace IntelliVerilog.Core;
public class BehaviorDesc {

}

public class PrimaryOp: BehaviorDesc {
    public nint ReturnAddress { get; }
    public PrimaryOp(nint returnAddress) {
        ReturnAddress = returnAddress;
    }
}
public class PrimaryExit: BehaviorDesc { }

public class PrimaryAssignment : PrimaryOp,IBasicAssignmentTerm {
    public IAssignableValue LeftValue { get; set; }
    public AbstractValue RightValue { get; set; }
    public ImmutableArray<GenericIndex> SelectedRange { get; set; }

    public IAssignableValue UntypedLeftValue => (IAssignableValue)LeftValue;

    public PrimaryAssignment(IAssignableValue leftValue, AbstractValue rightValue, ReadOnlySpan<GenericIndex> range, nint returnAddress) : base(returnAddress) {
        LeftValue = leftValue;
        RightValue = rightValue;
        SelectedRange = range.ToImmutableArray();
    }

    public BehaviorDesc ToBeahviorDesc(IAssignableValue? redirectedLeftValue) {
        if (redirectedLeftValue == null) return this;
        return new PrimaryAssignment(redirectedLeftValue, RightValue, SelectedRange.AsSpan(), nint.Zero);
    }
}

public class PrimaryCondEval : PrimaryOp {
    public AbstractValue Condition { get; }
    public CheckPoint<bool>? CheckPoint { get; set; }
    public PrimaryCondEval(nint returnAddress, AbstractValue condition) : base(returnAddress) {
        Condition = condition;
    }
}

public interface IBranchLikeDesc {
    AbstractValue? ConditionExpression { get; }
    List<BehaviorDesc> CurrentList { get; }
    CheckPoint<bool>? CheckPoint { get; }
    bool FindCandidateExit(Predicate<BehaviorDesc> predicate);
    bool NextState();
    IEnumerable<BehaviorDesc> UnwindBranch();
    IEnumerable<BehaviorDesc> GetSubNodes();
    IEnumerable<IEnumerable<BehaviorDesc>> GetBranches();
    void EnumerateDesc(Action<BehaviorDesc, List<IBranchLikeDesc>> callback, List<IBranchLikeDesc>? branchPath = null) {
        branchPath ??= new();

        branchPath.Add(this);

        foreach (var i in GetSubNodes()) {
            if (i is IBranchLikeDesc branch) branch.EnumerateDesc(callback, branchPath);
            else callback(i, branchPath);
        }

        branchPath.RemoveLast();
    }
}
public class SwitchDesc<TEnum>: SwitchDesc where TEnum : unmanaged, Enum {
    protected static Random m_RandomGenerator = new();
    public TEnum[] CandidateValues { get; }
    public override BigInteger this[int index] {
        get {
            return StaticEnum<TEnum>.ConvertEnumValue(CandidateValues[index]);
        }
    }
    public SwitchDesc(TEnum[] values, AbstractValue switchCond, nint returnAddress):base(values.Length + 1, switchCond, returnAddress) {
        var value = (ulong)m_RandomGenerator.NextInt64();
        var enumType = typeof(TEnum);

        while (values.Contains((TEnum)Enum.ToObject(enumType, value))) {
            value = (ulong)m_RandomGenerator.NextInt64();
        }

        CandidateValues = values.Append((TEnum)Enum.ToObject(enumType, value)).ToArray();
    }
}
public abstract class SwitchDesc: BehaviorDesc , IBranchLikeDesc {
    public AbstractValue SwitchValue { get; }
    public nint ReturnAddress { get; }

    public int CurrentListIndex { get; set; }
    public int LastCandidateListIndex { get; set; } = -1;
    public int CurrentExitIndex { get; set; } = -1;
    public CheckPoint<bool>? CheckPoint { get; set; }
    protected List<BehaviorDesc>[] m_BehaviorLists;
    protected bool[] m_IsFalsePath;

    public List<BehaviorDesc>[] BranchList => m_BehaviorLists;

    public abstract BigInteger this[int index] { get; }
    public List<BehaviorDesc> CurrentList => m_BehaviorLists[CurrentListIndex];

    public AbstractValue? ConditionExpression => SwitchValue;

    public SwitchDesc(int valueCount,AbstractValue switchCond, nint returnAddress) {
        SwitchValue = switchCond;
        ReturnAddress = returnAddress;
        m_BehaviorLists = new List<BehaviorDesc>[valueCount];
        m_IsFalsePath = new bool[valueCount];
        for(var i = 0; i < valueCount; i++) {
            m_BehaviorLists[i] = new();
        }
    }
    public bool FindCandidateExit(Predicate<BehaviorDesc> predicate) {
        var index = m_BehaviorLists[0].FindIndex(predicate);
        if (index < 0) return false;

        if(index < CurrentExitIndex) { // find overlapping
            return false;
        }
        if(index > CurrentExitIndex) {
            var falseMatchList = m_BehaviorLists[LastCandidateListIndex];
            m_IsFalsePath[LastCandidateListIndex] = true;

            Debug.Assert(falseMatchList.Count == 0);

            falseMatchList.Add(m_BehaviorLists[0][CurrentExitIndex]);
            CurrentExitIndex = index;
        }

        LastCandidateListIndex = CurrentListIndex;

        return true;
    }
    public bool NextState() {
        if (CurrentListIndex == m_BehaviorLists.Length - 1) return true;
        CurrentListIndex++;
        return false;
    }
   

    public IEnumerable<BehaviorDesc> UnwindBranch() {
        if(CurrentExitIndex == -1) {
            CurrentExitIndex = m_BehaviorLists[0].Count;
        }
        var result = m_BehaviorLists[0].GetRange(CurrentExitIndex, m_BehaviorLists[0].Count - CurrentExitIndex);
        m_BehaviorLists[0].RemoveRange(CurrentExitIndex, m_BehaviorLists[0].Count - CurrentExitIndex);

        for(var i=0;i< m_BehaviorLists.Length; i++) {
            if (m_IsFalsePath[i]) {
                var index = m_BehaviorLists[0].IndexOf(m_BehaviorLists[i][0]);
                for(var j=index+1;j< m_BehaviorLists[0].Count; j++) {
                    m_BehaviorLists[i].Add(m_BehaviorLists[0][j]);
                }
            }
        }

        result.Insert(0, this);
        return result;
    }

    public IEnumerable<BehaviorDesc> GetSubNodes() {
        return m_BehaviorLists.SelectMany(e => e);
    }

    public IEnumerable<IEnumerable<BehaviorDesc>> GetBranches() {
        return m_BehaviorLists;
    }
}

public class BranchDesc: BehaviorDesc, IBranchLikeDesc {
    public PrimaryCondEval Condition { get; }
    public int FirstMergeOpIndex { get; set; } = -1;
    public bool ProcessTrueBranch { get; set; } = true;
    public List<BehaviorDesc> CurrentList => ProcessTrueBranch ? m_TrueBranch : m_FalseBranch;
    public List<BehaviorDesc> BackList => (!ProcessTrueBranch) ? m_TrueBranch : m_FalseBranch;
    protected List<BehaviorDesc> m_TrueBranch = new();
    protected List<BehaviorDesc> m_FalseBranch = new();

    public List<BehaviorDesc> TrueBranch => m_TrueBranch;
    public List<BehaviorDesc> FalseBranch => m_FalseBranch;

    public CheckPoint<bool>? CheckPoint => Condition.CheckPoint;

    public AbstractValue? ConditionExpression => Condition?.Condition;

    public BranchDesc(PrimaryCondEval condition) {
        Condition = condition;
    }
    public IEnumerable<IEnumerable<BehaviorDesc>> GetBranches() {
        return new List<BehaviorDesc>[] { m_TrueBranch, m_FalseBranch};
    }

    public IEnumerable<BehaviorDesc> GetSubNodes() => TrueBranch.Concat(FalseBranch);
    public bool FindCandidateExit(Predicate<BehaviorDesc> predicate) {
        var index = BackList.FindIndex(predicate);
        if (index >= 0) {
            FirstMergeOpIndex = index;
            return true;
        }
        return false;
    }

    public bool NextState() {
        if (ProcessTrueBranch) {
            ProcessTrueBranch = false;
            return false;
        } else {
            return true;
        }
    }

    public IEnumerable<BehaviorDesc> UnwindBranch() {
        Condition.CheckPoint?.Dispose();

        if (FirstMergeOpIndex < 0)
            FirstMergeOpIndex = TrueBranch.Count;

        var postList = TrueBranch.GetRange(FirstMergeOpIndex, TrueBranch.Count - FirstMergeOpIndex);
        postList.Insert(0, this);

        TrueBranch.RemoveRange(FirstMergeOpIndex, TrueBranch.Count - FirstMergeOpIndex);

        return postList;
    }
}
public static class ListExtensions {
    public static T RemoveLast<T>(this List<T> list) {
        var last = list[list.Count - 1];
        list.RemoveAt(list.Count - 1);
        return last;
    }
}
public class BehaviorContext {
    protected List<IBranchLikeDesc> m_ActiveBranch = new();
    protected CheckPointRecorder m_Recorder;
    protected PrimaryExit m_EndOp;
    protected CheckPoint<bool>? m_EndCheckPoint;
    public bool IsInBranchContext => m_ActiveBranch.Count > 1;
    public IBranchLikeDesc Root => m_ActiveBranch[0];
    public BranchDesc TypedRoot => (BranchDesc)m_ActiveBranch[0];
    public IEnumerable<IBranchLikeDesc> BranchStack => m_ActiveBranch;
    public BehaviorContext(CheckPointRecorder recorder) {
        m_Recorder = recorder;
        m_EndOp = new();
        var dummyBranch = new BranchDesc(null!) {
            ProcessTrueBranch = false,
        };
        dummyBranch.TrueBranch.Add(m_EndOp);

        m_ActiveBranch.Add(dummyBranch);
    }
    
    public void ConstructionEnd() {
        if (m_EndCheckPoint == null) {
            m_EndCheckPoint = m_Recorder.MakeCheckPoint(false);
        }
        if (m_EndCheckPoint.Result) return;
        if (UnwindBranches(e => e == m_EndOp)) {
            ProcessBranchMerge();
        }
    }
    public bool UnwindBranches(Predicate<BehaviorDesc> predicate) {
        for(var i= m_ActiveBranch.Count - 1; i >= 0; i--) {
            var currentBranch = m_ActiveBranch[i];

            if (currentBranch.FindCandidateExit(predicate)) return true;
        }
        return false;
    }
    public TEnum NotifySwitchEnter<TEnum>(nint returnAddress, AbstractValue value) where TEnum:unmanaged, Enum{
        if (UnwindBranches(e => {
            if (e is SwitchDesc branch) {
                return branch.ReturnAddress == returnAddress && branch.SwitchValue.Equals(value);
            }
            return false;
        })) {
            ProcessBranchMerge();

            m_Recorder.RestoreCheckPoint(m_EndCheckPoint!, true);
            throw new UnreachableException();
        } else {
            var enumValues = Enum.GetValues<TEnum>();
            var branchDesc = new SwitchDesc<TEnum>(enumValues, value, returnAddress);
            m_ActiveBranch.Add(branchDesc);

            Interlocked.MemoryBarrier();

            var checkpoint = m_Recorder.MakeCheckPoint(true);

            branchDesc.CheckPoint = checkpoint;
            
            return branchDesc.CandidateValues[branchDesc.CurrentListIndex];
        }
    }
    public bool NotifyConditionEvaluation(nint returnAddress, AbstractValue condition) {
        if (UnwindBranches(e => {
            if (e is BranchDesc branch) {
                return branch.Condition.ReturnAddress == returnAddress && branch.Condition.Condition.Equals(condition);
            }
            return false;
        })) {
            ProcessBranchMerge();

            m_Recorder.RestoreCheckPoint(m_EndCheckPoint!, true);
            throw new UnreachableException();
        } else {
            var condDesc = new PrimaryCondEval(returnAddress, condition);
            var branchDesc = new BranchDesc(condDesc);
            m_ActiveBranch.Add(branchDesc);

            Interlocked.MemoryBarrier();

            var checkpoint = m_Recorder.MakeCheckPoint(true);

            condDesc.CheckPoint = checkpoint;

            return condDesc.CheckPoint.Result;
        }
    }
    protected void ProcessBranchMerge() {
        if (m_ActiveBranch.Count <= 1) return;

        var desc = m_ActiveBranch.Last();

        if (desc.NextState()) {
            m_ActiveBranch.RemoveAt(m_ActiveBranch.Count - 1);

            var currentContext = m_ActiveBranch.Last();

            currentContext.CurrentList.AddRange(desc.UnwindBranch());

            ProcessBranchMerge();
        } else {
            m_Recorder.RestoreCheckPoint(desc.CheckPoint!, false);

            throw new UnreachableException("What?");
        }
    }
    public void NotifyAssignment(nint returnAddress, IAssignableValue leftValue, AbstractValue rightValue, ReadOnlySpan<GenericIndex> range) {
        var immRange = range.ToImmutableArray();
        if (UnwindBranches(e => {
            if (e is PrimaryAssignment assignment) {
                return assignment.ReturnAddress == returnAddress && 
                    assignment.LeftValue == leftValue &&
                    assignment.RightValue.Equals(rightValue) &&
                    assignment.SelectedRange.SequenceEqual(immRange);
            }
            return false;
        })) {
            ProcessBranchMerge();
        } else {
            m_ActiveBranch.Last().CurrentList.Add(new PrimaryAssignment(leftValue, rightValue, range, returnAddress));
        }
    }
}
public unsafe class ReturnAddressTracker {
    protected static delegate* unmanaged[Cdecl]<nint, int, nint> m_FindCodeRange;
    static ReturnAddressTracker() {
        var clrPdbFile = IntelliVerilogLocator.GetService<IRuntimeDebugInfoService>()!;
        m_FindCodeRange = (delegate* unmanaged[Cdecl]<nint, int, nint>)clrPdbFile.FindGlobalFunctionEntry("ExecutionManager::FindCodeRange");
    }
    protected bool IsManagedCode(nint codePointer) {
        return m_FindCodeRange(codePointer, 0) != nint.Zero;
    }
    public nint TrackReturnAddress(object thisObject, int paramIndex = 1, int limit = 16384) {
        var stackPointerValue = stackalloc nint[1];
        var stackPointer = (nint)stackPointerValue;
        for (var i = stackPointer;i < stackPointer + limit; i += nint.Size) {
            var slotData = *(nint*)i;
            if (!IsManagedCode(slotData)) continue;

            var rcxSlot = i + paramIndex * nint.Size;
            ref var objectRef = ref Unsafe.AsRef<object>((void*)rcxSlot);
            if (objectRef == thisObject) return slotData;
        }
        throw new NotImplementedException();
    }
    public nint TrackReturnAddressValueType(nint firstArgument, int paramIndex = 1, int limit = 16384) {
        var stackPointerValue = stackalloc nint[1];
        var stackPointer = (nint)stackPointerValue;
        for (var i = stackPointer; i < stackPointer + limit; i += nint.Size) {
            var slotData = *(nint*)i;
            if (!IsManagedCode(slotData)) continue;

            var rcxSlot = i + paramIndex * nint.Size;
            //ref var objectRef = ref Unsafe.AsRef<T>(*(void**)rcxSlot);
            if (*(nint*)rcxSlot == firstArgument) return slotData;
        }
        throw new NotImplementedException();
    }
}

public class GeneratedModule {
    public CodeGenConfiguration Configuration { get; }
    public Type ModuleType { get; }
    public ComponentModel Model { get; }
    public string TypeName => ModuleType.Name;
    public string Code { get; }
    public GeneratedModule(CodeGenConfiguration configuration, Components.Module module, string code) {
        Configuration = configuration;
        ModuleType = module.GetType();
        Model = module.InternalModel;
        Code = code;
    }
}
public class IvDefaultCodeGenerator<TConfiguration> where TConfiguration:CodeGenConfiguration,ICodeGenConfiguration<TConfiguration>,new() {
    protected Dictionary<ComponentModel, Components.Module> m_ModelCache = new();
    protected Queue<Components.Module> m_GenerationQueue = new();
    protected ICodeGenBackend<TConfiguration> m_CodeGen;
    protected TConfiguration m_Configuration;
    public IvDefaultCodeGenerator(TConfiguration? configuration = null) {
        m_Configuration = configuration ?? new();
        m_CodeGen = TConfiguration.CreateBackend();
    }
    public IEnumerable<GeneratedModule> GenerateModule(Components.Module top) {
        var models = new List<GeneratedModule>();

        m_GenerationQueue.Enqueue(top);
        m_ModelCache.Add(top.InternalModel, top);


        while (m_GenerationQueue.Count != 0) {
            var currentModule = m_GenerationQueue.Dequeue();

            var code = m_CodeGen.GenerateModuleCode(currentModule, m_Configuration);
            models.Add(new(m_Configuration,currentModule, code));

            foreach (var i in currentModule.InternalModel.OverlappedObjects) {
                if (!(i.Value is SubComponentDesc subComponentInstGroup)) continue;
                foreach (var j in subComponentInstGroup) {
                    if (j is Components.Module subModule) {
                        if (!m_ModelCache.ContainsKey(j.InternalModel)) {
                            m_ModelCache.Add(j.InternalModel, subModule);

                            m_GenerationQueue.Enqueue(subModule);
                        }
                    }
                }
            }
        }

        return models;
    }
}

public static class GenerationHelpers { 
    public static void WriteGroupedCodeFiles(IEnumerable<GeneratedModule> modules, string outputDir) {
        var fullDirectory = Path.GetFullPath(outputDir);

        if(!Directory.Exists(fullDirectory)) Directory.CreateDirectory(fullDirectory);

        var groups = modules.GroupBy(e => {
            if (e.ModuleType.IsConstructedGenericType) return e.ModuleType.GetGenericTypeDefinition();
            return e.ModuleType;
        });

        foreach (var i in groups) {
            var fileName = ReflectionHelpers.DenseTypeName(i.Key);
            var firstModule = i.First();

            var filePath = Path.Join(fullDirectory, $"{fileName}{firstModule.Configuration.ExtensionName}");

            if (File.Exists(filePath)) File.Delete(filePath);

            IvLogger.Default.Info("CodeWrite", $"Write code file {filePath}");

            foreach(var j in i) {
                File.AppendAllText(filePath, j.Code);
            }
        }
    }
}

public struct GenericIndex {
    public SliceIndexType Type => ConstIndex.IndexType;
    public SliceIndex ConstIndex { get; }
    public AbstractValue? VariableIndex { get; }

    public GenericIndex(SliceIndex index) {
        Debug.Assert(index.IndexType != SliceIndexType.Variable);
        ConstIndex = index;
    }
    public GenericIndex(AbstractValue index) {
        ConstIndex = new(0, 0, type: SliceIndexType.Variable);
        VariableIndex = index;
    }
    public GenericIndex(Range range) : this((SliceIndex)range) { }
    public GenericIndex(int index) : this((SliceIndex)index) { }
    public static implicit operator GenericIndex(SliceIndex range) => new(range);

    public static implicit operator GenericIndex(Range range) => new(range);
    public static implicit operator GenericIndex(int index) => new(index);
    public static implicit operator GenericIndex(uint index) => new((int)index);

    /// <summary>
    /// Erase variable index infomation and assume accessed by index 0
    /// </summary>
    /// <returns></returns>
    public SliceIndex ToErased(bool throwOnVariable = false) {
        return Type != SliceIndexType.Variable ? ConstIndex : 
            (throwOnVariable ? throw new ArgumentException("Variable index not allowed here") : 0);
    }
}
public struct ImmutableBitset<TNumber> where TNumber : unmanaged, IUnsignedNumber<TNumber>, IShiftOperators<TNumber, int, TNumber>, IBitwiseOperators<TNumber, TNumber, TNumber> {
    private Bitset<TNumber> m_Bitset;
    public bool IsAllZero => m_Bitset.IsAllZero;
    public int Length => m_Bitset.Length;
    public ImmutableBitset(ReadOnlySpan<bool> values) {
        m_Bitset = new(values);
    }
    public ImmutableBitset(Bitset<TNumber> values) {
        m_Bitset = (values);
    }
    public bool this[int index] {
        get => m_Bitset[index];
    }
    public override string ToString() {
        return m_Bitset.ToString();
    }
    public bool IsSubsetOf(ImmutableBitset<TNumber> bitset)
        => m_Bitset.IsSubsetOf(bitset.m_Bitset);
}
public struct Bitset<TNumber> where TNumber : unmanaged, IUnsignedNumber<TNumber>, IShiftOperators<TNumber, int, TNumber>, IBitwiseOperators<TNumber, TNumber, TNumber> {
    private TNumber m_Value;
    public int Length { get; }
    public bool IsAllZero => m_Value == TNumber.Zero;
    public Bitset(int length) {
        m_Value = TNumber.Zero;
        Length = length;
    }
    public Bitset(ReadOnlySpan<bool> values) {
        m_Value = TNumber.Zero;
        for(var i = 0; i < values.Length; i++) {
            m_Value |= (values[i] ? TNumber.One : TNumber.Zero) << i;
        }
        Length = values.Length;
    }
    public bool this[int index] {
        get => ((m_Value >> index) & TNumber.One) != TNumber.Zero;
        set {
            var mask = TNumber.One << index;
            var val = (value ? TNumber.One : TNumber.Zero) << index;
            m_Value = (m_Value & ~mask) | val;
        }
    }
    public bool IsSubsetOf(Bitset<TNumber> bitset) {
        return (bitset.m_Value & m_Value) == m_Value;
    }
    public override string ToString() {
        var sb = new StringBuilder();
        var full = TNumber.Zero - TNumber.One;
        var value = m_Value;
        for(; full != TNumber.Zero; full >>= 1, value >>= 1) {
            sb.Append((value & TNumber.One) != TNumber.Zero ? '1' : '0');
        }
        return sb.ToString();
    }
}
public struct Lazy<T> {
    private Func<T>? m_EvalFunc;
    private T? m_Value;
    public bool IsNil => m_Value is null && m_EvalFunc is null;
    public T Value {
        get {
            if(m_EvalFunc is not null) {
                Debugger.NotifyOfCrossThreadDependency();
                m_Value = m_EvalFunc();
                m_EvalFunc = null;
            }
            return m_Value!;
        }
    }
    public bool IsEvaluated => m_EvalFunc is null;
    public Lazy(Func<T> evalFunc) => m_EvalFunc = evalFunc;
    public Lazy(T value) => m_Value = value;
    public static implicit operator Lazy<T>(T value) => new(value);
}
public unsafe static class App {

    public static void Main() {

        Debugger.Break();

        IntelliVerilogLocator.RegisterService(ManagedDebugInfoService.Instance);
        IntelliVerilogLocator.RegisterService<IDemangleSerivce>(CxxDemangle.Instance);
        
        var debugInfoLocator = new RuntimeInspectionDebugInfoLocator();
        var msvcrt = Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Where(e => e.ModuleName.StartsWith("msvcrt")).First();
        var coreclr = Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Where(e => e.ModuleName.StartsWith("coreclr")).First();
        var clrjit = Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Where(e => e.ModuleName.StartsWith("clrjit")).First();

        var msvcPdb = debugInfoLocator.GetDebugInfoFile(msvcrt);
        var coreclrPdb = debugInfoLocator.GetDebugInfoFile(coreclr);
        var clrjitPdb = debugInfoLocator.GetDebugInfoFile(clrjit);
        var clrPdbFile = new PdbLookupScope(new("coreclr", coreclrPdb), new("clrjit", clrjitPdb), new("msvcrt", msvcPdb));
        IntelliVerilogLocator.RegisterService<IRuntimeDebugInfoService>(clrPdbFile);
        IntelliVerilogLocator.RegisterService(new AnalysisContext());
        IntelliVerilogLocator.RegisterService(new ReturnAddressTracker());


        GCHelpers.Initialize();

        RuntimeInjection.AddCompileCallback(new IoBundleCompiler());
        RuntimeInjection.AddCompileCallback(new ModuleCompiler());
        RuntimeInjection.HookEnable();

        IntelliVerilogLocator.RegisterService(new ShapeContext());

        var dummyClock = new DummyClockReset();
        var dummyReset = new DummyClockReset();
        var clkDomain = new ClockDomain("def", dummyClock) { 
            Reset = dummyReset
        };

        var generator = new IvDefaultCodeGenerator<VerilogGenerationConfiguration>();

        using (ClockArea.Begin(clkDomain)) {
            var adder = new FullAdder();

            var modules = generator.GenerateModule(adder);

            GenerationHelpers.WriteGroupedCodeFiles(modules, "./output");
        }
        Console.ReadLine();

    }
    
}