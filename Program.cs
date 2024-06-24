using Iced.Intel;
using IntelliVerilog.Core.Analysis;
using IntelliVerilog.Core.CodeGen.Verilog;
using IntelliVerilog.Core.Components;
using IntelliVerilog.Core.DataTypes;
using IntelliVerilog.Core.Examples;
using IntelliVerilog.Core.Examples.TestSuites;
using IntelliVerilog.Core.Expressions;
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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

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
    public SpecifiedRange SelectedRange { get; set; }

    public IAssignableValue UntypedLeftValue => (IAssignableValue)LeftValue;

    public PrimaryAssignment(IAssignableValue leftValue, AbstractValue rightValue, SpecifiedRange range, nint returnAddress) : base(returnAddress) {
        LeftValue = leftValue;
        RightValue = rightValue;
        SelectedRange = range;
    }

    public BehaviorDesc ToBeahviorDesc(IAssignableValue? redirectedLeftValue) {
        if (redirectedLeftValue == null) return this;
        return new PrimaryAssignment(redirectedLeftValue, RightValue, SelectedRange, nint.Zero);
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

            m_Recorder.RestoreCheckPoint(m_EndCheckPoint, true);
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

            m_Recorder.RestoreCheckPoint(m_EndCheckPoint, true);
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
    public void NotifyAssignment(nint returnAddress, IAssignableValue leftValue, AbstractValue rightValue, SpecifiedRange range) {
        if (UnwindBranches(e => {
            if (e is PrimaryAssignment assignment) {
                return assignment.ReturnAddress == returnAddress && 
                    assignment.LeftValue == leftValue &&
                    assignment.RightValue.Equals(rightValue) &&
                    assignment.SelectedRange.Equals(range);
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

        var dummyClock = new DummyClockReset();
        var dummyReset = new DummyClockReset();
        var clkDomain = new ClockDomain("def", dummyClock) { 
            Reset = dummyReset
        };


        using (ClockArea.Begin(clkDomain)) {
            var adder = new TupleWholeAssignTest(3);

            var codeGen = new VerilogGenerator();
            var generatedModel = new Dictionary<ComponentModel, Components.Module>();
            var generationQueue = new Queue<Components.Module>();

            generationQueue.Enqueue(adder);
            generatedModel.Add(adder.InternalModel, adder);


            while (generationQueue.Count != 0) {
                var currentModule = generationQueue.Dequeue();

                var code = codeGen.GenerateModuleCode(currentModule);

                Console.WriteLine(code);

                foreach (var i in currentModule.InternalModel.OverlappedObjects) {
                    if (!(i.Value is SubComponentDesc subComponentInstGroup)) continue;
                    foreach (var j in subComponentInstGroup) {
                        if (j is Components.Module subModule) {
                            if (!generatedModel.ContainsKey(j.InternalModel)) {
                                generatedModel.Add(j.InternalModel, subModule);

                                generationQueue.Enqueue(subModule);
                            }
                        }
                    }
                }
            }
        }
        Console.ReadLine();
    
    }

}