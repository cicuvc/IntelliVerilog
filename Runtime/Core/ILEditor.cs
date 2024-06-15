using IntelliVerilog.Core.DataTypes;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;

namespace IntelliVerilog.Core.Runtime.Core {
    public class ILEditor : IEnumerable<(ILOpCode code, long operand)> {
        private static Dictionary<ILOpCode, OpCode> m_OpCodeMap = new();
        private static Dictionary<ILOpCode, int> m_OpCodeSizeMap = new();
        private static Dictionary<ILOpCode, ILOpCode> m_BranchExtensionMap = new() {
            { ILOpCode.Beq_s, ILOpCode.Beq },
            { ILOpCode.Bge_s, ILOpCode.Bge },
            { ILOpCode.Bge_un_s, ILOpCode.Bge_un },
            { ILOpCode.Ble_s, ILOpCode.Ble },
            { ILOpCode.Ble_un_s, ILOpCode.Ble_un },
            { ILOpCode.Bne_un_s, ILOpCode.Bne_un },
            { ILOpCode.Blt_s, ILOpCode.Blt },
            { ILOpCode.Blt_un_s, ILOpCode.Blt_un },
            { ILOpCode.Bgt_s, ILOpCode.Bgt },
            
           
            { ILOpCode.Br_s, ILOpCode.Br },
            { ILOpCode.Brtrue_s, ILOpCode.Brtrue },
            { ILOpCode.Brfalse_s, ILOpCode.Brfalse },
        };
        public static OpCode GetOpCodeInfo(ILOpCode opcode) => m_OpCodeMap[opcode];
        static ILEditor() {
            foreach (var i in typeof(OpCodes).GetFields()) {
                var value = (OpCode)(i.GetValue(null) ?? default(OpCode));
                m_OpCodeMap.Add((ILOpCode)value.Value, value);

                switch (value.OperandType) {
                    case OperandType.InlineNone: {
                        m_OpCodeSizeMap.Add((ILOpCode)value.Value, 0);
                        break;
                    }
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar: {
                        m_OpCodeSizeMap.Add((ILOpCode)value.Value, 1);
                        break;
                    }
                    case OperandType.InlineSwitch: {
                        m_OpCodeSizeMap.Add((ILOpCode)value.Value, 4);
                        break;
                    }

                    case OperandType.InlineVar: {
                        m_OpCodeSizeMap.Add((ILOpCode)value.Value, 2);
                        break;
                    }
                    case OperandType.ShortInlineR:
                    case OperandType.InlineBrTarget:
                    case OperandType.InlineField:
                    case OperandType.InlineI:
                    case OperandType.InlineMethod:
                    case OperandType.InlineSig:
                    case OperandType.InlineString:
                    case OperandType.InlineTok:
                    case OperandType.InlineType: {
                        m_OpCodeSizeMap.Add((ILOpCode)value.Value, 4);
                        break;
                    }
                    case OperandType.InlineI8:
                    case OperandType.InlineR: {
                        m_OpCodeSizeMap.Add((ILOpCode)value.Value, 8);
                        break;
                    }
                    default: throw new NotImplementedException();
                }
            }
            //m_OpCodeSizeMap[ILOpCode.Ldloca] = 4;
        }
        protected ImmutableArray<byte> m_ILCodes = ImmutableArray<byte>.Empty;
        protected List<(ILOpCode code, long operand)> m_Instructions = new();
        protected int m_InstOffset;
        public int InstOffset => m_InstOffset;
        public int Count => m_Instructions.Count;

        public (ILOpCode code, long operand) this[int index] {
            get => m_Instructions[index];
            set => m_Instructions[index] = value;
        }
        public ILEditor(ReadOnlySpan<byte> template) {
            ParseTemplate(template);

            m_InstOffset = template.Length; ;

        }
        protected unsafe void ParseTemplate(ReadOnlySpan<byte> template) {
            var offsetMap = (Span<int>)stackalloc int[template.Length];
            fixed (byte* pTemplate = template) {
                for (var i = 0; i < template.Length;) {
                    var offset = i;
                    var ilOpcode = (ILOpCode)template[i++];
                    if (((uint)ilOpcode) >= 249) {
                        ilOpcode = (ILOpCode)((((uint)ilOpcode) << 8) + template[i++]);
                    }
                    if (!m_OpCodeMap.ContainsKey(ilOpcode)) {
                        Debugger.Break();
                    }
                    var opcode = m_OpCodeMap[ilOpcode];
                    offsetMap[m_Instructions.Count] = offset;

                    switch (opcode.OperandType) {
                        case OperandType.InlineNone: {
                            
                            m_Instructions.Add((ilOpcode, 0));
                            break;
                        }
                        case OperandType.ShortInlineBrTarget: {
                            var targetOffset = (sbyte)pTemplate[i++];
                            Debug.Assert(i + targetOffset >= 0);
                            Debug.Assert(i + targetOffset < template.Length);
                            m_Instructions.Add((ilOpcode, i + targetOffset));
                            break;
                        }
                        case OperandType.ShortInlineI:
                        case OperandType.ShortInlineVar: {
                            m_Instructions.Add((ilOpcode, pTemplate[i++]));
                            break;
                        }
                        case OperandType.InlineSwitch: {
                            var branches = *(int*)(&pTemplate[i]);
                            m_Instructions.Add((ilOpcode, branches));
                            i += 4 + branches * 4;
                            break;
                        }


                        case OperandType.InlineVar: {
                            m_Instructions.Add((ilOpcode, *(short*)(&pTemplate[i])));
                            i += 2;
                            break;
                        }
                        
                        case OperandType.InlineBrTarget: {
                            var targetOffset = *(int*)(&pTemplate[i]);
                            i += 4;

                            Debug.Assert(targetOffset + i >= 0);
                            Debug.Assert(targetOffset + i < template.Length);
                            m_Instructions.Add((ilOpcode, targetOffset + i));
                            
                            break;
                        }
                        case OperandType.ShortInlineR:
                        case OperandType.InlineField:
                        case OperandType.InlineI:
                        case OperandType.InlineMethod:
                        case OperandType.InlineSig:
                        case OperandType.InlineString:
                        case OperandType.InlineTok:
                        case OperandType.InlineType: {
                            m_Instructions.Add((ilOpcode, *(int*)(&pTemplate[i])));
                            i += 4;
                            break;
                        }
                        case OperandType.InlineI8:
                        case OperandType.InlineR: {
                            m_Instructions.Add((ilOpcode, *(long*)(&pTemplate[i])));
                            i += 8;
                            break;
                        }
                        default: throw new NotImplementedException();
                    }
                }
            }
            offsetMap = offsetMap.Slice(0, m_Instructions.Count);
            for (var i = 0; i < m_Instructions.Count; i++) {
                var codeDesc = m_OpCodeMap[m_Instructions[i].code];
                if(codeDesc.FlowControl == FlowControl.Cond_Branch || codeDesc.FlowControl == FlowControl.Branch) {
                    var target = offsetMap.BinarySearch((int)m_Instructions[i].operand);
                    
                    Debug.Assert(target >= 0);

                    var inst = m_Instructions[i];
                    inst.operand = target;
                    m_Instructions[i] = inst;
                }

            }
        }
        public void Emit(ILOpCode opcode) {
            m_Instructions.Add((opcode, 0));
            m_InstOffset += ((int)opcode > 255 ? 2 : 1) + m_OpCodeSizeMap[opcode];
        }
        public int Emit(ILOpCode opcode, nint value) {
            m_Instructions.Add((opcode, (long)value));
            m_InstOffset += ((int)opcode > 255 ? 2 : 1) + m_OpCodeSizeMap[opcode];
            return m_Instructions.Count - 1;
        }
        public int Emit(ILOpCode opcode, long value) {
            m_Instructions.Add((opcode, value));
            m_InstOffset += ((int)opcode > 255 ? 2 : 1) + m_OpCodeSizeMap[opcode];
            return m_Instructions.Count - 1;
        }
        public int Emit(ILOpCode opcode, short value) {
            m_Instructions.Add((opcode, value));
            m_InstOffset += ((int)opcode > 255 ? 2 : 1) + m_OpCodeSizeMap[opcode];
            return m_Instructions.Count - 1;
        }
        public int Emit(ILOpCode opcode, int value) {
            m_Instructions.Add((opcode, value));
            m_InstOffset += ((int)opcode > 255 ? 2 : 1) + m_OpCodeSizeMap[opcode];
            return m_Instructions.Count - 1;
        }

        public IEnumerator<(ILOpCode code, long operand)> GetEnumerator() {
            return m_Instructions.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return m_Instructions.GetEnumerator();
        }
        protected int GetCodeSize() {
            var size = 0;
            foreach (var i in m_Instructions) {
                size += ((int)i.code > 255) ? 2 : 1;
                size += m_OpCodeSizeMap[i.code];
                if (i.code == ILOpCode.Switch) size += (int)i.operand * 4;
            }
            return size;
        }
        public unsafe byte[] GenerateCode() {
            var size = 0;
            var preEmitInst = (Span<(ILOpCode code, long operand)>)stackalloc (ILOpCode code, long operand)[m_Instructions.Count];
            var preArrangeOffset = (Span<int>)stackalloc int[m_Instructions.Count];
            var finalOffset = (Span<int>)stackalloc int[m_Instructions.Count];
            // pre-arrange pass
            for (var i=0;i< m_Instructions.Count; i++) {
                var inst = m_Instructions[i];
                var opCodeDesc = m_OpCodeMap[inst.code];

                if(opCodeDesc.FlowControl==(FlowControl.Branch) || opCodeDesc.FlowControl==(FlowControl.Cond_Branch)) {
                    inst.code = opCodeDesc.OperandType == OperandType.ShortInlineBrTarget ? m_BranchExtensionMap[inst.code] : inst.code;
                }

                var codeSize = ((int)inst.code > 255) ? 2 : 1; 
                var operandSize = m_OpCodeSizeMap[inst.code];
                if (inst.code == ILOpCode.Switch) 
                    operandSize += (int)inst.operand * 4;

                preArrangeOffset[i] = size;
                size += codeSize + operandSize;
            }

            var currentOffset = 0;
            for (var i = 0; i < m_Instructions.Count; i++) {
                var inst = m_Instructions[i];
                var opCodeDesc = m_OpCodeMap[inst.code];

                // TODO: Fix br compression
                if (opCodeDesc.FlowControl == (FlowControl.Branch) || opCodeDesc.FlowControl == (FlowControl.Cond_Branch)) {
                    inst.code = opCodeDesc.OperandType == OperandType.ShortInlineBrTarget ? m_BranchExtensionMap[inst.code] : inst.code;


                    var targetOffset = preArrangeOffset[(int)inst.operand];
                    var inst8bTotalLength = (((int)inst.code > 255) ? 2 : 1) + m_OpCodeSizeMap[inst.code];
                    var endOffset = currentOffset + inst8bTotalLength;
                    var displacement = targetOffset - endOffset;
                    //if (displacement < -128 || displacement > 127) {
                    //}
                    inst.operand = displacement;
                }

                var codeSize = ((int)inst.code > 255) ? 2 : 1;
                var operandSize = m_OpCodeSizeMap[inst.code];
                if (inst.code == ILOpCode.Switch)
                    operandSize += (int)inst.operand * 4;

                finalOffset[i] = currentOffset;
                currentOffset += codeSize + operandSize;
                preEmitInst[i] = (inst.code, inst.operand);
            }

            var outputBuffer = new byte[currentOffset];
            fixed(byte *pBuffer = outputBuffer) {
                for (var i = 0; i < preEmitInst.Length; i++) {
                    var (code, operand) = preEmitInst[i];
                    var emitOffset = finalOffset[i];
                    var operandSize = m_OpCodeSizeMap[code];

                    if (code == ILOpCode.Switch) {
                        throw new NotImplementedException();
                    } else {

                        if ((int)code > 255) {
                            pBuffer[emitOffset++] = (byte)((int)code >> 8);
                            pBuffer[emitOffset++] = (byte)((int)code & 0xFF);
                        } else {
                            pBuffer[emitOffset++] = (byte)code;
                        }

                        switch (operandSize) {
                            case 1: {
                                pBuffer[emitOffset] = (byte)operand; break;
                            }
                            case 2: {
                                *(short*)(&pBuffer[emitOffset]) = (short)operand;
                                break;
                            }
                            case 4: {
                                *(int*)(&pBuffer[emitOffset]) = (int)operand;
                                break;
                            }
                            case 8: {
                                *(long*)(&pBuffer[emitOffset]) = (long)operand;
                                break;
                            }
                        }
                    }
                }

            }


            return outputBuffer;
        }
    }
}
