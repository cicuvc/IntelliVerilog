using IntelliVerilog.Core.Runtime.Native;
using IntelliVerilog.Core.Runtime.Services;
using SharpPdb.Windows;
using SharpPdb.Windows.SymbolRecords;
using SharpPdb.Windows.TPI;
using SharpPdb.Windows.TypeRecords;
using SharpUtilities;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.Runtime.Injection {
    public class PdbTypeScope: IRuntimeTypeInfoService {
        protected TpiStream m_Stream;
        protected PdbLookupScope m_Scope;
        protected ClassRecord m_TypeRecord;
        protected FieldListRecord m_FieldList;
        protected List<(ulong, PdbTypeScope)> m_BaseType = new();
        protected Dictionary<string, OneMethodRecord> m_MethodTable = new();
        public PdbTypeScope(PdbLookupScope scope, TpiStream tpi, ClassRecord type) {
            m_Stream = tpi;
            m_Scope = scope;
            m_TypeRecord = type;

            m_FieldList = (FieldListRecord)m_Stream[type.FieldList];
            foreach (var i in m_FieldList.Fields) {
                if (i is OneMethodRecord method) {
                    m_MethodTable.Add(method.Name.String, method);
                }
                if (i is OverloadedMethodRecord overload) {
                    var overloadList = (MethodOverloadListRecord)m_Stream[overload.MethodList];
                    var index = 0;
                    foreach (var j in overloadList.Methods) {
                        m_MethodTable.Add($"{j.Name.String ?? overload.Name.String}#{index++}", j);
                    }

                }
                if (i is DataMemberRecord dxg) {
                    if (dxg.Name.String.Contains("vftable")) {
                        Debugger.Break();
                    }
                }
                if (i is BaseClassRecord baseClass) {
                    var baseClassType = (ClassRecord)tpi[baseClass.Type];
                    var baseClassScope = m_Scope.FindType(baseClassType.Name.String);
                    if (baseClassScope != null) {
                        m_BaseType.Add((baseClass.Offset, baseClassScope));
                    }

                }
            }
        }

        public bool GetFieldOffset(string name, out uint offset) {
            var result = LookupFieldOffset(name, out var offset_);
            offset = (uint)offset_;
            return result;
        }

        public bool GetVirtualMethodOffset(string name, out uint offset) {
            var result = LookupVtableOffset(name, out var offset_);
            offset = (uint)offset_;
            return result;
        }

        public bool LookupFieldOffset(string name, out ulong offset) {
            var fieldList = (FieldListRecord)m_Stream[m_TypeRecord.FieldList];
            foreach (var i in fieldList.Fields) {
                if (i is DataMemberRecord fieldMember) {
                    Console.WriteLine(fieldMember.Name);
                    if (fieldMember.Name.String.Equals(name)) {
                        offset = fieldMember.FieldOffset;
                        return true;
                    }
                }
            }
            offset = 0;
            return false;
        }
        public bool LookupVtableOffset(string name, out ulong offset) {
            if (m_MethodTable.ContainsKey(name)) {
                var tag = m_MethodTable[name];
                var baseType = m_Stream[tag.Type];
                offset = (ulong)tag.VFTableOffset;
                return true;
            } else {
                foreach (var i in m_BaseType) {
                    if (i.Item2.LookupVtableOffset(name, out offset)) {
                        offset += i.Item1;
                        return true;
                    }
                }
            }
            offset = 0;
            return false;

        }
    }
    public class PdbStreamAdapter : IBinaryReader {
        public long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public long Length => throw new NotImplementedException();

        public long BytesRemaining => throw new NotImplementedException();

        public IBinaryReader Duplicate() {
            throw new NotImplementedException();
        }

        public void Move(uint bytes) {
            throw new NotImplementedException();
        }

        public MemoryBuffer ReadBuffer(uint length) {
            throw new NotImplementedException();
        }

        public byte ReadByte() {
            throw new NotImplementedException();
        }

        public unsafe void ReadBytes(byte* bytes, uint count) {
            throw new NotImplementedException();
        }

        public StringReference ReadCString() {
            throw new NotImplementedException();
        }

        public StringReference ReadCStringWide() {
            throw new NotImplementedException();
        }

        public int ReadInt() {
            throw new NotImplementedException();
        }

        public long ReadLong() {
            throw new NotImplementedException();
        }

        public short ReadShort() {
            throw new NotImplementedException();
        }

        public uint ReadUint() {
            throw new NotImplementedException();
        }

        public ulong ReadUlong() {
            throw new NotImplementedException();
        }

        public ushort ReadUshort() {
            throw new NotImplementedException();
        }
    }
    public class PdbLookupScope :IRuntimeDebugInfoService{
        protected ImmutableDictionary<string, PdbFile> m_LoadedPdb = ImmutableDictionary<string, PdbFile>.Empty;
        public PdbLookupScope(params KeyValuePair<string, string>[] pdbFiles) {
            m_LoadedPdb = pdbFiles.Select(e => new KeyValuePair<string, PdbFile>(e.Key, new PdbFile(e.Value))).ToImmutableDictionary();
        }

        public PdbTypeScope? FindType(string typeName) {
            foreach (var i in m_LoadedPdb.Values) {
                foreach (var j in i.TpiStream.GetIndexes(TypeLeafKind.LF_CLASS)) {
                    var type = i.TpiStream[j] as ClassRecord;

                    if (type!.Name.String.Equals(typeName)) {
                        if (type!.Options.HasFlag(ClassOptions.ForwardReference)) {
                            continue;
                        }
                        return new PdbTypeScope(this, i.TpiStream, type);
                    }
                }
            }
            return null;
        }
        public ulong FindGlobalFunction(string typeName) {
            foreach (var i in m_LoadedPdb) {
                for (var j = 0; j < i.Value.PublicsStream.PublicSymbols.Count; j++) {
                    var pg = i.Value.PublicsStream.PublicSymbols[j];

                    var demangled = CxxDemangle.DemangleName(pg.Name.String);
                    if (demangled.Equals(typeName)) {
                        var module = Process.GetCurrentProcess().Modules.OfType<ProcessModule>().Where(e => e.ModuleName.StartsWith(i.Key)).First();
                        return i.Value.FindRelativeVirtualAddress(pg.Segment, pg.Offset) + (ulong)module.BaseAddress;
                    }
                }
            }
            return 0;
        }
        public ulong FindStaticFields(string typeName) {
            foreach (var i in m_LoadedPdb) {

                for (var j = 0; j < i.Value.GlobalsStream.Symbols.Count; j++) {
                    var symbol = i.Value.GlobalsStream.Symbols[j];
                    if (symbol is DataSymbol tag) {
                        if (tag?.Name.String.Equals(typeName) ?? false) {
                            var module = Process.GetCurrentProcess().Modules.OfType<ProcessModule>().Where(e => e.ModuleName.StartsWith(i.Key)).First();
                            return i.Value.FindRelativeVirtualAddress(tag.Segment, tag.Offset) + (ulong)module.BaseAddress;
                        }
                        continue;
                        //Console.WriteLine(tag?.Name.String);
                    }

                    //Console.WriteLine(symbol.GetType().Name);
                    //
                }
            }
            return 0;
        }

        public nint FindGlobalFunctionEntry(string name) {
            return (nint)FindGlobalFunction(name);
        }

        public nint FindGlobalVariableAddress(string name) {
            return (nint)FindStaticFields(name);
        }

        IRuntimeTypeInfoService? IRuntimeDebugInfoService.FindType(string name) {
            return FindType(name);
        }
    }
}
