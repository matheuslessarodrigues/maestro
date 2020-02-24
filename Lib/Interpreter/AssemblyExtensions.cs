using System.Text;

namespace Maestro
{
	internal sealed class AssemblyDebugView
	{
		internal readonly string[] lines;

		internal AssemblyDebugView(Assembly assembly)
		{
			var sb = new StringBuilder();
			assembly.Disassemble(sb);
			lines = sb.ToString().Split(new string[] { "\n", "\r\n" }, System.StringSplitOptions.RemoveEmptyEntries);
		}
	}

	public static class AssemblyExtensions
	{
		public static int FindSourceIndex(this Assembly self, int codeIndex)
		{
			for (var i = 0; i < self.sourceStartIndexes.count; i++)
			{
				if (codeIndex >= self.sourceStartIndexes.buffer[i])
					return i;
			}

			return -1;
		}

		internal static void Disassemble(this Assembly self, StringBuilder sb)
		{
			sb.Append("== ");
			sb.Append(self.bytes.count);
			sb.AppendLine(" bytes ==");

			if (self.sources.count > 0)
			{
				sb.AppendLine("line byte instruction");
				sb.AppendLine("---- ---- -----------");

				var currentSourceIndex = -1;
				for (var index = 0; index < self.bytes.count;)
				{
					var sourceIndex = self.FindSourceIndex(index);
					var source = self.sources.buffer[sourceIndex];
					if (sourceIndex != currentSourceIndex)
					{
						sb.AppendLine(source.uri);
						currentSourceIndex = sourceIndex;
					}

					PrintCommandName(self, index, sb);
					PrintLineNumber(self, source.content, index, sb);
					index = DisassembleInstruction(self, index, sb);
					sb.AppendLine();
				}
			}
			else
			{
				sb.AppendLine("byte instruction");
				sb.AppendLine("---- -----------");

				for (var index = 0; index < self.bytes.count;)
				{
					PrintCommandName(self, index, sb);
					index = DisassembleInstruction(self, index, sb);
					sb.AppendLine();
				}
			}

			sb.AppendLine("== end ==");
		}

		private static void PrintCommandName(Assembly self, int codeIndex, StringBuilder sb)
		{
			for (var i = 0; i < self.commandDefinitions.count; i++)
			{
				var definition = self.commandDefinitions.buffer[i];
				if (definition.codeIndex == codeIndex)
				{
					sb.Append("   # ");
					sb.AppendLine(definition.name);
					break;
				}
			}
		}

		private static void PrintLineNumber(Assembly self, string source, int index, StringBuilder sb)
		{
			var currentSourceIndex = self.sourceSlices.buffer[index].index;
			var currentPosition = FormattingHelper.GetLineAndColumn(source, currentSourceIndex);
			var lastLineIndex = -1;
			if (index > 0)
			{
				var lastSourceIndex = self.sourceSlices.buffer[index - 1].index;
				lastLineIndex = FormattingHelper.GetLineAndColumn(source, lastSourceIndex).lineIndex;
			}

			if (currentPosition.lineIndex == lastLineIndex)
				sb.Append("   | ");
			else
				sb.AppendFormat("{0,4} ", currentPosition.lineIndex);
		}

		internal static int DisassembleInstruction(this Assembly self, int index, StringBuilder sb)
		{
			var instructionCode = self.bytes.buffer[index];
			var instruction = (Instruction)instructionCode;

			if (instruction == Instruction.DebugHook)
				return DisassembleInstruction(self, index + 1, sb);

			sb.AppendFormat("{0:0000} ", index);

			switch (instruction)
			{
			case Instruction.Halt:
			case Instruction.Return:
			case Instruction.PushEmptyTuple:
			case Instruction.MergeTuple:
			case Instruction.PushFalse:
			case Instruction.PushTrue:
			case Instruction.PushInput:
			case Instruction.DebugHook:
			case Instruction.DebugPushDebugFrame:
			case Instruction.DebugPopDebugFrame:
				return OneByteInstruction(instruction, index, sb);
			case Instruction.PopTupleKeeping:
			case Instruction.Pop:
			case Instruction.SetLocal:
			case Instruction.PushLocal:
			case Instruction.DebugPopVariableInfo:
				return TwoByteInstruction(self, instruction, index, sb);
			case Instruction.PushLiteral:
				return LoadLiteralInstruction(self, instruction, index, sb);
			case Instruction.DebugPushVariableInfo:
				return DebugPushLocalInfoInstruction(self, instruction, index, sb);
			case Instruction.ExecuteNativeCommand:
				return ExecuteExternalCommandInstruction(self, instruction, index, sb);
			case Instruction.ExecuteCommand:
				return ExecuteCommandInstruction(self, instruction, index, sb);
			case Instruction.JumpBackward:
				return JumpInstruction(self, instruction, -1, index, sb);
			case Instruction.JumpForward:
			case Instruction.IfConditionJump:
			case Instruction.ForEachConditionJump:
				return JumpInstruction(self, instruction, 1, index, sb);
			default:
				sb.Append("Unknown instruction ");
				sb.Append(instruction.ToString());
				return index + 1;
			}
		}

		private static int OneByteInstruction(Instruction instruction, int index, StringBuilder sb)
		{
			sb.Append(instruction.ToString());
			return index + 1;
		}

		private static int TwoByteInstruction(Assembly assembly, Instruction instruction, int index, StringBuilder sb)
		{
			sb.Append(instruction.ToString());
			sb.Append(' ');
			sb.Append(assembly.bytes.buffer[++index]);
			return ++index;
		}

		private static int LoadLiteralInstruction(Assembly assembly, Instruction instruction, int index, StringBuilder sb)
		{
			var literalIndex = BytesHelper.BytesToUShort(
				assembly.bytes.buffer[++index],
				assembly.bytes.buffer[++index]
			);
			var value = assembly.literals.buffer[literalIndex];

			sb.Append(instruction.ToString());
			sb.Append(' ');
			value.AppendTo(sb);

			return ++index;
		}

		private static int DebugPushLocalInfoInstruction(Assembly assembly, Instruction instruction, int index, StringBuilder sb)
		{
			var literalIndex = BytesHelper.BytesToUShort(
				assembly.bytes.buffer[++index],
				assembly.bytes.buffer[++index]
			);
			var stackIndex = assembly.bytes.buffer[++index];

			var name = assembly.literals.buffer[literalIndex];

			sb.Append(instruction.ToString());
			sb.Append(" '");
			sb.Append(name.asObject);
			sb.Append("' at ");
			sb.Append(stackIndex);

			return ++index;
		}

		private static int ExecuteExternalCommandInstruction(Assembly assembly, Instruction instruction, int index, StringBuilder sb)
		{
			var instanceIndex = BytesHelper.BytesToUShort(
				assembly.bytes.buffer[++index],
				assembly.bytes.buffer[++index]
			);

			var instance = assembly.externalCommandInstances.buffer[instanceIndex];
			var definition = assembly.externalCommandDefinitions.buffer[instance.definitionIndex];

			sb.Append(instruction.ToString());
			sb.Append(" '");
			sb.Append(definition.name);
			sb.Append("'");

			return ++index;
		}

		private static int ExecuteCommandInstruction(Assembly assembly, Instruction instruction, int index, StringBuilder sb)
		{
			var definitionIndex = BytesHelper.BytesToUShort(
				assembly.bytes.buffer[++index],
				assembly.bytes.buffer[++index]
			);

			var definition = assembly.commandDefinitions.buffer[definitionIndex];

			sb.Append(instruction.ToString());
			sb.Append(" '");
			sb.Append(definition.name);
			sb.Append("'");

			return ++index;
		}

		private static int JumpInstruction(Assembly assembly, Instruction instruction, int direction, int index, StringBuilder sb)
		{
			var offset = BytesHelper.BytesToUShort(
				assembly.bytes.buffer[++index],
				assembly.bytes.buffer[++index]
			);

			sb.Append(instruction.ToString());
			sb.Append(' ');
			sb.Append(offset);
			sb.Append(" goto ");
			sb.Append(index + 1 + offset * direction);

			return ++index;
		}
	}
}