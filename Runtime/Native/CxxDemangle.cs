using IntelliVerilog.Core.Runtime.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.Runtime.Native {
    public enum DemangleFlags:ushort {
        UNDNAME_COMPLETE = 0x0000,
        UNDNAME_NO_LEADING_UNDERSCORES = 0x0001,
        UNDNAME_NO_MS_KEYWORDS = 0x0002,
        UNDNAME_NO_FUNCTION_RETURNS = 0x0004,
        UNDNAME_NO_ALLOCATION_MODEL = 0x0008,
        UNDNAME_NO_ALLOCATION_LANGUAGE = 0x0010,
        UNDNAME_NO_MS_THISTYPE = 0x0020,
        UNDNAME_NO_CV_THISTYPE = 0x0040,
        UNDNAME_NO_THISTYPE = 0x0060,
        UNDNAME_NO_ACCESS_SPECIFIERS = 0x0080,
        UNDNAME_NO_THROW_SIGNATURES = 0x0100,
        UNDNAME_NO_MEMBER_TYPE = 0x0200,
        UNDNAME_NO_RETURN_UDT_MODEL = 0x0400,
        UNDNAME_32_BIT_DECODE = 0x0800,
        UNDNAME_NAME_ONLY = 0x1000,
        UNDNAME_NO_ARGUMENTS = 0x2000,
        UNDNAME_NO_SPECIAL_SYMS = 0x4000,
    }
    public unsafe class CxxDemangle: IDemangleSerivce {
        private static void* m_AllocFunction;
        private static void* m_FreeFunction;
        private static delegate* unmanaged[Cdecl]<ref byte, ref byte, int, void*, void*, ushort, byte*> m_DemangleName;
        public static CxxDemangle Instance { get; } = new();
        static CxxDemangle() {
            var runtimeLibrary = NativeLibrary.Load("msvcrt");

            m_DemangleName = (delegate* unmanaged[Cdecl]<ref byte, ref byte, int, void*, void*, ushort, byte*>)NativeLibrary.GetExport(runtimeLibrary, "__unDName");
            m_AllocFunction = (void*)NativeLibrary.GetExport(runtimeLibrary, "malloc");
            m_FreeFunction = (void*)NativeLibrary.GetExport(runtimeLibrary, "free");
        }
        private static string DemangleInternal(string name, DemangleFlags flag) {
            var length = UTF8Encoding.Default.GetByteCount(name);
            var inputBuffer = (Span<byte>)stackalloc byte[length + 1];
            var outputBuffer = (Span<byte>)stackalloc byte[length * 4];
            UTF8Encoding.Default.GetBytes(name, inputBuffer);
            inputBuffer[length] = 0;

            m_DemangleName(ref outputBuffer[0], ref inputBuffer[0], length * 4, m_AllocFunction, m_FreeFunction, (ushort)flag);

            var resultLength = ((ReadOnlySpan<byte>)outputBuffer).IndexOf((byte)0);
            return UTF8Encoding.Default.GetString(outputBuffer[0..resultLength]);
        }
        public static string DemangleName(string fullSignature) {
            return DemangleInternal(fullSignature, DemangleFlags.UNDNAME_NAME_ONLY);
        }

        public string GetDemangledName(string signature) => DemangleName(signature);
    }
}
