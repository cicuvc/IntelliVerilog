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
        public int EHcount;
        public CorInfoOptions options;
        public CorInfoRegionKind regionKind;
        public CORINFO_SIG_INFO args;
        public CORINFO_SIG_INFO locals;
    }
    public enum CorInfoRegionKind {
        CORINFO_REGION_NONE,
        CORINFO_REGION_HOT,
        CORINFO_REGION_COLD,
        CORINFO_REGION_JIT,
    }
    public enum CorInfoOptions {
        CORINFO_OPT_INIT_LOCALS = 0x00000010, // zero initialize all variables

        CORINFO_GENERICS_CTXT_FROM_THIS = 0x00000020, // is this shared generic code that access the generic context from the this pointer?  If so, then if the method has SEH then the 'this' pointer must always be reported and kept alive.
        CORINFO_GENERICS_CTXT_FROM_METHODDESC = 0x00000040, // is this shared generic code that access the generic context from the ParamTypeArg(that is a MethodDesc)?  If so, then if the method has SEH then the 'ParamTypeArg' must always be reported and kept alive. Same as CORINFO_CALLCONV_PARAMTYPE
        CORINFO_GENERICS_CTXT_FROM_METHODTABLE = 0x00000080, // is this shared generic code that access the generic context from the ParamTypeArg(that is a MethodTable)?  If so, then if the method has SEH then the 'ParamTypeArg' must always be reported and kept alive. Same as CORINFO_CALLCONV_PARAMTYPE
        CORINFO_GENERICS_CTXT_MASK = (CORINFO_GENERICS_CTXT_FROM_THIS |
                                                   CORINFO_GENERICS_CTXT_FROM_METHODDESC |
                                                   CORINFO_GENERICS_CTXT_FROM_METHODTABLE),
        CORINFO_GENERICS_CTXT_KEEP_ALIVE = 0x00000100, // Keep the generics context alive throughout the method even if there is no explicit use, and report its location to the CLR

    }
    public enum CorInfoCallConv:int {
        // These correspond to CorCallingConvention

        CORINFO_CALLCONV_DEFAULT = 0x0,
        // Instead of using the below values, use the CorInfoCallConvExtension enum for unmanaged calling conventions.
        // CORINFO_CALLCONV_C          = 0x1,
        // CORINFO_CALLCONV_STDCALL    = 0x2,
        // CORINFO_CALLCONV_THISCALL   = 0x3,
        // CORINFO_CALLCONV_FASTCALL   = 0x4,
        CORINFO_CALLCONV_VARARG = 0x5,
        CORINFO_CALLCONV_FIELD = 0x6,
        CORINFO_CALLCONV_LOCAL_SIG = 0x7,
        CORINFO_CALLCONV_PROPERTY = 0x8,
        CORINFO_CALLCONV_UNMANAGED = 0x9,
        CORINFO_CALLCONV_NATIVEVARARG = 0xb,    // used ONLY for IL stub PInvoke vararg calls

        CORINFO_CALLCONV_MASK = 0x0f,     // Calling convention is bottom 4 bits
        CORINFO_CALLCONV_GENERIC = 0x10,
        CORINFO_CALLCONV_HASTHIS = 0x20,
        CORINFO_CALLCONV_EXPLICITTHIS = 0x40,
        CORINFO_CALLCONV_PARAMTYPE = 0x80,     // Passed last. Same as CORINFO_GENERICS_CTXT_FROM_PARAMTYPEARG
    }
    public enum CorInfoType:byte {
        CORINFO_TYPE_UNDEF = 0x0,
        CORINFO_TYPE_VOID = 0x1,
        CORINFO_TYPE_BOOL = 0x2,
        CORINFO_TYPE_CHAR = 0x3,
        CORINFO_TYPE_BYTE = 0x4,
        CORINFO_TYPE_UBYTE = 0x5,
        CORINFO_TYPE_SHORT = 0x6,
        CORINFO_TYPE_USHORT = 0x7,
        CORINFO_TYPE_INT = 0x8,
        CORINFO_TYPE_UINT = 0x9,
        CORINFO_TYPE_LONG = 0xa,
        CORINFO_TYPE_ULONG = 0xb,
        CORINFO_TYPE_NATIVEINT = 0xc,
        CORINFO_TYPE_NATIVEUINT = 0xd,
        CORINFO_TYPE_FLOAT = 0xe,
        CORINFO_TYPE_DOUBLE = 0xf,
        CORINFO_TYPE_STRING = 0x10,         // Not used, should remove
        CORINFO_TYPE_PTR = 0x11,
        CORINFO_TYPE_BYREF = 0x12,
        CORINFO_TYPE_VALUECLASS = 0x13,
        CORINFO_TYPE_CLASS = 0x14,
        CORINFO_TYPE_REFANY = 0x15,

        // CORINFO_TYPE_VAR is for a generic type variable.
        // Generic type variables only appear when the JIT is doing
        // verification (not NOT compilation) of generic code
        // for the EE, in which case we're running
        // the JIT in "import only" mode.

        CORINFO_TYPE_VAR = 0x16,
        CORINFO_TYPE_COUNT,                         // number of jit types
    };
    public unsafe struct CORINFO_SIG_INST {
        public uint classInstCount;
        public nint* classInst; // (representative, not exact) instantiation for class type variables in signature
        public uint methInstCount;
        public nint* methInst; // (representative, not exact) instantiation for method type variables in signature
    }
    public unsafe struct CORINFO_SIG_INFO {
        public CorInfoCallConv callConv;
        public nint retTypeClass;   // if the return type is a value class, this is its handle (enums are normalized)
        public nint retTypeSigClass;// returns the value class as it is in the sig (enums are not converted to primitives)
        public CorInfoType retType;// : 8;
        public byte flags;//  : 8;    // used by IL stubs code
        public ushort numArgs;// : 16;
        public CORINFO_SIG_INST sigInst;        // information about how type variables are being instantiated in generic code
        public nint args;
        public byte* pSig;
        public int cbSig;
        public nint methodSignature;// used in place of pSig and cbSig to reference a method signature object handle
        public nint scope;          // passed to getArgClass
        public uint token;
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
