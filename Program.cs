using Iced.Intel;
using IntelliVerilog.Core.Analysis;
using IntelliVerilog.Core.CodeGen.Verilog;
using IntelliVerilog.Core.Components;
using IntelliVerilog.Core.DataTypes;
using IntelliVerilog.Core.Examples;
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

public class BranchDesc: BehaviorDesc {
    public PrimaryCondEval Condition { get; }
    public int FirstMergeOpIndex { get; set; } = -1;
    public bool ProcessTrueBranch { get; set; } = true;
    public List<BehaviorDesc> CurrentList => ProcessTrueBranch ? m_TrueBranch : m_FalseBranch;
    public List<BehaviorDesc> BackList => (!ProcessTrueBranch) ? m_TrueBranch : m_FalseBranch;
    protected List<BehaviorDesc> m_TrueBranch = new();
    protected List<BehaviorDesc> m_FalseBranch = new();

    public List<BehaviorDesc> TrueBranch => m_TrueBranch;
    public List<BehaviorDesc> FalseBranch => m_FalseBranch;
    public BranchDesc(PrimaryCondEval condition) {
        Condition = condition;
    }
    public void EnumerateDesc(Action<BehaviorDesc, List<BranchDesc>> callback, List<BranchDesc>? branchPath = null) {
        branchPath ??= new();

        if(Condition != null) branchPath.Add(this);

        foreach (var i in TrueBranch.Concat(FalseBranch)) {
            if (i is BranchDesc branch) branch.EnumerateDesc(callback, branchPath);
            else callback(i, branchPath);
        }

        if (Condition != null) branchPath.RemoveLast();
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
    
    protected List<BranchDesc> m_ActiveBranch = new();
    protected CheckPointRecorder m_Recorder;
    protected PrimaryExit m_EndOp;
    public bool IsInBranchContext => m_ActiveBranch.Count > 1;
    public BranchDesc Root => m_ActiveBranch[0];
    public IEnumerable<BranchDesc> BranchStack => m_ActiveBranch;
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
        if(UnwindBranches(e => e == m_EndOp)) {
            ProcessBranchMerge();
        }
    }
    public bool UnwindBranches(Predicate<BehaviorDesc> predicate) {
        for(var i= m_ActiveBranch.Count - 1; i >= 0; i--) {
            var currentBranch = m_ActiveBranch[i];

            var index = currentBranch.BackList.FindIndex(predicate);
            if(index >= 0) {
                currentBranch.FirstMergeOpIndex = index;
                return true;
            }
        }
        return false;
    }
    public bool NotifyConditionEvaluation(nint returnAddress, AbstractValue condition) {
        if (UnwindBranches(e => {
            if (e is BranchDesc branch) {
                return branch.Condition.ReturnAddress == returnAddress && branch.Condition.Condition.Equals(condition);
            }
            return false;
        })) {
            ProcessBranchMerge();

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

        if (desc.ProcessTrueBranch) {
            desc.ProcessTrueBranch = false;
            m_Recorder.RestoreCheckPoint(desc.Condition.CheckPoint!, false);

            throw new UnreachableException("What?");
        } else {
            m_ActiveBranch.RemoveAt(m_ActiveBranch.Count - 1);
            desc.Condition.CheckPoint!.Dispose();

            var currentContext = m_ActiveBranch.Last();

            if (desc.FirstMergeOpIndex < 0)
                desc.FirstMergeOpIndex = desc.TrueBranch.Count;

            currentContext.CurrentList.Add(desc);
            for (var i = desc.FirstMergeOpIndex; i < desc.TrueBranch.Count; i++) {
                currentContext.CurrentList.Add(desc.TrueBranch[i]);
            }
            desc.TrueBranch.RemoveRange(desc.FirstMergeOpIndex, desc.TrueBranch.Count - desc.FirstMergeOpIndex);

            ProcessBranchMerge();
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
public class CA {
    public virtual void SetV(int x) {
        var rat = IntelliVerilogLocator.GetService<ReturnAddressTracker>();

        var ra = rat.TrackReturnAddress(this);

        Console.WriteLine(ra);
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

        var clkDom2 = new ClockDomain("ext", dummyClock) { 
            Reset = dummyReset
        };

        using (ClockArea.Begin(clkDomain)) {
            var adder = new DFF(3, clkDom2);

            var codeGen = new VerilogGenerator();
            var generatedModel = new Dictionary<ComponentModel, Components.Module>();
            var generationQueue = new Queue<Components.Module>();

            generationQueue.Enqueue(adder);
            generatedModel.Add(adder.InternalModel, adder);


            while (generationQueue.Count != 0) {
                var currentModule = generationQueue.Dequeue();

                var code = codeGen.GenerateModuleCode(currentModule);

                Console.WriteLine(code);

                foreach (var i in currentModule.InternalModel.SubComponents) {
                    foreach (var j in i.Value) {
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