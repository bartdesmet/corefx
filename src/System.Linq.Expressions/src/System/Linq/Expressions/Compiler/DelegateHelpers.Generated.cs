// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Dynamic;
using System.Dynamic.Utils;
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

        internal static TypeInfo NextTypeInfo(Type initialArg)
        {
            lock (_DelegateCache)
            {
                return NextTypeInfo(initialArg, _DelegateCache);
            }
        }

        internal static TypeInfo GetNextTypeInfo(Type initialArg, TypeInfo curTypeInfo)
        {
            lock (_DelegateCache)
            {
                return NextTypeInfo(initialArg, curTypeInfo);
            }
        }

        private static TypeInfo _DelegateCache = new TypeInfo();

        private const int MaximumArity = 17;

        internal class TypeInfo
        {
            public Type DelegateType;
            public Dictionary<Type, TypeInfo> TypeChain;
        }

        private static TypeInfo NextTypeInfo(Type initialArg, TypeInfo curTypeInfo)
        {
            Type lookingUp = initialArg;
            TypeInfo nextTypeInfo;
            if (curTypeInfo.TypeChain == null)
            {
                curTypeInfo.TypeChain = new Dictionary<Type, TypeInfo>();
            }

            if (!curTypeInfo.TypeChain.TryGetValue(lookingUp, out nextTypeInfo))
            {
                nextTypeInfo = new TypeInfo();
                if (!lookingUp.IsCollectible)
                {
                    curTypeInfo.TypeChain[lookingUp] = nextTypeInfo;
                }
            }

            return nextTypeInfo;
        }

#if !FEATURE_COMPILE

        public delegate object VBCallSiteDelegate0<T>(T callSite, object instance);
        public delegate object VBCallSiteDelegate1<T>(T callSite, object instance, ref object arg1);
        public delegate object VBCallSiteDelegate2<T>(T callSite, object instance, ref object arg1, ref object arg2);
        public delegate object VBCallSiteDelegate3<T>(T callSite, object instance, ref object arg1, ref object arg2, ref object arg3);
        public delegate object VBCallSiteDelegate4<T>(T callSite, object instance, ref object arg1, ref object arg2, ref object arg3, ref object arg4);
        public delegate object VBCallSiteDelegate5<T>(T callSite, object instance, ref object arg1, ref object arg2, ref object arg3, ref object arg4, ref object arg5);
        public delegate object VBCallSiteDelegate6<T>(T callSite, object instance, ref object arg1, ref object arg2, ref object arg3, ref object arg4, ref object arg5, ref object arg6);
        public delegate object VBCallSiteDelegate7<T>(T callSite, object instance, ref object arg1, ref object arg2, ref object arg3, ref object arg4, ref object arg5, ref object arg6, ref object arg7);


        private static Type TryMakeVBStyledCallSite(Type[] types)
        {
            // Shape of VB CallSiteDelegates is CallSite * (instance : obj) * [arg-n : byref obj] -> obj
            // array of arguments should contain at least 3 elements (callsite, instance and return type)
            if (types.Length < 3 || types[0].IsByRef || types[1] != typeof(object) || types[types.Length - 1] != typeof(object))
            {
                return null;
            }

            // check if all arguments starting from the second has type byref<obj>
            for (int i = 2; i < types.Length - 2; ++i)
            {
                Type t = types[i];
                if (!t.IsByRef || t.GetElementType() != typeof(object))
                {
                    return null;
                }
            }

            switch (types.Length - 1)
            {
                case 2: return typeof(VBCallSiteDelegate0<>).MakeGenericType(types[0]);
                case 3: return typeof(VBCallSiteDelegate1<>).MakeGenericType(types[0]);
                case 4: return typeof(VBCallSiteDelegate2<>).MakeGenericType(types[0]);
                case 5: return typeof(VBCallSiteDelegate3<>).MakeGenericType(types[0]);
                case 6: return typeof(VBCallSiteDelegate4<>).MakeGenericType(types[0]);
                case 7: return typeof(VBCallSiteDelegate5<>).MakeGenericType(types[0]);
                case 8: return typeof(VBCallSiteDelegate6<>).MakeGenericType(types[0]);
                case 9: return typeof(VBCallSiteDelegate7<>).MakeGenericType(types[0]);
                default: return null;
            }
        }
#endif

        /// <summary>
        /// Creates a new delegate, or uses a func/action
        /// Note: this method does not cache
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        internal static Type MakeNewDelegate(Type[] types)
        {
            Debug.Assert(types != null && types.Length > 0);

            // Can only used predefined delegates if we have no byref types and
            // the arity is small enough to fit in Func<...> or Action<...>
            bool needCustom;

            if (types.Length > MaximumArity)
            {
                needCustom = true;
            }
            else
            {
                needCustom = false;

                for (int i = 0; i < types.Length; i++)
                {
                    Type type = types[i];
                    if (type.IsByRef || type.IsByRefLike || type.IsPointer)
                    {
                        needCustom = true;
                        break;
                    }
                }
            }

            if (needCustom)
            {
#if FEATURE_COMPILE
                return MakeNewCustomDelegate(types);
#else
                return TryMakeVBStyledCallSite(types) ?? MakeNewCustomDelegate(types);
#endif
            }

            Type result;
            if (types[types.Length - 1] == typeof(void))
            {
                result = GetActionType(types.RemoveLast());
            }
            else
            {
                result = GetFuncType(types);
            }

            Debug.Assert(result != null);
            return result;
        }

        internal static Type GetFuncType(Type[] types)
        {
            switch (types.Length)
            {
                case 1:
                    return typeof(Func<>).MakeGenericType(types);
                case 2:
                    return typeof(Func<,>).MakeGenericType(types);
                case 3:
                    return typeof(Func<,,>).MakeGenericType(types);
                case 4:
                    return typeof(Func<,,,>).MakeGenericType(types);
                case 5:
                    return typeof(Func<,,,,>).MakeGenericType(types);
                case 6:
                    return typeof(Func<,,,,,>).MakeGenericType(types);
                case 7:
                    return typeof(Func<,,,,,,>).MakeGenericType(types);
                case 8:
                    return typeof(Func<,,,,,,,>).MakeGenericType(types);
                case 9:
                    return typeof(Func<,,,,,,,,>).MakeGenericType(types);
                case 10:
                    return typeof(Func<,,,,,,,,,>).MakeGenericType(types);
                case 11:
                    return typeof(Func<,,,,,,,,,,>).MakeGenericType(types);
                case 12:
                    return typeof(Func<,,,,,,,,,,,>).MakeGenericType(types);
                case 13:
                    return typeof(Func<,,,,,,,,,,,,>).MakeGenericType(types);
                case 14:
                    return typeof(Func<,,,,,,,,,,,,,>).MakeGenericType(types);
                case 15:
                    return typeof(Func<,,,,,,,,,,,,,,>).MakeGenericType(types);
                case 16:
                    return typeof(Func<,,,,,,,,,,,,,,,>).MakeGenericType(types);
                case 17:
                    return typeof(Func<,,,,,,,,,,,,,,,,>).MakeGenericType(types);

                default:
                    return null;
            }
        }

        internal static Type GetActionType(Type[] types)
        {
            switch (types.Length)
            {
                case 0:
                    return typeof(Action);

                case 1:
                    return typeof(Action<>).MakeGenericType(types);
                case 2:
                    return typeof(Action<,>).MakeGenericType(types);
                case 3:
                    return typeof(Action<,,>).MakeGenericType(types);
                case 4:
                    return typeof(Action<,,,>).MakeGenericType(types);
                case 5:
                    return typeof(Action<,,,,>).MakeGenericType(types);
                case 6:
                    return typeof(Action<,,,,,>).MakeGenericType(types);
                case 7:
                    return typeof(Action<,,,,,,>).MakeGenericType(types);
                case 8:
                    return typeof(Action<,,,,,,,>).MakeGenericType(types);
                case 9:
                    return typeof(Action<,,,,,,,,>).MakeGenericType(types);
                case 10:
                    return typeof(Action<,,,,,,,,,>).MakeGenericType(types);
                case 11:
                    return typeof(Action<,,,,,,,,,,>).MakeGenericType(types);
                case 12:
                    return typeof(Action<,,,,,,,,,,,>).MakeGenericType(types);
                case 13:
                    return typeof(Action<,,,,,,,,,,,,>).MakeGenericType(types);
                case 14:
                    return typeof(Action<,,,,,,,,,,,,,>).MakeGenericType(types);
                case 15:
                    return typeof(Action<,,,,,,,,,,,,,,>).MakeGenericType(types);
                case 16:
                    return typeof(Action<,,,,,,,,,,,,,,,>).MakeGenericType(types);

                default:
                    return null;
            }
        }
    }
}
