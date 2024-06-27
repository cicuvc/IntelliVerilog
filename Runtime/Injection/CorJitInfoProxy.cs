using IntelliVerilog.Core.Runtime.Services;
using IntelliVerilog.Core.Runtime.Unsafe;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.Runtime.Injection {
    // TODO: Add multi-thread support
    public unsafe class CorJitInfoProxy {
        private static object m_ProxyLock = new();
        private static object m_CompilationLock = new();
        private static int m_ProxyRefCount = 0;
        private static bool m_CompilationRunning = false;

        public static bool ProxyActivated {
            get {
                lock (m_ProxyLock) return m_ProxyRefCount > 0;
            }
        }

        //public static void NotifyCompilationStart() {
        //    lock (m_ProxyLock) {
        //        if (m_ProxyRefCount > 0) {

        //        }
        //    }
        //}
        //public static void NotifyCompilationEnd() {

        //}


        private static CorJitInfoProxy? m_CurrentProxy = null;

        private nint m_RawEEInfo;
        private nint m_RawVtbl;
        private List<(nint handle, nint typeHandle,  uint token)> m_TokenList = new();
        private delegate*unmanaged[Cdecl]<nint, CORINFO_RESOLVED_TOKEN*, void> m_ResolveToken;
        private delegate* unmanaged[Cdecl]<nint, CORINFO_RESOLVED_TOKEN*, void> m_NewResolveToken;

        private uint m_ResolveTokenOffset;
        private uint m_TryResolveTokenOffset = uint.MaxValue;
        static CorJitInfoProxy() {
            foreach (var i in typeof(CorJitInfoProxy).GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.NonPublic)) {
                RuntimeHelpers.PrepareMethod(i.MethodHandle);
            }
        }
        public CorJitInfoProxy(nint rawObject) {
            var debugInfoService = IntelliVerilogLocator.GetService<IRuntimeDebugInfoService>()!;
            var jitInfoType = debugInfoService.FindType("ICorJitInfo")!;

            Debug.Assert(jitInfoType.GetVirtualMethodOffset("resolveToken", out m_ResolveTokenOffset));
            jitInfoType.GetVirtualMethodOffset("tryResolveToken", out m_TryResolveTokenOffset);

            m_RawEEInfo = rawObject;
            m_RawVtbl = *(nint*)rawObject;

            m_NewResolveToken = (delegate* unmanaged[Cdecl]<nint, CORINFO_RESOLVED_TOKEN*, void>)(&ResolveToken);
            m_NewResolveToken(nint.Zero, null); // Initialize GC transition code


        }
        
        public void EnterProxy() {
            m_CurrentProxy = this;

            lock (m_ProxyLock) m_ProxyRefCount++;

            EnableProxy();
        }
        private unsafe static nint WritePointer(nint address, nint value) {
            var pageBase = (byte*)(((nint)address) & 0xFFFFFFFFF000);
            var oldProtection = MemoryAPI.API.SetProtection(new(pageBase, 4096), ProtectMode.Read | ProtectMode.Write);
            var oldValue = *(nint*)address;
            *((nint*)address) = value;
            MemoryAPI.API.SetProtection(new(pageBase, 4096), oldProtection);

            return oldValue;
        }
        private void EnableProxy() {
            if (m_CurrentProxy == null) return;
            m_ResolveToken = (delegate* unmanaged[Cdecl]<nint, CORINFO_RESOLVED_TOKEN*, void>)WritePointer((nint)(m_RawVtbl + m_ResolveTokenOffset), (nint)m_NewResolveToken);
        }
        private void DisableProxy() {
            if (m_CurrentProxy == null) return;
            m_NewResolveToken = (delegate* unmanaged[Cdecl]<nint, CORINFO_RESOLVED_TOKEN*, void>)WritePointer((nint)(m_RawVtbl + m_ResolveTokenOffset), (nint)m_ResolveToken);
        }
        public void ExitProxy() {
            DisableProxy();

            lock (m_ProxyLock) m_ProxyRefCount--;

            m_CurrentProxy = null;
        }
        public uint AllocateToken(Type type) {
            var newToken = 0xFF000000u | (uint)m_TokenList.Count;
            m_TokenList.Add((type.TypeHandle.Value, nint.Zero, newToken));
            return newToken;
        }
        public uint AllocateToken(MethodInfo method) {
            var newToken = 0xFF000000u | (uint)m_TokenList.Count;
            m_TokenList.Add((method.MethodHandle.Value,method.DeclaringType!.TypeHandle.Value, newToken));
            return newToken;
        }
        public MethodBase ResolveMethod(Module module, uint token) {
            var index = m_TokenList.FindIndex((e) => e.token == token);

            if (index >= 0) {
                var (handle, typeHandle, tok) = m_TokenList[index];
                return MethodBase.GetMethodFromHandle(RuntimeMethodHandle.FromIntPtr(handle), RuntimeTypeHandle.FromIntPtr(typeHandle))!;
            }
            return module.ResolveMethod((int)token)!;
        }
        [UnmanagedCallersOnly(CallConvs = new Type[] { typeof(CallConvCdecl)})]
        private static void ResolveToken(nint pthis, CORINFO_RESOLVED_TOKEN *tokenResolve) {
            if (pthis == nint.Zero) return;
            Debug.Assert(m_CurrentProxy != null);

            m_CurrentProxy.DisableProxy();
            RuntimeInjection.DisableProxy();


            var proxy = m_CurrentProxy;

            var queryToken = tokenResolve->token;
            var index = proxy.m_TokenList.FindIndex((e) => e.token == queryToken);

            var resolved = false;
            if(index >= 0) {
                tokenResolve->hClass = nint.Zero;
                tokenResolve->hField = nint.Zero;
                tokenResolve->hMethod = nint.Zero;
                tokenResolve->cbMethodSpec = 0;
                tokenResolve->cbTypeSpec = 0;
                tokenResolve->pMethodSpec = 0;
                tokenResolve->pTypeSpec = 0;

                switch ((CorInfoTokenKind)tokenResolve->tokenType) {
                    case CorInfoTokenKind.CORINFO_TOKENKIND_Newarr:
                    case CorInfoTokenKind.CORINFO_TOKENKIND_Box:
                    case CorInfoTokenKind.CORINFO_TOKENKIND_Class: {
                        tokenResolve->hClass = proxy.m_TokenList[index].handle;
                        
                        resolved = true;
                        break;
                    }
                    case CorInfoTokenKind.CORINFO_TOKENKIND_Field: {
                        tokenResolve->hClass = proxy.m_TokenList[index].typeHandle;
                        tokenResolve->hField = proxy.m_TokenList[index].handle;
                        resolved = true;
                        break;
                    }
                    case CorInfoTokenKind.CORINFO_TOKENKIND_Method: {
                        tokenResolve->hClass = proxy.m_TokenList[index].typeHandle;
                        tokenResolve->hMethod = proxy.m_TokenList[index].handle;
                        resolved = true;
                        break;
                    }
                    default: {
                        Console.WriteLine($"Not impl {(CorInfoTokenKind)tokenResolve->tokenType}");
                        throw new NotImplementedException();
                    }
                }
            }

            if (!resolved) {
                proxy.m_ResolveToken(pthis, tokenResolve);
                if(tokenResolve->token == 0x0A00039A) {

                    Debugger.Break();
                    Assembly.GetCallingAssembly().TryGetRawMetadata(out var blob, out var len);

                    var bb = new BlobReader((byte*)tokenResolve->pTypeSpec, (int)tokenResolve->cbTypeSpec);
                    var mm = new MetadataReader(blob, len);
                    var dec = new SignatureDecoder<string, string[]>(new SigDec(), mm, new string[] { "TData"});
                    var va2 = dec.DecodeType(ref bb, true);

                    
                }
            }

            RuntimeInjection.EnableProxy();
            m_CurrentProxy.EnableProxy();
        }
    }
    public class SigDec : ISignatureTypeProvider<string, string[]> {
        public string GetArrayType(string elementType, ArrayShape shape) {
            throw new NotImplementedException();
        }

        public string GetByReferenceType(string elementType) {
            throw new NotImplementedException();
        }

        public string GetFunctionPointerType(MethodSignature<string> signature) {
            throw new NotImplementedException();
        }

        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) {
            var sb = new StringBuilder();
            sb.Append(genericType);
            sb.Append('<');
            sb.AppendJoin(',', typeArguments);
            sb.Append('>');
            return sb.ToString();
        }

        public string GetGenericMethodParameter(string[] genericContext, int index) {
            throw new NotImplementedException();
        }

        public string GetGenericTypeParameter(string[] genericContext, int index) {
            return genericContext[index];
        }

        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) {
            throw new NotImplementedException();
        }

        public string GetPinnedType(string elementType) {
            throw new NotImplementedException();
        }

        public string GetPointerType(string elementType) {
            throw new NotImplementedException();
        }

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) {
            throw new NotImplementedException();
        }

        public string GetSZArrayType(string elementType) {
            throw new NotImplementedException();
        }

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) {
            var type = reader.GetTypeDefinition(handle);
            var name = reader.GetString(type.Name);
            var ns = reader.GetString(type.Namespace);
            return $"{ns}::{name}";
        }

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) {
            var typeRef = reader.GetTypeReference(handle);
            var name = reader.GetString(typeRef.Name);
            var ns = reader.GetString(typeRef.Namespace);
            return $"{ns}::{name}";
        }

        public string GetTypeFromSpecification(MetadataReader reader, string[] genericContext, TypeSpecificationHandle handle, byte rawTypeKind) {
            throw new NotImplementedException();
        }
    }
}
