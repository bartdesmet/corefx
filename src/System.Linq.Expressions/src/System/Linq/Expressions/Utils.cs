// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Reflection;
using static System.Linq.Expressions.CachedReflectionInfo;

namespace System.Linq.Expressions
{
    internal static class Utils
    {
        public static readonly object BoxedFalse = false;
        public static readonly object BoxedTrue = true;
        public static readonly object BoxedIntM1 = -1;
        public static readonly object BoxedInt0 = 0;
        public static readonly object BoxedInt1 = 1;
        public static readonly object BoxedInt2 = 2;
        public static readonly object BoxedInt3 = 3;

        private static readonly ConstantExpression s_true = Expression.Constant(BoxedTrue);
        private static readonly ConstantExpression s_false = Expression.Constant(BoxedFalse);

        private static readonly ConstantExpression s_m1 = Expression.Constant(BoxedIntM1);
        private static readonly ConstantExpression s_0 = Expression.Constant(BoxedInt0);
        private static readonly ConstantExpression s_1 = Expression.Constant(BoxedInt1);
        private static readonly ConstantExpression s_2 = Expression.Constant(BoxedInt2);
        private static readonly ConstantExpression s_3 = Expression.Constant(BoxedInt3);

        public static readonly DefaultExpression Empty = Expression.Empty();
        public static readonly ConstantExpression Null = Expression.Constant(null);

        public static ConstantExpression Constant(bool value) => value ? s_true : s_false;

        public static ConstantExpression Constant(int value)
        {
            switch (value)
            {
                case -1: return s_m1;
                case 0: return s_0;
                case 1: return s_1;
                case 2: return s_2;
                case 3: return s_3;
                default: return Expression.Constant(value);
            }
        }

        public static bool TryGetRawConstantValue(FieldInfo fi, out object value)
        {
            // TODO: It looks like GetRawConstantValue is not available at the moment, use it when it comes back.
            //value = fi.GetRawConstantValue();
            //return true;

            try
            {
                value = fi.GetValue(obj: null);
                return true;
            }
            catch
            {
                value = null;
                return false;
            }
        }

        public static bool IsStringSwitch(SwitchExpression node)
        {
            // If we have a comparison other than string equality, bail
            MethodInfo equality = String_op_Equality_String_String;
            if (equality != null && !equality.IsStatic)
            {
                equality = null;
            }

            if (node.Comparison != equality)
            {
                return false;
            }

            return true;
        }

        public static bool ShouldEmitHashtableSwitch(SwitchExpression node, out int numberOfTests)
        {
            numberOfTests = 0;

            if (!IsStringSwitch(node))
            {
                return false;
            }

            // All test values must be constant.
            foreach (SwitchCase c in node.Cases)
            {
                foreach (Expression t in c.TestValues)
                {
                    if (!(t is ConstantExpression))
                    {
                        return false;
                    }
                    numberOfTests++;
                }
            }

            // Must have >= 7 labels for it to be worth it.
            if (numberOfTests < 7)
            {
                return false;
            }

            return true;
        }

        public static bool IsStringHashtableSwitch(SwitchExpression node)
        {
            int ignored = 0;
            return ShouldEmitHashtableSwitch(node, out ignored);
        }
    }
}
