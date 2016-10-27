// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Dynamic.Utils;
using System.Diagnostics;

namespace System.Linq.Expressions.Compiler
{

    // Suppose we have something like:
    //
    //    (string s) => () => s
    //
    // We wish to generate the outer as:
    // 
    //      Func<string> OuterMethod(CompiledLambdaEnvironment<Empty, object[]> closure, string s)
    //      {
    //          Closure<string> locals = new Closure<string>();
    //          locals.Item1 = s;
    //          return ((DynamicMethod)closure.Constants[0]).CreateDelegate(typeof(Func<string>), new CompiledLambdaEnvironment<Closure<string>, object[]>(null, locals));
    //      }
    //      
    // ... and the inner as:
    // 
    //      string InnerMethod(CompiledLambdaEnvironment<Closure<string>, object[]> closure)
    //      {
    //          Closure<string> locals = closure.Locals;
    //          return locals.Item1;
    //      }
    //
    // This class tracks that "s" was hoisted into a closure, as the 0th
    // element in the array
    //
    /// <summary>
    /// Stores information about locals and arguments that are hoisted into
    /// the closure array because they're referenced in an inner lambda.
    /// 
    /// This class is sometimes emitted as a runtime constant for internal
    /// use to hoist variables/parameters in quoted expressions
    /// 
    /// Invariant: this class stores no mutable state
    /// </summary>
    internal sealed class HoistedLocals
    {
        // The parent locals, if any
        internal readonly HoistedLocals Parent;

        // A mapping of hoisted variables to their indexes in the array
        internal readonly ReadOnlyDictionary<Expression, int> Indexes;

        // The variables, in the order they appear in the array
        internal readonly ReadOnlyCollection<ParameterExpression> Variables;

        // The storage kinds for the variables
        internal readonly Dictionary<ParameterExpression, VariableStorageKind> Definitions;

        // A virtual variable for accessing this locals array
        internal readonly ParameterExpression SelfVariable;

        internal HoistedLocals(HoistedLocals parent, ReadOnlyCollection<ParameterExpression> vars, Dictionary<ParameterExpression, VariableStorageKind> definitions)
        {
            Parent = parent;
            Definitions = definitions;

            if (parent != null)
            {
                // Add the parent locals array as the 0th element in the array
                vars = new TrueReadOnlyCollection<ParameterExpression>(vars.AddFirst(parent.SelfVariable));
            }

            Variables = vars;

            int n = vars.Count;

            Debug.Assert(n > 0);

            Dictionary<Expression, int> indexes = new Dictionary<Expression, int>(n);
            Type[] types = new Type[n];

            for (int i = 0; i < n; i++)
            {
                ParameterExpression var = vars[i];
                indexes.Add(var, i);

                Type type = var.Type;

                VariableStorageKind storage = GetStorageKind(var);
                if ((storage & VariableStorageKind.Quoted) != 0)
                {
                    type = typeof(StrongBox<>).MakeGenericType(type);
                }

                types[i] = type;
            }

            Type closureType = DelegateHelpers.GetClosureType(types);

            SelfVariable = Expression.Variable(closureType, null);
            Indexes = new ReadOnlyDictionary<Expression, int>(indexes);
        }

        internal ParameterExpression ParentVariable
        {
            get { return Parent != null ? Parent.SelfVariable : null; }
        }

        internal static object[] GetParent(object[] locals)
        {
            return ((StrongBox<object[]>)locals[0]).Value;
        }

        internal static IRuntimeVariables GetParent(IRuntimeVariables locals)
        {
            return (IRuntimeVariables)locals[0];
        }

        internal VariableStorageKind GetStorageKind(ParameterExpression variable)
        {
            VariableStorageKind kind;

            HoistedLocals locals = this;

            while (locals != null)
            {
                if (variable == locals.SelfVariable)
                {
                    return VariableStorageKind.Local;
                }

                if (locals.Definitions.TryGetValue(variable, out kind))
                {
                    return kind;
                }

                locals = locals.Parent;
            }

            throw ContractUtils.Unreachable;
        }
    }
}
