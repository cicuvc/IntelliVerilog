using IntelliVerilog.Core.Runtime.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.Runtime.Core {
    public interface IGCHelpers {
        bool CollectorPaused { get; }
        bool CollectorDisabled { get; }
        void PauseCollector();
        void ResumeCollector();
        bool IsHeapPointer(nint pointer);
        void DisableCollector(int preAllocatedSize);
        void EnableCollector();
        
        void PinObjectUnsafe(object obj, bool isPinned);
        void PinObjectUnsafeRaw(nint obj, bool isPinned);
    }
    public interface IRuntimeHelpers {
        
    }
    public class GCHelpers {
        public static void Initialize() {
            if(Environment.Version.Major >= 5) {
                IntelliVerilogLocator.RegisterService<IGCHelpers>(new GCHelpersCore5Plus());
                return;
            }

            throw new NotImplementedException();
        }
    }
    public class GCHelpersCore5Plus : IGCHelpers {
        protected object m_SyncRoot = new();
        protected nint m_PtrGlobalGCHeap;
        protected nint m_PtrCollectorLock;


        protected uint m_VtblIsHeapPointerOffset;
        protected uint m_VtblTemporaryDisableConcurrentGC;
        protected uint m_VtblTemporaryEnableConcurrentGC;
        protected uint m_VtblIsConcurrentGCEnabled;
        protected uint m_VtblIsGCInProgressHelper;
        protected uint m_VtblWhichGeneration;
        protected uint m_VtblIsConcurrentGCInProgress;

        public bool CollectorPaused => m_PauseRequests > 0;

        public bool CollectorDisabled => m_DisableRequests > 0;

        protected uint m_PauseRequests = 0;
        protected uint m_DisableRequests = 0;
        protected bool m_BackgroundGCEnabled;

        public GCHelpersCore5Plus() {
            var inspectionLocator = IntelliVerilogLocator.GetService<IRuntimeDebugInfoService>();

            Debug.Assert(inspectionLocator != null);

            m_PtrGlobalGCHeap = inspectionLocator.FindGlobalVariableAddress("g_pGCHeap");
            m_PtrCollectorLock = inspectionLocator.FindGlobalVariableAddress(GCSettings.IsServerGC ? "WKS::gc_heap::gc_lock" : "SVR::gc_heap::gc_lock");

            var iGCHeapType = inspectionLocator.FindType("IGCHeap");

            Debug.Assert(iGCHeapType != null);
            Debug.Assert(iGCHeapType.GetVirtualMethodOffset("IsHeapPointer",out m_VtblIsHeapPointerOffset));
            Debug.Assert(iGCHeapType.GetVirtualMethodOffset("IsPromoted", out m_VtblWhichGeneration));
            Debug.Assert(iGCHeapType.GetVirtualMethodOffset("TemporaryDisableConcurrentGC", out m_VtblTemporaryDisableConcurrentGC));
            Debug.Assert(iGCHeapType.GetVirtualMethodOffset("TemporaryEnableConcurrentGC", out m_VtblTemporaryEnableConcurrentGC));
            Debug.Assert(iGCHeapType.GetVirtualMethodOffset("IsConcurrentGCEnabled", out m_VtblIsConcurrentGCEnabled));
            Debug.Assert(iGCHeapType.GetVirtualMethodOffset("IsGCInProgressHelper", out m_VtblIsGCInProgressHelper));
            Debug.Assert(iGCHeapType.GetVirtualMethodOffset("IsConcurrentGCInProgress", out m_VtblIsConcurrentGCInProgress));
        }
        public void DisableCollector(int preAllocatedSize) {
            lock (m_SyncRoot) {
                m_DisableRequests++;
                if (m_DisableRequests > 1) return;
                if (GCSettings.LatencyMode == GCLatencyMode.NoGCRegion) return;
                while (!GC.TryStartNoGCRegion(preAllocatedSize)) ;
            }
        }

        public void EnableCollector() {
            lock (m_SyncRoot) {
                m_DisableRequests--;

                if (m_DisableRequests > 0) return;
                if (GCSettings.LatencyMode != GCLatencyMode.NoGCRegion) {
                    Environment.FailFast("No-GC region exit unexpectedly. GC Heap may be corrupted");
                }
                GC.EndNoGCRegion();
            }
        }

        public unsafe bool IsHeapPointer(nint pointer) {
            var gcHeapPtr = *(nint*)m_PtrGlobalGCHeap;
            var vtblPtr = *(nint*)gcHeapPtr;
            var isHeapPointerFunc = ((nint*)vtblPtr)[m_VtblIsHeapPointerOffset / nint.Size];
            var isHeapPointer = (delegate* unmanaged[Cdecl]<nint, nint, bool, byte>)isHeapPointerFunc;

            var gen = (((delegate* unmanaged[Cdecl]<nint, nint, int>*)vtblPtr)[m_VtblWhichGeneration / nint.Size])(gcHeapPtr, pointer);
            return isHeapPointer(gcHeapPtr, pointer, false) != 0;
        }

        public unsafe void PauseCollector() {
            lock (m_SyncRoot) {
                m_PauseRequests++;
                if (m_PauseRequests > 1) return;

                var gcHeapPtr = *(nint*)m_PtrGlobalGCHeap;
                var vtblPtr = *(nint*)gcHeapPtr;

                m_BackgroundGCEnabled = (((delegate*unmanaged[Cdecl]<nint, byte>*)vtblPtr)[m_VtblIsConcurrentGCEnabled / nint.Size](gcHeapPtr) != 0);
                if (m_BackgroundGCEnabled) {
                    ((delegate*<nint, void>*)vtblPtr)[m_VtblTemporaryDisableConcurrentGC / nint.Size](gcHeapPtr);
                }

                var gcInProgressFunction = ((delegate* unmanaged[Cdecl]<nint, bool, byte>*)vtblPtr)[m_VtblIsGCInProgressHelper / nint.Size];
                var bgcInProgressFunction = ((delegate* unmanaged[Cdecl]<nint, byte>*)vtblPtr)[m_VtblIsConcurrentGCInProgress / nint.Size];
                
                while ((bgcInProgressFunction(gcHeapPtr) != 0) || (gcInProgressFunction(gcHeapPtr, true) != 0)) {
                }

                ref var lockRef = ref System.Runtime.CompilerServices.Unsafe.AsRef<int>((void*)m_PtrCollectorLock);
                while (Interlocked.Exchange(ref lockRef, 0) == -1) {
                }

               
            }
        }

        public unsafe void ResumeCollector() {
            lock (m_SyncRoot) {
                m_PauseRequests--;
                if (m_PauseRequests > 0) return;

                var gcHeapPtr = *(nint*)m_PtrGlobalGCHeap;
                var vtblPtr = *(nint*)gcHeapPtr;

                ref var lockRef = ref System.Runtime.CompilerServices.Unsafe.AsRef<int>((void*)m_PtrCollectorLock);
                Volatile.Write(ref lockRef, -1);

                if (m_BackgroundGCEnabled) {
                    ((delegate* unmanaged[Cdecl]<nint, void>*)vtblPtr)[m_VtblTemporaryEnableConcurrentGC / nint.Size](gcHeapPtr);
                }
            }
        }

        public unsafe void PinObjectUnsafe(object obj, bool isPinned) {
            lock (m_SyncRoot) {
                Debug.Assert(m_PauseRequests > 0);

                var objectPtr = (nint*)ObjectHelpers.GetObjectAddress(obj);
                if (isPinned) {
                    objectPtr[-1] |= 0x20000000;
                } else {
                    objectPtr[-1] &= ~(nint)0x20000000;
                }
            }
        }
        public unsafe void PinObjectUnsafeRaw(nint obj, bool isPinned) {
            lock (m_SyncRoot) {
                Debug.Assert(m_PauseRequests > 0);

                var objectPtr = (nint*)obj;
                if (isPinned) {
                    objectPtr[-1] |= 0x20000000;
                } else {
                    objectPtr[-1] &= ~(nint)0x20000000;
                }
            }
        }
    }
}
