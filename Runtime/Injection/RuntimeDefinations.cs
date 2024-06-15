using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.Runtime.Injection {
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct JitCompileInfo {
        public IntPtr ftn;
        public IntPtr modulePtr;
        public byte* ILCode;
        public int ILCodeLength;
        public int MaxStack;
        public IntPtr MethodHandle;
    }
    public enum CorInfoTokenKind {
        CORINFO_TOKENKIND_Class = 0x01,
        CORINFO_TOKENKIND_Method = 0x02,
        CORINFO_TOKENKIND_Field = 0x04,
        CORINFO_TOKENKIND_Mask = 0x07,

        // token comes from CEE_LDTOKEN
        CORINFO_TOKENKIND_Ldtoken = 0x10 | CORINFO_TOKENKIND_Class | CORINFO_TOKENKIND_Method | CORINFO_TOKENKIND_Field,

        // token comes from CEE_CASTCLASS or CEE_ISINST
        CORINFO_TOKENKIND_Casting = 0x20 | CORINFO_TOKENKIND_Class,

        // token comes from CEE_NEWARR
        CORINFO_TOKENKIND_Newarr = 0x40 | CORINFO_TOKENKIND_Class,

        // token comes from CEE_BOX
        CORINFO_TOKENKIND_Box = 0x80 | CORINFO_TOKENKIND_Class,

        // token comes from CEE_CONSTRAINED
        CORINFO_TOKENKIND_Constrained = 0x100 | CORINFO_TOKENKIND_Class,

        // token comes from CEE_NEWOBJ
        CORINFO_TOKENKIND_NewObj = 0x200 | CORINFO_TOKENKIND_Method,

        // token comes from CEE_LDVIRTFTN
        CORINFO_TOKENKIND_Ldvirtftn = 0x400 | CORINFO_TOKENKIND_Method,

        // token comes from devirtualizing a method
        CORINFO_TOKENKIND_DevirtualizedMethod = 0x800 | CORINFO_TOKENKIND_Method,
    }
    public struct CORINFO_RESOLVED_TOKEN {
        //
        // [In] arguments of resolveToken
        //
        public nint tokenContext;       //Context for resolution of generic arguments
        public nint tokenScope;
        public uint token;              //The source token
        public uint tokenType;

        //
        // [Out] arguments of resolveToken.
        // - Type handle is always non-NULL.
        // - At most one of method and field handles is non-NULL (according to the token type).
        // - Method handle is an instantiating stub only for generic methods. Type handle
        //   is required to provide the full context for methods in generic types.
        //
        public nint hClass;
        public nint hMethod;
        public nint hField;

        //
        // [Out] TypeSpec and MethodSpec signatures for generics. NULL otherwise.
        //
        public nint pTypeSpec;
        public uint cbTypeSpec;
        public nint pMethodSpec;
        public uint cbMethodSpec;
    }
    public abstract class ICorInfoHelper : VirtualBindBase {
        [VtableBind("getMethodClass")]
        public abstract IntPtr GetMethodClass(IntPtr handle);
        [VtableBind("getClassSize")]
        public abstract uint GetClassSize(IntPtr typeHandle);
        [VtableBind("getHeapClassSize")]
        public abstract uint GetHeapClassSize(IntPtr typeHandle);
        [VtableBind("getFieldOffset")]
        public abstract uint GetFieldOffset(IntPtr fieldHandle);
    }
}
