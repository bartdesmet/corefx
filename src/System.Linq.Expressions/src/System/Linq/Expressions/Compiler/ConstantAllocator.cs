// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Dynamic.Utils;
using System.Reflection;

namespace System.Linq.Expressions.Compiler
{
    internal sealed class ConstantAllocator : ExpressionVisitor
    {
        private readonly AnalyzedTree _tree;
        private readonly bool _compileToDynamicMethod;
        private readonly Stack<BoundConstants> _constants = new Stack<BoundConstants>();

        internal static void Allocate(LambdaExpression lambda, AnalyzedTree tree, bool compileToDynamicMethod)
        {
            var allocator = new ConstantAllocator(tree, compileToDynamicMethod);
            allocator.Visit(lambda);
        }

        private ConstantAllocator(AnalyzedTree tree, bool compileToDynamicMethod)
        {
            _tree = tree;
            _compileToDynamicMethod = compileToDynamicMethod;
        }

        public override Expression Visit(Expression node)
        {
            if (node != null && node.NodeType == ExpressionType.Dynamic)
            {
                return VisitDynamic(node);
            }

            return base.Visit(node);
        }

        protected internal override Expression VisitLambda<T>(Expression<T> node)
        {
            _constants.Push(_tree.Constants[node] = new BoundConstants());

            var res = base.VisitLambda(node);

            var constants = _constants.Pop();

            if (_constants.Count > 0)
            {
                if (_compileToDynamicMethod)
                {
                    Allocate(typeof(MethodInfo)); // for DynamicMethod case in EmitDelegateConstruction
                }

                Allocate(constants.GetConstantsType()); // for EmitClosureCreation
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

        private Expression VisitDynamic(Expression node)
        {
            var expr = (IDynamicExpression)node;

            var newArgs = ExpressionVisitorUtils.VisitArguments(this, expr);
            if (newArgs != null)
            {
                node = expr.Rewrite(newArgs);
                expr = (IDynamicExpression)node;
            }

            // create the call site prior to lambda compilation in order to get its type,
            // and return a replacement IDynamicExpression that stores the call site for
            // extraction during EmitDynamicExpression

            var result = new PartiallyEvaluatedDynamicExpression(expr);

            var site = result.CreateCallSite();
            var siteType = site.GetType();

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

        class FreeVariableScanner : ExpressionVisitor
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
                _stack.Push(new HashSet<ParameterExpression>(node.Variables));

                Visit(node.Expressions);

                _stack.Pop();

                return node;
            }

            protected internal override Expression VisitLambda<T>(Expression<T> node)
            {
                _stack.Push(new HashSet<ParameterExpression>(node.Parameters));

                Visit(node.Body);

                _stack.Pop();

                return node;
            }

            protected override CatchBlock VisitCatchBlock(CatchBlock node)
            {
                _stack.Push(new HashSet<ParameterExpression>(new[] { node.Variable }));

                Visit(node.Filter);
                Visit(node.Body);

                _stack.Pop();

                return node;
            }

            protected internal override Expression VisitParameter(ParameterExpression node)
            {
                foreach (var frame in _stack)
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
