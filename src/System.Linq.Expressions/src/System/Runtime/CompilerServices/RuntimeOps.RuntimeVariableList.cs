// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Diagnostics;
using System.Linq.Expressions.Compiler;

namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// This API supports the .NET Framework infrastructure and is not intended to be used directly from your code.
    /// Contains helper methods called from dynamically generated methods.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never), DebuggerStepThrough]
    public static partial class RuntimeOps
    {
        // NB: This overload is kept for backwards compatibility and is superseded by
        //     the CreateRuntimeVariables(IRuntimeVariables, long[]) overload.

        /// <summary>
        /// Creates an interface that can be used to modify closed over variables at runtime.
        /// </summary>
        /// <param name="data">The closure array.</param>
        /// <param name="indexes">An array of indexes into the closure array where variables are found.</param>
        /// <returns>An interface to access variables.</returns>
        [Obsolete("do not use this method", true), EditorBrowsable(EditorBrowsableState.Never)]
        public static IRuntimeVariables CreateRuntimeVariables(object[] data, long[] indexes)
        {
            return new LegacyRuntimeVariableList(data, indexes);
        }

        /// <summary>
        /// Creates an interface that can be used to modify closed over variables at runtime.
        /// </summary>
        /// <param name="data">The closure array.</param>
        /// <param name="indexes">An array of indexes into the closure array where variables are found.</param>
        /// <returns>An interface to access variables.</returns>
        [Obsolete("do not use this method", true), EditorBrowsable(EditorBrowsableState.Never)]
        public static IRuntimeVariables CreateRuntimeVariables(IRuntimeVariables data, long[] indexes)
        {
            return new RuntimeVariableList(data, indexes);
        }

        /// <summary>
        /// Creates an interface that can be used to modify closed over variables at runtime.
        /// </summary>
        /// <returns>An interface to access variables.</returns>
        [Obsolete("do not use this method", true), EditorBrowsable(EditorBrowsableState.Never)]
        public static IRuntimeVariables CreateRuntimeVariables()
        {
            return new EmptyRuntimeVariables();
        }

        private sealed class EmptyRuntimeVariables : IRuntimeVariables
        {
            int IRuntimeVariables.Count
            {
                get { return 0; }
            }

            object IRuntimeVariables.this[int index]
            {
                get
                {
                    throw new IndexOutOfRangeException();
                }
                set
                {
                    throw new IndexOutOfRangeException();
                }
            }
        }

        /// <summary>
        /// Provides a list of variables, supporting read/write of the values
        /// Exposed via RuntimeVariablesExpression
        /// </summary>
        private abstract class RuntimeVariableListBase<TLocals> : IRuntimeVariables
        {
            // The top level environment. It contains pointers to parent
            // environments, which are always in the first element
            private readonly TLocals _data;

            // An array of (int, int) pairs, each representing how to find a
            // variable in the environment data structure.
            //
            // The first integer indicates the number of times to go up in the
            // closure chain, the second integer indicates the index into that
            // closure chain.
            private readonly long[] _indexes;

            internal RuntimeVariableListBase(TLocals data, long[] indexes)
            {
                Debug.Assert(data != null);
                Debug.Assert(indexes != null);

                _data = data;
                _indexes = indexes;
            }

            public int Count
            {
                get { return _indexes.Length; }
            }

            public object this[int index]
            {
                get
                {
                    TLocals variables;
                    int slot;
                    GetStorage(index, out variables, out slot);
                    return Load(variables, slot);
                }
                set
                {
                    TLocals variables;
                    int slot;
                    GetStorage(index, out variables, out slot);
                    Store(variables, slot, value);
                }
            }

            private void GetStorage(int index, out TLocals variables, out int slot)
            {
                // We lookup the closure using two ints:
                // 1. The high dword is the number of parents to go up
                // 2. The low dword is the index into that array
                long closureKey = _indexes[index];

                // walk up the parent chain to find the real environment
                variables = _data;
                for (int parents = (int)(closureKey >> 32); parents > 0; parents--)
                {
                    variables = GetParent(variables);
                }

                // Return the variable storage
                slot = (int)closureKey;
            }

            protected abstract TLocals GetParent(TLocals locals);
            protected abstract object Load(TLocals locals, int index);
            protected abstract void Store(TLocals locals, int index, object value);
        }

        private sealed class RuntimeVariableList : RuntimeVariableListBase<IRuntimeVariables>
        {
            public RuntimeVariableList(IRuntimeVariables data, long[] indexes) : base(data, indexes)
            {
            }

            protected override IRuntimeVariables GetParent(IRuntimeVariables locals)
            {
                return HoistedLocals.GetParent(locals);
            }

            protected override object Load(IRuntimeVariables locals, int index)
            {
                return locals[index];
            }

            protected override void Store(IRuntimeVariables locals, int index, object value)
            {
                locals[index] = value;
            }
        }

        private sealed class LegacyRuntimeVariableList : RuntimeVariableListBase<object[]>
        {
            public LegacyRuntimeVariableList(object[] data, long[] indexes) : base(data, indexes)
            {
            }

            protected override object[] GetParent(object[] locals)
            {
                return HoistedLocals.GetParent(locals);
            }

            protected override object Load(object[] locals, int index)
            {
                return locals[index];
            }

            protected override void Store(object[] locals, int index, object value)
            {
                locals[index] = value;
            }
        }
    }
}
