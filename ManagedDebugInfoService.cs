using SharpPdb.Managed;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.PortableExecutable;

namespace IntelliVerilog.Core;

public class ManagedDebugInfoService {
    public static ManagedDebugInfoService Instance { get; } = new();
    protected Dictionary<System.Reflection.Module, IPdbFile?> m_PdbCache = new();
    private static string? LookupLocalName(IPdbLocalScope scope, int index) {
        foreach (var i in scope.Variables) {
            if (i.Index == index) return i.Name;
        }
        foreach (var i in scope.Children) {
            var result = LookupLocalName(i, index);
            if (result != null) return result;
        }
        return null;
    }
    public string? QueryLocalName(MethodBase method, int localIndex) {
        var module = method.Module;
        var defaultName = $"component_{method.MethodHandle.Value}_{localIndex}";

        if (!m_PdbCache.ContainsKey(module)) {
            using (var imageFile = module.Assembly.GetFile(module.Name)) {
                if(imageFile == null) {
                    m_PdbCache.Add(module, null);
                    return defaultName;
                }
                using var imageReader = new PEReader(imageFile);
                var debugInfo = imageReader.ReadDebugDirectory().Where(e => e.IsPortableCodeView)
                    .Select(e => imageReader.ReadCodeViewDebugDirectoryData(e)).First();
                var pdbFile = SharpPdb.Managed.PdbFileReader.OpenPdb(debugInfo.Path);
                m_PdbCache.Add(module, pdbFile);
            }
        }

        var pdb = m_PdbCache[module];
        if (pdb == null) return defaultName;
        var function = pdb.GetFunctionFromToken(method.MetadataToken);
        foreach (var i in function.LocalScopes) {
            var result = LookupLocalName(i, localIndex);
            if (result != null) return result;
        }
        return null;
    }
}
