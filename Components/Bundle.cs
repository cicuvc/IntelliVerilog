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
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.Components {
    public abstract class IoMemberInfo { 
        public abstract MemberInfo Member { get; }
        public string Name { get; }
        public abstract Type MemberType { get; }
        public abstract void SetValue(object instance,IUntypedPort? value);
        public abstract IUntypedPort? GetValue(object instance);
        public IoMemberInfo(string name) {
            Name = name;
        }
    }

    public class IoMemberPropertyInfo : IoMemberInfo {
        public IoMemberPropertyInfo(PropertyInfo member, string name) : base(name) {
            Member = member;
        }

        public override Type MemberType => ((PropertyInfo)Member).PropertyType;
        public override MemberInfo Member { get; }
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
        protected FieldInfo[] m_MemberChain;
        protected DynamicMethod m_WriteValue;
        protected DynamicMethod m_ReadValue;
        public override MemberInfo Member => m_MemberChain.Last();
        public IoMemberTupleFieldInfo(FieldInfo[] member, string name) : base(name) {
            m_MemberChain = member;
            var valueType = member.Last().FieldType;
            var unboxType = member[0].DeclaringType!;

            m_WriteValue = new DynamicMethod($"{Utility.GetRandomStringHex(16)}_set", null, new Type[] {
                typeof(object), valueType
            });

            var writeIG = m_WriteValue.GetILGenerator();
            writeIG.Emit(OpCodes.Ldarg_0);
            writeIG.Emit(OpCodes.Unbox, unboxType);
            for(var i=0;i < member.Length - 1; i++) {
                writeIG.Emit(OpCodes.Ldflda, member[i]);
            }
            writeIG.Emit(OpCodes.Ldarg_1);
            writeIG.Emit(OpCodes.Stfld, member.Last());
            writeIG.Emit(OpCodes.Ret);

            m_ReadValue = new DynamicMethod($"{Utility.GetRandomStringHex(16)}_get", valueType, new Type[] {
                typeof(object)
            });

            var readIG = m_ReadValue.GetILGenerator();
            readIG.Emit(OpCodes.Ldarg_0);
            readIG.Emit(OpCodes.Unbox, unboxType);
            for (var i = 0; i < member.Length - 1; i++) {
                readIG.Emit(OpCodes.Ldflda, member[i]);
            }
            readIG.Emit(OpCodes.Ldfld, member.Last());
            readIG.Emit(OpCodes.Ret);
        }
        public IUntypedPort? GetRawValue(object instance) {
            return (IUntypedPort?)m_ReadValue.Invoke(null, new object[] { instance });

        }
        public override IUntypedPort? GetValue(object instance) {
            var module = (ITupledModule)instance;
            return (IUntypedPort?)m_ReadValue.Invoke(null, new object[] { module.BoxedIoPorts ?? throw new NullReferenceException("IO port container is null")});
        }

        public override void SetValue(object instance, IUntypedPort? value) {
            var module = (ITupledModule)instance;
            m_WriteValue.Invoke(null, new object[] { module.BoxedIoPorts ?? throw new NullReferenceException("IO port container is null"), value! });
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
                        var totalElements = tupleNames.TransformNames.Count(e => e != null);
                        var tupleMembers = new List<IoMemberInfo>();

                        var residueFieldInfo = new List<FieldInfo>();
                        var currentType = tupleType;
                        for(var i=0;i < totalElements; i++) {
                            if(i % 7 == 0 && i>0) {
                                var rest = currentType.GetField("Rest")!;
                                residueFieldInfo.Add(rest);
                                currentType = rest.FieldType;
                            }

                            var field = currentType.GetField($"Item{(i % 7) + 1}")!;
                            if (field.GetCustomAttribute<IoIgnoreAttribute>() != null) continue;
                            var ioMember = new IoMemberTupleFieldInfo(residueFieldInfo.Append(field).ToArray(), tupleNames.TransformNames[i] ?? throw new NullReferenceException("[WARNING] Got null name hint"));
                            tupleMembers.Add(ioMember);
                        }

                        baseSet = baseSet.Concat(tupleMembers);
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
        public static IIoComponentProbeAuxiliary QueryProbeAuxiliary(Type ioComponentType) {
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

            throw new NotImplementedException($"Unable to find suitable probe auxiliary for {ReflectionHelpers.PrettyTypeName(ioComponentType)}");
        }
    }
    public class IoComponentProbableAttribute<TComponentAux> : Attribute where TComponentAux: IIoComponentProbeAuxiliary,new() {
        
    }
    [IoComponentProbable<BundleIoProbeAux>]
    public class IoBundle : IUntypedConstructionPort, IAssignableValue {
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

        public DataType UntypedType { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        [IoIgnore]
        public IUntypedDeclPort Creator => throw new NotSupportedException();
        [IoIgnore]
        public IUntypedConstructionPort InternalPort => throw new NotImplementedException();

        public Func<string> Name { get; set; } = () => "<unnamed bundle>";
        [IoIgnore]
        public ValueShape Shape => throw new NotImplementedException();

        public IoBundle() {
            Component = Component ?? null!;
            PortMember = PortMember ?? null!;
        }
        public void InitializeBundle(IoBundle parent, ComponentBase root, IoMemberInfo member) {
            Parent = parent;
            Component = root;
            PortMember = member;
        }

        public AssignmentInfo CreateAssignmentInfo() {
            throw new NotImplementedException();
        }
    }

}
