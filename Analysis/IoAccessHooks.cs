using IntelliVerilog.Core.Components;
using IntelliVerilog.Core.DataTypes;
using IntelliVerilog.Core.Expressions;
using IntelliVerilog.Core.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
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

            currentModel.AssignSubModuleConnections((IAssignableValue)oldValue, newValue, Range.All, returnAddress);

            return !(oldValue is IoBundle);
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


            if (!Unsafe.IsNullRef(ref oldRef)) {
                return ref oldRef;
            } else {
                if (value is Wire wire) {
                    buildingModel.AssignWire(localName, wire);
                }
                if(value is Reg register) {
                    buildingModel.AssignReg(localName, register);
                }
            }
            return ref value;
        }
        public static object NotifyLocalVariableWrite(object value, object oldValue, nint methodHandle, nint typeHandle, int localIndex) {
            var method = RuntimeMethodHandle.FromIntPtr(methodHandle);
            var methodInfo = MethodInfo.GetMethodFromHandle(method, RuntimeTypeHandle.FromIntPtr(typeHandle));

            var managedDebugService = IntelliVerilogLocator.GetService<ManagedDebugInfoService>()!;
            var localName = managedDebugService.QueryLocalName(methodInfo, localIndex);

            var analysisContext = IntelliVerilogLocator.GetService<AnalysisContext>()!;
            var buildingModel = analysisContext.CurrentComponent.InternalModel as ComponentBuildingModel;

            if(value is AbstractValue signalExpression) {
                var staged = buildingModel.AssignLocalSignalVariable(signalExpression.Type.GetType(),localName, signalExpression);

                var localVariable = methodInfo.GetMethodBody().LocalVariables[localIndex];
                Debug.Assert(staged.GetType().IsAssignableTo(localVariable.LocalType));

                IvLogger.Default.Verbose("ModuleConstructionHooks", $"Got staged value '{localName}'");

                return staged;
            }
            if(value is ComponentBase subComponent) {
                buildingModel.AssignLocalSubComponent(localName, subComponent);

                IvLogger.Default.Verbose("ModuleConstructionHooks", $"Got sub component '{localName}'");
            }
            
            return value;
        }
        public static void NotifyReferenceWrite(ref object target, object value, Components.Module module) {
            var buildingModel = module.InternalModel as ComponentBuildingModel;

            if (target is IAssignableValue assignable) {
                var returnTracker = IntelliVerilogLocator.GetService<ReturnAddressTracker>()!;
                var returnAddress = returnTracker.TrackReturnAddress(module, paramIndex: 3);

                buildingModel.AssignSubModuleConnections(assignable, value, .., returnAddress);
            }
        }
        public unsafe static void NotifyIoTupleSet<TTuple>(ref TTuple tuple, IUntypedPort newValue, Components.Module module,  nint fieldHandle) where TTuple: struct, ITuple {
            var field = RuntimeFieldHandle.FromIntPtr(fieldHandle);
            var fieldInfo = FieldInfo.GetFieldFromHandle(field, typeof(TTuple).TypeHandle);

            var boxed = (object)tuple;

            var buildingModel = module.InternalModel as ComponentBuildingModel;
            var oldValue = (IoComponent)fieldInfo.GetValue(boxed);

            // TODO: Fix reference check
            if(oldValue != null) {
                var returnTracker = IntelliVerilogLocator.GetService<ReturnAddressTracker>()!;
                var returnAddress = returnTracker.TrackReturnAddress(newValue, paramIndex : 3);

                buildingModel.AssignSubModuleConnections((IAssignableValue)oldValue, newValue, Range.All, returnAddress);
            } else {
                fieldInfo.SetValue(boxed, newValue);
                tuple = (TTuple)boxed;
            }
        }
    }
}
