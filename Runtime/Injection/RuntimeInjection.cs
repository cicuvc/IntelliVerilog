using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection.PortableExecutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using IntelliVerilog.Core.Runtime.Unsafe;
using IntelliVerilog.Core.Runtime.Services;
using System.Threading;

namespace IntelliVerilog.Core.Runtime.Injection {
    public unsafe static class RuntimeInjection {
        private const string m_JitModuleName = "clrjit.dll";
        private const string m_JitInterfaceName = "getJit";

        [UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private unsafe delegate int CompileMethodCallback(IntPtr pthis, IntPtr corinfo, JitCompileInfo* info, uint flags, IntPtr* native, uint* size);
        private static CompileMethodCallback? m_RealCompileMethod;

        private static delegate*unmanaged[Cdecl]<nint, nint, JitCompileInfo*, uint, nint*, uint*, int> m_RawCompileMethod;

        public static ProcessModule m_JitModule;
        private readonly static IntPtr m_CorJitCompiler;
        private static List<IMethodCompiler> m_CompileCallbacks = new();
        private static object m_CompilationLock = new();

        [ThreadStatic]
        private static bool m_HookByPass = false;

        private static uint m_GetMethodClassOffset;

        private static int m_NoProxyProcessCount = 0;
        private static ProxyState m_ProxyState = ProxyState.Free;
        private enum ProxyState: int {
            Free = 0,
            Proxy = 1,
            NoProxy = 2
        }

        public static void AddCompileCallback(IMethodCompiler callback) {
            m_CompileCallbacks.Add(callback);
        }
        unsafe static RuntimeInjection() {
            m_JitModule = FindModule(m_JitModuleName);

            var getJitMethod = (delegate*<IntPtr>)NativeLibrary.GetExport(m_JitModule.BaseAddress, m_JitInterfaceName);
            m_CorJitCompiler = getJitMethod();
        }
        private static ProcessModule FindModule(string name) {
            return Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Where(e => e.ModuleName.StartsWith(name)).First();
        }
        public unsafe static int CallRealCompiler(IntPtr pthis, IntPtr corinfo, JitCompileInfo* methodInfo, uint flags, IntPtr* native, uint* size) {
            if (m_RawCompileMethod == null) Environment.FailFast("Jit compiler lost");

            while (true) {
                lock (m_CompilationLock) {
                    if(m_ProxyState == ProxyState.Free || m_ProxyState == ProxyState.NoProxy) {
                        m_NoProxyProcessCount++;
                        m_ProxyState = ProxyState.NoProxy;
                        break;
                    }
                }
            }

            var result = m_RawCompileMethod(pthis, corinfo, methodInfo, flags, native, size);

            lock (m_CompilationLock) {
                m_NoProxyProcessCount--;
                if(m_NoProxyProcessCount == 0 && m_ProxyState == ProxyState.NoProxy) {
                    m_ProxyState = ProxyState.Free;
                }
            }

            return result;
        }
        public static void DisableProxy() {
            lock (m_CompilationLock) {
                if (m_ProxyState == ProxyState.Proxy) {
                    m_ProxyState = ProxyState.Free;
                }
            }
        }
        public static void EnableProxy() {
            while (true) {
                lock (m_CompilationLock) {
                    if (m_ProxyState == ProxyState.Free) {
                        m_ProxyState = ProxyState.Proxy;
                        break;
                    }
                }
            }
        }
        public unsafe static int CallRealCompiler(IntPtr pthis, IntPtr corinfo, JitCompileInfo* methodInfo, uint flags, IntPtr* native, uint* size, CorJitInfoProxy proxy) {
            if (m_RawCompileMethod == null) Environment.FailFast("Jit compiler lost");
            //var lockRequired = CorJitInfoProxy.ProxyActivated;

            while (true) {
                lock (m_CompilationLock) {
                    if (m_ProxyState == ProxyState.Free) {
                        m_ProxyState = ProxyState.Proxy;
                        break;
                    }
                }
            }

            proxy.EnterProxy();

            var result = m_RawCompileMethod(pthis, corinfo, methodInfo, flags, native, size);

            proxy.ExitProxy();


            lock (m_CompilationLock) {
                m_ProxyState = ProxyState.Free;
            }


            return result;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private unsafe static int DummyCompileMethod(IntPtr pthis, IntPtr corinfo, JitCompileInfo* methodInfo, uint flags, IntPtr* native, uint* size) {
            return 0;
        }
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private unsafe static int CompileMethod(IntPtr pthis, IntPtr corinfo, JitCompileInfo* methodInfo, uint flags, IntPtr* native, uint* size) {
            if (m_HookByPass) {
                return CallRealCompiler(pthis, corinfo, methodInfo, flags, native, size);
            }
            m_HookByPass = true;

            var vtbl = *(nint*)corinfo;
            var getMethodClass = ((delegate* unmanaged[Cdecl]<nint, nint, nint>*)vtbl)[m_GetMethodClassOffset / nint.Size];
            var declTypeHandle = getMethodClass(corinfo,methodInfo->ftn);
            var method = MethodBase.GetMethodFromHandle(RuntimeMethodHandle.FromIntPtr(methodInfo->ftn), RuntimeTypeHandle.FromIntPtr(declTypeHandle));
            //Console.WriteLine(method.Name);

            if (method != null) {
                if(method.Name.Contains("Hello")) Debugger.Break();
                foreach (var i in m_CompileCallbacks) {
                    if (i.Match(method, (IntPtr)methodInfo, out var compilationContext)) {
                        var result = i.CompileMethod(compilationContext, method, pthis, corinfo, methodInfo, flags, native, size);
                        m_HookByPass = false;
                        return result;
                    }
                }
            }
            m_HookByPass = false;

            return CallRealCompiler(pthis, corinfo, methodInfo, flags, native, size);
        }
       
        public unsafe static void HookEnable() {
            var pdbFile = IntelliVerilogLocator.GetService<IRuntimeDebugInfoService>();
            Debug.Assert(pdbFile != null);
            Debug.Assert(CorJitInfoProxy.ProxyActivated == false);

            var corJitCompiler = pdbFile.FindType("ICorJitCompiler");
            var corInfo = pdbFile.FindType("ICorJitInfo");

            if (corJitCompiler == null || corInfo == null) throw new NotSupportedException();
            if (!corJitCompiler.GetVirtualMethodOffset("compileMethod", out var compileMethodOffset)) {
                throw new NotSupportedException();
            }
            var vtable = *(byte**)m_CorJitCompiler;
            var functionVptr = vtable + compileMethodOffset;
            var pageBase = (byte*)(((long)functionVptr) & 0xFFFFFFFFF000);
            MemoryAPI.API.SetProtection(new MemoryRegion<byte>(pageBase, 4096), ProtectMode.Write | ProtectMode.Read);

            // Prepare GC transition code for real compileMethod function
            m_RawCompileMethod = (delegate* unmanaged[Cdecl]<nint, nint, JitCompileInfo*, uint, nint*, uint*, int>)(&DummyCompileMethod);
            m_RawCompileMethod(0, 0, null, 0, null, null);

            m_RawCompileMethod = (delegate* unmanaged[Cdecl]<nint, nint, JitCompileInfo*, uint, nint*, uint*, int>)(*(nint*)functionVptr);
            //m_RealCompileMethod = Marshal.GetDelegateForFunctionPointer<CompileMethodCallback>(oldCompiler);

            foreach (var i in typeof(RuntimeInjection).GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)) {
                RuntimeHelpers.PrepareMethod(i.MethodHandle);
            }

            Debug.Assert(corInfo.GetVirtualMethodOffset("getMethodClass", out m_GetMethodClassOffset));

            *(IntPtr*)functionVptr = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, JitCompileInfo*, uint, IntPtr*, uint*, int>)&CompileMethod;
        }
    }
}
