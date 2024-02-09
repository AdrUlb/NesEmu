using Microsoft.Win32;
using RenderThing.Bindings.Gl;
using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;

namespace NesEmu;

// https://www.pagetable.com/?p=410
// https://www.masswerk.at/6502/6502_instruction_set.html
// https://www.nesdev.org/obelisk-6502-guide/reference.html
// https://www.zimmers.net/anonftp/pub/cbm/documents/chipdata/64doc
internal sealed class Cpu
{
	private enum AddressingMode
	{
		Implied,
		Immediate,
		Relative,
		Accumulator,

		Zeropage,
		ZeropageXIndexed,
		ZeropageYIndexed,

		Absolute,
		AbsoluteXIndexed,
		AbsoluteYIndexed,

		Indirect,
		XIndexedIndirect,
		IndirectYIndexed,
	}

	private enum Instruction
	{
		Unknown,
		Adc,
		And,
		Asl,
		Bcc,
		Bcs,
		Beq,
		Bit,
		Bmi,
		Bne,
		Bpl,
		Brk,
		Bvc,
		Bvs,
		Clc,
		Cld,
		Cli,
		Clv,
		Cmp,
		Cpx,
		Cpy,
		Dec,
		Dex,
		Dey,
		Eor,
		Inc,
		Inx,
		Iny,
		Jam,
		Jmp,
		Jsr,
		Lda,
		Ldx,
		Ldy,
		Lsr,
		Nop,
		Ora,
		Php,
		Pha,
		Pla,
		Plp,
		Rol,
		Ror,
		Rti,
		Rts,
		Sbc,
		Sec,
		Sed,
		Sei,
		Sta,
		Stx,
		Sty,
		Tax,
		Tay,
		Tsx,
		Txa,
		Txs,
		Tya,
		IllDcp,
		IllIsc,
		IllLax,
		IllRla,
		IllRra,
		IllSax,
		IllSlo,
		IllSre
	}

	private enum InstructionType
	{
		Stack,
		Implied,
		Read,
		ReadModifyWrite,
		Write,
		InstJmp,
		InstJsr,
		Unknown
	}

	private readonly struct InstructionInfo(Instruction instruction, string mnemonic, AddressingMode addressingMode, InstructionType type = InstructionType.Unknown)
	{
		public readonly Instruction Instruction = instruction;
		public readonly string mnemonic = mnemonic;
		public readonly AddressingMode AddressingMode = addressingMode;
		public readonly InstructionType Type = type;

		public ushort Bytes => AddressingMode switch
		{
			AddressingMode.Implied => 1,
			AddressingMode.Immediate => 2,
			AddressingMode.Relative => 2,
			AddressingMode.Accumulator => 1,

			AddressingMode.Absolute => 3,
			AddressingMode.AbsoluteXIndexed => 3,
			AddressingMode.AbsoluteYIndexed => 3,

			AddressingMode.Indirect => 3,
			AddressingMode.XIndexedIndirect => 2,
			AddressingMode.IndirectYIndexed => 2,

			AddressingMode.Zeropage => 2,
			AddressingMode.ZeropageXIndexed => 2,
			AddressingMode.ZeropageYIndexed => 2,
			_ => throw new InvalidOperationException()
		};
	}

	#region Instructions

	private readonly InstructionInfo[] _instructions =
	[
		// 0x00 - 0x0F
		new(Instruction.Brk, "BRK", AddressingMode.Implied, InstructionType.Stack),
		new(Instruction.Ora, "ORA", AddressingMode.XIndexedIndirect),
		new(Instruction.Jam, "*JAM", AddressingMode.Implied, InstructionType.Implied),
		new(Instruction.IllSlo, "*SLO", AddressingMode.XIndexedIndirect),
		new(Instruction.Nop, "*NOP", AddressingMode.Zeropage, InstructionType.Read),
		new(Instruction.Ora, "ORA", AddressingMode.Zeropage, InstructionType.Read),
		new(Instruction.Asl, "ASL", AddressingMode.Zeropage, InstructionType.ReadModifyWrite),
		new(Instruction.IllSlo, "*SLO", AddressingMode.Zeropage, InstructionType.ReadModifyWrite),
		new(Instruction.Php, "PHP", AddressingMode.Implied, InstructionType.Stack),
		new(Instruction.Ora, "ORA", AddressingMode.Immediate),
		new(Instruction.Asl, "ASL", AddressingMode.Accumulator),
		new(Instruction.Unknown, "*ANC", AddressingMode.Immediate),
		new(Instruction.Nop, "*NOP", AddressingMode.Absolute, InstructionType.Read),
		new(Instruction.Ora, "ORA", AddressingMode.Absolute, InstructionType.Read),
		new(Instruction.Asl, "ASL", AddressingMode.Absolute, InstructionType.ReadModifyWrite),
		new(Instruction.IllSlo, "*SLO", AddressingMode.Absolute, InstructionType.ReadModifyWrite),
		// 0x10 - 0x1F
		new(Instruction.Bpl, "BPL", AddressingMode.Relative),
		new(Instruction.Ora, "ORA", AddressingMode.IndirectYIndexed),
		new(Instruction.Jam, "*JAM", AddressingMode.Implied, InstructionType.Implied),
		new(Instruction.IllSlo, "*SLO", AddressingMode.IndirectYIndexed),
		new(Instruction.Nop, "*NOP", AddressingMode.ZeropageXIndexed, InstructionType.Read),
		new(Instruction.Ora, "ORA", AddressingMode.ZeropageXIndexed, InstructionType.Read),
		new(Instruction.Asl, "ASL", AddressingMode.ZeropageXIndexed, InstructionType.ReadModifyWrite),
		new(Instruction.IllSlo, "*SLO", AddressingMode.ZeropageXIndexed, InstructionType.ReadModifyWrite),
		new(Instruction.Clc, "CLC", AddressingMode.Implied, InstructionType.Implied),
		new(Instruction.Ora, "ORA", AddressingMode.AbsoluteYIndexed, InstructionType.Read),
		new(Instruction.Nop, "*NOP", AddressingMode.Implied, InstructionType.Implied),
		new(Instruction.IllSlo, "*SLO", AddressingMode.AbsoluteYIndexed, InstructionType.ReadModifyWrite),
		new(Instruction.Nop, "*NOP", AddressingMode.AbsoluteXIndexed, InstructionType.Read),
		new(Instruction.Ora, "ORA", AddressingMode.AbsoluteXIndexed, InstructionType.Read),
		new(Instruction.Asl, "ASL", AddressingMode.AbsoluteXIndexed, InstructionType.ReadModifyWrite),
		new(Instruction.IllSlo, "*SLO", AddressingMode.AbsoluteXIndexed, InstructionType.ReadModifyWrite),
		// 0x20 - 0x2F
		new(Instruction.Jsr, "JSR", AddressingMode.Absolute, InstructionType.InstJsr),
		new(Instruction.And, "AND", AddressingMode.XIndexedIndirect),
		new(Instruction.Jam, "*JAM", AddressingMode.Implied, InstructionType.Implied),
		new(Instruction.IllRla, "*RLA", AddressingMode.XIndexedIndirect),
		new(Instruction.Bit, "BIT", AddressingMode.Zeropage, InstructionType.Read),
		new(Instruction.And, "AND", AddressingMode.Zeropage, InstructionType.Read),
		new(Instruction.Rol, "ROL", AddressingMode.Zeropage, InstructionType.ReadModifyWrite),
		new(Instruction.IllRla, "*RLA", AddressingMode.Zeropage, InstructionType.ReadModifyWrite),
		new(Instruction.Plp, "PLP", AddressingMode.Implied, InstructionType.Stack),
		new(Instruction.And, "AND", AddressingMode.Immediate),
		new(Instruction.Rol, "ROL", AddressingMode.Accumulator),
		new(Instruction.Unknown, "*ANC", AddressingMode.Immediate),
		new(Instruction.Bit, "BIT", AddressingMode.Absolute, InstructionType.Read),
		new(Instruction.And, "AND", AddressingMode.Absolute, InstructionType.Read),
		new(Instruction.Rol, "ROL", AddressingMode.Absolute, InstructionType.ReadModifyWrite),
		new(Instruction.IllRla, "*RLA", AddressingMode.Absolute, InstructionType.ReadModifyWrite),
		// 0x30 - 0x3F
		new(Instruction.Bmi, "BMI", AddressingMode.Relative),
		new(Instruction.And, "AND", AddressingMode.IndirectYIndexed),
		new(Instruction.Jam, "*JAM", AddressingMode.Implied, InstructionType.Implied),
		new(Instruction.IllRla, "*RLA", AddressingMode.IndirectYIndexed),
		new(Instruction.Nop, "*NOP", AddressingMode.ZeropageXIndexed, InstructionType.Read),
		new(Instruction.And, "AND", AddressingMode.ZeropageXIndexed, InstructionType.Read),
		new(Instruction.Rol, "ROL", AddressingMode.ZeropageXIndexed, InstructionType.ReadModifyWrite),
		new(Instruction.IllRla, "*RLA", AddressingMode.ZeropageXIndexed, InstructionType.ReadModifyWrite),
		new(Instruction.Sec, "SEC", AddressingMode.Implied, InstructionType.Implied),
		new(Instruction.And, "AND", AddressingMode.AbsoluteYIndexed, InstructionType.Read),
		new(Instruction.Nop, "*NOP", AddressingMode.Implied, InstructionType.Implied),
		new(Instruction.IllRla, "*RLA", AddressingMode.AbsoluteYIndexed, InstructionType.ReadModifyWrite),
		new(Instruction.Nop, "*NOP", AddressingMode.AbsoluteXIndexed, InstructionType.Read),
		new(Instruction.And, "AND", AddressingMode.AbsoluteXIndexed, InstructionType.Read),
		new(Instruction.Rol, "ROL", AddressingMode.AbsoluteXIndexed, InstructionType.ReadModifyWrite),
		new(Instruction.IllRla, "*RLA", AddressingMode.AbsoluteXIndexed, InstructionType.ReadModifyWrite),
		// 0x40 - 0x4F
		new(Instruction.Rti, "RTI", AddressingMode.Implied, InstructionType.Stack),
		new(Instruction.Eor, "EOR", AddressingMode.XIndexedIndirect),
		new(Instruction.Jam, "*JAM", AddressingMode.Implied, InstructionType.Implied),
		new(Instruction.IllSre, "*SRE", AddressingMode.XIndexedIndirect),
		new(Instruction.Nop, "*NOP", AddressingMode.Zeropage, InstructionType.Read),
		new(Instruction.Eor, "EOR", AddressingMode.Zeropage, InstructionType.Read),
		new(Instruction.Lsr, "LSR", AddressingMode.Zeropage, InstructionType.ReadModifyWrite),
		new(Instruction.IllSre, "*SRE", AddressingMode.Zeropage, InstructionType.ReadModifyWrite),
		new(Instruction.Pha, "PHA", AddressingMode.Implied, InstructionType.Stack),
		new(Instruction.Eor, "EOR", AddressingMode.Immediate),
		new(Instruction.Lsr, "LSR", AddressingMode.Accumulator),
		new(Instruction.Unknown, "ALR", AddressingMode.Immediate),
		new(Instruction.Jmp, "JMP", AddressingMode.Absolute, InstructionType.InstJmp),
		new(Instruction.Eor, "EOR", AddressingMode.Absolute, InstructionType.Read),
		new(Instruction.Lsr, "LSR", AddressingMode.Absolute, InstructionType.ReadModifyWrite),
		new(Instruction.IllSre, "*SRE", AddressingMode.Absolute, InstructionType.ReadModifyWrite),
		// 0x50 - 0x5F
		new(Instruction.Bvc, "BVC", AddressingMode.Relative),
		new(Instruction.Eor, "EOR", AddressingMode.IndirectYIndexed),
		new(Instruction.Jam, "*JAM", AddressingMode.Implied, InstructionType.Implied),
		new(Instruction.IllSre, "*SRE", AddressingMode.IndirectYIndexed),
		new(Instruction.Nop, "*NOP", AddressingMode.ZeropageXIndexed, InstructionType.Read),
		new(Instruction.Eor, "EOR", AddressingMode.ZeropageXIndexed, InstructionType.Read),
		new(Instruction.Lsr, "LSR", AddressingMode.ZeropageXIndexed, InstructionType.ReadModifyWrite),
		new(Instruction.IllSre, "*SRE", AddressingMode.ZeropageXIndexed, InstructionType.ReadModifyWrite),
		new(Instruction.Cli, "CLI", AddressingMode.Implied, InstructionType.Implied),
		new(Instruction.Eor, "EOR", AddressingMode.AbsoluteYIndexed, InstructionType.Read),
		new(Instruction.Nop, "*NOP", AddressingMode.Implied, InstructionType.Implied),
		new(Instruction.IllSre, "*SRE", AddressingMode.AbsoluteYIndexed, InstructionType.ReadModifyWrite),
		new(Instruction.Nop, "*NOP", AddressingMode.AbsoluteXIndexed, InstructionType.Read),
		new(Instruction.Eor, "EOR", AddressingMode.AbsoluteXIndexed, InstructionType.Read),
		new(Instruction.Lsr, "LSR", AddressingMode.AbsoluteXIndexed, InstructionType.ReadModifyWrite),
		new(Instruction.IllSre, "*SRE", AddressingMode.AbsoluteXIndexed, InstructionType.ReadModifyWrite),
		// 0x60 - 0x6F
		new(Instruction.Rts, "RTS", AddressingMode.Implied, InstructionType.Stack),
		new(Instruction.Adc, "ADC", AddressingMode.XIndexedIndirect),
		new(Instruction.Jam, "*JAM", AddressingMode.Implied, InstructionType.Implied),
		new(Instruction.IllRra, "*RRA", AddressingMode.XIndexedIndirect),
		new(Instruction.Nop, "*NOP", AddressingMode.Zeropage, InstructionType.Read),
		new(Instruction.Adc, "ADC", AddressingMode.Zeropage, InstructionType.Read),
		new(Instruction.Ror, "ROR", AddressingMode.Zeropage, InstructionType.ReadModifyWrite),
		new(Instruction.IllRra, "*RRA", AddressingMode.Zeropage, InstructionType.ReadModifyWrite),
		new(Instruction.Pla, "PLA", AddressingMode.Implied, InstructionType.Stack),
		new(Instruction.Adc, "ADC", AddressingMode.Immediate),
		new(Instruction.Ror, "ROR", AddressingMode.Accumulator),
		new(Instruction.Unknown, "*ARR", AddressingMode.Immediate),
		new(Instruction.Jmp, "JMP", AddressingMode.Indirect),
		new(Instruction.Adc, "ADC", AddressingMode.Absolute, InstructionType.Read),
		new(Instruction.Ror, "ROR", AddressingMode.Absolute, InstructionType.ReadModifyWrite),
		new(Instruction.IllRra, "*RRA", AddressingMode.Absolute, InstructionType.ReadModifyWrite),
		// 0x70 - 0x7F
		new(Instruction.Bvs, "BVS", AddressingMode.Relative),
		new(Instruction.Adc, "ADC", AddressingMode.IndirectYIndexed),
		new(Instruction.Jam, "*JAM", AddressingMode.Implied, InstructionType.Implied),
		new(Instruction.IllRra, "*RRA", AddressingMode.IndirectYIndexed),
		new(Instruction.Nop, "*NOP", AddressingMode.ZeropageXIndexed, InstructionType.Read),
		new(Instruction.Adc, "ADC", AddressingMode.ZeropageXIndexed, InstructionType.Read),
		new(Instruction.Ror, "ROR", AddressingMode.ZeropageXIndexed, InstructionType.ReadModifyWrite),
		new(Instruction.IllRra, "*RRA", AddressingMode.ZeropageXIndexed, InstructionType.ReadModifyWrite),
		new(Instruction.Sei, "SEI", AddressingMode.Implied, InstructionType.Implied),
		new(Instruction.Adc, "ADC", AddressingMode.AbsoluteYIndexed, InstructionType.Read),
		new(Instruction.Nop, "*NOP", AddressingMode.Implied, InstructionType.Implied),
		new(Instruction.IllRra, "*RRA", AddressingMode.AbsoluteYIndexed, InstructionType.ReadModifyWrite),
		new(Instruction.Nop, "*NOP", AddressingMode.AbsoluteXIndexed, InstructionType.Read),
		new(Instruction.Adc, "ADC", AddressingMode.AbsoluteXIndexed, InstructionType.Read),
		new(Instruction.Ror, "ROR", AddressingMode.AbsoluteXIndexed, InstructionType.ReadModifyWrite),
		new(Instruction.IllRra, "*RRA", AddressingMode.AbsoluteXIndexed, InstructionType.ReadModifyWrite),
		// 0x80 - 0x8F
		new(Instruction.Nop, "*NOP", AddressingMode.Immediate),
		new(Instruction.Sta, "STA", AddressingMode.XIndexedIndirect),
		new(Instruction.Nop, "*NOP", AddressingMode.Immediate),
		new(Instruction.IllSax, "*SAX", AddressingMode.XIndexedIndirect),
		new(Instruction.Sty, "STY", AddressingMode.Zeropage, InstructionType.Write),
		new(Instruction.Sta, "STA", AddressingMode.Zeropage, InstructionType.Write),
		new(Instruction.Stx, "STX", AddressingMode.Zeropage, InstructionType.Write),
		new(Instruction.IllSax, "*SAX", AddressingMode.Zeropage, InstructionType.Write),
		new(Instruction.Dey, "DEY", AddressingMode.Implied, InstructionType.Implied),
		new(Instruction.Nop, "*NOP", AddressingMode.Immediate),
		new(Instruction.Txa, "TXA", AddressingMode.Implied, InstructionType.Implied),
		new(Instruction.Unknown, "*ANE", AddressingMode.Immediate),
		new(Instruction.Sty, "STY", AddressingMode.Absolute, InstructionType.Write),
		new(Instruction.Sta, "STA", AddressingMode.Absolute, InstructionType.Write),
		new(Instruction.Stx, "STX", AddressingMode.Absolute, InstructionType.Write),
		new(Instruction.IllSax, "*SAX", AddressingMode.Absolute, InstructionType.Write),
		// 0x90 - 0x9F
		new(Instruction.Bcc, "BCC", AddressingMode.Relative),
		new(Instruction.Sta, "STA", AddressingMode.IndirectYIndexed),
		new(Instruction.Jam, "*JAM", AddressingMode.Implied, InstructionType.Implied),
		new(Instruction.Unknown, "*SHA", AddressingMode.IndirectYIndexed),
		new(Instruction.Sty, "STY", AddressingMode.ZeropageXIndexed, InstructionType.Write),
		new(Instruction.Sta, "STA", AddressingMode.ZeropageXIndexed, InstructionType.Write),
		new(Instruction.Stx, "STX", AddressingMode.ZeropageYIndexed, InstructionType.Write),
		new(Instruction.IllSax, "*SAX", AddressingMode.ZeropageYIndexed, InstructionType.Write),
		new(Instruction.Tya, "TYA", AddressingMode.Implied, InstructionType.Implied),
		new(Instruction.Sta, "STA", AddressingMode.AbsoluteYIndexed, InstructionType.Write),
		new(Instruction.Txs, "TXS", AddressingMode.Implied, InstructionType.Implied),
		new(Instruction.Unknown, "*TAS", AddressingMode.AbsoluteXIndexed, InstructionType.Write),
		new(Instruction.Unknown, "*SHY", AddressingMode.AbsoluteXIndexed, InstructionType.Write),
		new(Instruction.Sta, "STA", AddressingMode.AbsoluteXIndexed, InstructionType.Write),
		new(Instruction.Unknown, "*SHX", AddressingMode.AbsoluteYIndexed, InstructionType.Write),
		new(Instruction.Unknown, "*SHA", AddressingMode.AbsoluteYIndexed, InstructionType.Write),
		// 0xA0 - 0xAF
		new(Instruction.Ldy, "LDY", AddressingMode.Immediate),
		new(Instruction.Lda, "LDA", AddressingMode.XIndexedIndirect),
		new(Instruction.Ldx, "LDX", AddressingMode.Immediate),
		new(Instruction.IllLax, "*LAX", AddressingMode.XIndexedIndirect),
		new(Instruction.Ldy, "LDY", AddressingMode.Zeropage, InstructionType.Read),
		new(Instruction.Lda, "LDA", AddressingMode.Zeropage, InstructionType.Read),
		new(Instruction.Ldx, "LDX", AddressingMode.Zeropage, InstructionType.Read),
		new(Instruction.IllLax, "*LAX", AddressingMode.Zeropage, InstructionType.Read),
		new(Instruction.Tay, "TAY", AddressingMode.Implied, InstructionType.Implied),
		new(Instruction.Lda, "LDA", AddressingMode.Immediate),
		new(Instruction.Tax, "TAX", AddressingMode.Implied, InstructionType.Implied),
		new(Instruction.Unknown, "*LXA", AddressingMode.Immediate),
		new(Instruction.Ldy, "LDY", AddressingMode.Absolute, InstructionType.Read),
		new(Instruction.Lda, "LDA", AddressingMode.Absolute, InstructionType.Read),
		new(Instruction.Ldx, "LDX", AddressingMode.Absolute, InstructionType.Read),
		new(Instruction.IllLax, "*LAX", AddressingMode.Absolute, InstructionType.Read),
		// 0xB0 - 0xBF
		new(Instruction.Bcs, "BCS", AddressingMode.Relative),
		new(Instruction.Lda, "LDA", AddressingMode.IndirectYIndexed),
		new(Instruction.Jam, "*JAM", AddressingMode.Implied, InstructionType.Implied),
		new(Instruction.IllLax, "*LAX", AddressingMode.IndirectYIndexed),
		new(Instruction.Ldy, "LDY", AddressingMode.ZeropageXIndexed, InstructionType.Read),
		new(Instruction.Lda, "LDA", AddressingMode.ZeropageXIndexed, InstructionType.Read),
		new(Instruction.Ldx, "LDX", AddressingMode.ZeropageYIndexed, InstructionType.Read),
		new(Instruction.IllLax, "*LAX", AddressingMode.ZeropageYIndexed, InstructionType.Read),
		new(Instruction.Clv, "CLV", AddressingMode.Implied, InstructionType.Implied),
		new(Instruction.Lda, "LDA", AddressingMode.AbsoluteYIndexed, InstructionType.Read),
		new(Instruction.Tsx, "TSX", AddressingMode.Implied, InstructionType.Implied),
		new(Instruction.Unknown, "*LAS", AddressingMode.AbsoluteYIndexed, InstructionType.Read),
		new(Instruction.Ldy, "LDY", AddressingMode.AbsoluteXIndexed, InstructionType.Read),
		new(Instruction.Lda, "LDA", AddressingMode.AbsoluteXIndexed, InstructionType.Read),
		new(Instruction.Ldx, "LDX", AddressingMode.AbsoluteYIndexed, InstructionType.Read),
		new(Instruction.IllLax, "*LAX", AddressingMode.AbsoluteYIndexed, InstructionType.Read),
		// 0xC0 - 0xCF
		new(Instruction.Cpy, "CPY", AddressingMode.Immediate),
		new(Instruction.Cmp, "CMP", AddressingMode.XIndexedIndirect),
		new(Instruction.Nop, "*NOP", AddressingMode.Implied, InstructionType.Implied),
		new(Instruction.IllDcp, "*DCP", AddressingMode.XIndexedIndirect),
		new(Instruction.Cpy, "CPY", AddressingMode.Zeropage, InstructionType.Read),
		new(Instruction.Cmp, "CMP", AddressingMode.Zeropage, InstructionType.Read),
		new(Instruction.Dec, "DEC", AddressingMode.Zeropage, InstructionType.ReadModifyWrite),
		new(Instruction.IllDcp, "*DCP", AddressingMode.Zeropage, InstructionType.ReadModifyWrite),
		new(Instruction.Iny, "INY", AddressingMode.Implied, InstructionType.Implied),
		new(Instruction.Cmp, "CMP", AddressingMode.Immediate),
		new(Instruction.Dex, "DEX", AddressingMode.Implied, InstructionType.Implied),
		new(Instruction.Unknown, "*SBX", AddressingMode.Immediate),
		new(Instruction.Cpy, "CPY", AddressingMode.Absolute, InstructionType.Read),
		new(Instruction.Cmp, "CMP", AddressingMode.Absolute, InstructionType.Read),
		new(Instruction.Dec, "DEC", AddressingMode.Absolute, InstructionType.ReadModifyWrite),
		new(Instruction.IllDcp, "*DCP", AddressingMode.Absolute, InstructionType.ReadModifyWrite),
		// 0xD0 - 0xDF
		new(Instruction.Bne, "BNE", AddressingMode.Relative),
		new(Instruction.Cmp, "CMP", AddressingMode.IndirectYIndexed),
		new(Instruction.Jam, "*JAM", AddressingMode.Implied, InstructionType.Implied),
		new(Instruction.IllDcp, "*DCP", AddressingMode.IndirectYIndexed),
		new(Instruction.Nop, "*NOP", AddressingMode.ZeropageXIndexed, InstructionType.Read),
		new(Instruction.Cmp, "CMP", AddressingMode.ZeropageXIndexed, InstructionType.Read),
		new(Instruction.Dec, "DEC", AddressingMode.ZeropageXIndexed, InstructionType.ReadModifyWrite),
		new(Instruction.IllDcp, "*DCP", AddressingMode.ZeropageXIndexed, InstructionType.ReadModifyWrite),
		new(Instruction.Cld, "CLD", AddressingMode.Implied, InstructionType.Implied),
		new(Instruction.Cmp, "CMP", AddressingMode.AbsoluteYIndexed, InstructionType.Read),
		new(Instruction.Nop, "*NOP", AddressingMode.Implied, InstructionType.Implied),
		new(Instruction.IllDcp, "*DCP", AddressingMode.AbsoluteYIndexed, InstructionType.ReadModifyWrite),
		new(Instruction.Nop, "*NOP", AddressingMode.AbsoluteXIndexed, InstructionType.Read),
		new(Instruction.Cmp, "CMP", AddressingMode.AbsoluteXIndexed, InstructionType.Read),
		new(Instruction.Dec, "DEC", AddressingMode.AbsoluteXIndexed, InstructionType.ReadModifyWrite),
		new(Instruction.IllDcp, "*DCP", AddressingMode.AbsoluteXIndexed, InstructionType.ReadModifyWrite),
		// 0xE0 - 0xEF
		new(Instruction.Cpx, "CPX", AddressingMode.Immediate),
		new(Instruction.Sbc, "SBC", AddressingMode.XIndexedIndirect),
		new(Instruction.Nop, "*NOP", AddressingMode.Immediate),
		new(Instruction.IllIsc, "*ISC", AddressingMode.XIndexedIndirect),
		new(Instruction.Cpx, "CPX", AddressingMode.Zeropage, InstructionType.Read),
		new(Instruction.Sbc, "SBC", AddressingMode.Zeropage, InstructionType.Read),
		new(Instruction.Inc, "INC", AddressingMode.Zeropage, InstructionType.ReadModifyWrite),
		new(Instruction.IllIsc, "*ISC", AddressingMode.Zeropage, InstructionType.ReadModifyWrite),
		new(Instruction.Inx, "INX", AddressingMode.Implied, InstructionType.Implied),
		new(Instruction.Sbc, "SBC", AddressingMode.Immediate),
		new(Instruction.Nop, "NOP", AddressingMode.Implied, InstructionType.Implied),
		new(Instruction.Sbc, "*SBC", AddressingMode.Immediate),
		new(Instruction.Cpx, "CPX", AddressingMode.Absolute, InstructionType.Read),
		new(Instruction.Sbc, "SBC", AddressingMode.Absolute, InstructionType.Read),
		new(Instruction.Inc, "INC", AddressingMode.Absolute, InstructionType.ReadModifyWrite),
		new(Instruction.IllIsc, "*ISC", AddressingMode.Absolute, InstructionType.ReadModifyWrite),
		// 0xF0 - 0xFF
		new(Instruction.Beq, "BEQ", AddressingMode.Relative),
		new(Instruction.Sbc, "SBC", AddressingMode.IndirectYIndexed),
		new(Instruction.Jam, "*JAM", AddressingMode.Implied, InstructionType.Implied),
		new(Instruction.IllIsc, "*ISC", AddressingMode.IndirectYIndexed),
		new(Instruction.Nop, "*NOP", AddressingMode.ZeropageXIndexed, InstructionType.Read),
		new(Instruction.Sbc, "SBC", AddressingMode.ZeropageXIndexed, InstructionType.Read),
		new(Instruction.Inc, "INC", AddressingMode.ZeropageXIndexed, InstructionType.ReadModifyWrite),
		new(Instruction.IllIsc, "*ISC", AddressingMode.ZeropageXIndexed, InstructionType.ReadModifyWrite),
		new(Instruction.Sed, "SED", AddressingMode.Implied, InstructionType.Implied),
		new(Instruction.Sbc, "SBC", AddressingMode.AbsoluteYIndexed, InstructionType.Read),
		new(Instruction.Nop, "*NOP", AddressingMode.Implied, InstructionType.Implied),
		new(Instruction.IllIsc, "*ISC", AddressingMode.AbsoluteYIndexed, InstructionType.ReadModifyWrite),
		new(Instruction.Nop, "*NOP", AddressingMode.AbsoluteXIndexed, InstructionType.Read),
		new(Instruction.Sbc, "SBC", AddressingMode.AbsoluteXIndexed, InstructionType.Read),
		new(Instruction.Inc, "INC", AddressingMode.AbsoluteXIndexed, InstructionType.ReadModifyWrite),
		new(Instruction.IllIsc, "*ISC", AddressingMode.AbsoluteXIndexed, InstructionType.ReadModifyWrite)
	];

	#endregion

	#region Registers

	private ushort _regPc;
	private byte _regSpLo;
	private const byte _regSpHi = 0x01;
	private byte _regA;
	private byte _regX;
	private byte _regY;
	private ushort RegSp => (ushort)((_regSpHi << 8) | _regSpLo);

	#endregion

	#region Flags

	private bool _flagCarry;
	private bool _flagZero;
	private bool _flagInterruptDisable;
	private bool _flagDecimal; // Disabled but can still be set/cleared
	private bool _flagB;
	private bool _flagOverflow;
	private bool _flagNegative;

	#endregion

	public readonly CpuBus Bus;

	private byte _currentOpcode = 0;
	private int _step = 0;
	private int _fetchStep = 0;
	private int _cycles = 7;

	private ushort _fetchedAddress = 0;
	private byte _fetchLow;
	private byte _fetchHigh;
	private byte _fetchOperand;
	private bool _pageBoundaryCrossed;
	private bool _nmi = false;

	private Instruction CurrentInstruction => _instructions[_currentOpcode].Instruction;
	private AddressingMode CurrentAddressingMode => _instructions[_currentOpcode].AddressingMode;
	private InstructionType CurrentInstructionType => _instructions[_currentOpcode].Type;

	private byte RegStatus
	{
		get => (byte)
		(
			((_flagNegative ? 1 : 0) << 7) |
			((_flagOverflow ? 1 : 0) << 6) |
			(1 << 5) | // Unused, always 1
			((_flagB ? 1 : 0) << 4) | // Unused, 0 if pushed by interrupt, 1 if pushed by instruction
			((_flagDecimal ? 1 : 0) << 3) |
			((_flagInterruptDisable ? 1 : 0) << 2) |
			((_flagZero ? 1 : 0) << 1) |
			((_flagCarry ? 1 : 0) << 0)
		);

		set
		{
			_flagNegative = ((value >> 7) & 1) != 0;
			_flagOverflow = ((value >> 6) & 1) != 0;
			_flagB = ((value >> 4) & 1) != 0;
			_flagDecimal = ((value >> 3) & 1) != 0;
			_flagInterruptDisable = ((value >> 2) & 1) != 0;
			_flagZero = ((value >> 1) & 1) != 0;
			_flagCarry = (value & 1) != 0;
		}
	}

	public Cpu()
	{
		Bus = new();
	}

	public void Reset()
	{
		_regPc = (ushort)((Bus.ReadByte(0xFFFD) << 8) | Bus.ReadByte(0xFFFC));
		_regPc = 0xC000;
		_regSpLo = 0xFD;

		_flagNegative = false;
		_flagOverflow = false;
		_flagB = false;
		_flagDecimal = false;
		_flagInterruptDisable = true;
		_flagZero = false;
		_flagCarry = false;
	}

	public void RequestNmi()
	{
		_nmi = true;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private byte ReadByte(ushort address) => Bus.ReadByte(address);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void WriteByte(ushort address, byte value) => Bus.WriteByte(address, value);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private byte FetchByte() => ReadByte(_regPc++);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void PushByte(byte value)
	{
		WriteByte(RegSp, value);
		_regSpLo--;
	}

	private bool FetchAddress()
	{
		switch (CurrentAddressingMode)
		{
			case AddressingMode.Indirect: // 3 added cycles
				{
					switch (_fetchStep)
					{
						case 0:
							_fetchLow = FetchByte();
							_fetchStep++;
							return false;
						case 1:
							_fetchHigh = FetchByte();
							_fetchedAddress = (ushort)((_fetchHigh << 8) | _fetchLow);
							_fetchStep++;
							return false;
						case 2:
							_fetchLow = ReadByte(_fetchedAddress);
							_fetchedAddress++;
							// Reuse the same high byte to reproduce the "indirect jump bug"
							_fetchedAddress &= 0x00FF;
							_fetchedAddress |= (ushort)(_fetchHigh << 8);
							_fetchStep++;
							return false;
						case 3:
							_fetchHigh = ReadByte(_fetchedAddress);
							_fetchedAddress = (ushort)((_fetchHigh << 8) | _fetchLow);
							_fetchStep = 0;
							return true;
						default:
							throw new UnreachableException();
					}
				}
			case AddressingMode.XIndexedIndirect: // 3 added cycles
				{
					switch (_fetchStep)
					{
						case 0:
							_fetchedAddress = FetchByte();
							_fetchStep++;
							return false;
						case 1:
							_fetchedAddress += _regX;
							_fetchedAddress &= 0x00FF;
							_fetchStep++;
							return false;
						case 2:
							_fetchLow = ReadByte(_fetchedAddress);
							_fetchedAddress++;
							_fetchedAddress &= 0x00FF;
							_fetchStep++;
							return false;
						case 3:
							_fetchHigh = ReadByte(_fetchedAddress);
							_fetchedAddress = (ushort)((_fetchHigh << 8) | _fetchLow);
							_fetchStep = 0;
							return true;
						default:
							throw new UnreachableException();
					}
				}
			case AddressingMode.IndirectYIndexed: // 2 added cycles
				{
					switch (_fetchStep)
					{
						case 0:
							_fetchedAddress = FetchByte();
							_fetchStep++;
							return false;
						case 1:
							_fetchLow = ReadByte(_fetchedAddress);
							_fetchedAddress++;
							_fetchedAddress &= 0x00FF;
							_fetchStep++;
							return false;
						case 2:
							_fetchHigh = ReadByte(_fetchedAddress);
							_fetchedAddress = (ushort)((_fetchHigh << 8) | _fetchLow);
							_pageBoundaryCrossed = (_fetchedAddress & 0xFF00) != ((_fetchedAddress + _regY) & 0xFF00);
							_fetchedAddress += _regY;
							_fetchStep = 0;
							return true;
						default:
							throw new UnreachableException();
					}
				}
			default:
				throw new UnreachableException($"The addressing mode {CurrentAddressingMode} was used but not implemented (opcode 0x{_currentOpcode:X2}).");
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ReadMemoryOperand()
	{
		if (CurrentAddressingMode is AddressingMode.Immediate or AddressingMode.Accumulator)
			throw new UnreachableException($"Attempted to read memory operand from immediate or accumulator (opcode 0x{_currentOpcode:X2}).");

		_fetchOperand = ReadByte(_fetchedAddress);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void WriteMemoryOperand() => WriteByte(_fetchedAddress, _fetchOperand);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private string DisassembleNext()
	{
		var inst = _instructions[_currentOpcode];
		var b1 = ReadByte(_regPc);
		var b2 = ReadByte((ushort)(_regPc + 1));
		return inst.AddressingMode switch
		{
			AddressingMode.Relative => inst.mnemonic + $" ${_regPc + 1 + (sbyte)b1:X4}",
			AddressingMode.Immediate => inst.mnemonic + $" #${b1:X2}",

			AddressingMode.Absolute => inst.mnemonic + $" ${b2:X2}{b1:X2}",
			AddressingMode.AbsoluteXIndexed => inst.mnemonic + $" ${b2:X2}{b1:X2},X",
			AddressingMode.AbsoluteYIndexed => inst.mnemonic + $" ${b2:X2}{b1:X2},Y",

			AddressingMode.Zeropage => inst.mnemonic + $" ${b1:X2} = {ReadByte(b1):X2}",
			AddressingMode.ZeropageXIndexed => inst.mnemonic + $" ${b1:X2},X",
			AddressingMode.ZeropageYIndexed => inst.mnemonic + $" ${b1:X2},Y",

			AddressingMode.Indirect => inst.mnemonic + $" (${b2:X2}{b1:X2})",
			AddressingMode.XIndexedIndirect => inst.mnemonic + $" (${b1:X2},X)",
			AddressingMode.IndirectYIndexed => inst.mnemonic + $" (${b1:X2}),Y",

			_ => inst.mnemonic
		};
	}

	public void Tick()
	{
		if (_step == 0)
		{
			if (_nmi)
			{
				_nmi = false;
				PushByte((byte)(_regPc >> 8));
				PushByte((byte)(_regPc & 0xFF));
				PushByte(RegStatus);
				_fetchLow = ReadByte(0xFFFA);
				_fetchHigh = ReadByte(0xFFFB);
				_regPc = (ushort)((_fetchHigh << 8) | _fetchLow);
				_flagInterruptDisable = true;
			}

			_currentOpcode = FetchByte();

			var sb = new StringBuilder();
			sb.Append($"{(ushort)(_regPc - 1):X4}  {_currentOpcode:X2}");
			for (var i = 0; i < _instructions[_currentOpcode].Bytes - 1; i++)
				sb.Append($" {ReadByte((ushort)(_regPc + i)):X2}");
			sb.Append(new string(' ', 20 - sb.Length));
			sb.Append(DisassembleNext());
			sb.Append(new string(' ', 48 - sb.Length));
			sb.Append($"A:{_regA:X2} X:{_regX:X2} Y:{_regY:X2} P:{RegStatus:X2} SP:{_regSpLo:X2} CYC:{_cycles}");
			Console.WriteLine(sb);
			if (_cycles == 26554)
				Environment.Exit(0);
		}

		switch (CurrentAddressingMode)
		{
			case AddressingMode.Accumulator: ExecuteAddrAccumulator(); break;
			case AddressingMode.Implied: ExecuteAddrImplied(); break;
			case AddressingMode.Immediate: ExecuteAddrImmediate(); break;
			case AddressingMode.Absolute: ExecuteAddrAbsolute(); break;
			case AddressingMode.Zeropage: ExecuteAddrZeropage(); break;
			case AddressingMode.ZeropageXIndexed: ExecuteAddrZeropageIndexed(); break;
			case AddressingMode.ZeropageYIndexed: ExecuteAddrZeropageIndexed(); break;
			case AddressingMode.AbsoluteXIndexed: ExecuteAddrAbsoluteIndexed(); break;
			case AddressingMode.AbsoluteYIndexed: ExecuteAddrAbsoluteIndexed(); break;
			default:
				switch (CurrentInstruction)
				{
					case Instruction.Adc: ExecuteInstAdc(); break;
					case Instruction.And: ExecuteInstAnd(); break;
					case Instruction.Asl: ExecuteInstAsl(); break;
					case Instruction.Bcc: ExecuteInstBcc(); break;
					case Instruction.Bcs: ExecuteInstBcs(); break;
					case Instruction.Beq: ExecuteInstBeq(); break;
					case Instruction.Bit: ExecuteInstBit(); break;
					case Instruction.Bmi: ExecuteInstBmi(); break;
					case Instruction.Bne: ExecuteInstBne(); break;
					case Instruction.Bpl: ExecuteInstBpl(); break;
					case Instruction.Bvc: ExecuteInstBvc(); break;
					case Instruction.Bvs: ExecuteInstBvs(); break;
					case Instruction.Cmp: ExecuteInstCmp(); break;
					case Instruction.Cpx: ExecuteInstCpx(); break;
					case Instruction.Cpy: ExecuteInstCpy(); break;
					case Instruction.Dec: ExecuteInstDec(); break;
					case Instruction.Eor: ExecuteInstEor(); break;
					case Instruction.Inc: ExecuteInstInc(); break;
					case Instruction.Jmp: ExecuteInstJmp(); break;
					case Instruction.Lda: ExecuteInstLda(); break;
					case Instruction.Ldx: ExecuteInstLdx(); break;
					case Instruction.Ldy: ExecuteInstLdy(); break;
					case Instruction.Lsr: ExecuteInstLsr(); break;
					case Instruction.Nop: ExecuteInstNop(); break;
					case Instruction.Ora: ExecuteInstOra(); break;
					case Instruction.Rol: ExecuteInstRol(); break;
					case Instruction.Ror: ExecuteInstRor(); break;
					case Instruction.Sbc: ExecuteInstSbc(); break;
					case Instruction.Sta: ExecuteInstSta(); break;
					case Instruction.Stx: ExecuteInstStx(); break;
					case Instruction.Sty: ExecuteInstSty(); break;
					case Instruction.IllDcp: ExecuteInstIllDcp(); break;
					case Instruction.IllIsc: ExecuteInstIllIsc(); break;
					case Instruction.IllLax: ExecuteInstIllLax(); break;
					case Instruction.IllRla: ExecuteInstIllRla(); break;
					case Instruction.IllRra: ExecuteInstIllRra(); break;
					case Instruction.IllSax: ExecuteInstIllSax(); break;
					case Instruction.IllSlo: ExecuteInstIllSlo(); break;
					case Instruction.IllSre: ExecuteInstIllSre(); break;
					default:
						throw new NotImplementedException($"Opcode 0x{_currentOpcode:X2} not recognized.");
				}
				break;
		}

		_cycles++;
	}

	#region Addressing modes

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteAddrAccumulator()
	{
		switch (_step)
		{
			case 0: // fetch opcode, increment PC
				_step++;
				break;
			case 1: // read next instruction byte (and throw it away)
				ReadByte(_regPc);
				switch (CurrentInstruction)
				{
					case Instruction.Asl: ExecuteOpAsl(ref _regA); break;
					case Instruction.Lsr: ExecuteOpLsr(ref _regA); break;
					case Instruction.Rol: ExecuteOpRol(ref _regA); break;
					case Instruction.Ror: ExecuteOpRor(ref _regA); break;
					default: throw new UnreachableException();
				}
				_step = 0;
				break;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteAddrImplied()
	{
		if (CurrentInstructionType == InstructionType.Stack)
		{
			switch (CurrentInstruction)
			{
				case Instruction.Brk: ExecuteInstBrk(); break;
				case Instruction.Rti: ExecuteInstRti(); break;
				case Instruction.Rts: ExecuteInstRts(); break;
				case Instruction.Pha: ExecuteInstPha(); break;
				case Instruction.Php: ExecuteInstPhp(); break;
				case Instruction.Pla: ExecuteInstPla(); break;
				case Instruction.Plp: ExecuteInstPlp(); break;
				default: throw new UnreachableException();
			}
			return;
		}

		switch (_step)
		{
			case 0: // fetch opcode, increment PC
				_step++;
				break;
			case 1: // read next instruction byte (and throw it away)
				ReadByte(_regPc);
				switch (CurrentInstruction)
				{
					case Instruction.Clc: ExecuteOpClc(); break;
					case Instruction.Cld: ExecuteOpCld(); break;
					case Instruction.Cli: ExecuteOpCli(); break;
					case Instruction.Clv: ExecuteOpClv(); break;
					case Instruction.Dex: ExecuteOpDex(); break;
					case Instruction.Dey: ExecuteOpDey(); break;
					case Instruction.Inx: ExecuteOpInx(); break;
					case Instruction.Iny: ExecuteOpIny(); break;
					case Instruction.Nop: break;
					case Instruction.Sec: ExecuteOpSec(); break;
					case Instruction.Sed: ExecuteOpSed(); break;
					case Instruction.Sei: ExecuteOpSei(); break;
					case Instruction.Tax: ExecuteOpTax(); break;
					case Instruction.Tay: ExecuteOpTay(); break;
					case Instruction.Tsx: ExecuteOpTsx(); break;
					case Instruction.Txa: ExecuteOpTxa(); break;
					case Instruction.Txs: ExecuteOpTxs(); break;
					case Instruction.Tya: ExecuteOpTya(); break;
					default: throw new UnreachableException();
				}
				_step = 0;
				break;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteAddrImmediate()
	{
		switch (_step)
		{
			case 0: // fetch opcode, increment PC
				_step++;
				break;
			case 1: // fetch value, increment PC
				{
					var value = FetchByte();
					switch (CurrentInstruction)
					{
						case Instruction.Adc: ExecuteOpAdc(value); break;
						case Instruction.And: ExecuteOpAnd(value); break;
						case Instruction.Cmp: ExecuteOpCmp(value); break;
						case Instruction.Cpx: ExecuteOpCpx(value); break;
						case Instruction.Cpy: ExecuteOpCpy(value); break;
						case Instruction.Eor: ExecuteOpEor(value); break;
						case Instruction.Lda: ExecuteOpLda(value); break;
						case Instruction.Ldx: ExecuteOpLdx(value); break;
						case Instruction.Ldy: ExecuteOpLdy(value); break;
						case Instruction.Nop: break;
						case Instruction.Ora: ExecuteOpOra(value); break;
						case Instruction.Sbc: ExecuteOpSbc(value); break;
						default: throw new UnreachableException();
					}
					_step = 0;
					break;
				}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteAddrAbsolute()
	{
		switch (CurrentInstructionType)
		{
			case InstructionType.InstJmp:
				switch (_step)
				{
					case 0: // fetch opcode, increment PC
						_step++;
						break;
					case 1: // fetch low address byte, increment PC
						_fetchLow = FetchByte();
						_step++;
						break;
					case 2: // copy low address byte to PCL, fetch high address byte to PCH
						_regPc = (ushort)((FetchByte() << 8) | _fetchLow);
						_step = 0;
						break;
				}
				break;
			case InstructionType.InstJsr:
				ExecuteInstJsr();
				break;
			case InstructionType.Read:
				switch (_step)
				{
					case 0: // fetch opcode, increment PC
						_step++;
						break;
					case 1: // fetch low byte of address, increment PC
						_fetchLow = FetchByte();
						_step++;
						break;
					case 2: // fetch high byte of address, increment PC
						_fetchHigh = FetchByte();
						_step++;
						break;
					case 3: // read from effective address
						_fetchedAddress = (ushort)((_fetchHigh << 8) | _fetchLow);
						var value = ReadByte(_fetchedAddress);
						switch (CurrentInstruction)
						{
							case Instruction.Adc: ExecuteOpAdc(value); break;
							case Instruction.And: ExecuteOpAnd(value); break;
							case Instruction.Bit: ExecuteOpBit(value); break;
							case Instruction.Cmp: ExecuteOpCmp(value); break;
							case Instruction.Cpx: ExecuteOpCpx(value); break;
							case Instruction.Cpy: ExecuteOpCpy(value); break;
							case Instruction.Eor: ExecuteOpEor(value); break;
							case Instruction.Lda: ExecuteOpLda(value); break;
							case Instruction.Ldx: ExecuteOpLdx(value); break;
							case Instruction.Ldy: ExecuteOpLdy(value); break;
							case Instruction.Nop: break;
							case Instruction.Ora: ExecuteOpOra(value); break;
							case Instruction.Sbc: ExecuteOpSbc(value); break;
							case Instruction.IllLax: ExecuteOpIllLax(value); break;
						}
						_step = 0;
						break;
				}
				break;
			case InstructionType.ReadModifyWrite:
				switch (_step)
				{
					case 0: // fetch opcode, increment PC
						_step++;
						break;
					case 1: // fetch low byte of address, increment PC
						_fetchLow = FetchByte();
						_step++;
						break;
					case 2: // fetch high byte of address, increment PC
						_fetchHigh = FetchByte();
						_step++;
						break;
					case 3: // read from effective address
						_fetchedAddress = (ushort)((_fetchHigh << 8) | _fetchLow);
						_fetchOperand = ReadByte(_fetchedAddress);
						_step++;
						break;
					case 4: // write the value back to effective address, and do the operation on it
						WriteByte(_fetchedAddress, _fetchOperand);
						switch (CurrentInstruction)
						{
							case Instruction.Asl: ExecuteOpAsl(ref _fetchOperand); break;
							case Instruction.Dec: ExecuteOpDec(ref _fetchOperand); break;
							case Instruction.Inc: ExecuteOpInc(ref _fetchOperand); break;
							case Instruction.Lsr: ExecuteOpLsr(ref _fetchOperand); break;
							case Instruction.Rol: ExecuteOpRol(ref _fetchOperand); break;
							case Instruction.Ror: ExecuteOpRor(ref _fetchOperand); break;
							case Instruction.IllDcp: ExecuteOpIllDcp(ref _fetchOperand); break;
							case Instruction.IllIsc: ExecuteOpIllIsc(ref _fetchOperand); break;
							case Instruction.IllRla: ExecuteOpIllRla(ref _fetchOperand); break;
							case Instruction.IllRra: ExecuteOpIllRra(ref _fetchOperand); break;
							case Instruction.IllSlo: ExecuteOpIllSlo(ref _fetchOperand); break;
							case Instruction.IllSre: ExecuteOpIllSre(ref _fetchOperand); break;
						}
						_step++;
						break;
					case 5: // write the new value to effective address
						WriteByte(_fetchedAddress, _fetchOperand);
						_step = 0;
						break;
				}
				break;
			case InstructionType.Write:
				switch (_step)
				{
					case 0: // fetch opcode, increment PC
						_step++;
						break;
					case 1: // fetch low byte of address, increment PC
						_fetchLow = FetchByte();
						_step++;
						break;
					case 2: // fetch high byte of address, increment PC
						_fetchHigh = FetchByte();
						_step++;
						break;
					case 3: // write register to effective address
						_fetchedAddress = (ushort)((_fetchHigh << 8) | _fetchLow);
						switch (CurrentInstruction)
						{
							case Instruction.Sta: WriteByte(_fetchedAddress, ExecuteOpSta()); break;
							case Instruction.Stx: WriteByte(_fetchedAddress, ExecuteOpStx()); break;
							case Instruction.Sty: WriteByte(_fetchedAddress, ExecuteOpSty()); break;
							case Instruction.IllSax: WriteByte(_fetchedAddress, ExecuteOpIllSax()); break;
						}
						_step = 0;
						break;
				}
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteAddrZeropage()
	{
		switch (CurrentInstructionType)
		{
			case InstructionType.Read:
				switch (_step)
				{
					case 0: // fetch opcode, increment PC
						_step++;
						break;
					case 1: // fetch address, increment PC
						_fetchedAddress = FetchByte();
						_step++;
						break;
					case 2: // read from effective address
						var value = ReadByte(_fetchedAddress);
						switch (CurrentInstruction)
						{
							case Instruction.Adc: ExecuteOpAdc(value); break;
							case Instruction.And: ExecuteOpAnd(value); break;
							case Instruction.Bit: ExecuteOpBit(value); break;
							case Instruction.Cmp: ExecuteOpCmp(value); break;
							case Instruction.Cpx: ExecuteOpCpx(value); break;
							case Instruction.Cpy: ExecuteOpCpy(value); break;
							case Instruction.Eor: ExecuteOpEor(value); break;
							case Instruction.Lda: ExecuteOpLda(value); break;
							case Instruction.Ldx: ExecuteOpLdx(value); break;
							case Instruction.Ldy: ExecuteOpLdy(value); break;
							case Instruction.Nop: break;
							case Instruction.Ora: ExecuteOpOra(value); break;
							case Instruction.Sbc: ExecuteOpSbc(value); break;
							case Instruction.IllLax: ExecuteOpIllLax(value); break;
						}
						_step = 0;
						break;
				}
				break;
			case InstructionType.ReadModifyWrite:
				switch (_step)
				{
					case 0: // fetch opcode, increment PC
						_step++;
						break;
					case 1: // fetch address, increment PC
						_fetchedAddress = FetchByte();
						_step++;
						break;
					case 2: // read from effective address
						_fetchOperand = ReadByte(_fetchedAddress);
						_step++;
						break;
					case 3: // write the value back to effective address, and do the operation on it
						WriteByte(_fetchedAddress, _fetchOperand);
						switch (CurrentInstruction)
						{
							case Instruction.Asl: ExecuteOpAsl(ref _fetchOperand); break;
							case Instruction.Dec: ExecuteOpDec(ref _fetchOperand); break;
							case Instruction.Inc: ExecuteOpInc(ref _fetchOperand); break;
							case Instruction.Lsr: ExecuteOpLsr(ref _fetchOperand); break;
							case Instruction.Rol: ExecuteOpRol(ref _fetchOperand); break;
							case Instruction.Ror: ExecuteOpRor(ref _fetchOperand); break;
							case Instruction.IllDcp: ExecuteOpIllDcp(ref _fetchOperand); break;
							case Instruction.IllIsc: ExecuteOpIllIsc(ref _fetchOperand); break;
							case Instruction.IllRla: ExecuteOpIllRla(ref _fetchOperand); break;
							case Instruction.IllRra: ExecuteOpIllRra(ref _fetchOperand); break;
							case Instruction.IllSlo: ExecuteOpIllSlo(ref _fetchOperand); break;
							case Instruction.IllSre: ExecuteOpIllSre(ref _fetchOperand); break;
						}
						_step++;
						break;
					case 4: // write the new value to effective address
						WriteByte(_fetchedAddress, _fetchOperand);
						_step = 0;
						break;
				}
				break;
			case InstructionType.Write:
				switch (_step)
				{
					case 0: // fetch opcode, increment PC
						_step++;
						break;
					case 1: // fetch address, increment PC
						_fetchedAddress = FetchByte();
						_step++;
						break;
					case 2: // write register to effective address
						switch (CurrentInstruction)
						{
							case Instruction.Sta: WriteByte(_fetchedAddress, ExecuteOpSta()); break;
							case Instruction.Stx: WriteByte(_fetchedAddress, ExecuteOpStx()); break;
							case Instruction.Sty: WriteByte(_fetchedAddress, ExecuteOpSty()); break;
							case Instruction.IllSax: WriteByte(_fetchedAddress, ExecuteOpIllSax()); break;
						}
						_step = 0;
						break;
				}
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteAddrZeropageIndexed()
	{
		var index = CurrentAddressingMode switch
		{
			AddressingMode.ZeropageXIndexed => _regX,
			AddressingMode.ZeropageYIndexed => _regY,
			_ => throw new UnreachableException()
		};

		switch (CurrentInstructionType)
		{
			case InstructionType.Read:
				switch (_step)
				{
					case 0: // fetch opcode, increment PC
						_step++;
						break;
					case 1: // fetch address, increment PC
						_fetchedAddress = FetchByte();
						_step++;
						break;
					case 2: // read from address, add index register to it
						ReadByte(_fetchedAddress);
						_fetchedAddress += index;
						_fetchedAddress &= 0xFF;
						_step++;
						break;
					case 3: // read from effective address
						var value = ReadByte(_fetchedAddress);
						switch (CurrentInstruction)
						{
							case Instruction.Adc: ExecuteOpAdc(value); break;
							case Instruction.And: ExecuteOpAnd(value); break;
							case Instruction.Bit: ExecuteOpBit(value); break;
							case Instruction.Cmp: ExecuteOpCmp(value); break;
							case Instruction.Cpx: ExecuteOpCpx(value); break;
							case Instruction.Cpy: ExecuteOpCpy(value); break;
							case Instruction.Eor: ExecuteOpEor(value); break;
							case Instruction.Lda: ExecuteOpLda(value); break;
							case Instruction.Ldx: ExecuteOpLdx(value); break;
							case Instruction.Ldy: ExecuteOpLdy(value); break;
							case Instruction.Nop: break;
							case Instruction.Ora: ExecuteOpOra(value); break;
							case Instruction.Sbc: ExecuteOpSbc(value); break;
							case Instruction.IllLax: ExecuteOpIllLax(value); break;
						}
						_step = 0;
						break;
				}
				break;
			case InstructionType.ReadModifyWrite:
				switch (_step)
				{
					case 0: // fetch opcode, increment PC
						_step++;
						break;
					case 1: // fetch address, increment PC
						_fetchedAddress = FetchByte();
						_step++;
						break;
					case 2: // read from address, add index register X to it
						ReadByte(_fetchedAddress);
						_fetchedAddress += _regX;
						_fetchedAddress &= 0xFF;
						_step++;
						break;
					case 3: // read from effective address
						_fetchOperand = ReadByte(_fetchedAddress);
						_step++;
						break;
					case 4: // write the value back to effective address, and do the operation on it
						WriteByte(_fetchedAddress, _fetchOperand);
						switch (CurrentInstruction)
						{
							case Instruction.Asl: ExecuteOpAsl(ref _fetchOperand); break;
							case Instruction.Dec: ExecuteOpDec(ref _fetchOperand); break;
							case Instruction.Inc: ExecuteOpInc(ref _fetchOperand); break;
							case Instruction.Lsr: ExecuteOpLsr(ref _fetchOperand); break;
							case Instruction.Rol: ExecuteOpRol(ref _fetchOperand); break;
							case Instruction.Ror: ExecuteOpRor(ref _fetchOperand); break;
							case Instruction.IllDcp: ExecuteOpIllDcp(ref _fetchOperand); break;
							case Instruction.IllIsc: ExecuteOpIllIsc(ref _fetchOperand); break;
							case Instruction.IllRla: ExecuteOpIllRla(ref _fetchOperand); break;
							case Instruction.IllRra: ExecuteOpIllRra(ref _fetchOperand); break;
							case Instruction.IllSlo: ExecuteOpIllSlo(ref _fetchOperand); break;
							case Instruction.IllSre: ExecuteOpIllSre(ref _fetchOperand); break;
						}
						_step++;
						break;
					case 5: // write the new value to effective address
						WriteByte(_fetchedAddress, _fetchOperand);
						_step = 0;
						break;
				}
				break;
			case InstructionType.Write:
				switch (_step)
				{
					case 0: // fetch opcode, increment PC
						_step++;
						break;
					case 1: // fetch address, increment PC
						_fetchedAddress = FetchByte();
						_step++;
						break;
					case 2: // read from address, add index register to it
						ReadByte(_fetchedAddress);
						_fetchedAddress += index;
						_fetchedAddress &= 0xFF;
						_step++;
						break;
					case 3: // write to effective address
						switch (CurrentInstruction)
						{
							case Instruction.Sta: WriteByte(_fetchedAddress, ExecuteOpSta()); break;
							case Instruction.Stx: WriteByte(_fetchedAddress, ExecuteOpStx()); break;
							case Instruction.Sty: WriteByte(_fetchedAddress, ExecuteOpSty()); break;
							case Instruction.IllSax: WriteByte(_fetchedAddress, ExecuteOpIllSax()); break;
						}
						_step = 0;
						break;
				}
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteAddrAbsoluteIndexed()
	{
		var index = CurrentAddressingMode switch
		{
			AddressingMode.AbsoluteXIndexed => _regX,
			AddressingMode.AbsoluteYIndexed => _regY,
			_ => throw new UnreachableException()
		};

		switch (CurrentInstructionType)
		{
			case InstructionType.Read:
				switch (_step)
				{
					case 0: // fetch opcode, increment PC
						_step++;
						break;
					case 1: // fetch low byte of address, increment PC
						_fetchLow = FetchByte();
						_step++;
						break;
					case 2: // fetch high byte of address, add index register to low address byte, increment PC
						_fetchHigh = FetchByte();
						var oldLow = _fetchLow;
						_fetchLow += index;
						_pageBoundaryCrossed = oldLow + index != _fetchLow;
						_step++;
						break;
					case 3: // read from effective address, fix the high byte of effective address
						_fetchedAddress = (ushort)((_fetchHigh << 8) | _fetchLow);

						// If page boundary was not crossed, do not incur penalty cycle, address is correct
						//  and will only be read once immediately in this cycle, finishing the instruction
						if (!_pageBoundaryCrossed)

							goto case 4;

						// If page boundary was crossed, read from incorrect address,
						//  fix the high byte and incur a penalty cycle
						ReadByte(_fetchedAddress);

						_fetchHigh++;
						_fetchedAddress = (ushort)((_fetchHigh << 8) | _fetchLow);

						_step++;
						break;
					case 4: // re-read from effective address
						var value = ReadByte(_fetchedAddress);
						switch (CurrentInstruction)
						{
							case Instruction.Adc: ExecuteOpAdc(value); break;
							case Instruction.And: ExecuteOpAnd(value); break;
							case Instruction.Bit: ExecuteOpBit(value); break;
							case Instruction.Cmp: ExecuteOpCmp(value); break;
							case Instruction.Cpx: ExecuteOpCpx(value); break;
							case Instruction.Cpy: ExecuteOpCpy(value); break;
							case Instruction.Eor: ExecuteOpEor(value); break;
							case Instruction.Lda: ExecuteOpLda(value); break;
							case Instruction.Ldx: ExecuteOpLdx(value); break;
							case Instruction.Ldy: ExecuteOpLdy(value); break;
							case Instruction.Nop: break;
							case Instruction.Ora: ExecuteOpOra(value); break;
							case Instruction.Sbc: ExecuteOpSbc(value); break;
							// TODO: case Instruction.IllLas: ExecuteOpLas(value); break;
							case Instruction.IllLax: ExecuteOpIllLax(value); break;
								// TODO: case Instruction.IllShs: ExecuteOpShs(value); break;
						}
						_step = 0;
						break;
				}
				break;
			case InstructionType.ReadModifyWrite:
				switch (_step)
				{
					case 0: // fetch opcode, increment PC
						_step++;
						break;
					case 1: // fetch low byte of address, increment PC
						_fetchLow = FetchByte();
						_step++;
						break;
					case 2: // fetch high byte of address, add index register X to low address byte, increment PC
						_fetchHigh = FetchByte();
						var oldLow = _fetchLow;
						_fetchLow += index;
						_pageBoundaryCrossed = oldLow + index != _fetchLow;
						_step++;
						break;
					case 3: // read from effective address, fix the high byte of effective address
						_fetchedAddress = (ushort)((_fetchHigh << 8) | _fetchLow);
						ReadByte(_fetchedAddress);

						if (_pageBoundaryCrossed)
							_fetchHigh++;

						_step++;
						break;
					case 4:
						_fetchedAddress = (ushort)((_fetchHigh << 8) | _fetchLow);
						_fetchOperand = ReadByte(_fetchedAddress);
						_step++;
						break;
					case 5: // write the value back to effective address, and do the operation on it
						WriteByte(_fetchedAddress, _fetchOperand);
						switch (CurrentInstruction)
						{
							case Instruction.Asl: ExecuteOpAsl(ref _fetchOperand); break;
							case Instruction.Dec: ExecuteOpDec(ref _fetchOperand); break;
							case Instruction.Inc: ExecuteOpInc(ref _fetchOperand); break;
							case Instruction.Lsr: ExecuteOpLsr(ref _fetchOperand); break;
							case Instruction.Rol: ExecuteOpRol(ref _fetchOperand); break;
							case Instruction.Ror: ExecuteOpRor(ref _fetchOperand); break;
							case Instruction.IllDcp: ExecuteOpIllDcp(ref _fetchOperand); break;
							case Instruction.IllIsc: ExecuteOpIllIsc(ref _fetchOperand); break;
							case Instruction.IllRla: ExecuteOpIllRla(ref _fetchOperand); break;
							case Instruction.IllRra: ExecuteOpIllRra(ref _fetchOperand); break;
							case Instruction.IllSlo: ExecuteOpIllSlo(ref _fetchOperand); break;
							case Instruction.IllSre: ExecuteOpIllSre(ref _fetchOperand); break;
						}
						_step++;
						break;
					case 6: // write the new value to effective address
						WriteByte(_fetchedAddress, _fetchOperand);
						_step = 0;
						break;
				}
				break;
			case InstructionType.Write:
				switch (_step)
				{
					case 0: // fetch opcode, increment PC
						_step++;
						break;
					case 1: // fetch low byte of address, increment PC
						_fetchLow = FetchByte();
						_step++;
						break;
					case 2: // fetch high byte of address, add index register to low address byte, increment PC
						_fetchHigh = FetchByte();
						var oldLow = _fetchLow;
						_fetchLow += index;
						_pageBoundaryCrossed = oldLow + index != _fetchLow;
						_step++;
						break;
					case 3: // read from effective address, fix the high byte of effective address
						_fetchedAddress = (ushort)((_fetchHigh << 8) | _fetchLow);
						ReadByte(_fetchedAddress);

						if (_pageBoundaryCrossed)
							_fetchHigh++;

						_step++;
						break;
					case 4: // write to effective address
						_fetchedAddress = (ushort)((_fetchHigh << 8) | _fetchLow);
						switch (CurrentInstruction)
						{
							case Instruction.Sta: WriteByte(_fetchedAddress, ExecuteOpSta()); break;
							case Instruction.Stx: WriteByte(_fetchedAddress, ExecuteOpStx()); break;
							case Instruction.Sty: WriteByte(_fetchedAddress, ExecuteOpSty()); break;
								// TODO: case Instruction.IllSha: WriteByte(_fetchedAddress, ExecuteOpIllSha()); break;
								// TODO: case Instruction.IllShx: WriteByte(_fetchedAddress, ExecuteOpIllShx()); break;
								// TODO: case Instruction.IllShy: WriteByte(_fetchedAddress, ExecuteOpIllShy()); break;
						}
						_step = 0;
						break;
				}
				break;
			default:
				throw new UnreachableException();
		}
	}

	#endregion

	#region Special instructions

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstBrk()
	{
		switch (_step)
		{
			case 0: // fetch opcode, increment PC
				_step++;
				break;
			case 1: // read next instruction byte (and throw it away), increment PC
				FetchByte();
				_step++;
				break;
			case 2: // push PCH on stack, decrement S
				WriteByte(RegSp, (byte)((_regPc + 2) >> 8));
				_regSpLo--;
				_step++;
				break;
			case 3: // push PCL on stack, decrement S
				WriteByte(RegSp, (byte)((_regPc + 2) & 0xFF));
				_regSpLo--;
				_step++;
				break;
			case 4: // push P on stack (with B flag set), decrement S
				_flagB = true;
				WriteByte(RegSp, RegStatus);
				_regSpLo--;
				_flagB = false;
				_step++;
				break;
			case 5: // fetch PCL
				_fetchLow = ReadByte(0xFFFE);
				_step++;
				break;
			case 6: // fetch PCH
				_fetchHigh = ReadByte(0xFFFF);
				_regPc = (ushort)((_fetchHigh << 8) | _fetchLow);
				_flagInterruptDisable = true;
				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstRti()
	{
		switch (_step)
		{
			case 0: // fetch opcode, increment PC
				_step++;
				break;
			case 1: // read next instruction byte (and throw it away)
				ReadByte(_regPc);
				_step++;
				break;
			case 2: // increment S
				_regSpLo++;
				_step++;
				break;
			case 3: // pull P from stack, increment S
				RegStatus = ReadByte(RegSp);
				_regSpLo++;
				_flagB = false;
				_step++;
				break;
			case 4: // pull PCL from stack, increment S
				_regPc = ReadByte(RegSp);
				_regSpLo++;
				_step++;
				break;
			case 5: // pull PCH from stack
				_regPc |= (ushort)(ReadByte(RegSp) << 8);
				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstRts()
	{
		switch (_step)
		{
			case 0: // fetch opcode, increment PC
				_step++;
				break;
			case 1: // read next instruction byte (and throw it away)
				ReadByte(_regPc);
				_step++;
				break;
			case 2: // increment S
				_regSpLo++;
				_step++;
				break;
			case 3: // pull PCL from stack, increment S
				_regPc = ReadByte(RegSp);
				_regSpLo++;
				_step++;
				break;
			case 4: // pull PCH from stack
				_regPc |= (ushort)(ReadByte(RegSp) << 8);
				_step++;
				break;
			case 5: // increment PC
				_regPc++;
				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstPha()
	{
		switch (_step)
		{
			case 0: // fetch opcode, increment PC
				_step++;
				break;
			case 1: // read next instruction byte (and throw it away)
				ReadByte(_regPc);
				_step++;
				break;
			case 2: // push register on stack, decrement S
				WriteByte(RegSp, _regA);
				_regSpLo--;
				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstPhp()
	{
		switch (_step)
		{
			case 0: // fetch opcode, increment PC
				_step++;
				break;
			case 1: // read next instruction byte (and throw it away)
				ReadByte(_regPc);
				_step++;
				break;
			case 2: // push register on stack, decrement S
				_flagB = true;
				WriteByte(RegSp, RegStatus);
				_flagB = false;
				_regSpLo--;
				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstPla()
	{
		switch (_step)
		{
			case 0: // fetch opcode, increment PC
				_step++;
				break;
			case 1: // read next instruction byte (and throw it away)
				ReadByte(_regPc);
				_step++;
				break;
			case 2: // increment S
				_regSpLo++;
				_step++;
				break;
			case 3: // pull register from stack
				_regA = ReadByte(RegSp);
				_flagNegative = ((_regA >> 7) & 1) != 0;
				_flagZero = _regA == 0;
				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstPlp()
	{
		switch (_step)
		{
			case 0: // fetch opcode, increment PC
				_step++;
				break;
			case 1: // read next instruction byte (and throw it away)
				ReadByte(_regPc);
				_step++;
				break;
			case 2: // increment S
				_regSpLo++;
				_step++;
				break;
			case 3: // pull register from stack
				RegStatus = ReadByte(RegSp);
				_flagB = false;
				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstJsr()
	{
		switch (_step)
		{
			case 0: // fetch opcode, increment PC
				_step++;
				break;
			case 1: // fetch low address byte, increment PC
				_fetchLow = FetchByte();
				_step++;
				break;
			case 2: // internal operation (predecrement S?)
				_step++;
				break;
			case 3: // push PCH on stack, decrement S
				WriteByte(RegSp, (byte)(_regPc >> 8));
				_regSpLo--;
				_step++;
				break;
			case 4: // push PCL on stack, decrement S
				WriteByte(RegSp, (byte)(_regPc & 0xFF));
				_regSpLo--;
				_step++;
				break;
			case 5: // copy low address byte to PCL, fetch high address byte to PCH
				_regPc = (ushort)((FetchByte() << 8) | _fetchLow);
				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	#endregion

	#region Operations

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpAdc(byte value)
	{
		var result = _regA + value + (_flagCarry ? 1 : 0);
		var overflow = (_regA & (1 << 7)) == (value & (1 << 7)) && (value & (1 << 7)) != (result & (1 << 7));

		_regA = (byte)result;

		_flagNegative = ((_regA >> 7) & 1) != 0;
		_flagZero = _regA == 0;
		_flagCarry = result > byte.MaxValue;
		_flagOverflow = overflow;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpAnd(byte value)
	{
		_regA &= value;
		_flagNegative = ((_regA >> 7) & 1) != 0;
		_flagZero = _regA == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpAsl(ref byte value)
	{
		var carry = ((value >> 7) & 1) != 0;

		value <<= 1;

		_flagNegative = ((value >> 7) & 1) != 0;
		_flagZero = value == 0;
		_flagCarry = carry;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpBit(byte value)
	{
		_flagNegative = ((value >> 7) & 1) != 0;
		_flagZero = (_regA & value) == 0;
		_flagOverflow = ((value >> 6) & 1) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpClc() => _flagCarry = false;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpCld() => _flagDecimal = false;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpCli() => _flagInterruptDisable = false;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpClv() => _flagOverflow = false;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpCmp(byte value)
	{
		var result = _regA - value;

		_flagNegative = ((result >> 7) & 1) != 0;
		_flagZero = _regA == value;
		_flagCarry = _regA >= value;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpCpx(byte value)
	{
		var result = _regX - value;

		_flagNegative = ((result >> 7) & 1) != 0;
		_flagZero = _regX == value;
		_flagCarry = _regX >= value;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpCpy(byte value)
	{
		var result = _regY - value;

		_flagNegative = ((result >> 7) & 1) != 0;
		_flagZero = _regY == value;
		_flagCarry = _regY >= value;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpIllDcp(ref byte value)
	{
		value--;
		var result = _regA - value;

		_flagNegative = ((result >> 7) & 1) != 0;
		_flagZero = _regA == value;
		_flagCarry = _regA >= value;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpDec(ref byte value)
	{
		value--;

		_flagNegative = ((value >> 7) & 1) != 0;
		_flagZero = value == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpDex()
	{
		_regX--;
		_flagNegative = (_regX & (1 << 7)) != 0;
		_flagZero = _regX == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpDey()
	{
		_regY--;
		_flagNegative = (_regY & (1 << 7)) != 0;
		_flagZero = _regY == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpEor(byte value)
	{
		_regA ^= value;
		_flagNegative = ((_regA >> 7) & 1) != 0;
		_flagZero = _regA == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpInc(ref byte value)
	{
		value++;

		_flagNegative = ((value >> 7) & 1) != 0;
		_flagZero = value == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpInx()
	{
		_regX++;
		_flagNegative = (_regX & (1 << 7)) != 0;
		_flagZero = _regX == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpIny()
	{
		_regY++;
		_flagNegative = (_regY & (1 << 7)) != 0;
		_flagZero = _regY == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpLda(byte value)
	{
		_regA = value;
		_flagNegative = ((_regA >> 7) & 1) != 0;
		_flagZero = _regA == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpLdx(byte value)
	{
		_regX = value;
		_flagNegative = ((_regX >> 7) & 1) != 0;
		_flagZero = _regX == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpLdy(byte value)
	{
		_regY = value;
		_flagNegative = ((_regY >> 7) & 1) != 0;
		_flagZero = _regY == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpLsr(ref byte value)
	{
		var carry = (value & 1) != 0;

		value >>= 1;

		_flagNegative = ((value >> 7) & 1) != 0;
		_flagZero = value == 0;
		_flagCarry = carry;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpOra(byte value)
	{
		_regA |= value;
		_flagNegative = ((_regA >> 7) & 1) != 0;
		_flagZero = _regA == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpRol(ref byte value)
	{
		var carry = ((value >> 7) & 1) != 0;

		value <<= 1;
		value |= (byte)(_flagCarry ? 1 : 0);

		_flagNegative = ((value >> 7) & 1) != 0;
		_flagZero = value == 0;
		_flagCarry = carry;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpRor(ref byte value)
	{
		var carry = (value & 1) != 0;

		value >>= 1;
		value |= (byte)((_flagCarry ? 1 : 0) << 7);

		_flagNegative = ((value >> 7) & 1) != 0;
		_flagZero = value == 0;
		_flagCarry = carry;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpSbc(byte value)
	{
		var result = _regA - value - (_flagCarry ? 0 : 1);
		var underflow = (_regA & (1 << 7)) == ((255 - value) & (1 << 7)) && ((255 - value) & (1 << 7)) != (result & (1 << 7));

		_regA = (byte)result;

		_flagNegative = ((_regA >> 7) & 1) != 0;
		_flagZero = _regA == 0;
		_flagCarry = result >= 0;
		_flagOverflow = underflow;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpSec() => _flagCarry = true;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpSed() => _flagDecimal = true;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpSei() => _flagInterruptDisable = true;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private byte ExecuteOpSta() => _regA;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private byte ExecuteOpStx() => _regX;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private byte ExecuteOpSty() => _regY;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpTax()
	{
		_regX = _regA;
		_flagNegative = ((_regX >> 7) & 1) != 0;
		_flagZero = _regX == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpTay()
	{
		_regY = _regA;
		_flagNegative = ((_regY >> 7) & 1) != 0;
		_flagZero = _regY == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpTsx()
	{
		_regX = _regSpLo;
		_flagNegative = ((_regX >> 7) & 1) != 0;
		_flagZero = _regX == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpTxa()
	{
		_regA = _regX;
		_flagNegative = ((_regA >> 7) & 1) != 0;
		_flagZero = _regA == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpTxs()
	{
		_regSpLo = _regX;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpTya()
	{
		_regA = _regY;
		_flagNegative = ((_regA >> 7) & 1) != 0;
		_flagZero = _regA == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpIllIsc(ref byte value)
	{
		value++;

		var result = _regA - value - (_flagCarry ? 0 : 1);
		var underflow = (_regA & (1 << 7)) == ((255 - value) & (1 << 7)) && ((255 - value) & (1 << 7)) != (result & (1 << 7));

		_regA = (byte)result;

		_flagNegative = ((_regA >> 7) & 1) != 0;
		_flagZero = _regA == 0;
		_flagCarry = result >= 0;
		_flagOverflow = underflow;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpIllLax(byte value)
	{
		_regA = value;
		_regX = value;

		_flagNegative = ((value >> 7) & 1) != 0;
		_flagZero = value == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpIllRla(ref byte value)
	{
		var carry = ((value >> 7) & 1) != 0;

		value <<= 1;
		value |= (byte)(_flagCarry ? 1 : 0);

		_regA &= value;

		_flagNegative = ((_regA >> 7) & 1) != 0;
		_flagZero = _regA == 0;
		_flagCarry = carry;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpIllRra(ref byte value)
	{
		var carry = (value & 1) != 0;

		value >>= 1;
		value |= (byte)((_flagCarry ? 1 : 0) << 7);

		var result = _regA + value + (carry ? 1 : 0);
		var overflow = (_regA & (1 << 7)) == (value & (1 << 7)) && (value & (1 << 7)) != (result & (1 << 7));

		_regA = (byte)result;

		_flagNegative = ((_regA >> 7) & 1) != 0;
		_flagZero = _regA == 0;
		_flagCarry = result > byte.MaxValue;
		_flagOverflow = overflow;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private byte ExecuteOpIllSax() => (byte)(_regA & _regX);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpIllSlo(ref byte value)
	{
		var carry = ((value >> 7) & 1) != 0;

		value <<= 1;

		_flagNegative = ((value >> 7) & 1) != 0;
		_flagCarry = carry;

		_regA |= value;
		_flagNegative = ((_regA >> 7) & 1) != 0;
		_flagZero = _regA == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpIllSre(ref byte value)
	{
		var carry = (value & 1) != 0;

		value >>= 1;

		_regA ^= value;

		_flagCarry = carry;
		_flagNegative = ((_regA >> 7) & 1) != 0;
		_flagZero = _regA == 0;
	}

	#endregion

	#region Old

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstAdc()
	{
		switch (_step)
		{
			case 0: // fetch opcode, increment PC
				_step++;
				break;
			case 1: // fetch address, increment PC on every byte
				if (CurrentAddressingMode == AddressingMode.Immediate)
				{
					_fetchOperand = FetchByte();
					goto immediate;
				}

				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2: // read from effective address
				ReadMemoryOperand();
			immediate:

				var result = _regA + _fetchOperand + (_flagCarry ? 1 : 0);
				var overflow = (_regA & (1 << 7)) == (_fetchOperand & (1 << 7)) && (_fetchOperand & (1 << 7)) != (result & (1 << 7));

				_regA = (byte)result;

				_flagNegative = ((_regA >> 7) & 1) != 0;
				_flagZero = _regA == 0;
				_flagCarry = result > byte.MaxValue;
				_flagOverflow = overflow;

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstAnd()
	{
		switch (_step)
		{
			case 0: // fetch opcode, increment PC
				_step++;
				break;
			case 1: // fetch address, increment PC on every byte
				if (CurrentAddressingMode == AddressingMode.Immediate)
				{
					_fetchOperand = FetchByte();
					goto immediate;
				}

				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2: // read from effective address
				ReadMemoryOperand();
			immediate:

				_regA &= _fetchOperand;
				_flagNegative = ((_regA >> 7) & 1) != 0;
				_flagZero = _regA == 0;

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstAsl()
	{
		switch (_step)
		{
			case 0: // fetch opcode, increment PC
				_step++;
				break;
			case 1: // fetch address, increment PC on every byte
				if (CurrentAddressingMode == AddressingMode.Accumulator)
				{
					var carry = ((_regA >> 7) & 1) != 0;

					_regA <<= 1;

					_flagNegative = ((_regA >> 7) & 1) != 0;
					_flagZero = _regA == 0;
					_flagCarry = carry;

					_step = 0;
					break;
				}

				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				_step++;

				if (CurrentAddressingMode is not AddressingMode.AbsoluteXIndexed)
					goto case 3;

				break;
			case 3: // read from effective address
				ReadMemoryOperand();

				_step++;
				break;
			case 4: // write the value back to effective address, and do the operation on it
				{
					WriteMemoryOperand();
					var carry = ((_fetchOperand >> 7) & 1) != 0;

					_fetchOperand <<= 1;

					_flagNegative = ((_fetchOperand >> 7) & 1) != 0;
					_flagZero = _fetchOperand == 0;
					_flagCarry = carry;

					_step++;
					break;
				}
			case 5: // write the new value to effective address
				WriteMemoryOperand();

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstBcc() => ExecuteConditionalBranch(!_flagCarry);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstBcs() => ExecuteConditionalBranch(_flagCarry);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstBeq() => ExecuteConditionalBranch(_flagZero);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstBit()
	{
		switch (_step)
		{
			case 0: // Fetch opcode
				_step++;
				break;
			case 1: // Fetch address
				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				ReadMemoryOperand();

				_flagNegative = ((_fetchOperand >> 7) & 1) != 0;
				_flagZero = (_regA & _fetchOperand) == 0;
				_flagOverflow = ((_fetchOperand >> 6) & 1) != 0;

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstBmi() => ExecuteConditionalBranch(_flagNegative);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstBne() => ExecuteConditionalBranch(!_flagZero);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstBpl() => ExecuteConditionalBranch(!_flagNegative);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstBvc() => ExecuteConditionalBranch(!_flagOverflow);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstBvs() => ExecuteConditionalBranch(_flagOverflow);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstCmp()
	{
		switch (_step)
		{
			case 0:
				_step++;
				break;
			case 1:
				if (CurrentAddressingMode == AddressingMode.Immediate)
				{
					_fetchOperand = FetchByte();
					goto immediate;
				}

				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				ReadMemoryOperand();
			immediate:

				var result = _regA - _fetchOperand;

				_flagNegative = ((result >> 7) & 1) != 0;
				_flagZero = _regA == _fetchOperand;
				_flagCarry = _regA >= _fetchOperand;

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstCpx()
	{
		switch (_step)
		{
			case 0:
				_step++;
				break;
			case 1:
				if (CurrentAddressingMode == AddressingMode.Immediate)
				{
					_fetchOperand = FetchByte();
					goto immediate;
				}

				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				ReadMemoryOperand();
			immediate:

				var result = _regX - _fetchOperand;

				_flagNegative = ((result >> 7) & 1) != 0;
				_flagZero = _regX == _fetchOperand;
				_flagCarry = _regX >= _fetchOperand;

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstCpy()
	{
		switch (_step)
		{
			case 0:
				_step++;
				break;
			case 1:
				if (CurrentAddressingMode == AddressingMode.Immediate)
				{
					_fetchOperand = FetchByte();
					goto immediate;
				}

				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				ReadMemoryOperand();
			immediate:

				var result = _regY - _fetchOperand;

				_flagNegative = ((result >> 7) & 1) != 0;
				_flagZero = _regY == _fetchOperand;
				_flagCarry = _regY >= _fetchOperand;

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstDec()
	{
		switch (_step)
		{
			case 0: // fetch opcode, increment PC
				_step++;
				break;
			case 1: // fetch address, increment PC on every byte
				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				_step++;

				if (CurrentAddressingMode is not AddressingMode.AbsoluteXIndexed)
					goto case 3;

				break;
			case 3: // read from effective address
				ReadMemoryOperand();

				_step++;
				break;
			case 4: // write the value back to effective address, and do the operation on it
				{
					WriteMemoryOperand();
					_fetchOperand--;

					_flagNegative = ((_fetchOperand >> 7) & 1) != 0;
					_flagZero = _fetchOperand == 0;

					_step++;
					break;
				}
			case 5:
				WriteMemoryOperand();

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstEor()
	{
		switch (_step)
		{
			case 0: // fetch opcode, increment PC
				_step++;
				break;
			case 1: // fetch address, increment PC on every byte
				if (CurrentAddressingMode == AddressingMode.Immediate)
				{
					_fetchOperand = FetchByte();
					goto immediate;
				}

				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2: // read from effective address
				ReadMemoryOperand();
			immediate:

				_regA ^= _fetchOperand;
				_flagNegative = ((_regA >> 7) & 1) != 0;
				_flagZero = _regA == 0;

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstInc()
	{
		switch (_step)
		{
			case 0: // fetch opcode, increment PC
				_step++;
				break;
			case 1: // fetch address, increment PC on every byte
				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				_step++;

				if (CurrentAddressingMode is not AddressingMode.AbsoluteXIndexed)
					goto case 3;

				break;
			case 3: // read from effective address
				ReadMemoryOperand();
				_step++;
				break;
			case 4: // write the value back to effective address, and do the operation on it
				{
					WriteMemoryOperand();
					_fetchOperand++;

					_flagNegative = ((_fetchOperand >> 7) & 1) != 0;
					_flagZero = _fetchOperand == 0;

					_step++;
					break;
				}
			case 5: // write the new value to effective address
				WriteMemoryOperand();
				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstJmp()
	{
		switch (_step)
		{
			case 0: // Fetch opcode
				_step++;
				break;
			case 1:
				if (!FetchAddress())
					break;

				_regPc = _fetchedAddress;

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstLda()
	{
		switch (_step)
		{
			case 0: // fetch opcode, increment PC
				_step++;
				break;
			case 1: // fetch low byte of address, increment PC, fetch high byte of address, increment PC
				if (CurrentAddressingMode == AddressingMode.Immediate)
				{
					_fetchOperand = FetchByte();
					goto immediate;
				}

				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2: // extra cycle for crossed page boundary
				if (!_pageBoundaryCrossed || CurrentAddressingMode is not AddressingMode.AbsoluteXIndexed and not AddressingMode.AbsoluteYIndexed and not AddressingMode.IndirectYIndexed)
				{
					_step = 3;
					goto case 3;
				}
				_step++;
				break;
			case 3: // read from effective address
				ReadMemoryOperand();
			immediate:
				_regA = _fetchOperand;
				_flagNegative = ((_regA >> 7) & 1) != 0;
				_flagZero = _regA == 0;

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstLdx()
	{
		switch (_step)
		{
			case 0: // fetch opcode, increment PC
				_step++;
				break;
			case 1: // fetch low byte of address, increment PC, fetch high byte of address, increment PC
				if (CurrentAddressingMode == AddressingMode.Immediate)
				{
					_fetchOperand = FetchByte();
					goto immediate;
				}

				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2: // extra cycle for crossed page boundary
				_step++;

				if (!_pageBoundaryCrossed || CurrentAddressingMode is not AddressingMode.AbsoluteYIndexed)
					goto case 3;

				break;
			case 3: // read from effective address
				ReadMemoryOperand();
			immediate:
				_regX = _fetchOperand;
				_flagNegative = ((_regX >> 7) & 1) != 0;
				_flagZero = _regX == 0;

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstLdy()
	{
		switch (_step)
		{
			case 0: // fetch opcode, increment PC
				_step++;
				break;
			case 1: // fetch low byte of address, increment PC, fetch high byte of address, increment PC
				if (CurrentAddressingMode == AddressingMode.Immediate)
				{
					_fetchOperand = FetchByte();
					goto immediate;
				}

				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2: // extra cycle for crossed page boundary
				_step++;

				if (!_pageBoundaryCrossed || CurrentAddressingMode is not AddressingMode.AbsoluteXIndexed)
					goto case 3;

				break;
			case 3: // read from effective address
				ReadMemoryOperand();
			immediate:
				_regY = _fetchOperand;
				_flagNegative = ((_regY >> 7) & 1) != 0;
				_flagZero = _regY == 0;

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstLsr()
	{
		switch (_step)
		{
			case 0: // fetch opcode, increment PC
				_step++;
				break;
			case 1: // read address
				if (CurrentAddressingMode == AddressingMode.Accumulator)
				{
					var carry = (_regA & 1) != 0;

					_regA >>= 1;

					_flagNegative = ((_regA >> 7) & 1) != 0;
					_flagZero = _regA == 0;
					_flagCarry = carry;
					_step = 0;
					break;
				}

				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				_step++;
				if (CurrentAddressingMode is not AddressingMode.AbsoluteXIndexed)
					goto case 3;
				break;
			case 3: // read from effective address
				ReadMemoryOperand();
				_step++;
				break;
			case 4: // write the value back to effective address, and do the operation on it
				{
					WriteMemoryOperand();
					var carry = (_fetchOperand & 1) != 0;

					_fetchOperand >>= 1;

					_flagNegative = ((_fetchOperand >> 7) & 1) != 0;
					_flagZero = _fetchOperand == 0;
					_flagCarry = carry;



					_step++;
					break;
				}
			case 5: // write the new value to effective address
				WriteMemoryOperand();
				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstNop()
	{
		switch (_step)
		{
			case 0: // Fetch opcode
				_step++;
				break;
			case 1:
				if (CurrentAddressingMode == AddressingMode.Implied)
				{
					_step = 0;
					break;
				}

				if (CurrentAddressingMode == AddressingMode.Immediate)
				{
					FetchByte();
					_step = 0;
					break;
				}

				_step++;
				break;
			case 2:
				if (!FetchAddress())
					break;

				if (CurrentAddressingMode == AddressingMode.AbsoluteXIndexed && _pageBoundaryCrossed)
				{
					_step++;
					break;
				}

				_step = 0;
				break;
			case 3:
				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstOra()
	{
		switch (_step)
		{
			case 0: // fetch opcode, increment PC
				_step++;
				break;
			case 1: // fetch address, increment PC on every byte
				if (CurrentAddressingMode == AddressingMode.Immediate)
				{
					_fetchOperand = FetchByte();
					goto immediate;
				}

				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2: // read from effective address
				ReadMemoryOperand();
			immediate:

				_regA |= _fetchOperand;
				_flagNegative = ((_regA >> 7) & 1) != 0;
				_flagZero = _regA == 0;

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstRol()
	{
		switch (_step)
		{
			case 0: // fetch opcode, increment PC
				_step++;
				break;
			case 1: // fetch address, increment PC on every byte
				if (CurrentAddressingMode == AddressingMode.Accumulator)
				{
					var carry = ((_regA >> 7) & 1) != 0;

					_regA <<= 1;
					_regA |= (byte)(_flagCarry ? 1 : 0);

					_flagNegative = ((_regA >> 7) & 1) != 0;
					_flagZero = _regA == 0;
					_flagCarry = carry;

					_step = 0;
					break;
				}

				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				_step++;

				if (CurrentAddressingMode is not AddressingMode.AbsoluteXIndexed)
					goto case 3;

				break;
			case 3: // read from effective address
				ReadMemoryOperand();
				_step++;
				break;
			case 4: // write the value back to effective address, and do the operation on it
				{
					WriteMemoryOperand();
					var carry = ((_fetchOperand >> 7) & 1) != 0;

					_fetchOperand <<= 1;
					_fetchOperand |= (byte)(_flagCarry ? 1 : 0);

					_flagNegative = ((_fetchOperand >> 7) & 1) != 0;
					_flagZero = _fetchOperand == 0;
					_flagCarry = carry;

					_step++;
					break;
				}
			case 5: // write the new value to effective address
				WriteMemoryOperand();
				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstRor()
	{
		switch (_step)
		{
			case 0: // fetch opcode, increment PC
				_step++;
				break;
			case 1: // fetch address, increment PC on every byte
				if (CurrentAddressingMode == AddressingMode.Accumulator)
				{
					var carry = (_regA & 1) != 0;

					_regA >>= 1;
					_regA |= (byte)((_flagCarry ? 1 : 0) << 7);

					_flagNegative = ((_regA >> 7) & 1) != 0;
					_flagZero = _regA == 0;
					_flagCarry = carry;

					_step = 0;
					break;
				}

				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				_step++;

				if (CurrentAddressingMode is not AddressingMode.AbsoluteXIndexed)
					goto case 3;

				break;
			case 3: // read from effective address
				ReadMemoryOperand();

				_step++;
				break;
			case 4: // write the value back to effective address, and do the operation on it
				{
					WriteMemoryOperand();
					var carry = (_fetchOperand & 1) != 0;

					_fetchOperand >>= 1;
					_fetchOperand |= (byte)((_flagCarry ? 1 : 0) << 7);

					_flagNegative = ((_fetchOperand >> 7) & 1) != 0;
					_flagZero = _fetchOperand == 0;
					_flagCarry = carry;

					_step++;
					break;
				}
			case 5: // write the new value to effective address
				WriteMemoryOperand();
				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstSbc()
	{
		switch (_step)
		{
			case 0:
				_step++;
				break;
			case 1:
				if (CurrentAddressingMode == AddressingMode.Immediate)
				{
					_fetchOperand = FetchByte();
					goto immediate;
				}

				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				ReadMemoryOperand();
			immediate:

				var result = _regA - _fetchOperand - (_flagCarry ? 0 : 1);
				var underflow = (_regA & (1 << 7)) == ((255 - _fetchOperand) & (1 << 7)) && ((255 - _fetchOperand) & (1 << 7)) != (result & (1 << 7));

				_regA = (byte)result;

				_flagNegative = ((_regA >> 7) & 1) != 0;
				_flagZero = _regA == 0;
				_flagCarry = result >= 0;
				_flagOverflow = underflow;

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstSta()
	{
		switch (_step)
		{
			case 0: // Fetch opcode
				_step++;
				break;
			case 1: // Fetch address
				if (!FetchAddress())
					break;

				_step++;
				if (CurrentAddressingMode is not AddressingMode.AbsoluteXIndexed and not AddressingMode.AbsoluteYIndexed and not AddressingMode.IndirectYIndexed)
					_step++;

				break;
			case 2:
				_step++;
				break;
			case 3:
				_fetchOperand = _regA;
				WriteMemoryOperand();
				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstStx()
	{
		switch (_step)
		{
			case 0: // Fetch opcode
				_step++;
				break;
			case 1: // Fetch address
				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				_fetchOperand = _regX;
				WriteMemoryOperand();
				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstSty()
	{
		switch (_step)
		{
			case 0: // Fetch opcode
				_step++;
				break;
			case 1: // Fetch address
				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				_fetchOperand = _regY;
				WriteMemoryOperand();
				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstIllDcp()
	{
		switch (_step)
		{
			case 0: // fetch opcode, increment PC
				_step++;
				break;
			case 1: // fetch address, increment PC on every byte
				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				_step++;

				if (CurrentAddressingMode is not AddressingMode.AbsoluteXIndexed and not AddressingMode.AbsoluteYIndexed and not AddressingMode.IndirectYIndexed)
					goto case 3;

				break;
			case 3: // read from effective address
				ReadMemoryOperand();
				_step++;
				break;
			case 4: // write the value back to effective address, and do the operation on it
				{
					WriteMemoryOperand();
					_fetchOperand--;
					var result = _regA - _fetchOperand;

					_flagNegative = ((result >> 7) & 1) != 0;
					_flagZero = _regA == _fetchOperand;
					_flagCarry = _regA >= _fetchOperand;

					_step++;
					break;
				}
			case 5:
				WriteMemoryOperand();
				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstIllIsc()
	{
		switch (_step)
		{
			case 0: // fetch opcode, increment PC
				_step++;
				break;
			case 1: // fetch address, increment PC on every byte
				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				_step++;

				if (CurrentAddressingMode is not AddressingMode.AbsoluteXIndexed and not AddressingMode.AbsoluteYIndexed and not AddressingMode.IndirectYIndexed)
					goto case 3;

				break;
			case 3: // read from effective address
				ReadMemoryOperand();
				_step++;
				break;
			case 4: // write the value back to effective address, and do the operation on it
				{
					WriteMemoryOperand();
					_fetchOperand++;

					var result = _regA - _fetchOperand - (_flagCarry ? 0 : 1);
					var underflow = (_regA & (1 << 7)) == ((255 - _fetchOperand) & (1 << 7)) && ((255 - _fetchOperand) & (1 << 7)) != (result & (1 << 7));

					_regA = (byte)result;

					_flagNegative = ((_regA >> 7) & 1) != 0;
					_flagZero = _regA == 0;
					_flagCarry = result >= 0;
					_flagOverflow = underflow;

					_step++;
					break;
				}
			case 5: // write the new value to effective address
				WriteMemoryOperand();

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstIllLax()
	{
		switch (_step)
		{
			case 0: // Fetch opcode
				_step++;
				break;
			case 1: // Fetch address
				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				if (!_pageBoundaryCrossed || CurrentAddressingMode is not AddressingMode.AbsoluteYIndexed and not AddressingMode.IndirectYIndexed)
				{
					_step = 3;
					goto case 3;
				}
				_step++;
				break;
			case 3:
				ReadMemoryOperand();
				_regA = _fetchOperand;
				_regX = _fetchOperand;

				_flagNegative = ((_regX >> 7) & 1) != 0;
				_flagZero = _regX == 0;

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstIllRla()
	{
		switch (_step)
		{
			case 0: // fetch opcode, increment PC
				_step++;
				break;
			case 1: // fetch address, increment PC on every byte
				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				_step++;

				if (CurrentAddressingMode is not AddressingMode.AbsoluteXIndexed and not AddressingMode.AbsoluteYIndexed and not AddressingMode.IndirectYIndexed)
					goto case 3;

				break;
			case 3: // read from effective address
				ReadMemoryOperand();
				_step++;
				break;
			case 4: // write the value back to effective address, and do the operation on it
				{
					WriteMemoryOperand();
					var carry = ((_fetchOperand >> 7) & 1) != 0;

					_fetchOperand <<= 1;
					_fetchOperand |= (byte)(_flagCarry ? 1 : 0);

					_regA &= _fetchOperand;

					_flagNegative = ((_regA >> 7) & 1) != 0;
					_flagZero = _regA == 0;
					_flagCarry = carry;

					_step++;
					break;
				}
			case 5: // write the new value to effective address
				WriteMemoryOperand();

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstIllRra()
	{
		switch (_step)
		{
			case 0: // fetch opcode, increment PC
				_step++;
				break;
			case 1: // fetch address, increment PC on every byte
				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				_step++;

				if (CurrentAddressingMode is not AddressingMode.AbsoluteXIndexed and not AddressingMode.AbsoluteYIndexed and not AddressingMode.IndirectYIndexed)
					goto case 3;

				break;
			case 3: // read from effective address
				ReadMemoryOperand();
				_step++;
				break;
			case 4: // write the value back to effective address, and do the operation on it
				{
					WriteMemoryOperand();
					var carry = (_fetchOperand & 1) != 0;

					_fetchOperand >>= 1;
					_fetchOperand |= (byte)((_flagCarry ? 1 : 0) << 7);

					var result = _regA + _fetchOperand + (carry ? 1 : 0);
					var overflow = (_regA & (1 << 7)) == (_fetchOperand & (1 << 7)) && (_fetchOperand & (1 << 7)) != (result & (1 << 7));

					_regA = (byte)result;

					_flagNegative = ((_regA >> 7) & 1) != 0;
					_flagZero = _regA == 0;
					_flagCarry = result > byte.MaxValue;
					_flagOverflow = overflow;

					_step++;
					break;
				}
			case 5: // write the new value to effective address
				WriteMemoryOperand();
				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstIllSax()
	{
		switch (_step)
		{
			case 0: // Fetch opcode
				_step++;
				break;
			case 1: // Fetch address
				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				if (!_pageBoundaryCrossed || CurrentAddressingMode is not AddressingMode.AbsoluteYIndexed and not AddressingMode.IndirectYIndexed)
				{
					_step = 3;
					goto case 3;
				}
				_step++;
				break;
			case 3:
				ReadMemoryOperand();
				_fetchOperand = (byte)(_regA & _regX);

				_step = 0;
				WriteMemoryOperand();
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstIllSlo()
	{
		switch (_step)
		{
			case 0: // fetch opcode, increment PC
				_step++;
				break;
			case 1: // fetch address, increment PC on every byte
				if (CurrentAddressingMode == AddressingMode.Accumulator)
				{
					var carry = ((_regA >> 7) & 1) != 0;

					_regA <<= 1;

					_flagNegative = ((_regA >> 7) & 1) != 0;
					_flagZero = _regA == 0;
					_flagCarry = carry;

					_step = 0;
					break;
				}

				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				_step++;

				if (CurrentAddressingMode is not AddressingMode.AbsoluteXIndexed and not AddressingMode.AbsoluteYIndexed and not AddressingMode.IndirectYIndexed)
					goto case 3;

				break;
			case 3: // read from effective address
				ReadMemoryOperand();

				_step++;
				break;
			case 4: // write the value back to effective address, and do the operation on it
				{
					WriteMemoryOperand();
					var carry = ((_fetchOperand >> 7) & 1) != 0;

					_fetchOperand <<= 1;

					_flagNegative = ((_fetchOperand >> 7) & 1) != 0;
					_flagCarry = carry;

					_regA |= _fetchOperand;
					_flagNegative = ((_regA >> 7) & 1) != 0;
					_flagZero = _regA == 0;

					_step++;
					break;
				}
			case 5: // write the new value to effective address
				WriteMemoryOperand();

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstIllSre()
	{
		switch (_step)
		{
			case 0: // fetch opcode, increment PC
				_step++;
				break;
			case 1: // fetch address, increment PC on every byte
				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				_step++;

				if (CurrentAddressingMode is not AddressingMode.AbsoluteXIndexed and not AddressingMode.AbsoluteYIndexed and not AddressingMode.IndirectYIndexed)
					goto case 3;

				break;
			case 3: // read from effective address
				ReadMemoryOperand();

				_step++;
				break;
			case 4: // write the value back to effective address, and do the operation on it
				{
					WriteMemoryOperand();
					var carry = (_fetchOperand & 1) != 0;

					_fetchOperand >>= 1;

					_regA ^= _fetchOperand;

					_flagCarry = carry;
					_flagNegative = ((_regA >> 7) & 1) != 0;
					_flagZero = _regA == 0;
					_step++;
					break;
				}
			case 5: // write the new value to effective address
				WriteMemoryOperand();

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteConditionalBranch(bool condition)
	{
		switch (_step)
		{
			case 0: // Fetch opcode
				_step++;
				break;
			case 1: // Check condition, stop if false
				_fetchOperand = FetchByte(); // Fetch the offset

				if (!condition)
				{
					_step = 0;
					break;
				}

				_step++;
				break;
			case 2:
				var page = _regPc >> 8;
				_regPc = (ushort)(_regPc + (sbyte)_fetchOperand);
				var newPage = _regPc >> 8;
				if (page == newPage)
					goto case 3;
				_step++;
				break;
			case 3: // An additional step that simply wastes a cycle if branching to different page
				_step = 0;
				break;
			default:
				throw new UnreachableException();

		}
	}

	#endregion
}
