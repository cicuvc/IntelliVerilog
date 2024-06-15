using IntelliVerilog.Core.Analysis;
using IntelliVerilog.Core.DataTypes;
using IntelliVerilog.Core.Expressions;
using IntelliVerilog.Core.Logging;
using IntelliVerilog.Core.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.Components {
    public abstract class IoMemberInfo { 
        public MemberInfo Member { get; }
        public string Name { get; }
        public abstract Type MemberType { get; }
        public abstract void SetValue(object instance,IUntypedPort? value);
        public abstract IUntypedPort? GetValue(object instance);
        public IoMemberInfo(MemberInfo member, string name) {
            Member = member;
            Name = name;
        }
    }

    public class IoMemberPropertyInfo : IoMemberInfo {
        public IoMemberPropertyInfo(PropertyInfo member, string name) : base(member, name) {
        }

        public override Type MemberType => ((PropertyInfo)Member).PropertyType;

        public override IUntypedPort? GetValue(object instance) {
            return (IUntypedPort?)((PropertyInfo)Member).GetValue(instance);
        }

        public override void SetValue(object instance, IUntypedPort? value) {
            IoAccessHooks.NotifyIoBundlePropertyEnter();
            ((PropertyInfo)Member).SetValue(instance,value);
            IoAccessHooks.NotifyIoBundlePropertyExit();
        }
    }
    public class IoMemberTupleFieldInfo : IoMemberInfo {
        public override Type MemberType => ((FieldInfo)Member).FieldType;
        public IoMemberTupleFieldInfo(FieldInfo member, string name) : base(member, name) {
        }

        public override IUntypedPort? GetValue(object instance) {
            var ioTuple = (ITupledModule)instance;
            return (IUntypedPort?)((FieldInfo)Member).GetValue(ioTuple.BoxedIoPorts);
        }

        public override void SetValue(object instance, IUntypedPort? value) {
            var ioTuple = (ITupledModule)instance;
            ((FieldInfo)Member).SetValue(ioTuple.BoxedIoPorts, value);
        }
    }
    public class BundleIoProbeAux : IIoComponentProbeAuxiliary {
        public virtual IEnumerable<IoMemberInfo> GetIoMembers(Type type) {

            var context = IntelliVerilogLocator.GetService<AnalysisContext>()!;

            if (!context.BundleMemberCache.ContainsKey(type)) {
                var bundleMembers = type.GetProperties().Where(e => {
                    if (e.GetCustomAttribute<IoIgnoreAttribute>() != null)
                        return false;
                    if (e.PropertyType.IsAssignableTo(typeof(IUntypedPort))) {
                        return true;
                    }
                    return false;
                }).Select(e => {
                    return new IoMemberPropertyInfo(e, e.Name);
                }).ToArray();
                context.BundleMemberCache.Add(type, bundleMembers);
            }

            return context.BundleMemberCache[type];

        }

        public virtual void InitializeIoPorts(object bundleInstance) {
            // TODO
            var currentBundle = (IoBundle)bundleInstance;
            var member = GetIoMembers(bundleInstance.GetType());
            foreach(var i in member) {
                if (!i.MemberType.IsAssignableTo(typeof(IUntypedPort))) {
                    IvLogger.Default.Info("IoInitialization", "Not implement IUntypedPort");
                    continue;
                }

                if (i.MemberType.IsSubclassOf(typeof(IoBundle))) {
                    var placeHolder = (IoBundle)RuntimeHelpers.GetUninitializedObject(i.MemberType);
                    placeHolder.InitializeBundle(currentBundle, currentBundle.Component, i);

                    var ioAux = IoComponentProbableHelpers.QueryProbeAuxiliary(i.MemberType);
                    ioAux?.InitializeIoPorts(placeHolder);

                    i.SetValue(bundleInstance, placeHolder);
                }
                if (i.MemberType.IsSubclassOf(typeof(IoComponent))) {
                    if (i.MemberType.IsAssignableTo(typeof(IUnspecifiedPortFactory))) {
                        var createPort = i.MemberType.GetMethod(nameof(IUnspecifiedPortFactory.CreateUnspecified), BindingFlags.FlattenHierarchy | BindingFlags.Static | BindingFlags.Public);
                        Debug.Assert(createPort != null);

                        var portObject = (IUntypedPort?)createPort.Invoke(null, new object[] { bundleInstance, currentBundle.Component, i});
                        i.SetValue(bundleInstance, portObject);
                    }
                }
                
                continue;

            }


           
        }
    }
    public class ModuleIoProbeAux : BundleIoProbeAux {
        
        public override IEnumerable<IoMemberInfo> GetIoMembers(Type type) {
            Debug.Assert(type.IsAssignableTo(typeof(Module)));

            var context = IntelliVerilogLocator.GetService<AnalysisContext>()!;
            var cache = context.GetModuleModelCache(GetType());

            if (!context.IoMemberCache.ContainsKey(type)) {
                var baseSet = base.GetIoMembers(type);

                if (ReflectionHelpers.IsSubClassOfGeneric(type, typeof(Module<>), out var specifiedType)) {
                    var tupleType = specifiedType!.GetGenericArguments()[0]!;
                    var tupleNames = type.GetCustomAttribute<TupleElementNamesAttribute>();

                    if(tupleNames != null) {
                        baseSet = tupleNames.TransformNames.Where((e, index) => {
                            var field = tupleType.GetField($"Item{index + 1}");
                            if (field.GetCustomAttribute<IoIgnoreAttribute>() != null)
                                return false;
                            if (field.FieldType.IsAssignableTo(typeof(IUntypedPort))) {
                                return true;
                            }
                            return false;
                        }).Select((e, index) => {
                            var field = tupleType.GetField($"Item{index + 1}");
                            return new IoMemberTupleFieldInfo(field, e);
                        }).Concat(baseSet);
                    }
                    
                }

                context.IoMemberCache.Add(type, baseSet.ToArray());
            }

            return context.IoMemberCache[type];
        }
        public override void InitializeIoPorts(object bundleInstance) {
            base.InitializeIoPorts(bundleInstance);
        }
    }
    public interface IIoComponentProbeAuxiliary {
        IEnumerable<IoMemberInfo> GetIoMembers(Type type);
        void InitializeIoPorts(object bundleInstance);
    }
    public class IoComponentProbableHelpers {
        private static Dictionary<Type, IIoComponentProbeAuxiliary> m_ProbeAuxCache = new();
        public static IIoComponentProbeAuxiliary? QueryProbeAuxiliary(Type ioComponentType) {
            var lookupType = ioComponentType;
            while (lookupType != null) {
                if (m_ProbeAuxCache.ContainsKey(lookupType)) return m_ProbeAuxCache[lookupType];

                foreach (var i in lookupType.GetCustomAttributes(false)) {
                    var attributeType = i.GetType();
                    if (!attributeType.IsConstructedGenericType) continue;
                    if (attributeType.GetGenericTypeDefinition() == typeof(IoComponentProbableAttribute<>)) {
                        var auxType = i.GetType().GetGenericArguments()[0];
                        var auxObject = (IIoComponentProbeAuxiliary)Activator.CreateInstance(auxType)!;
                        m_ProbeAuxCache.Add(lookupType, auxObject);
                        
                        return auxObject;
                    }
                }
                lookupType = lookupType.BaseType;
            }

            return null;
        }
    }
    public class IoComponentProbableAttribute<TComponentAux> : Attribute where TComponentAux: IIoComponentProbeAuxiliary,new() {
        
    }
    [IoComponentProbable<BundleIoProbeAux>]
    public class IoBundle : IUntypedConstructionPort {
        [IoIgnore]
        public IoBundle Parent { get; protected set; } = null!;
        [IoIgnore]
        public AbstractValue UntypedRValue => throw new NotImplementedException();

        [IoIgnore]
        public ComponentBase Component { get; protected set; }
        [IoIgnore]
        public IoMemberInfo PortMember { get; protected set; }

        public bool Constructed { get; set; }

        public IoPortPath Location => new(this, PortMember);

        public IoPortDirection Direction => IoPortDirection.Bundle;

        public GeneralizedPortFlags Flags => GeneralizedPortFlags.Bundle | (Constructed ? GeneralizedPortFlags.Constructed : 0);

        public DataType UntypedType => throw new NotSupportedException();
        [IoIgnore]
        public IUntypedDeclPort Creator => throw new NotSupportedException();
        [IoIgnore]
        public IUntypedConstructionPort InternalPort => throw new NotImplementedException();

        public IoBundle() {
            Component = Component ?? null!;
            PortMember = PortMember ?? null!;
        }
        public void InitializeBundle(IoBundle parent, ComponentBase root, IoMemberInfo member) {
            Parent = parent;
            Component = root;
            PortMember = member;
        }
    }

}
