using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.Utils {
    public static class ReflectionHelpers {
        public static string PrettyTypeName(Type type, bool withNamespace = true) {
            var typeName = withNamespace ? $"{type.Namespace}::{type.Name}" : type.Name;
            if (type.IsConstructedGenericType) {
                var genericPart = type.GetGenericArguments().Select(e => PrettyTypeName(e, false)).Aggregate((u, v) => $"{u},{v}");
                typeName += $"[{genericPart}]";
            }
            return typeName;
        }
        public static bool IsSubClassOfGeneric(Type? deriveType, Type genericDef, out Type? constructedType) {
            for(; deriveType!=null; deriveType = deriveType.BaseType) {
                if (deriveType.IsConstructedGenericType) {
                    if(deriveType.GetGenericTypeDefinition() == genericDef) {
                        constructedType = deriveType;
                        return true;
                    }
                }
            }
            constructedType = null;
            return false;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static unsafe nint GetReturnAddress(nint thisObject) {
            return ((nint*)thisObject)[4];
        }
    }
}
