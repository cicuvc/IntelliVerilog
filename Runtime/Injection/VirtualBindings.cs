using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace IntelliVerilog.Core.Runtime.Injection {
    public unsafe abstract class VirtualBindBase {
        protected IntPtr** m_Instance;
        public void WithInstance(IntPtr instance) {
            m_Instance = (IntPtr**)instance;
        }
    }
    public class VtableBind : Attribute {
        public string Name { get; }
        public VtableBind(string name) => Name = name;
    }
    public class VirtualBindingHelpers {
        private static AssemblyBuilder m_Assembly;
        private static ModuleBuilder m_Module;
        static VirtualBindingHelpers() {
            m_Assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("NATIVE_BINDING"), AssemblyBuilderAccess.Run);
            m_Module = m_Assembly.DefineDynamicModule("NATIVE_BINDING");
        }
        public static Type GetVirtualBind<T>(Func<string, int> vtblOffset) where T : VirtualBindBase {
            var baseType = typeof(T);
            var newType = m_Module.DefineType($"{baseType.Name}Binding", TypeAttributes.Class | TypeAttributes.Public, baseType);

            var instanceField = typeof(VirtualBindBase).GetField("m_Instance", BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (var i in baseType.GetMethods()) {
                if (!i.IsAbstract) continue;

                var paramTypes = i.GetParameters().Select(e => e.ParameterType).ToArray();
                var method = newType.DefineMethod(i.Name, MethodAttributes.Public | MethodAttributes.Virtual, i.ReturnType, paramTypes);

                var bindName = (i.GetCustomAttribute<VtableBind>()?.Name) ?? method.Name;

                var ilGen = method.GetILGenerator();

                ilGen.Emit(OpCodes.Ldarg_0);
                ilGen.Emit(OpCodes.Ldfld, instanceField!);
                ilGen.Emit(OpCodes.Conv_I);
                for (var j = 1; j <= paramTypes.Length; j++)
                    ilGen.Emit(OpCodes.Ldarg, j);


                ilGen.Emit(OpCodes.Ldarg_0);
                ilGen.Emit(OpCodes.Ldfld, instanceField!);

                ilGen.Emit(OpCodes.Ldind_I);
                ilGen.Emit(OpCodes.Ldc_I4, vtblOffset(bindName));
                ilGen.Emit(OpCodes.Add);
                ilGen.Emit(OpCodes.Ldind_I);



                ilGen.EmitCalli(OpCodes.Calli, CallingConvention.Cdecl, i.ReturnType, paramTypes.Prepend(typeof(IntPtr)).ToArray());
                ilGen.Emit(OpCodes.Ret);

                newType.DefineMethodOverride(method, i);
            }

            var compiledType = newType.CreateType();
            foreach (var i in compiledType.GetMethods()) {
                RuntimeHelpers.PrepareMethod(i.MethodHandle);
            }
            return compiledType;
        }
    }
}
