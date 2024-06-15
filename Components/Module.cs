using IntelliVerilog.Core.Analysis;
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

        protected ref TIoPorts UseDefaultIo(TIoPorts value) {
            if (m_InternalModel is ComponentBuildingModel building) {
                var boxedValue = (object)value;
                var type = GetType();
                var ioAux = IoComponentProbableHelpers.QueryProbeAuxiliary(type)!;

                foreach (var i in ioAux.GetIoMembers(type)) {
                    if (i.Member.DeclaringType != typeof(TIoPorts)) continue;

                    var refValue = (IUntypedPort?)((FieldInfo)i.Member).GetValue(boxedValue);
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
    }

    [ModuleCompilerIgnore]
    [IoComponentProbable<ModuleIoProbeAux>]
    public class Module : ComponentBase {
        protected ComponentModel? m_InternalModel;

        public override ComponentModel InternalModel => m_InternalModel!;

        public Module() {
            
        }
        public bool QueryComponentCache(object[] parameters) {
            var context = IntelliVerilogLocator.GetService<AnalysisContext>()!;

            var cache = context.GetModuleModelCache(GetType());

            m_InternalModel = cache.QueryModelCache(parameters,this, out var found);
            InitializeBundle(this, this, null!);

            CatagoryName = Utility.GetRandomStringHex(8);

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
            var context = IntelliVerilogLocator.GetService<AnalysisContext>()!;

            context.OnExitConstruction(this);
        }
        protected void ModuleConstructorExit() {
            InitExternalPorts();
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
        protected void InitExternalPorts() {
            InitExternalBundles(this);

            foreach (var (key,_) in m_InternalModel!.IoPortShape) {
                var path = key.Location;

                var externalPort = key.Creator.CreateExternalPlaceholder(this, key.PortMember, key);

                path.TraceSetValue(this, externalPort);
            }
        }
        

    }
}
