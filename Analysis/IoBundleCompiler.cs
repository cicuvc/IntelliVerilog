using IntelliVerilog.Core.Components;
using IntelliVerilog.Core.Logging;
using IntelliVerilog.Core.Runtime.Core;
using IntelliVerilog.Core.Runtime.Injection;
using IntelliVerilog.Core.Utils;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.Analysis {
    public static class IoBundleRegistry {
        private static Dictionary<Type, IoBundleTypeCache> m_Caches = new();
        public static IoBundleTypeCache GetCacheForType(Type type) {
            if(m_Caches.TryGetValue(type, out var cache)) {
                return cache;
            }
            m_Caches.Add(type, cache = new(type));
            return cache;
        }
    }
    public class IoBundleTypeCache {
        public Type BundleType { get; }
        public Dictionary<MethodBase, PropertyInfo> PropertyGetters { get; } = new();
        public Dictionary<MethodBase, PropertyInfo> PropertySetters { get; } = new();
        public IoBundleTypeCache(Type type) {
            BundleType = type;

            var instance = IoComponentProbableHelpers.QueryProbeAuxiliary(type);

            var members = instance.GetIoMembers(type);
            foreach(var i in members) {
                if(i.Member is PropertyInfo property) {
                    var getter = property.GetGetMethod();
                    var setter = property.GetSetMethod();
                    if(getter == null || setter == null) {
                        throw new NotSupportedException();
                    }

                    PropertyGetters.Add(getter, property);
                    PropertySetters.Add(setter, property);
                }
            }
        }
    }
    public class IoBundleCompiler : IMethodCompiler {
        public unsafe int CompileMethod(object? context, MethodBase method, nint pthis, nint corinfo, JitCompileInfo* methodInfo, uint flags, nint* native, uint* size) {
            IvLogger.Default.Verbose("IoBundleCompiler", $"Transform bundle-like class {method.DeclaringType.Name}, method = {method.Name}");

            Debug.Assert(context != null);
            
            var property = (PropertyInfo)context;
            var proxy = new CorJitInfoProxy(corinfo);

            var getter = property.GetGetMethod()!;
            var setter = property.GetGetMethod()!;
            var isGetter = getter == method;

            var code = new ReadOnlySpan<byte>(methodInfo->ILCode, methodInfo->ILCodeLength);
            var editor = new ILEditor(code);

            var firstOpCode = editor[0];
            editor[0] = (ILOpCode.Br, editor.Count);

            var hookEnter = IoAccessHooks.NotifyIoBundlePropertyEnter;
            var hookExit = IoAccessHooks.NotifyIoBundlePropertyExit;
            var hookInvoked = IoAccessHooks.IsHookInvoked;

            var getReturnAddress = ReflectionHelpers.GetReturnAddress;
            

            editor.Emit(ILOpCode.Call, proxy.AllocateToken(hookInvoked.Method)); // [bool]
            editor.Emit(ILOpCode.Brfalse, editor.Count + 3); // if(!hookInvoked) {
            editor.Emit(firstOpCode.code, firstOpCode.operand);
            editor.Emit(ILOpCode.Br, 1);  // jump 1 

            editor.Emit(ILOpCode.Call, proxy.AllocateToken(hookEnter.Method));

            if (isGetter) {
                var notifyGetter = IoAccessHooks.NotifyIoBundleGet;
                editor.Emit(ILOpCode.Ldarg_0);
                editor.Emit(ILOpCode.Dup);
                editor.Emit(ILOpCode.Call, proxy.AllocateToken(getter));
                editor.Emit(ILOpCode.Call, proxy.AllocateToken(notifyGetter.Method));

                editor.Emit(ILOpCode.Call, proxy.AllocateToken(hookExit.Method));
                editor.Emit(ILOpCode.Ret);
            } else {
                var notifySetter = IoAccessHooks.NotifyIoBundleSet;

                editor.Emit(ILOpCode.Ldarg_0);
                editor.Emit(ILOpCode.Dup);
                editor.Emit(ILOpCode.Call, proxy.AllocateToken(getter));
                editor.Emit(ILOpCode.Ldarg_1);
                editor.Emit(ILOpCode.Call, proxy.AllocateToken(notifySetter.Method));
                editor.Emit(ILOpCode.Call, proxy.AllocateToken(hookExit.Method));

                editor.Emit(ILOpCode.Brtrue, editor.Count + 2);
                
                editor.Emit(ILOpCode.Ret);

                editor.Emit(firstOpCode.code, firstOpCode.operand);
                editor.Emit(ILOpCode.Br, 1);  // jump 1 
            }


            var buffer = editor.GenerateCode();

            fixed(byte* pBuffer = buffer) {
                var oldBuffer = methodInfo->ILCode;
                var oldLength = methodInfo->ILCodeLength;
                methodInfo->ILCode = pBuffer;
                methodInfo->ILCodeLength = buffer.Length;

                var result =  RuntimeInjection.CallRealCompiler(pthis, corinfo, methodInfo, flags, native, size, proxy);


                methodInfo->ILCode = oldBuffer;
                methodInfo->ILCodeLength = oldLength;

                return result;
            }

        }

        public bool Match(MethodBase method, nint methodInfo, out object? context) {
            if(method.DeclaringType?.IsSubclassOf(typeof(IoBundle)) ?? false) {
                var declType = method.DeclaringType!;

                var typeCache = IoBundleRegistry.GetCacheForType(declType);
                if (typeCache.PropertyGetters.ContainsKey(method)) {
                    context = typeCache.PropertyGetters[method];
                    return true;
                }
                if (typeCache.PropertySetters.ContainsKey(method)) {
                    context = typeCache.PropertySetters[method];
                    return true;
                }
            }
            context = null;
            return false;
        }
    }
}
