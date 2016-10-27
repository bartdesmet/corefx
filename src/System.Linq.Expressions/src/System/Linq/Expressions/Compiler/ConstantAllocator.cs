// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic.Utils;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace System.Linq.Expressions.Compiler
{
    internal sealed class ConstantAllocator : ExpressionVisitor
    {
        private readonly AnalyzedTree _tree;
        private readonly bool _compileToDynamicMethod;
        private readonly Stack<BoundConstants> _constants = new Stack<BoundConstants>();
        private readonly StackGuard _guard = new StackGuard();

        internal static LambdaExpression Allocate(LambdaExpression lambda, AnalyzedTree tree, bool compileToDynamicMethod)
        {
            var allocator = new ConstantAllocator(tree, compileToDynamicMethod);
            return allocator.VisitAndConvert(lambda, nameof(Allocate));
        }

        private ConstantAllocator(AnalyzedTree tree, bool compileToDynamicMethod)
        {
            _tree = tree;
            _compileToDynamicMethod = compileToDynamicMethod;
        }

        public override Expression Visit(Expression node)
        {
            // When compling deep trees, we run the risk of triggering a terminating StackOverflowException,
            // so we use the StackGuard utility here to probe for sufficient stack and continue the work on
            // another thread when we run out of stack space.
            if (!_guard.TryEnterOnCurrentStack())
            {
                return _guard.RunOnEmptyStack((ConstantAllocator @this, Expression n) => @this.Visit(n), this, node);
            }

            if (node != null && node.NodeType == ExpressionType.Dynamic)
            {
                return VisitDynamic(node);
            }

            return base.Visit(node);
        }

        private bool _inInvocation;

        protected internal override Expression VisitInvocation(InvocationExpression node)
        {
            LambdaExpression lambda = node.LambdaOperand;

            // optimization: inline code for literal lambda's directly
            if (lambda != null)
            {
                // in case node.Expression is a Quote, the node.LambdaOperand property
                // extracts the Operand of the Quote; simplify the expression in this
                // case, which is what LambdaCompiler does anyway
                node = node.Rewrite(lambda, null);

                _inInvocation = true;

                Expression res = base.VisitInvocation(node);

                Debug.Assert(!_inInvocation); // flag should be cleared by VisitLambda<T>

                return res;
            }

            return base.VisitInvocation(node);
        }

        protected internal override Expression VisitLambda<T>(Expression<T> node)
        {
            bool inInvocation = _inInvocation;

            if (inInvocation)
            {
                // immediately set to false; we only want to bypass constant analysis
                // for the LambdaOperand of the InvocationExpression
                _inInvocation = false;
            }
            else
            {
                _constants.Push(new BoundConstants());
            }

            var res = (LambdaExpression)base.VisitLambda(node);

            if (!inInvocation)
            {
                BoundConstants constants = _constants.Pop();

                _tree.Constants[res] = constants;

                if (_constants.Count > 0)
                {
                    if (_compileToDynamicMethod)
                    {
                        Allocate(typeof(MethodInfo)); // for DynamicMethod case in EmitDelegateConstruction
                    }

                    Type type = constants.GetConstantsType();

                    if (type != null)
                    {
                        Allocate(type); // for EmitClosureCreation
                    }
                }
            }

            return res;
        }

        protected internal override Expression VisitConstant(ConstantExpression node)
        {
            Reference(node.Value, node.Type);

            return node;
        }

        protected internal override Expression VisitTypeBinary(TypeBinaryExpression node)
        {
            // Reduce node to introduce nodes that may contain live constants,
            // e.g. Type objects that cannot be emitted using ldtoken.

            if (node.NodeType == ExpressionType.TypeEqual)
            {
                return Visit(node.ReduceTypeEqual());
            }

            return base.VisitTypeBinary(node);
        }

        protected internal override Expression VisitMember(MemberExpression node)
        {
            var fi = node.Member as FieldInfo;
            if (fi != null)
            {
                object value;

                if (fi.IsLiteral && Utils.TryGetRawConstantValue(fi, out value))
                {
                    Reference(value, node.Type);
                    return node;
                }
            }

            return base.VisitMember(node);
        }

        protected internal override Expression VisitUnary(UnaryExpression node)
        {
            if (node.NodeType == ExpressionType.Quote)
            {
                Allocate(node.Type);

                bool hasFreeVariable;
                if (!_tree.QuoteHasFreeVariable.TryGetValue(node, out hasFreeVariable))
                {
                    hasFreeVariable = FreeVariableScanner.HasFreeVariable(node.Operand);
                    _tree.QuoteHasFreeVariable[node] = hasFreeVariable;
                }

                if (hasFreeVariable)
                {
                    Allocate(typeof(object)); // for HoistedLocals passed to RuntimeOps.Quote
                }

                // Don't go into the quoted expression; we don't want to rewrite anything
                // that shows up in the quote. The whole quote is emitted as a constant,
                // so there's no need to recurse here. Any inner compilation on the quote
                // will re-enter here and deal with constants deeper down.

                return node;
            }

            return base.VisitUnary(node);
        }

        protected internal override Expression VisitRuntimeVariables(RuntimeVariablesExpression node)
        {
            if (node.Variables.Count > 0)
            {
                Allocate(typeof(long[])); // for indexes passed to RuntimeOps.CreateRuntimeVariables
            }

            return base.VisitRuntimeVariables(node);
        }

        protected internal override Expression VisitSwitch(SwitchExpression node)
        {
            if (Utils.IsStringHashtableSwitch(node))
            {
                Allocate(typeof(StrongBox<Dictionary<string, int>>)); // for switch table lazy field
            }

            return base.VisitSwitch(node);
        }

        private Expression VisitDynamic(Expression node)
        {
            var expr = (IDynamicExpression)node;

            Expression[] newArgs = ExpressionVisitorUtils.VisitArguments(this, expr);
            if (newArgs != null)
            {
                node = expr.Rewrite(newArgs);
                expr = (IDynamicExpression)node;
            }

            // create the call site prior to lambda compilation in order to get its type,
            // and return a replacement IDynamicExpression that stores the call site for
            // extraction during EmitDynamicExpression

            var result = new PartiallyEvaluatedDynamicExpression(expr);

            object site = result.CreateCallSite();
            Type siteType = site.GetType();

            Allocate(siteType); // for site used in site.Target.Invoke(site, args)

            return result;
        }

        private void Reference(object value, Type type)
        {
            // Constants that can be emitted into IL don't need to be stored on
            // the delegate

            if (!ILGen.CanEmitConstant(value, type))
            {
                _constants.Peek().AddReference(value, type);
            }
        }

        private void Allocate(Type type)
        {
            _constants.Peek().Allocate(type);
        }

        private sealed class FreeVariableScanner : ExpressionVisitor
        {
            private readonly Stack<HashSet<ParameterExpression>> _stack = new Stack<HashSet<ParameterExpression>>();
            private bool _hasFreeVariable;

            private FreeVariableScanner()
            {
            }

            public static bool HasFreeVariable(Expression expression)
            {
                var fvs = new FreeVariableScanner();

                fvs.Visit(expression);

                return fvs._hasFreeVariable;
            }

            public override Expression Visit(Expression node)
            {
                if (_hasFreeVariable)
                {
                    return node;
                }

                return base.Visit(node);
            }

            protected internal override Expression VisitBlock(BlockExpression node)
            {
                int count = node.Variables.Count;
                if (count > 0)
                {
                    _stack.Push(new HashSet<ParameterExpression>(node.Variables));
                }

                Visit(node.Expressions);

                if (count > 0)
                {
                    _stack.Pop();
                }

                return node;
            }

            protected internal override Expression VisitLambda<T>(Expression<T> node)
            {
                int count = node.Parameters.Count;
                if (count > 0)
                {
                    _stack.Push(new HashSet<ParameterExpression>(node.Parameters));
                }

                Visit(node.Body);

                if (count > 0)
                {
                    _stack.Pop();
                }

                return node;
            }

            protected override CatchBlock VisitCatchBlock(CatchBlock node)
            {
                if (node.Variable != null)
                {
                    _stack.Push(new HashSet<ParameterExpression>(new[] { node.Variable }));
                }

                Visit(node.Filter);
                Visit(node.Body);

                if (node.Variable != null)
                {
                    _stack.Pop();
                }

                return node;
            }

            protected internal override Expression VisitParameter(ParameterExpression node)
            {
                foreach (HashSet<ParameterExpression> frame in _stack)
                {
                    if (frame.Contains(node))
                    {
                        return node;
                    }
                }

                _hasFreeVariable = true;
                return node;
            }
        }
    }

    class PartiallyEvaluatedDynamicExpression : Expression, IDynamicExpression
    {
        private readonly IDynamicExpression _node;
        private readonly object _site;

        public PartiallyEvaluatedDynamicExpression(IDynamicExpression node)
        {
            _node = node;
            _site = node.CreateCallSite();
        }

        public override ExpressionType NodeType => ExpressionType.Dynamic;
        public int ArgumentCount => _node.ArgumentCount;
        public Type DelegateType => _node.DelegateType;
        public object CreateCallSite() => _site;
        public Expression GetArgument(int index) => _node.GetArgument(index);
        public Expression Rewrite(Expression[] args) => _node.Rewrite(args);
    }
}
