using Xunit;
using Maestro;

public sealed class ParserTests
{
	[Theory]
	[InlineData("0;")]
	[InlineData("+0;")]
	[InlineData("-0;")]
	[InlineData("1.05;")]
	[InlineData("-1.05;")]
	[InlineData("true;")]
	[InlineData("false;")]
	[InlineData("\"string\";")]
	[InlineData("99, true, \"string\";")]
	[InlineData("(false);")]
	[InlineData("(99, true, \"string\");")]
	[InlineData("(((false)));")]
	public void SimpleStatements(string source)
	{
		TestHelper.Compile(source);
	}

	[Theory]
	[InlineData("bypass 0;")]
	[InlineData("1, false | bypass 0;")]
	[InlineData("1, false | bypass 0 | bypass 1;")]
	[InlineData("1, false | bypass (\"string\" | bypass 1);")]
	[InlineData("1, false | bypass bypass bypass 0;")]
	public void CommandStatements(string source)
	{
		var engine = new Engine();
		engine.RegisterCommand("bypass", () => new BypassCommand<Tuple1>());
		source = "external command bypass 1;\n" + source;
		TestHelper.Compile(engine, source);
	}

	[Theory]
	[InlineData("0")]
	[InlineData("1 false | bypass 0;")]
	[InlineData("(0;")]
	[InlineData("0);")]
	[InlineData(")0;")]
	[InlineData("(0;);")]
	public void FailStatements(string source)
	{
		Assert.Throws<CompileErrorException>(() =>
		{
			var engine = new Engine();
			engine.RegisterCommand("bypass", () => new BypassCommand<Tuple1>());
			source = "external command bypass 1;\n" + source;
			TestHelper.Compile(engine, source);
		});
	}
}