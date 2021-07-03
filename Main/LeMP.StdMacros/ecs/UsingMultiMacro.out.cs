// Generated from UsingMultiMacro.ecs by LeMP custom tool. LeMP version: 30.1.0.0
// Note: you can give command-line arguments to the tool via 'Custom Tool Namespace':
// --no-out-header       Suppress this message
// --verbose             Allow verbose messages (shown by VS as 'warnings')
// --timeout=X           Abort processing thread after X seconds (default: 10)
// --macros=FileName.dll Load macros from FileName.dll, path relative to this file 
// Use #importMacros to use macros in a given namespace, e.g. #importMacros(Loyc.LLPG);
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Loyc;	// For Symbol
using Loyc.Collections;	// For .Single()
using Loyc.Syntax;	// For LNode
using Loyc.Ecs;	// For EcsLanguageService
using S = Loyc.Syntax.CodeSymbols;

namespace LeMP.ecs
{
	partial class StandardMacros
	{
		[LexicalMacro("using System(, .Collections.Generic, .Linq, .Text);", 
		"Generates multiple using-statements from a single one.", 
		"#import", Mode = MacroMode.Passive | MacroMode.Normal)] 
		public static LNode UsingMulti(LNode input, IMacroContext context)
		{
			{
				LNode multiNamespace;
				if (input.Calls(CodeSymbols.Import, 1) && (multiNamespace = input.Args[0]) != null) {
					try {
						var list = GetNamespaces(multiNamespace);
						if (list == null)
							return null;
						return LNode.Call(CodeSymbols.Splice, LNode.List(list.Select(namespc => (LNode) LNode.Call(CodeSymbols.Import, LNode.List(namespc))))).IncludingTriviaFrom(input);
					} catch (LogException exc) {
						exc.Msg.WriteTo(context.Sink);
					}
				}
			}
			return null;
		}

		static IEnumerable<LNode> GetNamespaces(LNode multiName) {
			{
				LNode outerNamespace;
				LNodeList args;
				if (multiName.Calls(CodeSymbols.Dot) || multiName.Calls(CodeSymbols.Of)) { } else if (multiName.IsCall && (outerNamespace = multiName.Target) != null && (args = multiName.Args).IsEmpty | true) {
					// Allow Namespace { stuff; } as alternate notation; just ignore the braces
					if (args.Count == 1 && args[0].Calls(S.Braces))
						args = args[0].Args;

					return args.SelectMany(arg => GetNamespaces(arg) ?? ListExt.Single(arg))
					.Select(subNS => MergeIdentifiers(outerNamespace, subNS));
				}
			}
			return null;
		}
		static LNode MergeIdentifiers(LNode left, LNode right)
		{
			if (left == null)
				return right;
			if (right.IsIdNamed(S.Missing))
				return left;
			{
				LNode right1, right2;
				if (right.Calls(CodeSymbols.Dot, 1) && (right2 = right.Args[0]) != null) {
					return LNode.Call(CodeSymbols.Dot, LNode.List(left, right2)).SetStyle(NodeStyle.Operator);
				} else if (right.Calls(CodeSymbols.Dot, 2) && (right1 = right.Args[0]) != null && (right2 = right.Args[1]) != null) {
					return LNode.Call(CodeSymbols.Dot, LNode.List(MergeIdentifiers(left, right1), right2)).SetStyle(NodeStyle.Operator);
				} else {
					throw new LogException(Severity.Note, right, "Multi-using statement seems malformed. Correct example: `using System(.Text, .Linq));`");
				}
			}
		}
	}
}
