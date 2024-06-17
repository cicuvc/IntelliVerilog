using Iced.Intel;
using IntelliVerilog.Core.Runtime.Services;
using IntelliVerilog.Core.Runtime.Unsafe;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.Runtime.Core {
    public struct CheckPointData {
        public nint RBX; // 0
        public nint RBP; // 8
        public nint RDI; // 16
        public nint RSI; // 24
        public nint RSP; // 32
        public nint R12; // 40
        public nint R13; // 48
        public nint R14; // 56
        public nint R15; // 64
        public nint RIP; // 72
        public nint RAX; // 80
    }
    public class CheckPoint<T>:IDisposable {
        private bool m_DisposedValue;
        public bool Initialized { get; set; } = false;
        public T? Result { get; set; }
        public CheckPointRecorder Recorder { get; }
        public CheckPointData RegisterData { get; set; }
        public MemoryRegion<byte> StackStorage { get; set; }
        public MemoryRegion<byte> PinMask { get; set; }
        public ulong RegisterPinMask { get; set; }
        public CheckPoint(CheckPointRecorder recorder) {
            Recorder = recorder;
        }
        public void RestoreCheckPoint(T value) {
            Recorder.RestoreCheckPoint(this, value);
        }

        protected virtual void Dispose(bool disposing) {
            if (!m_DisposedValue) {
                if (disposing) {}

                MemoryAPI.API.Free(StackStorage);
                MemoryAPI.API.Free(PinMask);
                m_DisposedValue = true;
            }
        }

        ~CheckPoint() {
            Dispose(disposing: false);
        }

        public void Dispose() {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
    public unsafe class CheckPointRecorder {
        protected nint m_StackBase;

        protected static object m_SyncRoot = new();
        protected static delegate*<CheckPointData*, nint> m_CaptureContext;
        protected static delegate*<CheckPointData*, void> m_RestoreContext;
        protected static nint[] m_ModuleVtables;
        protected static uint m_PtrModuleFieldOffset;
        public static void EnterRecordEnvironment(Action<CheckPointRecorder> captureContext) {
            var stackBase = stackalloc nint[1];
            captureContext(new((nint)stackBase));
        }
        public static CheckPointRecorder CreateRecorder(nint stackBase) {
            return new(stackBase);
        }
        private struct MemoryBasicInformation {
            public nint BaseAddress;
            public nint AllocationBase;
            public uint AllocationProtect;
            public short PartitionId;
            public ulong RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }
        [DllImport("kernel32")]
        private extern static nint VirtualQuery(nint address, out MemoryBasicInformation mi, int size);
        private static bool IsAcceessibleMemory(nint address) {
            var e = address / 4096 * 4096;
            VirtualQuery(e, out var mi, sizeof(MemoryBasicInformation));
            return mi.State == 0x1000;
        }
        static CheckPointRecorder() {
            var debugInfo = IntelliVerilogLocator.GetService<IRuntimeDebugInfoService>();
            Debug.Assert(debugInfo != null);

            m_ModuleVtables = new nint[] {
                debugInfo.FindGlobalVariableAddress("Module::`vftable'"),
                debugInfo.FindGlobalVariableAddress("EditAndContinueModule::`vftable'")
            };
            var methodTableType = debugInfo.FindType("MethodTable")!;
            Debug.Assert(methodTableType.GetFieldOffset("m_pLoaderModule", out m_PtrModuleFieldOffset));


            var assembler = new Assembler(64);
            assembler.sub(AssemblerRegisters.rsp, 8);
            assembler.mov(AssemblerRegisters.rax, AssemblerRegisters.__qword_ptr[AssemblerRegisters.rsp + 8]);
            assembler.mov(AssemblerRegisters.__qword_ptr[AssemblerRegisters.rcx + 72], AssemblerRegisters.rax); // rip

            assembler.mov(AssemblerRegisters.__qword_ptr[AssemblerRegisters.rcx], AssemblerRegisters.rbx);
            assembler.mov(AssemblerRegisters.__qword_ptr[AssemblerRegisters.rcx + 8], AssemblerRegisters.rbp);
            assembler.mov(AssemblerRegisters.__qword_ptr[AssemblerRegisters.rcx + 16], AssemblerRegisters.rdi);
            assembler.mov(AssemblerRegisters.__qword_ptr[AssemblerRegisters.rcx + 24], AssemblerRegisters.rsi);
            assembler.mov(AssemblerRegisters.__qword_ptr[AssemblerRegisters.rcx + 32], AssemblerRegisters.rsp);
            assembler.mov(AssemblerRegisters.__qword_ptr[AssemblerRegisters.rcx + 40], AssemblerRegisters.r12);
            assembler.mov(AssemblerRegisters.__qword_ptr[AssemblerRegisters.rcx + 48], AssemblerRegisters.r13);
            assembler.mov(AssemblerRegisters.__qword_ptr[AssemblerRegisters.rcx + 56], AssemblerRegisters.r14);
            assembler.mov(AssemblerRegisters.__qword_ptr[AssemblerRegisters.rcx + 64], AssemblerRegisters.r15);

            assembler.xchg(AssemblerRegisters.__qword_ptr[AssemblerRegisters.rcx + 80], AssemblerRegisters.rax);

            assembler.add(AssemblerRegisters.rsp, 8);

            assembler.ret();

            var captureContextCode = new byte[256];
            using var ms = new MemoryStream(captureContextCode);
            assembler.Assemble(new StreamCodeWriter(ms), 0);



            assembler = new Assembler(64);

            assembler.sub(AssemblerRegisters.rsp, 8);

            assembler.mov(AssemblerRegisters.rbx, AssemblerRegisters.__qword_ptr[AssemblerRegisters.rcx]);
            assembler.mov(AssemblerRegisters.rbp, AssemblerRegisters.__qword_ptr[AssemblerRegisters.rcx + 8]);
            assembler.mov(AssemblerRegisters.rdi, AssemblerRegisters.__qword_ptr[AssemblerRegisters.rcx + 16]);
            assembler.mov(AssemblerRegisters.rsi, AssemblerRegisters.__qword_ptr[AssemblerRegisters.rcx + 24]);
            assembler.mov(AssemblerRegisters.rsp, AssemblerRegisters.__qword_ptr[AssemblerRegisters.rcx + 32]);
            assembler.mov(AssemblerRegisters.r12, AssemblerRegisters.__qword_ptr[AssemblerRegisters.rcx + 40]);
            assembler.mov(AssemblerRegisters.r13, AssemblerRegisters.__qword_ptr[AssemblerRegisters.rcx + 48]);
            assembler.mov(AssemblerRegisters.r14, AssemblerRegisters.__qword_ptr[AssemblerRegisters.rcx + 56]);
            assembler.mov(AssemblerRegisters.r15, AssemblerRegisters.__qword_ptr[AssemblerRegisters.rcx + 64]);

            assembler.mov(AssemblerRegisters.rax, AssemblerRegisters.__qword_ptr[AssemblerRegisters.rcx + 72]); // rip
            assembler.mov(AssemblerRegisters.__qword_ptr[AssemblerRegisters.rsp + 8], AssemblerRegisters.rax);

            assembler.mov(AssemblerRegisters.rax, AssemblerRegisters.__qword_ptr[AssemblerRegisters.rcx + 80]);

            assembler.add(AssemblerRegisters.rsp, 8);

            assembler.ret();

            var restoreCode = new byte[256];
            using var restoreMs = new MemoryStream(restoreCode);
            assembler.Assemble(new StreamCodeWriter(restoreMs), 0);



            var codeMemory = MemoryAPI.API.AllocAlign(4096, 4096);
            MemoryAPI.API.SetProtection(codeMemory, ProtectMode.Read | ProtectMode.Write | ProtectMode.Execute);

            fixed (byte* pCaptureContextCode = captureContextCode)
                Buffer.MemoryCopy(pCaptureContextCode, codeMemory.Address + 0, 256, 256);
            fixed (byte* pRestoreContextCode = restoreCode)
                Buffer.MemoryCopy(pRestoreContextCode, codeMemory.Address + 256, 256, 256);

            m_CaptureContext = (delegate*<CheckPointData*, nint>)codeMemory.Address;
            m_RestoreContext = (delegate*<CheckPointData*, void>)(codeMemory.Address + 256);
        }
        protected CheckPointRecorder(nint stackBase) {
            m_StackBase = stackBase;
        }
        public CheckPoint<T> MakeCheckPoint<T>(T defaultReturnValue) {
            var checkPoint = new CheckPoint<T>(this) { Result = defaultReturnValue };
            var registerData = new CheckPointData();

            m_CaptureContext(&registerData);
            if (checkPoint.Initialized) {
                var gcHelper = IntelliVerilogLocator.GetService<IGCHelpers>()!;
                gcHelper.ResumeCollector();

                return checkPoint;
            }

            lock (m_SyncRoot) {
                checkPoint.Initialized = true;

                var gcHelper = IntelliVerilogLocator.GetService<IGCHelpers>()!;

                gcHelper.PauseCollector();

                var currentStackBase = registerData.RSP;
                var stackStorageSize = m_StackBase - currentStackBase;
                var stackStorage = MemoryAPI.API.Alloc((uint)stackStorageSize);
                var pinStorage = MemoryAPI.API.Alloc((uint)(stackStorageSize / nint.Size));
                Buffer.MemoryCopy((void*)currentStackBase, stackStorage.Address, stackStorageSize, stackStorageSize);

                var stackStorageView = stackStorage.AsRegion<nint>();
                for (var i = 0u; i < stackStorageView.ElementLength; i++) {
                    var address = stackStorageView[i];
                    pinStorage[i] = (byte)((CheckAndPinObject(gcHelper, address, true)) ? 1 : 0);
                }
                for (var i = 0; i < sizeof(CheckPointData) / nint.Size; i++) {
                    var address = ((nint*)&registerData)[i];
                    if (CheckAndPinObject(gcHelper, address, true)) {
                        checkPoint.RegisterPinMask |= 1ul << (i);
                    }
                }

                checkPoint.PinMask = pinStorage;
                checkPoint.StackStorage = stackStorage;
                checkPoint.RegisterData = registerData;

                gcHelper.ResumeCollector();
            }
            return checkPoint;
        }
        internal void RestoreCheckPoint<T>(CheckPoint<T> checkPoint, T returnValue) {
            var currentStackPointer = stackalloc nint[1];
            if (currentStackPointer + 64 > (nint*)checkPoint.RegisterData.RSP) {
                RestoreCheckPoint(checkPoint, returnValue);
                return;
            }

            lock (m_SyncRoot) {
                var gcHelper = IntelliVerilogLocator.GetService<IGCHelpers>()!;

                gcHelper.PauseCollector();

                var stackStorageView = checkPoint.StackStorage.AsRegion<nint>();
                var registerData = checkPoint.RegisterData;
                var registerPinMask = checkPoint.RegisterPinMask;
                var pinMask = checkPoint.PinMask;
                checkPoint.Result = returnValue;

                var stackStorageSize = (int)checkPoint.StackStorage.ByteLength;
                Buffer.MemoryCopy(checkPoint.StackStorage.Address, (void*)checkPoint.RegisterData.RSP, stackStorageSize, stackStorageSize);



                for (var i = 0u; i < stackStorageView.ElementLength; i++) {
                    var address = stackStorageView[i];
                    if (pinMask[i] != 0) {
                        gcHelper.PinObjectUnsafeRaw(address, false);
                    }
                }
                for (var i = 0; i < sizeof(CheckPointData) / nint.Size; i++) {
                    var address = ((nint*)&registerData)[i];
                    if (((registerPinMask >> (i)) & 0x1) != 0) {
                        gcHelper.PinObjectUnsafeRaw(address, false);
                    }
                }



                m_RestoreContext(&registerData);
            }
        }
        protected static bool CheckAndPinObject(IGCHelpers gcHelper, nint address, bool isPinned = true) {
            if (gcHelper.IsHeapPointer(address)) {
                var pMT = *(nint*)address;

                if (!IsAcceessibleMemory(pMT)) return false;
                var module = *(nint*)(pMT + m_PtrModuleFieldOffset);
                if (!IsAcceessibleMemory(module)) return false;
                var vtb = *(nint*)module;

                if (m_ModuleVtables.Contains(vtb)) {
                    var obj = ObjectHelpers.GetObjectAtAddress(address);
                    gcHelper.PinObjectUnsafe(obj, isPinned);
                    return true;
                }
            }
            return false;
        }
    }
}
