using IntelliVerilog.Core.Logging;
using SharpPdb.Native;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading.Tasks;
using TqdmSharp;

namespace IntelliVerilog.Core.Runtime.Injection {
    [MemoryPack.MemoryPackable]
    public partial class RuntimeInspectionConfiguration {
        public string SymbolServer { get; } = "http://msdl.microsoft.com/download/symbols";
    }
    public interface IRuntimeInspectionDebugInfoLocator {
        string GetDebugInfoFile(ProcessModule module);
    }
    public class RuntimeInspectionDebugInfoLocator : IRuntimeInspectionDebugInfoLocator {
        public string GetDebugInfoFile(ProcessModule module) {
            var progressBar = default(Tqdm.ProgressBar);

            var coreclrPdb = GetPdbFile(module, (current, total) => {
                if(progressBar == null) {
                    progressBar = new Tqdm.ProgressBar(useColor: true,useExpMovingAvg: false);
                    progressBar.SetLabel($"Loading debug info for {module.ModuleName}");
                }
                progressBar.Progress((int)(current/1024), (int)(total/1024));
                
            });

            while(!coreclrPdb.IsCompleted)
                coreclrPdb.Wait(20);
            progressBar?.Finish();

            return coreclrPdb.Result;
        }

        public async Task<string> GetPdbFile(ProcessModule module, Action<long, long> progress) {
            var coreclrImage = new PEReader(new FileStream(module.FileName, FileMode.Open, FileAccess.Read, FileShare.Read));
            var codeView = coreclrImage.ReadDebugDirectory().Where(e => e.Type.HasFlag(DebugDirectoryEntryType.CodeView)).First();
            var pdbInfo = coreclrImage.ReadCodeViewDebugDirectoryData(codeView);
            var pdbName = Path.ChangeExtension(module.ModuleName, "pdb");
            var server = "http://msdl.microsoft.com/download/symbols";
            var pdbSignature = $"{pdbInfo.Guid.ToString().Replace("-", "")}{pdbInfo.Age}";

            var pdbCacheName = Path.GetFullPath($"./{module.ModuleName}_{pdbSignature}.pdb");
            var pdbTempName = Path.GetFullPath($"./{module.ModuleName}_{pdbSignature}.tmp");

            if (File.Exists(pdbCacheName)) {
                IvLogger.Default.Info("RuntimeInspectionDebugInfoLocator", $"Find {pdbCacheName} for module {module.ModuleName}");
                return Path.GetFullPath(pdbCacheName);
            }

            var requestPath = $"{server}/{pdbName}/{pdbSignature}/{pdbName}";

            IvLogger.Default.Info("RuntimeInspectionDebugInfoLocator", $"Request {requestPath} for module {module.ModuleName}");

            using (var downloadSession = new HttpClient()) {
                using (var download = await downloadSession.GetAsync(requestPath, HttpCompletionOption.ResponseHeadersRead)) {
                    var length = download.Content.Headers.ContentLength ?? long.MaxValue;

                    using (var downloadStream = download.Content.ReadAsStream()) {
                        using (var pdbFile = new FileStream(pdbTempName, FileMode.Create)) {
                            var copyBuffer = new byte[4096];
                            for (var readLength = 0L; readLength < length;) {
                                var copyLength = downloadStream.Read(copyBuffer);
                                pdbFile.Write(copyBuffer, 0, copyLength);
                                readLength += copyLength;
                                progress(readLength, length);
                            }
                        }
                    }

                }
            }
            File.Move(pdbTempName, pdbCacheName);
            return Path.GetFullPath(pdbCacheName);
        }
    }
}
