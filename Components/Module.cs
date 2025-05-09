﻿using IntelliVerilog.Core.Analysis;
using IntelliVerilog.Core.Expressions;
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

namespace IntelliVerilog.Core.Components {
    public class ModuleCompilerIgnoreAttribute:Attribute {

    }
    public interface ITupledModule {
        object? BoxedIoPorts { get; }
    }
    [ModuleCompilerIgnore]
    public class Module<TIoPorts> : Module, ITupledModule where TIoPorts:struct, ITuple {
        protected object? m_BoxedIoPorts;
        public ref TIoPorts IO => ref Unsafe.Unbox<TIoPorts>(m_BoxedIoPorts!);

        public object? BoxedIoPorts => m_BoxedIoPorts;
        public override bool IsModuleIo(ref byte portReference) {
            return Unsafe.AreSame(ref Unsafe.As<byte, TIoPorts>(ref portReference), ref IO);
        }
        protected ref TIoPorts UseDefaultIo(TIoPorts value) {
            if (m_InternalModel is ComponentBuildingModel building) {
                var boxedValue = (object)value;
                var type = GetType();
                var ioAux = IoComponentProbableHelpers.QueryProbeAuxiliary(type)!;

                foreach (var i in ioAux.GetIoMembers(type)) {
                    if (!(i is IoMemberTupleFieldInfo tupleField)) continue;

                    var refValue = tupleField.GetRawValue(boxedValue);
                    var finalValue = MakeInternalSinglePort(i, this, refValue);

                    building.RegisterIoPorts(finalValue);
                }

                return ref IO;
            } else {
                throw new NotImplementedException();
            }
        }
        protected override void StartConstruction(ref Module module) {
            m_BoxedIoPorts = default(TIoPorts);
            base.StartConstruction(ref module);
        }
        protected override void InitExternalPorts() {
            m_BoxedIoPorts = default(TIoPorts);

            base.InitExternalPorts();
        }
    }

    [ModuleCompilerIgnore]
    [IoComponentProbable<ModuleIoProbeAux>]
    public class Module : ComponentBase {
        protected ComponentModel? m_InternalModel;

        public override ComponentModel InternalModel => m_InternalModel!;

        public Module() {
            
        }
        public override bool IsModuleIo(object portReference) {
            var thisType = GetType();
            var ioAux = IoComponentProbableHelpers.QueryProbeAuxiliary(thisType);
            foreach(var i in ioAux.GetIoMembers(thisType)) {
                if (i.GetValue(this) == portReference) return true;
            }
            return false;
        }
        public override bool IsModuleIo(ref byte portReference) {
            return false;
        }
        public bool QueryComponentCache(object[] parameters) {
            var context = IntelliVerilogLocator.GetService<AnalysisContext>()!;

            var cache = context.GetModuleModelCache(GetType());

            parameters = parameters.Append(ScopedLocator.GetService<ClockDomain>()!).ToArray();

            m_InternalModel = cache.QueryModelCache(parameters,this, out var found);
            InitializeBundle(this, this, null!);

            return found;
        }
        protected unsafe virtual void StartConstruction(ref Module module) {
            var stackBase = ((nint*)Unsafe.AsPointer(ref module)) + 4;
            Console.WriteLine((nint)stackBase);

            m_InternalModel!.Behavior = new(Runtime.Core.CheckPointRecorder.CreateRecorder((nint)stackBase));

            var ioAux = IoComponentProbableHelpers.QueryProbeAuxiliary(GetType());
            ioAux?.InitializeIoPorts(this);

            var context = IntelliVerilogLocator.GetService<AnalysisContext>()!;
            context.OnEnterConstruction(this);
        }
        protected void EndConstruction() {
            var model = (ComponentBuildingModel)m_InternalModel!;
            model.Behavior.ConstructionEnd();

            model.ModelCheck();

            var context = IntelliVerilogLocator.GetService<AnalysisContext>()!;

            context.OnExitConstruction(this);
        }
        protected void ModuleConstructorExit() {
            InitComponentBase();

            InitExternalPorts();

            var context = IntelliVerilogLocator.GetService<AnalysisContext>()!;
            if(context.CurrentComponent != null) {
                var model = context.GetComponentBuildingModel(throwOnNull: true)!;
                model.AddEntity(this);

                foreach (var i in (InternalModel.UsedClockDomains)) {
                    model.RegisterClockDomain(i);
                }
            }

        }
        protected IUntypedPort MakeInternalSinglePort(IoMemberInfo ioMember, IoBundle dstPortContainer, IUntypedPort? refPortValue) {
            var dstValue = ioMember.GetValue(dstPortContainer);

            if (dstValue is IoBundle dstBundle) {
                var type = dstBundle.GetType();
                var ioAux = IoComponentProbableHelpers.QueryProbeAuxiliary(type)!;

                foreach (var i in ioAux.GetIoMembers(type)) {
                    var refPortObject = refPortValue == null ? null : i.GetValue(refPortValue);
                    MakeInternalSinglePort(i, dstBundle, refPortObject);
                }

                return dstBundle;
            }
            var finalValue = default(IUntypedPort);

            if (refPortValue == null) {
                if (dstValue is IUntypedDeclPort ioComponent) {
                    finalValue = ioComponent.CreateInternalPlaceholder(dstPortContainer, ioMember);
                    ioMember.SetValue(dstPortContainer, finalValue);
                } else {
                    throw new NotImplementedException();
                }
            } else {
                var declRefPort = (IUntypedDeclPort)refPortValue;
                finalValue = declRefPort.CreateInternalPlaceholder(dstPortContainer, ioMember);
                ioMember.SetValue(dstPortContainer, finalValue);
            }
            return finalValue;

            
            throw new NotSupportedException();
        }

        protected T UseIoPorts<T>(T? bundle, T? value = default(T)) where T : IUntypedPort {
            if(m_InternalModel is ComponentBuildingModel building) {
                var located = (IUntypedLocatedPort)bundle!;
                var finalValue = MakeInternalSinglePort(located.PortMember, this, value);

                building.RegisterIoPorts(finalValue);

                return (T)finalValue;
            } else {
                throw new NotImplementedException();
            }
            
        }
        protected void InitExternalBundles(IoBundle currentBundle) {
            var type = currentBundle.GetType();
            var ioAux = IoComponentProbableHelpers.QueryProbeAuxiliary(type)!;

            foreach(var i in ioAux.GetIoMembers(type)) {
                if (i.MemberType.IsSubclassOf(typeof(IoBundle))) {
                    var bundle = (IoBundle)RuntimeHelpers.GetUninitializedObject(i.MemberType);

                    bundle.InitializeBundle(currentBundle, currentBundle.Component, i);
                    i.SetValue(currentBundle, bundle);
                }
            }
        }
        protected virtual void InitExternalPorts() {
            
            InitExternalBundles(this);

            foreach (var key in m_InternalModel!.IoPortShape) {
                if (key.Flags.HasFlag(GeneralizedPortFlags.ClockReset)) continue;

                var path = key.Location;

                var externalPort = key.Creator.CreateExternalPlaceholder(this, key.PortMember, key);

                path.TraceSetValue(this, externalPort);
            }
        }
        

    }
}
