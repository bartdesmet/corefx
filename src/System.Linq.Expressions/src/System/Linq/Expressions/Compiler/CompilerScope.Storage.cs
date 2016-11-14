// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace System.Linq.Expressions.Compiler
{
    internal sealed partial class CompilerScope
    {
        private abstract class Storage
        {
            internal readonly LambdaCompiler Compiler;
            internal readonly ParameterExpression Variable;

            internal Storage(LambdaCompiler compiler, ParameterExpression variable)
            {
                Compiler = compiler;
                Variable = variable;
            }

            internal abstract void EmitLoad();
            internal abstract void EmitAddress();
            internal abstract void EmitStore();

            internal virtual void EmitStore(Storage value)
            {
                value.EmitLoad();
                EmitStore();
            }

            internal virtual void FreeLocal()
            {
            }
        }

        private sealed class LocalStorage : Storage
        {
            private readonly LocalBuilder _local;

            internal LocalStorage(LambdaCompiler compiler, ParameterExpression variable)
                : base(compiler, variable)
            {
                // ByRef variables are supported. This is used internally by
                // the compiler when emitting an inlined lambda invoke, to
                // handle ByRef parameters. BlockExpression prevents this
                // from being exposed to user created trees.
                _local = compiler.GetNamedLocal(variable.IsByRef ? variable.Type.MakeByRefType() : variable.Type, variable);
            }

            internal override void EmitLoad()
            {
                Compiler.IL.Emit(OpCodes.Ldloc, _local);
            }

            internal override void EmitStore()
            {
                Compiler.IL.Emit(OpCodes.Stloc, _local);
            }

            internal override void EmitAddress()
            {
                Compiler.IL.Emit(OpCodes.Ldloca, _local);
            }
        }

        private sealed class ArgumentStorage : Storage
        {
            private readonly int _argument;

            internal ArgumentStorage(LambdaCompiler compiler, ParameterExpression p)
                : base(compiler, p)
            {
                _argument = compiler.GetLambdaArgument(compiler.Parameters.IndexOf(p));
            }

            internal override void EmitLoad()
            {
                Compiler.IL.EmitLoadArg(_argument);
            }

            internal override void EmitStore()
            {
                Compiler.IL.EmitStoreArg(_argument);
            }

            internal override void EmitAddress()
            {
                Compiler.IL.EmitLoadArgAddress(_argument);
            }
        }

        private sealed class LocalBoxStorage : Storage
        {
            private readonly LocalBuilder _boxLocal;
            private readonly Type _boxType;
            private readonly FieldInfo _boxValueField;

            internal LocalBoxStorage(LambdaCompiler compiler, ParameterExpression variable)
                : base(compiler, variable)
            {
                _boxType = typeof(StrongBox<>).MakeGenericType(variable.Type);
                _boxValueField = _boxType.GetField("Value");
                _boxLocal = compiler.GetNamedLocal(_boxType, variable);
            }

            internal override void EmitLoad()
            {
                EmitLoadBox();
                Compiler.IL.Emit(OpCodes.Ldfld, _boxValueField);
            }

            internal override void EmitStore()
            {
                LocalBuilder value = Compiler.GetLocal(Variable.Type);
                Compiler.IL.Emit(OpCodes.Stloc, value);
                EmitLoadBox();
                Compiler.IL.Emit(OpCodes.Ldloc, value);
                Compiler.FreeLocal(value);
                Compiler.IL.Emit(OpCodes.Stfld, _boxValueField);
            }

            internal override void EmitStore(Storage value)
            {
                EmitLoadBox();
                value.EmitLoad();
                Compiler.IL.Emit(OpCodes.Stfld, _boxValueField);
            }

            internal override void EmitAddress()
            {
                EmitLoadBox();
                Compiler.IL.Emit(OpCodes.Ldflda, _boxValueField);
            }

            internal void EmitLoadBox()
            {
                Compiler.IL.Emit(OpCodes.Ldloc, _boxLocal);
            }

            internal void EmitStoreBox()
            {
                Compiler.IL.Emit(OpCodes.Stloc, _boxLocal);
            }
        }

        private sealed class ClosureStorage : Storage
        {
            private readonly int _index;
            private readonly Storage _closure;
            private readonly FieldInfo _closureField;

            internal ClosureStorage(Storage closure, int index, ParameterExpression variable)
                : base(closure.Compiler, variable)
            {
                _closure = closure;
                _index = index;

                Type closureType = closure.Variable.Type;
                _closureField = closureType.GetField("Item" + (index + 1));
                Debug.Assert(_closureField != null);
            }

            internal override void EmitLoad()
            {
                _closure.EmitLoad();
                Compiler.IL.Emit(OpCodes.Ldfld, _closureField);
            }

            internal override void EmitStore()
            {
                LocalBuilder value = Compiler.GetLocal(Variable.Type);
                Compiler.IL.Emit(OpCodes.Stloc, value);
                _closure.EmitLoad();
                Compiler.IL.Emit(OpCodes.Ldloc, value);
                Compiler.FreeLocal(value);
                Compiler.IL.Emit(OpCodes.Stfld, _closureField);
            }

            internal override void EmitStore(Storage value)
            {
                _closure.EmitLoad();
                value.EmitLoad();
                Compiler.IL.Emit(OpCodes.Stfld, _closureField);
            }

            internal override void EmitAddress()
            {
                _closure.EmitLoad();
                Compiler.IL.Emit(OpCodes.Ldflda, _closureField);
            }
        }

        // closures containing StrongBox<T> slots are used for backwards compatibility
        // when a variable is referenced in a Quote node; see EmitNewHoistedLocals for
        // more information

        private sealed class ClosureBoxStorage : Storage
        {
            private readonly int _index;
            private readonly Storage _closure;
            private readonly FieldInfo _closureField;
            private readonly Type _boxType;
            private readonly FieldInfo _boxValueField;

            internal ClosureBoxStorage(Storage closure, int index, ParameterExpression variable)
                : base(closure.Compiler, variable)
            {
                _closure = closure;
                _index = index;

                Type closureType = closure.Variable.Type;
                _closureField = closureType.GetField("Item" + (index + 1));
                Debug.Assert(_closureField != null);

                _boxType = typeof(StrongBox<>).MakeGenericType(variable.Type);
                _boxValueField = _boxType.GetField("Value");
            }

            internal override void EmitLoad()
            {
                EmitLoadBox();
                Compiler.IL.Emit(OpCodes.Ldfld, _boxValueField);
            }

            internal override void EmitStore()
            {
                LocalBuilder value = Compiler.GetLocal(Variable.Type);
                Compiler.IL.Emit(OpCodes.Stloc, value);
                EmitLoadBox();
                Compiler.IL.Emit(OpCodes.Ldloc, value);
                Compiler.FreeLocal(value);
                Compiler.IL.Emit(OpCodes.Stfld, _boxValueField);
            }

            internal override void EmitStore(Storage value)
            {
                EmitLoadBox();
                value.EmitLoad();
                Compiler.IL.Emit(OpCodes.Stfld, _boxValueField);
            }

            internal override void EmitAddress()
            {
                EmitLoadBox();
                Compiler.IL.Emit(OpCodes.Ldflda, _boxValueField);
            }

            internal void EmitLoadBox()
            {
                _closure.EmitLoad();
                Compiler.IL.Emit(OpCodes.Ldfld, _closureField);
            }
        }
    }
}
