// Generated from UserDefinedMacroMacro.ecs by LeMP custom tool. LeMP version: 30.1.0.0
// Note: you can give command-line arguments to the tool via 'Custom Tool Namespace':
// --no-out-header       Suppress this message
// --verbose             Allow verbose messages (shown by VS as 'warnings')
// --timeout=X           Abort processing thread after X seconds (default: 10)
// --macros=FileName.dll Load macros from FileName.dll, path relative to this file 
// Use #importMacros to use macros in a given namespace, e.g. #importMacros(Loyc.LLPG);
using Loyc;
using Loyc.Collections;
using Loyc.Ecs;
using Loyc.Syntax;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static LeMP.StandardMacros;
using S = Loyc.Ecs.EcsCodeSymbols;

namespace LeMP.ecs
{
	partial class StandardMacros
	{
		[LexicalMacro("macro (pattern) { /* C# macro method body */ }", 
		"Compiles and registers a macro that recognizes the specified syntactical pattern and " + 
		"optionally produces a replacement pattern. LeMP will preprocess the method body before " + 
		"giving it to the C# scripting engine, so LeMP macros can be used inside the method body. " + 
		"The method body has access to two parameter variables (LNode node, IMacroContext context) " + 
		"and it can change the matched code by returning new code. " + 
		"MacroMode enum values can be attached as attributes. For example, the [PriorityOverride] attribute will cause this macro to supercede other macros that use default priority. " + 
		"Example: macro ($lhs += 1) { return quote($lhs++); }")] 
		public static LNode macro(LNode node, IMacroContext context)
		{
			if (node.Args.Count != 2)
				return Reject(context, node, "Expected one macro pattern and one body.");
			LNode pattern = node.Args[0], body = node.Args[1];
			if (!body.Calls(S.Braces))
				return Reject(context, body, "Expected a braced block containing C# code to run when the pattern on the left is matched.");

			return CompileMacro(pattern, body, context, node.Attrs);
		}

		[LexicalMacro(@"macro Name($arg1, $arg2, ...) { /* C# macro method body */ }; macro Name($arg1, $arg2, ...) => /* C# expression */;", 
		"Compiles and registers a macro that recognizes the specified syntactical pattern and " + 
		"optionally produces a replacement pattern. LeMP will preprocess the method body before " + 
		"giving it to the C# scripting engine, so LeMP macros can be used inside the method body. " + 
		"The method body has access to two parameter variables (LNode node, IMacroContext context) " + 
		"and it can change the matched code by returning new code. " + 
		"MacroMode enum values can be attached as attributes. For example, the [PriorityOverride] attribute will cause this macro to supercede other macros that use default priority. " + 
		"Example: macro ExprToString($expr) { return LNode.Literal(expr.ToString()); }", 
		"#fn", Mode = MacroMode.Passive)] 
		public static LNode methodStyleMacro(LNode node, IMacroContext context)
		{
			if (EcsValidators.MethodDefinitionKind(node, out var defineKw, out var macroName, out var args, out var body) == S.Fn && body != null && defineKw.IsIdNamed("macro"))
			{
				var pattern = args.WithTarget(macroName);
				return CompileMacro(pattern, body, context, node.Attrs);
			}
			return null;
		}

		public static LNode CompileMacro(LNode pattern, LNode body, IMacroContext context, LNodeList attrs)
		{
			var modeNodes = attrs.Where(a => Enum.TryParse(a.Name.Name, out MacroMode _));
			// unwrap braces (they're not part of the pattern, they just enable statement syntax in EC#)
			var pattern_apos = pattern.UnwrapBraces();
			MacroMode modes = GetMacroMode(ref attrs, pattern_apos);

			// compileTime {...} can recognize macro method definitions. 
			// Take advantage of this by generating a macro method which it will register for us.
			LNode macroName = pattern_apos.Target ?? pattern_apos;
			LNode syntax = F.Literal(pattern_apos.ToString());
			LNode description = attrs.FirstOrDefault(a => a.Value is string) ?? F.Literal("User-defined macro at {0}".Localized(pattern.Range.Start));
			attrs = attrs.SmartWhere(a => !(a.Value is string));	// remove docstring, if any
			var extraArgs = LNode.List();
			if (macroName.IsId) {
				extraArgs.Add(F.Literal(macroName.Name.Name));
			} else {
				Debug.Assert((modes & (MacroMode.MatchEveryCall | MacroMode.MatchEveryIdentifier | MacroMode.MatchEveryLiteral)) != 0);
			}

			// ensure operator macros like `'+` are not printed as `operator+` which C# will reject
			if (EcsValidators.IsOperator(macroName.Name))
				macroName = F.Id(EcsValidators.SanitizeIdentifier(macroName.Name.Name));

			LNode modesExpr = null;
			foreach (LNode mode in modeNodes)
				modesExpr = LNode.MergeBinary(modesExpr, LNode.Call(CodeSymbols.Dot, LNode.List(LNode.Call(CodeSymbols.Dot, LNode.List(LNode.Call(CodeSymbols.ColonColon, LNode.List(LNode.Id((Symbol) "global"), LNode.Id((Symbol) "LeMP"))).SetStyle(NodeStyle.Operator), LNode.Id((Symbol) "MacroMode"))).SetStyle(NodeStyle.Operator), mode)).SetStyle(NodeStyle.Operator), S.OrBits);
			if (modesExpr != null)
				extraArgs.Add(LNode.Call(CodeSymbols.Assign, LNode.List(LNode.Id((Symbol) "Mode"), modesExpr)).SetStyle(NodeStyle.Operator));

			LNode lmAttribute = LNode.Call(LNode.Call(CodeSymbols.Dot, LNode.List(LNode.Call(CodeSymbols.ColonColon, LNode.List(LNode.Id((Symbol) "global"), LNode.Id((Symbol) "LeMP"))).SetStyle(NodeStyle.Operator), LNode.Id((Symbol) "LexicalMacroAttribute"))).SetStyle(NodeStyle.Operator), LNode.List().Add(syntax).Add(description).AddRange(extraArgs));

			if (!body.Calls(S.Braces))
				body = LNode.Call(CodeSymbols.Braces, LNode.List(LNode.Call(CodeSymbols.Return, LNode.List(body)))).SetStyle(NodeStyle.StatementBlock);

			body = context.PreProcess(body);

			// Look for "using" statements above the macro() call
			LNodeList usingDirectives = LNode.List(context.PreviousSiblings.Where(n => n.Calls(S.Import)));

			// Look for "using" and "#r" statements at the beginning of the body
			if (body.Calls(S.Braces)) {
				var bodyUsings = body.Args.TakeNowWhile(stmt => stmt.Calls(S.Import) || stmt.Calls(S.CsiReference));
				usingDirectives.AddRange(bodyUsings);
				body = body.WithArgs(body.Args.Slice(bodyUsings.Count));
			}

			// Create a matchCode statement unless the pattern is MacroName($(.._)), which always matches
			if (!(pattern_apos.HasSimpleHeadWithoutPAttrs() && pattern_apos.Target.IsId 
			&& pattern_apos.ArgCount == 1 && pattern_apos[0].Equals(LNode.Call(CodeSymbols.Substitute, LNode.List(LNode.Call(CodeSymbols.DotDot, LNode.List(LNode.Id((Symbol) "_"))).SetStyle(NodeStyle.Operator))).SetStyle(NodeStyle.Operator))))
			{
				// Note: the body is already preprocessed; #noLexicalMacros prevents double-processing
				body = LNode.Call(CodeSymbols.Braces, LNode.List(LNode.Call((Symbol) "matchCode", LNode.List(LNode.Id((Symbol) "#node"), LNode.Call(CodeSymbols.Braces, LNode.List(LNode.Call(CodeSymbols.Case, LNode.List(pattern)), LNode.Call((Symbol) "#noLexicalMacros", LNode.List(body.AsList(S.Braces))))).SetStyle(NodeStyle.StatementBlock))).SetStyle(NodeStyle.Special), LNode.Call(CodeSymbols.Return, LNode.List(LNode.Literal(null))))).SetStyle(NodeStyle.StatementBlock);
			}

			return LNode.Call((Symbol) "compileTime", LNode.List(LNode.Call(CodeSymbols.Braces, LNode.List().AddRange(usingDirectives).Add(LNode.Call(LNode.List().Add(lmAttribute).AddRange(attrs).Add(LNode.Id(CodeSymbols.Public)).Add(LNode.Id(CodeSymbols.Static)), CodeSymbols.Fn, LNode.List(LNode.Id((Symbol) "LNode"), macroName, LNode.Call(CodeSymbols.AltList, LNode.List(LNode.Call(CodeSymbols.Var, LNode.List(LNode.Id((Symbol) "LNode"), LNode.Id((Symbol) "#node"))), LNode.Call(CodeSymbols.Var, LNode.List(LNode.Call(CodeSymbols.Dot, LNode.List(LNode.Call(CodeSymbols.ColonColon, LNode.List(LNode.Id((Symbol) "global"), LNode.Id((Symbol) "LeMP"))).SetStyle(NodeStyle.Operator), LNode.Id((Symbol) "IMacroContext"))).SetStyle(NodeStyle.Operator), LNode.Id((Symbol) "#context"))))), body)))).SetStyle(NodeStyle.StatementBlock))).SetStyle(NodeStyle.Special);
		}
	}
}