using IntelliVerilog.Core.Components;
using IntelliVerilog.Core.Expressions;
using IntelliVerilog.Core.Logging;
using IntelliVerilog.Core.Runtime.Core;
using IntelliVerilog.Core.Runtime.Injection;
using IntelliVerilog.Core.Utils;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.Analysis {
    public class ModuleCompiler : IMethodCompiler {

        private void TrackTupleTypes(Type type, HashSet<Type> typeList) {
            foreach(var i in type.GetFields()) {
                if (i.FieldType.IsAssignableTo(typeof(ITuple))){
                    typeList.Add(i.FieldType);
                    TrackTupleTypes(i.FieldType, typeList);
                }
            }
        }
        private bool IsTupleIoLike(Type type) {
            if (!type.IsAssignableTo(typeof(ITuple)) && type.IsValueType) return false;
            foreach (var i in type.GetFields()) {
                if (!i.FieldType.IsAssignableTo(typeof(IUntypedPort))) {
                    if (!IsTupleIoLike(i.FieldType)) return false;
                }
            }
            return true;
        }
        private bool IsStoreLocal((ILOpCode opcode,long operand) code, out int index) {
            index = code.opcode switch {
                ILOpCode.Stloc_0 => 0,
                ILOpCode.Stloc_1 => 1,
                ILOpCode.Stloc_2 => 2,
                ILOpCode.Stloc_3 => 3,
                ILOpCode.Stloc_s => (int)code.operand,
                ILOpCode.Stloc => (int)code.operand,
                _ => -1
            };

            return index >= 0;
        }
        protected void FindMethodCallerInstruction(MethodBase callerMethod,MethodBase method) {
            var callerBody = callerMethod.GetMethodBody()!;
            var callerModule = callerMethod.Module;
            var ilEditor = new ILEditor(callerBody.GetILAsByteArray());

            foreach(var (code, operand) in ilEditor) {
                if(code == ILOpCode.Newobj) {
                    var calleeValidation = callerModule.ResolveMethod((int)operand);
                    if(calleeValidation.MetadataToken == method.MetadataToken) {

                    }
                }
            }
        }
        public unsafe int CompileMethod(object? context, MethodBase method, nint pthis, nint corinfo, JitCompileInfo* methodInfo, uint flags, nint* native, uint* size) {
            IvLogger.Default.Verbose("ModuleCompiler", $"Transform module-like class {method.DeclaringType.FullName}");
            
            var argumentList = method.GetParameters();

            methodInfo->MaxStack = 12;

            var proxy = new CorJitInfoProxy(corinfo);

            var code = new ReadOnlySpan<byte>(methodInfo->ILCode, methodInfo->ILCodeLength);
            var editor = new ILEditor(code);

            var ioPortTypes = new HashSet<Type>();
            var mainModule = method.Module;
            var moduleType = method.DeclaringType!;
            var useIoBundleMethod = typeof(Components.Module).GetMethod("UseIoPorts", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var useIoDefaultMethod = moduleType.GetMethod("UseDefaultIo", BindingFlags.NonPublic | BindingFlags.Instance)!;


            var startModuleConstruction = typeof(Components.Module).GetMethod("StartConstruction", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var endModuleConstruction = typeof(Components.Module).GetMethod("EndConstruction", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var moduleCtorExit = typeof(Components.Module).GetMethod("ModuleConstructorExit", BindingFlags.NonPublic | BindingFlags.Instance)!;

            var localVariableStoreHook = typeof(IoAccessHooks).GetMethod(nameof(IoAccessHooks.NotifyLocalVariableWrite), BindingFlags.Static | BindingFlags.Public);
            var localVariableRefStoreHook = typeof(IoAccessHooks).GetMethod(nameof(IoAccessHooks.NotifyLocalReferenceWrite), BindingFlags.Static | BindingFlags.Public);

            var codeBaseLength = editor.Count;
            var methodBody = method.GetMethodBody();
            for (var i = 0; i < codeBaseLength; i++) {
                var (opcode, operand) = editor[i];

                if (IsStoreLocal((opcode, operand), out var localIndex)){
                    var localInfo = methodBody.LocalVariables[localIndex];
                    var rawType = localInfo.LocalType.HasElementType ? localInfo.LocalType.GetElementType() : null;
                    if (localInfo.LocalType.IsSubclassOf(typeof(AbstractValue)) || 
                        localInfo.LocalType.IsSubclassOf(typeof(ComponentBase)) ||
                        (rawType?.IsSubclassOf(typeof(Wire)) ?? false) || (rawType?.IsSubclassOf(typeof(Reg)) ?? false)) {
                        editor[i] = (ILOpCode.Br, editor.Count);
                        
                        editor.Emit(ILOpCode.Ldloc, localIndex);
                        editor.Emit(ILOpCode.Ldc_i8, (long)method.MethodHandle.Value);
                        editor.Emit(ILOpCode.Ldc_i8, (long)method.DeclaringType!.TypeHandle.Value);
                        editor.Emit(ILOpCode.Ldc_i4, localIndex);

                        var hook = ((rawType?.IsSubclassOf(typeof(Wire)) ?? false) || (rawType?.IsSubclassOf(typeof(Reg)) ?? false)) ? localVariableRefStoreHook : localVariableStoreHook;


                        editor.Emit(ILOpCode.Call, proxy.AllocateToken(hook));

                        editor.Emit(opcode, operand); // store value

                        editor.Emit(ILOpCode.Br, i + 1);
                    }
                }
            }

            codeBaseLength = editor.Count;
            var nofityReferenceWrite = IoAccessHooks.NotifyReferenceWrite;
            for (var i = 0; i < codeBaseLength; i++) {
                var (opcode, operand) = editor[i];

                if (opcode == ILOpCode.Stind_ref) {
                    editor[i] = (ILOpCode.Br, editor.Count);

                    editor.Emit(ILOpCode.Ldarg_0);
                    editor.Emit(ILOpCode.Call, proxy.AllocateToken(nofityReferenceWrite.Method));

                    editor.Emit(ILOpCode.Br, i + 1);
                }
            }

            codeBaseLength = editor.Count;
            for (var i = 0;i< codeBaseLength; i++) {
                var (opcode, operand) = editor[i];

                if(opcode == ILOpCode.Ret) {
                    editor[i] = (ILOpCode.Br, editor.Count);
                    editor.Emit(ILOpCode.Ldarg_0);
                    editor.Emit(ILOpCode.Call, proxy.AllocateToken(endModuleConstruction));

                    editor.Emit(ILOpCode.Ldarg_0);
                    editor.Emit(ILOpCode.Call, proxy.AllocateToken(moduleCtorExit));
                    editor.Emit(ILOpCode.Ret);
                }else if(opcode == ILOpCode.Tail) {
                    throw new NotImplementedException("Got tail call");
                }
            }

            var typeGenerics = method.DeclaringType.IsConstructedGenericType ? method.DeclaringType.GetGenericArguments() : null;

            foreach (var i in editor) {
                var info = ILEditor.GetOpCodeInfo(i.code);
                var type = info.OperandType switch {
                    OperandType.InlineType => mainModule.ResolveType((int)i.operand, typeGenerics,null),
                    OperandType.InlineField => mainModule.ResolveField((int)i.operand, typeGenerics, null).DeclaringType,
                    _ => null
                } ;
                if (type != null) {
                    
                    if (type.IsByRef) type = type.GetElementType();
                    if (IsTupleIoLike(type)) {
                        if (!ioPortTypes.Contains(type)) {
                            ioPortTypes.Add(type);
                            TrackTupleTypes(type, ioPortTypes);
                            IvLogger.Default.Verbose("ModuleCompiler", $"Find IO type {ReflectionHelpers.PrettyTypeName(type)}");
                        }
                    }
                }
            }
            foreach(var i in methodBody.LocalVariables) {
                var type = i.LocalType;
                if (type.IsByRef) type = type.GetElementType();
                if (IsTupleIoLike(type)) {
                    if (!ioPortTypes.Contains(type)) {
                        ioPortTypes.Add(type);
                        TrackTupleTypes(type, ioPortTypes);
                        IvLogger.Default.Verbose("ModuleCompiler", $"Find IO type {ReflectionHelpers.PrettyTypeName(type)}");
                    }
                }
            }



            var tupleSetHook = IoAccessHooks.NotifyIoTupleSet;
            for (var i = 0; i < editor.Count; i++) {
                var (opcode, operand) = editor[i];
                if(opcode == ILOpCode.Stfld) {
                    var fieldRef = mainModule.ResolveField((int)operand, typeGenerics,null)!;
                    if (fieldRef.DeclaringType != null && ioPortTypes.Contains(fieldRef.DeclaringType)) {

                        editor[i] = (ILOpCode.Br, editor.Count);

                        editor.Emit(ILOpCode.Ldarg_0);
                        editor.Emit(ILOpCode.Ldc_i8, fieldRef.FieldHandle.Value);
                        editor.Emit(ILOpCode.Ldc_i8, fieldRef.DeclaringType.TypeHandle.Value);

                        editor.Emit(ILOpCode.Call, proxy.AllocateToken(tupleSetHook.Method));

                        editor.Emit(ILOpCode.Br, i + 1);
                    }
                }
            }

            var notifyReferenceValueTypeWrite = IoAccessHooks.NotifyReferenceValueTypeWrite;
            codeBaseLength = editor.Count;
            for (var i = 0; i < codeBaseLength; i++) {
                var (opcode, operand) = editor[i];

                if (opcode == ILOpCode.Stobj) {
                    var referenceType = mainModule.ResolveType((int)operand, typeGenerics, null);

                    if (referenceType != null && ioPortTypes.Contains(referenceType)) {

                        editor[i] = (ILOpCode.Br, editor.Count);

                        editor.Emit(ILOpCode.Box, proxy.AllocateToken(referenceType));
                        editor.Emit(ILOpCode.Ldarg_0);
                        editor.Emit(ILOpCode.Call, proxy.AllocateToken(notifyReferenceValueTypeWrite.Method));

                        editor.Emit(ILOpCode.Br, i + 1);
                    }
                }
            }

            var firstOpCode = editor[0];
            editor[0] = (ILOpCode.Br, editor.Count);

            editor.Emit(ILOpCode.Ldarg_0);
            
            editor.Emit(ILOpCode.Ldc_i4, argumentList.Length);
            editor.Emit(ILOpCode.Newarr, proxy.AllocateToken(typeof(object).MakeArrayType()));

            for (var i = 0; i < argumentList.Length; i++) {
                editor.Emit(ILOpCode.Dup);
                editor.Emit(ILOpCode.Ldc_i4, i);
                editor.Emit(ILOpCode.Ldarg, i + 1);
                if (argumentList[i].ParameterType.IsValueType) {
                    editor.Emit(ILOpCode.Box, proxy.AllocateToken(argumentList[i].ParameterType));
                }
                editor.Emit(ILOpCode.Stelem_ref);
            }


            var queryComponentCache = typeof(Components.Module).GetMethod("QueryComponentCache")!;
            editor.Emit(ILOpCode.Call, proxy.AllocateToken(queryComponentCache));

            editor.Emit(ILOpCode.Brfalse, editor.Count + 4);

            editor.Emit(ILOpCode.Ldarg_0);
            editor.Emit(ILOpCode.Call, proxy.AllocateToken(moduleCtorExit));
            editor.Emit(ILOpCode.Ret);

            editor.Emit(ILOpCode.Ldarg_0);
            editor.Emit(ILOpCode.Ldarga_s, 0);
            editor.Emit(ILOpCode.Callvirt, proxy.AllocateToken(startModuleConstruction));
            editor.Emit(firstOpCode.code, firstOpCode.operand);
            editor.Emit(ILOpCode.Br, 1);
            

            var buffer = editor.GenerateCode();

            var dec = new ILEditor(buffer);
            fixed (byte* pBuffer = buffer) {
                var oldBuffer = methodInfo->ILCode;
                var oldLength = methodInfo->ILCodeLength;
                methodInfo->ILCode = pBuffer;
                methodInfo->ILCodeLength = buffer.Length;

                var result = RuntimeInjection.CallRealCompiler(pthis, corinfo, methodInfo, flags, native, size, proxy);

                methodInfo->ILCode = oldBuffer;
                methodInfo->ILCodeLength = oldLength;

                return result;
            }
        }

        public bool Match(MethodBase method, nint methodInfo, out object? context) {
            var declType = method.DeclaringType;
            
            
            if (declType?.IsSubclassOf(typeof(Components.Module)) ?? false) {
                if (declType.GetCustomAttribute<ModuleCompilerIgnoreAttribute>(false) == null) {
                    if (method.IsConstructor) {
                        context = null;
                        return true;
                    }
                }
            }
            context = null;
            return false;
        }
    }
}
