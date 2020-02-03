namespace Maestro
{
	internal sealed class Compiler
	{
		private readonly struct State
		{
			public readonly string sourceContent;
			public readonly int sourceIndex;

			public readonly int tokenizerIndex;
			public readonly Token previousToken;
			public readonly Token currentToken;

			public State(string sourceContent, int sourceIndex, int tokenizerIndex, Token previousToken, Token currentToken)
			{
				this.sourceContent = sourceContent;
				this.sourceIndex = sourceIndex;

				this.tokenizerIndex = tokenizerIndex;
				this.previousToken = previousToken;
				this.currentToken = currentToken;
			}
		}

		public Mode mode;
		public readonly Parser parser;
		public ByteCodeChunk chunk;

		public int sourceIndex;
		public bool isInPanicMode;

		public Buffer<CompileError> errors = new Buffer<CompileError>();

		public Buffer<Variable> variables = new Buffer<Variable>(256);
		public Buffer<Scope> scopes = new Buffer<Scope>(1);

		private Buffer<State> stateStack = new Buffer<State>();

		public Compiler()
		{
			void AddTokenizerError(Slice slice, IFormattedMessage error)
			{
				AddHardError(slice, error);
			}

			parser = new Parser(AddTokenizerError);
		}

		public void Reset(Mode mode, ByteCodeChunk chunk)
		{
			this.mode = mode;
			this.chunk = chunk;

			errors.ZeroClear();
			stateStack.ZeroClear();
		}

		private void RestoreState(State state)
		{
			parser.tokenizer.Reset(state.sourceContent, state.tokenizerIndex);
			parser.Reset(state.previousToken, state.currentToken);
			sourceIndex = state.sourceIndex;

			isInPanicMode = false;
			scopes.count = 0;
		}

		public void BeginSource(string source, int sourceIndex)
		{
			chunk.sourceStartIndexes.PushBack(chunk.bytes.count);

			stateStack.PushBack(new State(
				parser.tokenizer.source,
				this.sourceIndex,

				parser.tokenizer.nextIndex,
				parser.previousToken,
				parser.currentToken
			));

			RestoreState(new State(
				source,
				sourceIndex,
				0,
				new Token(TokenKind.End, new Slice()),
				new Token(TokenKind.End, new Slice())
			));

			this.PushScope(ScopeType.Normal);
		}

		public void EndSource()
		{
			var current = stateStack.PopLast();
			this.PopScope();
			RestoreState(current);

			if (stateStack.count == 0)
				this.EmitInstruction(Instruction.Halt);
		}

		public void AddSoftError(Slice slice, IFormattedMessage error)
		{
			if (!isInPanicMode)
				errors.PushBack(new CompileError(sourceIndex, slice, error));
		}

		public void AddHardError(Slice slice, IFormattedMessage error)
		{
			if (!isInPanicMode)
			{
				isInPanicMode = true;
				errors.PushBack(new CompileError(sourceIndex, slice, error));
			}
		}
	}
}