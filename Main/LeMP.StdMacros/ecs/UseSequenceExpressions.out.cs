// Generated from UseSequenceExpressions.ecs by LeMP custom tool. LeMP version: 30.1.0.0
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
using System.Diagnostics;
using Loyc;
using Loyc.Syntax;
using Loyc.Collections;
using Loyc.Ecs;
using LeMP.CSharp7.To.OlderVersions;
using static LeMP.StandardMacros;
using S = Loyc.Syntax.CodeSymbols;

namespace LeMP.ecs
{
	partial class StandardMacros
	{
		static readonly Symbol sy__numuseSequenceExpressionsIsRunning = (Symbol) "#useSequenceExpressionsIsRunning", sy__numrunSequence = (Symbol) "#runSequence";
		[LexicalMacro("#runSequence(expression or statement list); };", 
		"Allows #runSequence at brace-scope without the use of #useSequenceExpressions", 
		"#runSequence", Mode = MacroMode.Passive)] 
		public static LNode runSequence(LNode node, IMacroContext context)
		{
			if (context.Parent.Calls(S.Braces))
				return node.With(S.Splice, MaybeRemoveNoOpFromRunSeq(node.Args));
			if (!context.ScopedProperties.ContainsKey(sy__numuseSequenceExpressionsIsRunning))
				Reject(context, node, "#useSequenceExpressions is required to make #runSequence work");
			return null;
		}

		public static LNodeList MaybeRemoveNoOpFromRunSeq(LNodeList runSeq)
		{
			// Delete final no-op in case of e.g. Foo()::id; => #runSequence(var id = Foo(); id)
			if (runSeq.Count > 1 && runSeq.Last.IsId)
				return runSeq.WithoutLast(1);
			return runSeq;
		}

		[LexicalMacro("#useSequenceExpressions; ... if (Foo.Bar()::b.Baz != null) b.Baz.Method(); ...", 
		"Enables the use of variable-declaration and #runSequence expressions, including the quick-binding operator `::` and the `with` expression, in the code that follows." 
		+ "Technically this allows any executable code in an expression context, such as while and for-loops, " 
		+ "but its name comes from the fact that it is usually used to allow variable declarations. " 
		+ "#useSequenceExpressions expects to be used in a declaration context, " 
		+ "e.g. at class or namespace level, not within a function.", 
		"#useSequenceExpressions", Mode = MacroMode.NoReprocessing | MacroMode.MatchIdentifierOrCall)] 
		public static LNode useSequenceExpressions(LNode node, IMacroContext context)
		{
			var tmp_10 = context.GetArgsAndBody(true);
			var args = tmp_10.Item1;
			var body = tmp_10.Item2;
			if (args.Count > 0)
				context.Sink.Error(node[1], "#useSequenceExpressions does not support arguments.");

			{
				context.ScopedProperties[sy__numuseSequenceExpressionsIsRunning] = G.BoxedTrue;
				try {
					body = context.PreProcess(body);
				} finally { context.ScopedProperties.Remove(sy__numuseSequenceExpressionsIsRunning); }
			}
			var ers = new EliminateRunSequences(context);
			return ers.EliminateSequenceExpressions(body, false).AsLNode(S.Splice);
		}

		class EliminateRunSequences
		{
			// This is an internal signal attached to #runSequence that that it is 
			// not necessary to create a temporary variable to hold a copy of 
			// earlier parts of the outer expression, because the sequence has no
			// potentially-relevant side effects.
			static readonly LNode _trivia_pure = LNode.Id("%pure");

			// This is an internal signal that it is not necessary to create a temporary 
			// variable to hold a copy of a value because that value is already a copy. 
			// e.g. this prevents an extra temporary from being created for `C` in
			// `A.B[C::c] = D::d` (otherwise `D::d` would cause `c` to get copied to a temporary.)
			static readonly LNode _trivia_isTmpVar = LNode.Id("%isTmpVar");
			public IMacroContext Context;

			public EliminateRunSequences(IMacroContext context) {
				Context = context;
			}
			LNode[] _arrayOf1 = new LNode[1];

			public LNodeList EliminateSequenceExpressions(LNodeList stmts, bool isDeclContext)
			{
				return stmts.SmartSelectMany(stmt => {
					/*
					// Optimization: scan find out whether this construct has any block 
					// expressions. If not, skip it.
					hasBlockExprs = false;
					stmt.ReplaceRecursive(new Func<LNode, Maybe<LNode>>(n => {
						if (!hasBlockExprs)
							hasBlockExprs = n.IsCall && (
								(n.Calls(S.ColonColon, 2) && n.Args[1].IsId) ||
								(n.Calls(S.Var, 2) && n.AttrNamed(S.Out) != null) ||
								(n.Calls(S.In, 2) && n.Args[1].Calls(S.Braces)));
						return hasBlockExprs ? n : null;
					}));
					if (!hasBlockExprs)
						return stmt;
					*/
					LNode result = EliminateSequenceExpressions(stmt, isDeclContext);
					if (result != stmt) {
						if (result.Calls(sy__numrunSequence))
							return result.Args;
					}
					_arrayOf1[0] = result;
					return _arrayOf1;
				});
			}

			public LNode EliminateSequenceExpressions(LNode stmt, bool isDeclContext)
			{
				LNode retType, name, argList, bases, body, initValue;
				if (EcsValidators.SpaceDefinitionKind(stmt, out name, out bases, out body) != null) {
					// Space definition: class, struct, etc.
					return body == null ? stmt : stmt.WithArgChanged(2, EliminateSequenceExpressions(body, true));
				} else if (EcsValidators.MethodDefinitionKind(stmt, out retType, out name, out argList, out body, true) != null) {
					// Method definition
					return body == null ? stmt : stmt.WithArgChanged(3, EliminateSequenceExpressionsInLambdaExpr(body, retType));
				} else if (EcsValidators.IsPropertyDefinition(stmt, out retType, out name, out argList, out body, out initValue)) {
					// Property definition
					stmt = stmt.WithArgChanged(3, 
					body.WithArgs(part => {
						if (part.ArgCount == 1 && part[0].Calls(S.Braces))
							part = part.WithArgChanged(0, EliminateSequenceExpressions(part[0], false));
						return part;
					}));
					if (initValue != null) {
						var initMethod = EliminateRunSeqFromInitializer(retType, name, ref initValue);
						if (initMethod != null) {
							stmt = stmt.WithArgChanged(4, initValue);
							return LNode.Call((Symbol) "#runSequence", LNode.List(stmt, initMethod));
						}
					}
					return stmt;
				} else if (stmt.Calls(CodeSymbols.Braces)) {
					return stmt.WithArgs(EliminateSequenceExpressions(stmt.Args, isDeclContext));
				} else if (stmt.CallsMin(S.Var, 2) && isDeclContext) {
					// Eliminate blocks from field member
					var results = new List<LNode> { 
						stmt
					};
					var vars = stmt.Args;
					var varType = vars[0];
					for (int i = 1; i < vars.Count; i++) {
						var var = vars[i];
						if (var.Calls(CodeSymbols.Assign, 2) && (name = var.Args[0]) != null && (initValue = var.Args[1]) != null) {
							var initMethod = EliminateRunSeqFromInitializer(varType, name, ref initValue);
							if (initMethod != null) {
								results.Add(initMethod);
								vars[i] = vars[i].WithArgChanged(1, initValue);
							}
						}
					}
					if (results.Count > 1) {
						results[0] = stmt.WithArgs(vars);
						return LNode.List(results).AsLNode(sy__numrunSequence);
					}
					return stmt;
				} else
					return EliminateSequenceExpressionsInExecStmt(stmt);
			}

			bool UnwrapRunSequence(ref LNode expr, out LNodeList exprSeq) => (exprSeq = UnwrapRunSequence(ref expr)).Count != 0;
			LNodeList UnwrapRunSequence(ref LNode runSeq)
			{
				var result = LNode.List();
				if (runSeq.Calls(sy__numrunSequence))
				{
					if (runSeq.ArgCount == 1 && runSeq[0].Calls(S.Braces))
						result = runSeq[0].Args;
					else
						result = runSeq.Args;

					if (result.Count == 0) {
						Context.Sink.Error(runSeq, "#runSequence() has no arguments, which is not supported.");
					} else if (result.Count == 1) {
						runSeq = result[0].PlusAttrs(runSeq.Attrs);
						return LNode.List();
					}
				}
				return result;
			}

			LNode EliminateSequenceExpressionsInLambdaExpr(LNode expr, LNode retType)
			{
				var stmt = EliminateSequenceExpressions(expr, false);
				if (UnwrapRunSequence(ref stmt, out var stmtSeq)) {
					stmt = stmt.With(S.Braces, stmtSeq);
					if (!retType.IsIdNamed(S.Void)) {
						if (retType.IsIdNamed(S.Missing) && stmt.Args.Last.IsCall)
							Context.Sink.Warning(expr, "This lambda must be converted to a braced block, but in LeMP it's not possible to tell whether the return keyword is needed. The output assumes `return` is required.");
						stmt = stmt.WithArgChanged(stmt.Args.Count - 1, LNode.Call(CodeSymbols.Return, LNode.List(stmt.Args.Last)));
					}
				}
				return stmt;
			}

			LNode EliminateSequenceExpressionsInExecStmt(LNode stmt)
			{
				{
					LNode block, collection, cond, init, initValue, loopVar, name, tmp_11, tmp_12, tmp_13, type, varType;
					LNodeList attrs, incs, inits;
					if (stmt.Calls(CodeSymbols.Braces)) {
						return stmt.WithArgs(EliminateSequenceExpressions(stmt.Args, false));
					} else if ((attrs = stmt.Attrs).IsEmpty | true && stmt.Calls(CodeSymbols.Fixed, 2) && (init = stmt.Args[0]) != null && (block = stmt.Args[1]) != null) {
						init = EliminateSequenceExpressionsInExecStmt(init);
						block = EliminateSequenceExpressionsInChildStmt(block);
						if (UnwrapRunSequence(ref init, out var initSeq)) {
							return LNode.Call(LNode.List(attrs), CodeSymbols.Braces, LNode.List().AddRange(initSeq.WithoutLast(1)).Add(LNode.Call(CodeSymbols.Fixed, LNode.List(initSeq.Last, block)))).SetStyle(NodeStyle.StatementBlock);
						} else
							return stmt.WithArgs(init, block);
					} else if ((attrs = stmt.Attrs).IsEmpty | true && stmt.Calls(CodeSymbols.While, 2) && (cond = stmt.Args[0]) != null && (block = stmt.Args[1]) != null) {
						block = EliminateSequenceExpressionsInChildStmt(block);
						if (BubbleUpAndUnwrapRunSequence(ref cond, out var condSeq)) {
							return LNode.Call(LNode.List(attrs), CodeSymbols.For, LNode.List(LNode.Call(CodeSymbols.AltList), LNode.Missing, LNode.Call(CodeSymbols.AltList), LNode.Call(CodeSymbols.Braces, LNode.List().AddRange(condSeq.WithoutLast(1)).Add(LNode.Call(CodeSymbols.If, LNode.List(condSeq.Last, block, LNode.Call(CodeSymbols.Break))))).SetStyle(NodeStyle.StatementBlock)));
						} else
							return stmt.WithArgs(cond, block);
					} else if ((attrs = stmt.Attrs).IsEmpty | true && stmt.Calls(CodeSymbols.DoWhile, 2) && (block = stmt.Args[0]) != null && (cond = stmt.Args[1]) != null) {
						block = EliminateSequenceExpressionsInChildStmt(block);
						if (BubbleUpAndUnwrapRunSequence(ref cond, out var condSeq)) {
							var continue_N = F.Id(NextTempName(Context, "continue_"));
							var bodyStmts = block.AsList(S.Braces);
							bodyStmts.AddRange(condSeq.WithoutLast(1));
							bodyStmts.Add(LNode.Call(CodeSymbols.Assign, LNode.List(continue_N, condSeq.Last)).SetStyle(NodeStyle.Operator));
							return LNode.Call(LNode.List(attrs), CodeSymbols.For, LNode.List(LNode.Call(CodeSymbols.AltList, LNode.List(LNode.Call(CodeSymbols.Var, LNode.List(LNode.Id(CodeSymbols.Bool), LNode.Call(CodeSymbols.Assign, LNode.List(continue_N, LNode.Literal(true))).SetStyle(NodeStyle.Operator))))), continue_N, LNode.Call(CodeSymbols.AltList), LNode.Call(CodeSymbols.Braces, LNode.List(bodyStmts)).SetStyle(NodeStyle.StatementBlock)));
						} else
							return stmt.WithArgs(block, cond);
					} else if ((attrs = stmt.Attrs).IsEmpty | true && stmt.Calls(CodeSymbols.For, 4) && stmt.Args[0].Calls(CodeSymbols.AltList) && (inits = stmt.Args[0].Args).IsEmpty | true && (cond = stmt.Args[1]) != null && stmt.Args[2].Calls(CodeSymbols.AltList) && (incs = stmt.Args[2].Args).IsEmpty | true && (block = stmt.Args[3]) != null) {
						return ESEInForLoop(stmt, attrs, inits, cond, incs, block);
					} else if ((attrs = stmt.Attrs).IsEmpty | true && stmt.Calls(CodeSymbols.ForEach, 3) && (tmp_11 = stmt.Args[0]) != null && tmp_11.Calls(CodeSymbols.Var, 2) && (type = tmp_11.Args[0]) != null && (loopVar = tmp_11.Args[1]) != null && (collection = stmt.Args[1]) != null && (block = stmt.Args[2]) != null) {
						Debug.Assert(collection.Equals(stmt[1]));
						block = EliminateSequenceExpressionsInChildStmt(block);
						if (BubbleUpAndUnwrapRunSequence(ref collection, out var collectionSeq)) {
							return LNode.Call(LNode.List(attrs), CodeSymbols.Braces, LNode.List().AddRange(collectionSeq.WithoutLast(1)).Add(LNode.Call(CodeSymbols.ForEach, LNode.List(LNode.Call(CodeSymbols.Var, LNode.List(type, loopVar)), collectionSeq.Last, block)))).SetStyle(NodeStyle.StatementBlock);
						} else {
							if (!collection.Equals(stmt[1]))
								stmt = stmt.WithArgChanged(1, collection);
							return stmt.WithArgChanged(2, block);
						}
					} else if ((attrs = stmt.Attrs).IsEmpty | true && stmt.Calls(CodeSymbols.UsingStmt, 2) && (tmp_12 = stmt.Args[0]) != null && tmp_12.Calls(CodeSymbols.Var, 2) && (varType = tmp_12.Args[0]) != null && (init = tmp_12.Args[1]) != null && (block = stmt.Args[1]) != null) {
						block = EliminateSequenceExpressionsInChildStmt(block);
						if (BubbleUpAndUnwrapRunSequence(ref init, out var initSeq)) {
							return LNode.Call(LNode.List(attrs), CodeSymbols.Braces, LNode.List().AddRange(initSeq.WithoutLast(1)).Add(LNode.Call(CodeSymbols.UsingStmt, LNode.List(LNode.Call(CodeSymbols.Var, LNode.List(varType, initSeq.Last)), block)))).SetStyle(NodeStyle.StatementBlock);
						} else
							return stmt.WithArgs(stmt[0].WithArgChanged(1, init), block);
					} else if (stmt.CallsMin(CodeSymbols.If, 1) || stmt.Calls(CodeSymbols.UsingStmt, 2) || stmt.Calls(CodeSymbols.Lock, 2) || stmt.Calls(CodeSymbols.SwitchStmt, 2) && stmt.Args[1].Calls(CodeSymbols.Braces)) {
						return ProcessBlockCallStmt(stmt, 1);
					} else if ((attrs = stmt.Attrs).IsEmpty | true && stmt.Calls(CodeSymbols.Var, 2) && (type = stmt.Args[0]) != null && (tmp_13 = stmt.Args[1]) != null && tmp_13.Calls(CodeSymbols.Assign, 2) && (name = tmp_13.Args[0]) != null && (initValue = tmp_13.Args[1]) != null) {
						var initValue_apos = initValue;
						if (BubbleUpAndUnwrapRunSequence(ref initValue_apos, out var initValueSeq)) {
							initValueSeq[initValueSeq.Count - 1] = LNode.Call(LNode.List(attrs), CodeSymbols.Var, LNode.List(type, LNode.Call(CodeSymbols.Assign, LNode.List(name, initValueSeq.Last)).SetStyle(NodeStyle.Operator)));
							return initValue_apos.WithArgs(initValueSeq);
						} else if (initValue_apos != initValue)
							return stmt.WithArgChanged(1, LNode.Call(CodeSymbols.Assign, LNode.List(name, initValue_apos)).SetStyle(NodeStyle.Operator));
					} else {
						if (stmt.CallsMin(S.Try, 2)) {
							return ESEInTryStmt(stmt);
						} else if (stmt.HasSpecialName && stmt.ArgCount >= 1 && stmt.Args.Last.Calls(S.Braces)) {
							return ProcessBlockCallStmt(stmt, stmt.ArgCount - 1);
						} else {
							// Ordinary expression statement
							return BubbleUpBlocks(stmt, stmtContext: true);
						}
					}
				}
				return stmt;
			}

			LNode ESEInForLoop(LNode stmt, LNodeList attrs, LNodeList init, LNode cond, LNodeList inc, LNode block)
			{
				var preInit = LNodeList.Empty;
				var init_apos = init.SmartSelect(init1 => {
					init1 = EliminateSequenceExpressionsInExecStmt(init1);
					if (UnwrapRunSequence(ref init1, out var init1Seq)) {
						preInit.AddRange(init1Seq.WithoutLast(1));
						return init1Seq.Last;
					}
					return init1;
				});
				var cond_apos = cond;
				BubbleUpAndUnwrapRunSequence(ref cond_apos, out var cond_aposSeq);
				var inc_apos = inc.SmartSelectMany(inc1 => {
					inc1 = BubbleUpBlocks(inc1);
					return inc1.AsList(sy__numrunSequence);
				});

				block = EliminateSequenceExpressionsInChildStmt(block);
				if (init_apos != init || cond_apos != cond || cond_aposSeq.Count != 0 || inc_apos != inc)
				{
					if (inc_apos != inc && inc_apos.Count > 1) {
						var blockStmts = block.AsList(S.Braces).AddRange(inc_apos);
						block = blockStmts.AsLNode(S.Braces);
						inc_apos = LNode.List();
					}
					if (cond_aposSeq.Count != 0) {
						var preCond = cond_aposSeq.WithoutLast(1);
						cond_apos = cond_aposSeq.Last;
						stmt = LNode.Call(CodeSymbols.For, LNode.List(LNode.Call(CodeSymbols.AltList, LNode.List(init_apos)), LNode.Missing, LNode.Call(CodeSymbols.AltList, LNode.List(inc_apos)), LNode.Call(CodeSymbols.Braces, LNode.List().AddRange(preCond).Add(LNode.Call(CodeSymbols.If, LNode.List(cond_apos, block, LNode.Call(CodeSymbols.Break))))).SetStyle(NodeStyle.StatementBlock)));
					} else {
						stmt = LNode.Call(LNode.List(attrs), CodeSymbols.For, LNode.List(LNode.Call(CodeSymbols.AltList, LNode.List(init_apos)), cond_apos, LNode.Call(CodeSymbols.AltList, LNode.List(inc_apos)), block));
					}
					if (preInit.Count != 0) {
						stmt = LNode.Call(CodeSymbols.Braces, LNode.List().AddRange(preInit).Add(stmt)).SetStyle(NodeStyle.StatementBlock);
					}
					return stmt;
				} else {
					return stmt.WithArgChanged(3, block);
				}
			}

			LNode ESEInTryStmt(LNode stmt)
			{
				var args = stmt.Args.ToWList();
				// Process `try` part
				args[0] = EliminateSequenceExpressionsInChildStmt(args[0]);
				// Process `catch` and `finally` clauses (`when` clause not supported)
				for (int i = 1; i < args.Count; i++) {
					var part = args[i];
					if (part.Calls(S.Finally, 1) || part.Calls(S.Catch, 3)) {
						int lasti = part.ArgCount - 1;
						args[i] = part.WithArgChanged(lasti, EliminateSequenceExpressionsInChildStmt(part.Args[lasti]));
					}
				}
				return stmt.WithArgs(args.ToVList());
			}

			LNode ProcessBlockCallStmt(LNode stmt, int childStmtsStartAt)
			{
				List<LNode> childStmts = stmt.Args.Slice(childStmtsStartAt).ToList();
				LNode partialStmt = stmt.WithArgs(stmt.Args.Initial(childStmtsStartAt));
				LNodeList advanceSequence;
				if (ProcessBlockCallStmt2(ref partialStmt, out advanceSequence, childStmts)) {
					stmt = partialStmt.PlusArgs(childStmts);
					if (advanceSequence.Count != 0)
						return LNode.Call(CodeSymbols.Braces, LNode.List().AddRange(advanceSequence).Add(stmt)).SetStyle(NodeStyle.StatementBlock);
					return stmt;	// only the child statements changed
				} else
					return stmt;	// no changes
			}

			// This is called to process the two parts of a block call, e.g.
			// #if(cond, {T}, {F}) => partialStmt = #if(cond); childStmts = {{T}, {F}}
			// Returns true if anything changed (i.e. sequence expr detected)
			bool ProcessBlockCallStmt2(ref LNode partialStmt, out LNodeList advanceSequence, List<LNode> childStmts)
			{
				// Process the child statement(s)
				bool childChanged = false;
				for (int i = 0; i < childStmts.Count; i++) {
					var oldChild = childStmts[i];
					childStmts[i] = EliminateSequenceExpressionsInChildStmt(oldChild);
					childChanged |= (oldChild != childStmts[i]);
				}
				var BubbleUp_GeneralCall2_14 = BubbleUp_GeneralCall2(partialStmt);
				advanceSequence = BubbleUp_GeneralCall2_14.Item1;
				partialStmt = BubbleUp_GeneralCall2_14.Item2;
				return childChanged || !advanceSequence.IsEmpty;
			}

			LNode EliminateSequenceExpressionsInChildStmt(LNode stmt)
			{
				stmt = EliminateSequenceExpressionsInExecStmt(stmt);
				if (UnwrapRunSequence(ref stmt, out var stmtSeq))
					return stmt.With(S.Braces, MaybeRemoveNoOpFromRunSeq(stmtSeq));
				return stmt;
			}

			/// Eliminates run sequence(s) in a field initializer expression.
			/// If any are found, a method is returned to encapsulate the 
			/// initialization code, e.g.
			///   expr on entry: Foo()::foo.x + foo.y
			///   return value:  static retType fieldName_initializer() {
			///                      var foo = Foo();
			///                      return foo.x + foo.y;
			///                  }
			///   expr on exit:  fieldName_initializer()
			LNode EliminateRunSeqFromInitializer(LNode retType, LNode fieldName, ref LNode expr)
			{
				if (BubbleUpAndUnwrapRunSequence(ref expr, out var exprSeq)) {
					var statements = exprSeq.WithoutLast(1);
					var finalResult = exprSeq.Last;

					LNode methodName = F.Id(KeyNameComponentOf(fieldName).Name + "_initializer");
					expr = LNode.Call(methodName);
					return LNode.Call(LNode.List(LNode.Id(CodeSymbols.Static)), CodeSymbols.Fn, LNode.List(retType, methodName, LNode.Call(CodeSymbols.AltList), LNode.Call(CodeSymbols.Braces, LNode.List().AddRange(statements).Add(LNode.Call(CodeSymbols.Return, LNode.List(finalResult)))).SetStyle(NodeStyle.StatementBlock)));
				} else
					return null;	// most common case
			}

			bool BubbleUpAndUnwrapRunSequence(LNode expr, out LNodeList exprSeq) => BubbleUpAndUnwrapRunSequence(ref expr, out exprSeq);
			bool BubbleUpAndUnwrapRunSequence(ref LNode expr, out LNodeList exprSeq)
			{
				expr = BubbleUpBlocks(expr);
				exprSeq = UnwrapRunSequence(ref expr);
				return exprSeq.Count != 0;
			}

			// This method's main goal is to move #runSequence from child nodes to outer nodes:
			//   Foo(a, #runSequence(b(), c())) => #runSequence(var a_10 = a; b(); Foo(a_10, c()));
			// It also converts variable declarations, e.g. 
			//   Foo()::foo => #runSequence(var foo = Foo(), foo)
			LNode BubbleUpBlocks(LNode expr, bool stmtContext = false)
			{
				if (!expr.IsCall)
					return expr;

				LNode result = null;
				if (!stmtContext) {
					{
						LNode tmp_15, value, varName, varType;
						LNodeList attrs;
						if (expr.Calls(CodeSymbols.Braces)) {
							Context.Sink.Warning(expr, "A braced block is not supported directly within an expression. Did you mean to use `#runSequence {...}`?");
							result = expr;
							
							// A variable declaration with no initializer and an `out` attribute attached - no does not need to be handled in C# 7+
							//case { [$(...(attrs [((LNodeList)#).NodeNamed(S.Out) != null]))]
							//		$varType $(varName [#.IsId]); }:
							//	if (varType.IsIdNamed(S.Missing))
							//		Context.Sink.Error(expr, "#useSequenceExpressions: the data type of this variable declaration cannot be inferred and must be stated explicitly.");
							//	else
							//		result = quote( [$_trivia_pure] 
							//			#runSequence($(expr.WithoutAttrNamed(S.Out)), out $varName) );
						} else if ((attrs = expr.Attrs).IsEmpty | true && expr.Calls(CodeSymbols.Var, 2) && (varType = expr.Args[0]) != null && (tmp_15 = expr.Args[1]) != null && tmp_15.Calls(CodeSymbols.Assign, 2) && (varName = tmp_15.Args[0]) != null && (value = tmp_15.Args[1]) != null) {
							if (stmtContext)
								result = expr;	// no-op
							else
								result = ConvertVarDeclToRunSequence(attrs, varType, varName, value);
						}
					}
				}
				if (result == null) {
					{
						LNode args, code, value, varName;
						LNodeList attrs;
						if ((attrs = expr.Attrs).IsEmpty | true && expr.Calls(CodeSymbols.ColonColon, 2) && (value = expr.Args[0]) != null && IsQuickBindLhs(value) && (varName = expr.Args[1]) != null && varName.IsId) {
							result = ConvertVarDeclToRunSequence(attrs, F.Missing, varName, value);
						} else if ((attrs = expr.Attrs).IsEmpty | true && expr.Calls(CodeSymbols.Lambda, 2) && (args = expr.Args[0]) != null && (code = expr.Args[1]) != null) {
							result = expr.WithArgChanged(1, EliminateSequenceExpressionsInLambdaExpr(code, F.Missing));
						} else {
							if (expr.Calls(sy__numrunSequence))
								result = expr;
							else
								result = BubbleUp_GeneralCall(expr);
						}
					}
				}

				// #runSequences can be nested by the user or produced by BubbleUp_GeneralCall,
				// so process the code inside #runSequence too
				if (result.Calls(sy__numrunSequence))
					return result.WithArgs(EliminateSequenceExpressions(result.Args, false));
				else
					return result;
			}

			// Bubbles up a call, e.g. 
			//   Foo(x, #runSequence(y, z)) => #runSequence(var x_10 = x, y, Foo(x_10, z)) 
			LNode BubbleUp_GeneralCall(LNode expr)
			{
				var BubbleUp_GeneralCall2_16 = BubbleUp_GeneralCall2(expr);
				var combinedSequence = BubbleUp_GeneralCall2_16.Item1;
				expr = BubbleUp_GeneralCall2_16.Item2;
				if (combinedSequence.Count != 0)
					return LNode.Call((Symbol) "#runSequence", LNode.List().AddRange(combinedSequence).Add(expr));
				else
					return expr;
			}
			// Bubbles up a call. The returned pair consists of 
			// 1. A sequence of statements to run before the call
			// 2. The call with all (outer) #runSequences removed
			Pair<LNodeList, LNode> BubbleUp_GeneralCall2(LNode expr)
			{
				var target = expr.Target;
				var args = expr.Args;
				var combinedSequence = LNode.List();

				// Bubbe up target
				if (BubbleUpAndUnwrapRunSequence(ref target, out var targetSeq)) {
					combinedSequence = targetSeq.WithoutLast(1);
					expr = expr.WithTarget(targetSeq.Last);
				} else if (target != expr.Target && !target.Equals(expr.Target)) {
					expr = expr.WithTarget(target);
				}

				// Bubble up each argument
				var isAssignment = EcsValidators.IsAssignmentOperator(expr.Name);
				if (isAssignment) {
					LNode lhs = BubbleUpBlocks(expr.Args[0]);
					LNode rhs = BubbleUpBlocks(expr.Args[1]);
					args = LNode.List(lhs, rhs);
				} else {	// most common case
					args = args.SmartSelect(arg => BubbleUpBlocks(arg));
				}

				int lastRunSeq = args.FinalIndexWhere(a => a.CallsMin(sy__numrunSequence, 1)) ?? -1;
				if (lastRunSeq >= 0) {
					// last index of #runSequence that is not marked pure
					int lastRunSeqImpure = args.Initial(lastRunSeq + 1).FinalIndexWhere(a => 
					a.CallsMin(sy__numrunSequence, 1) && a.AttrNamed(_trivia_pure.Name) == null) ?? -1;

					if (lastRunSeq > 0 && 
					(args.Count == 2 && (target.IsIdNamed(S.And) || target.IsIdNamed(S.Or)) 
					|| args.Count == 3 && target.IsIdNamed(S.QuestionMark))) {
						Context.Sink.Error(target, 
						"#useSequenceExpressions is not designed to support sequences or variable declarations on the right-hand side of the `&&`, `||` or `?` operators. The generated code will be incorrect.");
					}

					var argsW = args.ToList();
					for (int i = 0; i <= lastRunSeq; i++) {
						LNode arg = argsW[i];
						if (!arg.IsLiteral) {
							if (UnwrapRunSequence(ref arg, out var argSeq)) {
								combinedSequence.AddRange(argSeq.WithoutLast(1));
								argsW[i] = arg = argSeq.Last;
							} else
								argsW[i] = arg;
							if (i < lastRunSeqImpure) {
								if (i == 0 && (expr.CallsMin(S.IndexBracks, 1) || expr.CallsMin(S.NullIndexBracks, 1))) {
								// Consider foo[#runSequence(f(), i)]. In case this appears in
								// an lvalue context and `foo` is a struct, we cannot store `foo` in 
								// a temporary, as this may silently change the code's behavior.
								// Better to take the risk of evaluating `foo` after `f()`.
								 } else if (!arg.Attrs.Contains(_trivia_isTmpVar)) {
									if (isAssignment || arg.Attrs.Any(a => a.IsIdNamed(S.Ref) || a.IsIdNamed(S.Out)))
										argsW[i] = MaybeCreateTemporaryForLValue(arg, ref combinedSequence);
									else {
										// Create a temporary variable to hold this argument
										LNode tmpVarName, tmpVarDecl = TempVarDecl(Context, arg, out tmpVarName);
										combinedSequence.Add(tmpVarDecl);
										argsW[i] = tmpVarName.PlusAttr(_trivia_isTmpVar);
									}
								}
							}
						}
					}

					expr = expr.WithArgs(LNode.List(argsW));
				}

				return Pair.Create(combinedSequence, expr);
			}

			// Creates a temporary for an LValue (left side of `=`, or `ref` parameter)
			// e.g. f(x).Foo becomes f(x_N).Foo, and `var x_N = x` is added to `stmtSequence`,
			// where N is a unique integer for the temporary variable.
			LNode MaybeCreateTemporaryForLValue(LNode expr, ref LNodeList stmtSequence)
			{
				{
					LNode _, lhs;
					if (expr.Calls(CodeSymbols.Dot, 2) && (lhs = expr.Args[0]) != null || expr.CallsMin(CodeSymbols.Of, 1) && (lhs = expr.Args[0]) != null) {
						return expr.WithArgChanged(0, MaybeCreateTemporaryForLValue(lhs, ref stmtSequence));
					} else if ((_ = expr) != null && !_.IsCall) {
						return expr;
					} else {
						var args = expr.Args.ToWList();
						int i = 0;
						if (expr.CallsMin(S.IndexBracks, 1) || expr.CallsMin(S.NullIndexBracks, 1)) {
							// Consider foo[i]. We cannot always store `foo` in a temporary, as
							// this may change the code's behavior in case `foo` is a struct.
							i = 1;
						}
						for (; i < args.Count; i++) {
							if (!args[i].IsLiteral && !args[i].Attrs.Contains(_trivia_isTmpVar)) {
								LNode tmpVarName;
								stmtSequence.Add(TempVarDecl(Context, args[i], out tmpVarName));
								args[i] = tmpVarName.PlusAttr(_trivia_isTmpVar);
							}
						}
						return expr.WithArgs(args.ToVList());
					}
				}
			}

			LNode ConvertVarDeclToRunSequence(LNodeList attrs, LNode varType, LNode varName, LNode initValue)
			{
				initValue = BubbleUpBlocks(initValue);
				varType = varType ?? F.Missing;

				// `Foo(ref int x = 0)` should become `Foo(#runSequence(int x = 0, ref x))` BUT
				// `Foo(ref int x = ref y)` should probably be `Foo(#runSequence(ref int x = ref y, x))`
				LNode @ref = null;
				if (initValue.AttrNamed(S.Ref) == null)
					attrs = attrs.WithoutNodeNamed(S.Ref, out @ref);

				var varName_apos = varName.PlusAttr(_trivia_isTmpVar);
				if (@ref != null)
					varName_apos = varName_apos.PlusAttr(@ref);

				// If the initializer itself calls #runSequence...
				if (UnwrapRunSequence(ref initValue, out var initValueSeq)) {
					LNodeList seq = initValueSeq.WithoutLast(1);
					seq.Add(LNode.Call(LNode.List(attrs), CodeSymbols.Var, LNode.List(varType, LNode.Call(CodeSymbols.Assign, LNode.List(varName_apos, initValueSeq.Last)).SetStyle(NodeStyle.Operator))));
					seq.Add(varName_apos);
					return initValue.WithArgs(seq);
				} else {
					// usual case
					var newVarDecl = LNode.Call(LNode.List(attrs), CodeSymbols.Var, LNode.List(varType, LNode.Call(CodeSymbols.Assign, LNode.List(varName, initValue)).SetStyle(NodeStyle.Operator)));
					return LNode.Call((Symbol) "#runSequence", LNode.List(newVarDecl, varName_apos));
				}
			}

			static bool IsQuickBindLhs(LNode value)
			{
				if (!value.IsId)
					return true;
				return char.IsUpper(value.Name.Name.TryGet(0, '\0'));
			}
		}
	}
}