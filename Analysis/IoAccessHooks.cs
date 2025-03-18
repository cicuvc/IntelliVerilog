using IntelliVerilog.Core.Components;
using IntelliVerilog.Core.DataTypes;
using IntelliVerilog.Core.Expressions;
using IntelliVerilog.Core.Logging;
using IntelliVerilog.Core.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.Analysis {
    public static class IoAccessHooks {
        [ThreadStatic]
        private static bool m_HookInvoked = false;

        public static bool IsHookInvoked() {
            return m_HookInvoked;
        }
        public static void NotifyIoBundlePropertyEnter() {
            m_HookInvoked = true;
        }
        public static void NotifyIoBundlePropertyExit() {
            m_HookInvoked = false;
        }
        public static bool NotifyIoBundleSet(IoBundle bundleObject, IUntypedPort oldValue, IUntypedPort newValue) {
            var returnTracker = IntelliVerilogLocator.GetService<ReturnAddressTracker>()!;
            var returnAddress = returnTracker.TrackReturnAddress(bundleObject);

            var analysisContext = IntelliVerilogLocator.GetService<AnalysisContext>()!;
            var currentModel = analysisContext.CurrentComponent?.InternalModel as ComponentBuildingModel;

            if (currentModel == null || (oldValue == null)) return true;

            currentModel.AssignSubModuleConnections((IAssignableValue)oldValue, newValue, new(Array.Empty<GenericIndex>()), returnAddress);

            return false;
        }
        public static object NotifyIoBundleGet(IoBundle bundle, IUntypedPort oldValue) {
            if(oldValue is IoComponent ioComponent) {
                return oldValue;
            }

            return oldValue;
        }
        public static ref object NotifyLocalReferenceWrite(ref object value, ref object oldRef, nint methodHandle, nint typeHandle, int localIndex) {
            var method = RuntimeMethodHandle.FromIntPtr(methodHandle);
            var methodInfo = MethodInfo.GetMethodFromHandle(method, RuntimeTypeHandle.FromIntPtr(typeHandle));

            var managedDebugService = IntelliVerilogLocator.GetService<ManagedDebugInfoService>()!;
            var localName = managedDebugService.QueryLocalName(methodInfo, localIndex);

            var analysisContext = IntelliVerilogLocator.GetService<AnalysisContext>()!;
            var buildingModel = analysisContext.CurrentComponent.InternalModel as ComponentBuildingModel;


            if(value is IOverlappedObject overlapped) {
                buildingModel.AssignEntityName(localName, overlapped);
            }

            return ref value;
        }
        public static object NotifyLocalVariableWrite(object value, object oldValue, nint methodHandle, nint typeHandle, int localIndex) {
            var method = RuntimeMethodHandle.FromIntPtr(methodHandle);
            var methodInfo = MethodInfo.GetMethodFromHandle(method, RuntimeTypeHandle.FromIntPtr(typeHandle));

            var managedDebugService = IntelliVerilogLocator.GetService<ManagedDebugInfoService>()!;
            var localName = managedDebugService.QueryLocalName(methodInfo, localIndex);

            localName ??= managedDebugService.GetAutoIncLocalName();

            var analysisContext = IntelliVerilogLocator.GetService<AnalysisContext>()!;
            var buildingModel = analysisContext.CurrentComponent.InternalModel as ComponentBuildingModel;

            if(value is AbstractValue signalExpression) {
                var staged = buildingModel.AssignLocalSignalVariable(signalExpression.UntypedType.GetType(),localName, signalExpression);

                var localVariable = methodInfo.GetMethodBody().LocalVariables[localIndex];
                Debug.Assert(staged.GetType().IsAssignableTo(localVariable.LocalType));

                IvLogger.Default.Verbose("ModuleConstructionHooks", $"Got staged value '{localName}'");

                return staged;
            }
            if(value is ComponentBase subComponent) {
                buildingModel.AssignEntityName(localName, subComponent);

                IvLogger.Default.Verbose("ModuleConstructionHooks", $"Got sub component '{localName}'");
            }
            
            return value;
        }
        public static void NotifyReferenceWrite(ref object target, object value, Components.Module module) {
            var buildingModel = module.InternalModel as ComponentBuildingModel;


            if (!(target is IReferenceTraceObject traceObject)) return;
            if (!buildingModel.ReferenceTraceObjects.ContainsKey(traceObject)) return;
            if (target is IAssignableValue assignable) {
                var returnTracker = IntelliVerilogLocator.GetService<ReturnAddressTracker>()!;
                var returnAddress = returnTracker.TrackReturnAddress(module, paramIndex: 3);

                buildingModel.AssignSubModuleConnections(assignable, value, new(Array.Empty<GenericIndex>()), returnAddress);
            }
            if(target is AbstractValue genericExpr) {
                genericExpr = genericExpr.UnwrapCast();
                //if (genericExpr is IUntypedGeneralBitSelectionExpression bitSelection) {
                //    var untypedLhs = (object)bitSelection.UntypedBaseValue.UnwrapCast();
                //    if (untypedLhs is IInvertedOutput iio) {
                //        untypedLhs = iio.InternalOut;
                //    }

                //    if (untypedLhs is IAssignableValue lhs) {
                //        var returnTracker = IntelliVerilogLocator.GetService<ReturnAddressTracker>()!;
                //        var returnAddress = returnTracker.TrackReturnAddress(module, paramIndex: 3);

                //        buildingModel.AssignSubModuleConnections(lhs, value, bitSelection.SelectedRange.ToGenericIndices(), returnAddress);
                //        return;
                //    } 
                    
                //    throw new InvalidOperationException("Assign to non-assignable ref trace object");
                //}
            }
           

        }
        public static void NotifyReferenceValueTypeWrite(ref byte target, object box, Components.Module module){
            var buildingModel = module.InternalModel as ComponentBuildingModel;

            var returnTracker = IntelliVerilogLocator.GetService<ReturnAddressTracker>()!;
            var returnAddress = returnTracker.TrackReturnAddress(module, paramIndex: 3);

            var internalModel = module.InternalModel;
            foreach(var i in internalModel.GetSubComponents()) {
                if(i.IsModuleIo(ref target)) {
                    var ioAux = IoComponentProbableHelpers.QueryProbeAuxiliary(i.GetType());
                    foreach (var j in ioAux.GetIoMembers(i.GetType())) {
                        var fieldInfo = (FieldInfo)j.Member;
                        var leftValue = (IAssignableValue)j.GetValue(i);
                        var rightValue = fieldInfo.GetValue(box);
                        if (rightValue != null) {
                            buildingModel.AssignSubModuleConnections(leftValue, rightValue, new(Array.Empty<GenericIndex>()), returnAddress);
                        }
                    }
                    return;
                }
            }
            if(module.IsModuleIo(ref target)) {
                var ioAux = IoComponentProbableHelpers.QueryProbeAuxiliary(module.GetType());
                foreach (var j in ioAux.GetIoMembers(module.GetType())) {
                    var fieldInfo = (FieldInfo)j.Member;
                    var leftValue = (IAssignableValue)j.GetValue(module);
                    var rightValue = fieldInfo.GetValue(box);
                    if (rightValue != null) {
                        buildingModel.AssignSubModuleConnections(leftValue, rightValue, new(Array.Empty<GenericIndex>()), returnAddress);
                    }
                }
                return;
            }

            throw new NotImplementedException();
        }

        public unsafe static void NotifyIoTupleSet(ref byte tupleRef, IUntypedPort newValue, Components.Module module,  nint fieldHandle, nint typeHandle) {
            var field = RuntimeFieldHandle.FromIntPtr(fieldHandle);
            var fieldInfo = FieldInfo.GetFieldFromHandle(field, RuntimeTypeHandle.FromIntPtr(typeHandle));

            var buildingModel = module.InternalModel as ComponentBuildingModel;
            var oldValue = (IoComponent)FieldAccessorRegistry.GetField(fieldInfo, ref tupleRef);

            // TODO: Fix reference check
            if (oldValue != null) {
                var returnTracker = IntelliVerilogLocator.GetService<ReturnAddressTracker>()!;
                var returnAddress = returnTracker.TrackReturnAddress(newValue, paramIndex : 2);

                buildingModel.AssignSubModuleConnections((IAssignableValue)oldValue, newValue, new(Array.Empty<GenericIndex>()), returnAddress);
            } else {
                FieldAccessorRegistry.SetField(fieldInfo, ref tupleRef, newValue);
            }
        }
    }
    public static class FieldAccessorRegistry {
        private delegate object GetFieldDelegate(ref byte rawValue);
        private delegate void SetFieldDelegate(ref byte rawValue,object value);
        private static Dictionary<FieldInfo, GetFieldDelegate> m_Getters = new();
        private static Dictionary<FieldInfo, SetFieldDelegate> m_Setters = new();
        public static object GetField(FieldInfo field, ref byte rawValue) {
            if (!m_Getters.ContainsKey(field)) {
                var getter = new DynamicMethod(Utility.GetRandomStringHex(16), typeof(object), new Type[] { typeof(byte).MakeByRefType() });
                var ig = getter.GetILGenerator();
                ig.Emit(OpCodes.Ldarg_0);
                ig.Emit(OpCodes.Ldfld, field);
                ig.Emit(OpCodes.Ret);

                var getterDelegate = getter.CreateDelegate<GetFieldDelegate>();
                m_Getters.Add(field, getterDelegate);
            }
            return m_Getters[field](ref rawValue);
        }
        public static void SetField(FieldInfo field, ref byte rawValue,object value) {
            if (!m_Setters.ContainsKey(field)) {
                var setter = new DynamicMethod(Utility.GetRandomStringHex(16), typeof(void), new Type[] { typeof(byte).MakeByRefType(),typeof(object) });
                var ig = setter.GetILGenerator();
                ig.Emit(OpCodes.Ldarg_0);
                ig.Emit(OpCodes.Ldarg_1);
                ig.Emit(OpCodes.Stfld, field);
                ig.Emit(OpCodes.Ret);

                var setterDelegate = setter.CreateDelegate<SetFieldDelegate>();
                m_Setters.Add(field, setterDelegate);
            }
            m_Setters[field](ref rawValue,value);
        }
    }
}
