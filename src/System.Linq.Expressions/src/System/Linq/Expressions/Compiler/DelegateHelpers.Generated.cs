// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace System.Linq.Expressions.Compiler
{
    internal static partial class DelegateHelpers
    {
        private static readonly Type[] ClosureTypes = new[]
        {
            typeof(Closure<>),
            typeof(Closure<,>),
            typeof(Closure<,,>),
            typeof(Closure<,,,>),
            typeof(Closure<,,,,>),
            typeof(Closure<,,,,,>),
            typeof(Closure<,,,,,,>),
            typeof(Closure<,,,,,,,>),
            typeof(Closure<,,,,,,,,>),
            typeof(Closure<,,,,,,,,,>),
            typeof(Closure<,,,,,,,,,,>),
            typeof(Closure<,,,,,,,,,,,>),
            typeof(Closure<,,,,,,,,,,,,>),
            typeof(Closure<,,,,,,,,,,,,,>),
            typeof(Closure<,,,,,,,,,,,,,,>),
            typeof(Closure<,,,,,,,,,,,,,,,>),
        };

        private static readonly object MakeClosureTypeLock = new object();

        private static Dictionary<int, Type> CustomClosureTypes;

        /// <summary>
        /// Finds a delegate type using the types in the array. 
        /// We use the cache to avoid copying the array, and to cache the
        /// created delegate type
        /// </summary>
        internal static Type MakeDelegateType(Type[] types)
        {
            lock (_DelegateCache)
            {
                TypeInfo curTypeInfo = _DelegateCache;

                // arguments & return type
                for (int i = 0; i < types.Length; i++)
                {
                    curTypeInfo = NextTypeInfo(types[i], curTypeInfo);
                }

                // see if we have the delegate already
                if (curTypeInfo.DelegateType == null)
                {
                    // clone because MakeCustomDelegate can hold onto the array.
                    curTypeInfo.DelegateType = MakeNewDelegate((Type[])types.Clone());
                }

                return curTypeInfo.DelegateType;
            }
        }

        internal static Type GetClosureType(Type[] types)
        {
            int arity = types.Length;

            Type closureType;

            if (arity < ClosureTypes.Length)
            {
                closureType = ClosureTypes[arity - 1];
            }
            else
            {
                closureType = MakeClosureType(arity);
            }

            return closureType.MakeGenericType(types);
        }

        internal static Type MakeClosureType(int arity)
        {
            lock (MakeClosureTypeLock)
            {
                Type type;

                if (CustomClosureTypes == null)
                {
                    CustomClosureTypes = new Dictionary<int, Type>();
                }
                else
                {
                    if (CustomClosureTypes.TryGetValue(arity, out type))
                    {
                        return type;
                    }
                }

                type = MakeNewClosureType(arity);

                CustomClosureTypes[arity] = type;

                return type;
            }
        }

        internal static Type MakeNewClosureType(int arity)
        {
            TypeBuilder builder = AssemblyGen.DefineClosureType("System.Runtime.CompilerServices.Closure`" + arity);

            var genericParameterNames = new string[arity];

            for (int i = 0; i < arity; i++)
            {
                genericParameterNames[i] = "T" + (i + 1);
            }

            GenericTypeParameterBuilder[] genericParameterTypes = builder.DefineGenericParameters(genericParameterNames);

            builder.AddInterfaceImplementation(typeof(IRuntimeVariables));

            PropertyBuilder count = builder.DefineProperty("Count", PropertyAttributes.None, typeof(int), Type.EmptyTypes);

            MethodBuilder countGetter = builder.DefineMethod("get_Count", MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.NewSlot | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.Final, typeof(int), Type.EmptyTypes);

            ILGenerator countGetterILGen = countGetter.GetILGenerator();

            countGetterILGen.Emit(OpCodes.Ldc_I4, arity);
            countGetterILGen.Emit(OpCodes.Ret);

            count.SetGetMethod(countGetter);

            PropertyBuilder indexer = builder.DefineProperty("Item", PropertyAttributes.None, typeof(object), new[] { typeof(int) });

            MethodBuilder indexerGetter = builder.DefineMethod("get_Item", MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.NewSlot | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.Final, typeof(object), new[] { typeof(int) });
            MethodBuilder indexerSetter = builder.DefineMethod("set_Item", MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.NewSlot | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.Final, typeof(void), new[] { typeof(int), typeof(object) });

            ILGenerator indexerGetterILGen = indexerGetter.GetILGenerator();
            ILGenerator indexerSetterILGen = indexerSetter.GetILGenerator();

            indexerGetterILGen.Emit(OpCodes.Ldarg_1);
            indexerSetterILGen.Emit(OpCodes.Ldarg_1);

            var indexerGetterLabels = new Label[arity];
            var indexerSetterLabels = new Label[arity];

            for (var i = 0; i < arity; i++)
            {
                indexerGetterLabels[i] = indexerGetterILGen.DefineLabel();
                indexerSetterLabels[i] = indexerSetterILGen.DefineLabel();
            }

            ConstructorInfo indexOutOfRangeCtor = typeof(IndexOutOfRangeException).GetConstructor(Type.EmptyTypes);

            indexerGetterILGen.Emit(OpCodes.Switch, indexerGetterLabels);
            indexerGetterILGen.Emit(OpCodes.Newobj, indexOutOfRangeCtor);
            indexerGetterILGen.Emit(OpCodes.Throw);

            indexerSetterILGen.Emit(OpCodes.Switch, indexerSetterLabels);
            indexerSetterILGen.Emit(OpCodes.Newobj, indexOutOfRangeCtor);
            indexerSetterILGen.Emit(OpCodes.Throw);

            for (int i = 0; i < arity; i++)
            {
                Type type = genericParameterTypes[i].AsType();
                FieldBuilder field = builder.DefineField("Item" + (i + 1), type, FieldAttributes.Public);

                indexerGetterILGen.MarkLabel(indexerGetterLabels[i]);
                indexerGetterILGen.Emit(OpCodes.Ldarg_0);
                indexerGetterILGen.Emit(OpCodes.Ldfld, field);
                indexerGetterILGen.Emit(OpCodes.Box, type);
                indexerGetterILGen.Emit(OpCodes.Ret);

                indexerSetterILGen.MarkLabel(indexerSetterLabels[i]);
                indexerSetterILGen.Emit(OpCodes.Ldarg_0);
                indexerSetterILGen.Emit(OpCodes.Ldarg_2);
                indexerSetterILGen.Emit(OpCodes.Unbox_Any, type);
                indexerSetterILGen.Emit(OpCodes.Stfld, field);
                indexerSetterILGen.Emit(OpCodes.Ret);
            }

            indexer.SetGetMethod(indexerGetter);
            indexer.SetSetMethod(indexerSetter);

            return builder.CreateTypeInfo().AsType();
        }
    }
}
