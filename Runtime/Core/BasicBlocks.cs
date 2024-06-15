
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;

namespace IntelliVerilog.Core.Runtime.Core {
    public class ILDecompiler {
        public List<Blocks> BasicBlocks { get; }
        public ILDecompiler(ILEditor editor) {
            var exitBlock = new ExitBlocks();
            var blocks = new Dictionary<int, Blocks>() { { -1, exitBlock } };

            var currentBlock = new BasicBlocks();
            for (var i = 0; i < editor.Count; i++) {
                var opcode = editor[i].code;
                var opcodeInfo = ILEditor.GetOpCodeInfo(opcode);
                if(opcodeInfo.FlowControl != FlowControl.Next) {
                    var exitInstruction = opcode == ILOpCode.Ret ||
                        opcode == ILOpCode.Tail ||
                        opcode == ILOpCode.Jmp ||
                        opcode == ILOpCode.Throw;

                    var nextBlock = exitInstruction ? (Blocks)exitBlock : new BasicBlocks() { StartIndex = i + 1 };

                    currentBlock.ConnectDefaultExit(nextBlock);
                    currentBlock.ExitInstruction = editor[i].code;
                    blocks.Add(currentBlock.StartIndex, currentBlock);

                    currentBlock = exitInstruction ? new BasicBlocks() { StartIndex = i + 1 } : (BasicBlocks)nextBlock;
                } else {
                    currentBlock.Instructions.Add(editor[i]);
                }
            }

            foreach(var (index, block) in blocks) {
                if(block is BasicBlocks basicBlock) {
                    var endInstOffset = basicBlock.StartIndex + basicBlock.Instructions.Count;
                    var endInst = editor[endInstOffset];
                    var opcodeInfo = ILEditor.GetOpCodeInfo(endInst.code);
                    if(opcodeInfo.FlowControl == FlowControl.Branch || opcodeInfo.FlowControl == FlowControl.Cond_Branch) {
                        basicBlock.ConnectBranchExit(blocks[(int)endInst.operand]);
                    }
                    if(endInst.code == ILOpCode.Br || endInst.code == ILOpCode.Br_s) {
                        basicBlock.ConnectDefaultExit(exitBlock);
                    }
                }
            }

            BasicBlocks = blocks.Values.ToList();
        }
        public void DeduceCFG() {
            while(BasicBlocks.Count > 1) {
                var success = false;
                foreach(var i in BasicBlocks) {
                    if(i.PrevBlocks.Count == 1 && i.ExitDegree == 1 && i.DefaultExit != null) {
                        if(i.DefaultExit.PrevBlocks.Count == 1 && i.DefaultExit.ExitDegree == 1) {
                            var preBlock = i;
                            var postBlock = i.DefaultExit;

                            var newBlock = new SequentialBlock(preBlock, postBlock);

                            preBlock.PrevBlocks[0].FixExits(preBlock, newBlock);
                            newBlock.ConnectDefaultExit(postBlock.DefaultExit);
                            postBlock.BreakDefaultExit();

                            BasicBlocks.Remove(preBlock);
                            BasicBlocks.Remove(postBlock);
                            BasicBlocks.Add(newBlock);

                            success = true;
                            break;
                        }
                    }
                }
                if (success) continue;

                foreach (var i in BasicBlocks) {
                    
                }
            }
        }
    }
    public class SequentialBlock: Blocks {
        public override int ExitDegree => 1;
        public Blocks PreBlock { get; }
        public Blocks PostBlock { get; }
        public SequentialBlock(Blocks preBlock, Blocks postBlock) {
            PreBlock = preBlock;
            PostBlock = postBlock;
        }
    }
    public class LoopBlock: Blocks {
        public override int ExitDegree => 1;
        public Blocks CheckBlock { get; }
        public Blocks LoopBody { get; }
    }
    public class CondBlock : Blocks {
        public override int ExitDegree => 1;
        public Blocks ConditionBlock { get; }
        public Blocks TrueBlock { get; }
        public Blocks FalseBlock { get; }
        public Blocks MergeBlock { get; }
    }
    public abstract class Blocks {
        public int StartIndex { get; set; }
        public List<Blocks> PrevBlocks { get; } = new();
        public Blocks? DefaultExit { get; protected set; }
        public abstract int ExitDegree { get; }
        public virtual void FixExits(Blocks oldBlock, Blocks newBlock) {
            if (DefaultExit == oldBlock) ConnectDefaultExit(newBlock);
        }

        public void ConnectDefaultExit(Blocks next) {
            if (DefaultExit != null) BreakDefaultExit();
            next.PrevBlocks.Add(this);
            DefaultExit = next;
        }
        public void BreakDefaultExit() {
            DefaultExit?.PrevBlocks.Remove(this);
            DefaultExit = null;
        }
    }
    public class ExitBlocks: Blocks {
        public override int ExitDegree => 0;
    }
    public class BasicBlocks: Blocks {
        public Blocks? BranchExit { get; protected set; }
        public void ConnectBranchExit(Blocks next) {
            if (BranchExit != null) BreakBranchExit();
            next.PrevBlocks.Add(this);
            BranchExit = next;
        }
        public void BreakBranchExit() {
            BranchExit?.PrevBlocks.Remove(this);
            BranchExit = null;
        }

        public override int ExitDegree => (DefaultExit != null ? 1 : 0) + (BranchExit != null ? 1 : 0);
        public ILOpCode ExitInstruction { get; set; }
        public List<(ILOpCode opcode, long operand)> Instructions { get; } = new();

        /// <summary>
        /// Make instruction at index the last instruction of current block. The latter part
        /// is splited and return
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        public BasicBlocks SplitBlock(int index) {
            var newBlock = new BasicBlocks() { 
                DefaultExit = DefaultExit,
                BranchExit = BranchExit,
                ExitInstruction = ExitInstruction
            };
            for(var i=index+1;i< Instructions.Count; i++) {
                newBlock.Instructions.Add(Instructions[i]);
            }
            Instructions.RemoveRange(index, Instructions.Count - index);

            ExitInstruction = ILOpCode.Br;
            DefaultExit = newBlock;
            BranchExit = null;

            newBlock.PrevBlocks.Add(this);

            return newBlock;
        }
        public override void FixExits(Blocks oldBlock, Blocks newBlock) {
            base.FixExits(oldBlock, newBlock);
            if (BranchExit == oldBlock) ConnectBranchExit(newBlock);
        }
    }
}
